using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using KjTabBar.Helpers;

namespace KjTabBar.Views
{
    internal sealed class TabBarWindowRuntimeCoordinator : IDisposable
    {
        private static readonly TimeSpan FastPositionPollingInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan FallbackPositionPollingInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan ImmediateSyncThrottleInterval = TimeSpan.FromMilliseconds(150);
        private const int ExplorerGoneConfirmationCount = 2;

        private readonly Dispatcher _dispatcher;
        private readonly Func<bool> _isExplorerAlive;
        private readonly Action _updatePosition;
        private readonly Func<Task> _syncWithExplorerAsync;
        private readonly Action _closeWindow;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer _syncTimer;
        private IntPtr _locationHook = IntPtr.Zero;
        private IntPtr _destroyHook = IntPtr.Zero;
        private IntPtr _trackedExplorerHwnd = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _locationEventCallback;
        private NativeMethods.WinEventDelegate _destroyEventCallback;
        private bool _isSyncTickRunning;
        private DateTime _lastImmediateSyncRequestUtc = DateTime.MinValue;
        private int _consecutiveExplorerGoneCount;

        public TabBarWindowRuntimeCoordinator(
            Dispatcher dispatcher,
            Func<bool> isExplorerAlive,
            Action updatePosition,
            Func<Task> syncWithExplorerAsync,
            Action closeWindow)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _isExplorerAlive = isExplorerAlive ?? throw new ArgumentNullException(nameof(isExplorerAlive));
            _updatePosition = updatePosition ?? throw new ArgumentNullException(nameof(updatePosition));
            _syncWithExplorerAsync = syncWithExplorerAsync ?? throw new ArgumentNullException(nameof(syncWithExplorerAsync));
            _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
        }

        public void Start(IntPtr explorerHwnd)
        {
            _consecutiveExplorerGoneCount = 0;
            RegisterLocationHook(explorerHwnd);

            if (_positionTimer == null)
            {
                _positionTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
                _positionTimer.Tick += PositionTimer_Tick;
            }

            _positionTimer.Interval = GetPositionTimerInterval(_locationHook);
            _positionTimer.Start();

            if (_syncTimer == null)
            {
                _syncTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
                _syncTimer.Interval = SyncInterval;
                _syncTimer.Tick += SyncTimer_Tick;
            }

            _syncTimer.Start();
            _updatePosition();
        }

        public void HandleRenderSizeChanged(bool isLoaded, bool heightChanged)
        {
            if (!ShouldRepositionAfterRenderSizeChange(isLoaded, heightChanged))
            {
                return;
            }

            _updatePosition();
        }

        public void HandleDpiChanged()
        {
            _updatePosition();
        }

        public void RebindExplorer(IntPtr explorerHwnd)
        {
            _consecutiveExplorerGoneCount = 0;
            RegisterLocationHook(explorerHwnd);
            _updatePosition();
        }

        public void Stop()
        {
            _consecutiveExplorerGoneCount = 0;
            UnregisterLocationHook();

            if (_positionTimer != null)
            {
                _positionTimer.Tick -= PositionTimer_Tick;
                _positionTimer.Stop();
                _positionTimer = null;
            }

            if (_syncTimer != null)
            {
                _syncTimer.Tick -= SyncTimer_Tick;
                _syncTimer.Stop();
                _syncTimer = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        internal void HandlePositionTimerTick()
        {
            if (!_isExplorerAlive())
            {
                _consecutiveExplorerGoneCount++;
                if (_consecutiveExplorerGoneCount < ExplorerGoneConfirmationCount)
                {
                    return;
                }

                Stop();
                _closeWindow();
                return;
            }

            _consecutiveExplorerGoneCount = 0;
            _updatePosition();
        }

        internal async Task HandleSyncTimerTickAsync()
        {
            if (_isSyncTickRunning)
            {
                return;
            }

            _isSyncTickRunning = true;
            try
            {
                await _syncWithExplorerAsync();
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowRuntimeCoordinator", "SyncWithExplorerAsync failed.", ex);
            }
            finally
            {
                _isSyncTickRunning = false;
            }
        }

        internal void HandleLocationChangeEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            try
            {
                if (!ShouldHandleLocationChangeEvent(eventType, hwnd, idObject, _trackedExplorerHwnd))
                {
                    return;
                }

                if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _dispatcher.BeginInvoke(new Action(BeginUpdatePositionAndSync));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Error in location change callback.", ex);
            }
        }

        internal void HandleDestroyEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            try
            {
                if (!ShouldHandleDestroyEvent(eventType, hwnd, idObject, _trackedExplorerHwnd))
                {
                    return;
                }

                if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _dispatcher.BeginInvoke(new Action(CloseAfterExplorerDestroyed));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Error in destroy callback.", ex);
            }
        }

