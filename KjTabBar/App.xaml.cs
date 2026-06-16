using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar
{
    public partial class App : Application
    {
        private IExplorerService _explorerService = new Models.ExplorerManager();
        private System.Threading.Mutex _mutex;
        private TabBarRegistry _tabBars = new TabBarRegistry();
        private ExplorerWindowTrackingState _windowTracking = new ExplorerWindowTrackingState();
        private AppRuntimeCoordinator _appRuntimeCoordinator = new AppRuntimeCoordinator();
        private AppServiceFactory _appServiceFactory = new AppServiceFactory();
        private AppBootstrapper _appBootstrapper;

        private DesktopForegroundTracker _desktopForegroundTracker = new DesktopForegroundTracker();
        private TrayIconService _trayIconService = new TrayIconService();
        private TabPersistenceService _tabPersistence = new TabPersistenceService();
        private LanguageResourceService _languageResourceService = new LanguageResourceService();
        private AppBootstrapResult _bootstrapResult;

        private static readonly TimeSpan MaxHiddenDuration = TimeSpan.FromSeconds(2);

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _appRuntimeCoordinator.Shutdown(new AppRuntimeContext
            {
                SaveTarget = _bootstrapResult != null && _bootstrapResult.Services != null
                    ? _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget()
                    : null,
                TabPersistence = _tabPersistence,
                MonitorTimer = _bootstrapResult != null ? _bootstrapResult.MonitorTimer : null,
                MonitorTickHandler = MonitorTimer_Tick,
                ExplorerService = _explorerService,
                TabBars = _tabBars,
                TrayIconService = _trayIconService,
                ShowEventHook = _bootstrapResult != null ? _bootstrapResult.ShowEventHook : null,
                MoveSizeEndEventHook = _bootstrapResult != null ? _bootstrapResult.MoveSizeEndEventHook : null,
                WindowTracking = _windowTracking,
                ForegroundEventHook = _bootstrapResult != null ? _bootstrapResult.ForegroundEventHook : null,
                Mutex = _mutex
            });
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (SetupCustomActions.IsPostInstallHelperRequest(e != null ? e.Args : null))
            {
                SetupCustomActions.RunPostInstallHelper(e.Args);
                Shutdown();
                return;
            }

            ApplyLanguageResource();

            if (StandardUserRelaunchService.ShouldRelaunchAsStandardUser(e))
            {
                if (StandardUserRelaunchService.TryRelaunchAsStandardUser())
                {
                    Shutdown();
                    return;
                }
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (_appBootstrapper == null)
            {
                _appBootstrapper = new AppBootstrapper(_appRuntimeCoordinator, _appServiceFactory);
            }

            _bootstrapResult = _appBootstrapper.Initialize(new AppBootstrapContext
            {
                ExplorerService = _explorerService,
                TabBars = _tabBars,
                WindowTracking = _windowTracking,
                DesktopForegroundTracker = _desktopForegroundTracker,
                TabPersistence = _tabPersistence,
                Dispatcher = Dispatcher,
                GetUserSettings = delegate { return UserSettings.Current; },
                RegisterTabBar = _tabBars.Add,
                MaxHiddenDuration = MaxHiddenDuration,
                TrayIconService = _trayIconService,
                TryFindResource = TryFindResource,
                Shutdown = Shutdown,
                ForegroundEventCallback = ForegroundEventCallback,
                ShowEventCallback = ShowEventCallback,
                MoveSizeEndEventCallback = MoveSizeEndEventCallback,
                MonitorTickHandler = MonitorTimer_Tick
            });

            if (_bootstrapResult == null)
            {
                Shutdown();
                return;
            }

            _mutex = _bootstrapResult.Mutex;

            ThemeManager.Instance.StartMonitoring();

            MonitorTimer_Tick(null, null);
        }

        private void ApplyLanguageResource()
        {
            try
            {
                _languageResourceService.ApplyLanguageResource(this.Resources.MergedDictionaries, System.Threading.Thread.CurrentThread.CurrentUICulture);
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "Failed to apply language resources.", ex);
            }
        }

        /// <summary>
        /// EVENT_OBJECT_SHOW コールバック。
        /// 新規エクスプローラーウィンドウが表示された瞬間に非表示にし、
        /// タイマーTickでの吸収処理まで表示を抑制する。
        /// </summary>
        private void ShowEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != NativeMethods.EVENT_OBJECT_SHOW) return;
                if (idObject != 0) return;
                _bootstrapResult.Services.ExplorerWindowMonitorCoordinator.HandleShowEvent(
                    hwnd,
                    _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget,
                    _bootstrapResult.Services.ExplorerTabTargetResolver.HasActiveControlPanelTab);
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "Failed while hiding a pending explorer window.", ex);
            }
        }

        private void ForegroundEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
                if (hwnd == IntPtr.Zero) return;

                StringBuilder className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                _bootstrapResult.Services.ExplorerLaunchTracker.UpdateForegroundState(hwnd, className.ToString());
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "ForegroundEventCallback failed.", ex);
            }
        }

        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                MonitorTimer_TickCore();
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogErrorThrottled("App", "MonitorTimerTick", "MonitorTimer_Tick failed.", ex, TimeSpan.FromMinutes(5));
                // 例外が発生してもタイマーは継続
            }
        }

        private void MonitorTimer_TickCore()
        {
            List<ExplorerWindowProcessRequest> requests = _bootstrapResult.Services.AppMonitorCycleCoordinator.RunCycle(
                _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget,
                DateTime.UtcNow);
            for (int i = 0; i < requests.Count; i++)
            {
                try
                {
                    _ = ProcessNewExplorerWindowAsync(requests[i].ExplorerHwnd, requests[i].ValidTarget);
                }
                catch (Exception ex)
                {
                    Helpers.AppLogger.LogError("App", "Failed to queue explorer window processing.", ex);
                    _windowTracking.ProcessingExplorerWindows.Remove(requests[i].ExplorerHwnd);
                }
            }
        }

        private async Task ProcessNewExplorerWindowAsync(IntPtr hwnd, TabBarViewModel validTarget)
        {
            await _bootstrapResult.Services.ExplorerWindowProcessingCoordinator.ProcessAsync(
                hwnd,
                validTarget,
                _bootstrapResult.Services.AppUiDispatcherAdapter.FindControlPanelTabBarTarget,
                _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget,
                _bootstrapResult.Services.AppUiDispatcherAdapter.HasEquivalentControlPanelTab,
                _bootstrapResult.Services.AppUiDispatcherAdapter.HasActiveControlPanelTab,
                _tabBars.Contains,
                delegate (IntPtr targetHwnd)
                {
                    return _windowTracking.IgnoredWindows.Contains(targetHwnd);
                });
        }

        private void MoveSizeEndEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != NativeMethods.EVENT_SYSTEM_MOVESIZEEND) return;
                if (idObject != 0) return; // OBJID_WINDOW
                if (hwnd == IntPtr.Zero) return;

                StringBuilder className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() != "CabinetWClass") return;

                TabBarViewModel activeTabBarVM = _bootstrapResult != null && _bootstrapResult.Services != null
                    ? _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget()
                    : null;
                if (activeTabBarVM == null) return;
                if (hwnd == activeTabBarVM.ExplorerHwnd) return;

                TabBarWindow window;
                if (_tabBars.TryGetTabBarWindow(activeTabBarVM.ExplorerHwnd, out window))
                {
                    NativeMethods.POINT mousePos;
                    if (NativeMethods.GetCursorPos(out mousePos) && window.IsPointOverAbsorbZone(mousePos))
                    {
                        _ = ComThreadService.Instance.InvokeAsync(() =>
                        {
                            string path = _explorerService.GetCurrentPath(hwnd);
                            if (!string.IsNullOrEmpty(path))
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    bool isControlPanel = _explorerService.IsControlPanelPath(path);
                                    _bootstrapResult.Services.ExplorerWindowInteractionService.AbsorbExplorerWindow(
                                        hwnd,
                                        activeTabBarVM,
                                        path,
                                        allowSpecialPath: true,
                                        isControlPanelPath: isControlPanel,
                                        ignoreExplorerWindow: null
                                    );
                                }));
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "MoveSizeEndEventCallback failed.", ex);
            }
        }
    }
}
