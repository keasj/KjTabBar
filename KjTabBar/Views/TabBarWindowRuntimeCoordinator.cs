using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using KjTabBar.Helpers;

namespace KjTabBar.Views
{
    internal sealed class TabBarWindowRuntimeCoordinator : IDisposable
    {
        private static readonly TimeSpan FastPositionPollingInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan FallbackPositionPollingInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMilliseconds(300);

        private readonly Dispatcher _dispatcher;
        private readonly Func<bool> _isExplorerAlive;
        private readonly Action _updatePosition;
        private readonly Func<Task> _syncWithExplorerAsync;
        private readonly Action _closeWindow;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer _syncTimer;
        private IntPtr _locationHook = IntPtr.Zero;
        private IntPtr _trackedExplorerHwnd = IntPtr.Zero;
        private NativeMethods.WinEventDelegate _locationEventCallback;
        private bool _isSyncTickRunning;

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
            RegisterLocationHook(explorerHwnd);
            _updatePosition();
        }

        public void Stop()
        {
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
                Stop();
                _closeWindow();
                return;
            }

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

                _dispatcher.BeginInvoke(new Action(_updatePosition));
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowRuntimeCoordinator", "Error in location change callback.", ex);
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
    }
}