        internal static TimeSpan GetPositionTimerInterval(IntPtr locationHook)
        {
            return locationHook == IntPtr.Zero
                ? FastPositionPollingInterval
                : FallbackPositionPollingInterval;
        }

        internal static bool ShouldHandleLocationChangeEvent(uint eventType, IntPtr hwnd, int idObject, IntPtr trackedExplorerHwnd)
        {
            return eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE &&
                   idObject == 0 &&
                   hwnd != IntPtr.Zero &&
                   hwnd == trackedExplorerHwnd;
        }

        internal static bool ShouldHandleDestroyEvent(uint eventType, IntPtr hwnd, int idObject, IntPtr trackedExplorerHwnd)
        {
            return eventType == NativeMethods.EVENT_OBJECT_DESTROY &&
                   idObject == 0 &&
                   hwnd != IntPtr.Zero &&
                   hwnd == trackedExplorerHwnd;
        }

        internal static bool ShouldRepositionAfterRenderSizeChange(bool isLoaded, bool heightChanged)
        {
            return isLoaded && heightChanged;
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            HandlePositionTimerTick();
        }

        private async void SyncTimer_Tick(object sender, EventArgs e)
        {
            await HandleSyncTimerTickAsync();
        }

        private void BeginUpdatePositionAndSync()
        {
            _updatePosition();
            RequestImmediateSync();
        }

        private void RegisterLocationHook(IntPtr explorerHwnd)
        {
            UnregisterLocationHook();
            if (explorerHwnd == IntPtr.Zero)
            {
                return;
            }

            _trackedExplorerHwnd = explorerHwnd;

            try
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(explorerHwnd, out processId);
                if (processId == 0)
                {
                    return;
                }

                _locationEventCallback = HandleLocationChangeEvent;
                _locationHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    _locationEventCallback,
                    processId,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);

                _destroyEventCallback = HandleDestroyEvent;
                _destroyHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    IntPtr.Zero,
                    _destroyEventCallback,
                    processId,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Failed to register location event hook.", ex);
            }
            finally
            {
                UpdatePositionTimerInterval();
            }
        }

        private void UnregisterLocationHook()
        {
            _trackedExplorerHwnd = IntPtr.Zero;
            if (_destroyHook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWinEvent(_destroyHook);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Failed to unhook destroy event hook.", ex);
                }

                _destroyHook = IntPtr.Zero;
                _destroyEventCallback = null;
            }

            if (_locationHook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWinEvent(_locationHook);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Failed to unhook location event hook.", ex);
                }

                _locationHook = IntPtr.Zero;
                _locationEventCallback = null;
            }

            UpdatePositionTimerInterval();
        }

        private void UpdatePositionTimerInterval()
        {
            if (_positionTimer != null)
            {
                _positionTimer.Interval = GetPositionTimerInterval(_locationHook);
            }
        }

        private void RequestImmediateSync()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastImmediateSyncRequestUtc != DateTime.MinValue &&
                (nowUtc - _lastImmediateSyncRequestUtc) < ImmediateSyncThrottleInterval)
            {
                return;
            }

            _lastImmediateSyncRequestUtc = nowUtc;
            if (_isSyncTickRunning)
            {
                return;
            }

            _ = HandleSyncTimerTickAsync();
        }

        private void CloseAfterExplorerDestroyed()
        {
            if (_trackedExplorerHwnd == IntPtr.Zero)
            {
                return;
            }

            Stop();
            _closeWindow();
        }
    }
}
