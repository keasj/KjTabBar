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
        private DispatcherTimer _monitorTimer;
        private TabBarRegistry _tabBars = new TabBarRegistry();
        private ExplorerWindowTrackingState _windowTracking = new ExplorerWindowTrackingState();
        private AppRuntimeCoordinator _appRuntimeCoordinator = new AppRuntimeCoordinator();
        private AppServiceFactory _appServiceFactory = new AppServiceFactory();

        private DesktopForegroundTracker _desktopForegroundTracker = new DesktopForegroundTracker();
        private WinEventHookRegistration _foregroundEventHook;
        private TrayIconService _trayIconService = new TrayIconService();
        private TabPersistenceService _tabPersistence = new TabPersistenceService();
        private ControlPanelTabSearch _controlPanelTabSearch;
        private ExplorerLaunchTracker _explorerLaunchTracker;
        private ExplorerWindowEvaluationService _explorerWindowEvaluationService;
        private ExplorerWindowInteractionService _explorerWindowInteractionService;
        private ExplorerWindowMonitorCoordinator _explorerWindowMonitorCoordinator;
        private ExplorerWindowOutcomeCoordinator _explorerWindowOutcomeCoordinator;
        private ExplorerWindowProcessingCoordinator _explorerWindowProcessingCoordinator;
        private ExplorerTabTargetResolver _explorerTabTargetResolver;
        private AppUiDispatcherAdapter _appUiDispatcherAdapter;
        private AppMonitorCycleCoordinator _appMonitorCycleCoordinator;
        private LanguageResourceService _languageResourceService = new LanguageResourceService();
        private MemoryMaintenanceService _memoryMaintenance;
        private WinEventHookRegistration _showEventHook;

        private static readonly TimeSpan MaxHiddenDuration = TimeSpan.FromSeconds(2);
        private DesktopPathClassifier _desktopPathClassifier;



        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _appRuntimeCoordinator.Shutdown(new AppRuntimeContext
            {
                SaveTarget = _appUiDispatcherAdapter != null ? _appUiDispatcherAdapter.FindValidTabBarTarget() : null,
                TabPersistence = _tabPersistence,
                MonitorTimer = _monitorTimer,
                MonitorTickHandler = MonitorTimer_Tick,
                ExplorerService = _explorerService,
                TabBars = _tabBars,
                TrayIconService = _trayIconService,
                ShowEventHook = _showEventHook,
                WindowTracking = _windowTracking,
                ForegroundEventHook = _foregroundEventHook,
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

            if (!_appRuntimeCoordinator.TryAcquireSingleInstanceMutex("KjTabBar_Application_Mutex", out _mutex))
            {
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppServiceBundle bundle = _appServiceFactory.Create(
                _explorerService,
                _tabBars,
                _windowTracking,
                _desktopForegroundTracker,
                _tabPersistence,
                Dispatcher,
                delegate { return UserSettings.Current; },
                _tabBars.Add,
                MaxHiddenDuration);
            _memoryMaintenance = bundle.MemoryMaintenance;
            _controlPanelTabSearch = bundle.ControlPanelTabSearch;
            _explorerTabTargetResolver = bundle.ExplorerTabTargetResolver;
            _desktopPathClassifier = bundle.DesktopPathClassifier;
            _explorerLaunchTracker = bundle.ExplorerLaunchTracker;
            _appUiDispatcherAdapter = bundle.AppUiDispatcherAdapter;
            _explorerWindowEvaluationService = bundle.ExplorerWindowEvaluationService;
            _explorerWindowInteractionService = bundle.ExplorerWindowInteractionService;
            _explorerWindowMonitorCoordinator = bundle.ExplorerWindowMonitorCoordinator;
            _explorerWindowOutcomeCoordinator = bundle.ExplorerWindowOutcomeCoordinator;
            _explorerWindowProcessingCoordinator = bundle.ExplorerWindowProcessingCoordinator;
            _appMonitorCycleCoordinator = bundle.AppMonitorCycleCoordinator;

            _trayIconService.Initialize(name => TryFindResource(name), Shutdown);
            _foregroundEventHook = _appRuntimeCoordinator.TryRegisterWinEventHook("foreground", NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, ForegroundEventCallback, "Failed to set up foreground hook. Falling back to polling.");
            _showEventHook = _appRuntimeCoordinator.TryRegisterWinEventHook("show", NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW, ShowEventCallback, "Failed to set up show hook.");
            _monitorTimer = _appRuntimeCoordinator.CreateMonitorTimer(TimeSpan.FromMilliseconds(500), MonitorTimer_Tick);

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
                _explorerWindowMonitorCoordinator.HandleShowEvent(hwnd, _appUiDispatcherAdapter.FindValidTabBarTarget, _explorerTabTargetResolver.HasActiveControlPanelTab);
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
                _explorerLaunchTracker.UpdateForegroundState(hwnd, className.ToString());
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
            List<ExplorerWindowProcessRequest> requests = _appMonitorCycleCoordinator.RunCycle(_appUiDispatcherAdapter.FindValidTabBarTarget, DateTime.UtcNow);
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
            await _explorerWindowProcessingCoordinator.ProcessAsync(
                hwnd,
                validTarget,
                _appUiDispatcherAdapter.FindControlPanelTabBarTarget,
                _appUiDispatcherAdapter.FindValidTabBarTarget,
                _appUiDispatcherAdapter.HasEquivalentControlPanelTab,
                _appUiDispatcherAdapter.HasActiveControlPanelTab,
                _tabBars.Contains,
                delegate (IntPtr targetHwnd)
                {
                    return _windowTracking.IgnoredWindows.Contains(targetHwnd);
                });
        }
    }
}
