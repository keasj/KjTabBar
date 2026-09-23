using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private IExplorerService _explorerService = new Models.ExplorerManager(true);
        private bool _isShellWorker;
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
        private DesktopRepeatedLaunchService _desktopRepeatedLaunch;
        private bool _hasRestoredHiddenExplorerWindowsAfterFatalException;
        private System.Threading.Timer _diagnosticDispatcherProbe;
        private Stopwatch _pendingDiagnosticProbe;

        private static readonly TimeSpan MaxHiddenDuration = TimeSpan.FromSeconds(2);

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private bool _shutdownRequested;

        private async void RequestShutdownAfterFileOperations()
        {
            if (_shutdownRequested) return;
            _shutdownRequested = true;
            await FileOperationTracker.Shared.StopAndWaitAsync();
            Shutdown();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            if (_isShellWorker) return;
            if (_desktopRepeatedLaunch != null) _desktopRepeatedLaunch.Dispose();
            if (_diagnosticDispatcherProbe != null) _diagnosticDispatcherProbe.Dispose();
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
            IDisposable disposableExplorer = _explorerService as IDisposable;
            if (disposableExplorer != null) disposableExplorer.Dispose();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLogger.LogError("App", "Unhandled dispatcher exception.", e.Exception);
            RestoreHiddenExplorerWindowsAfterFatalException();
            AppLogger.Flush();
            e.Handled = true;
            Shutdown(-1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            if (exception != null)
            {
                AppLogger.LogError("App", "Unhandled AppDomain exception.", exception);
            }
            else
            {
                AppLogger.LogError(
                    "App",
                    string.Format("Unhandled AppDomain exception object. IsTerminating={0}", e.IsTerminating),
                    new InvalidOperationException("Unhandled exception object was not an Exception instance."));
            }

            RestoreHiddenExplorerWindowsAfterFatalException();
            AppLogger.Flush();
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.LogError("App", "Unobserved task exception.", e.Exception);
            AppLogger.Flush();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (ShellWorkerHost.IsWorkerRequest(e != null ? e.Args : null))
            {
                _isShellWorker = true;
                ShellWorkerHost.Run(e.Args);
                Shutdown();
                return;
            }

            if (SetupCustomActions.IsPostInstallHelperRequest(e != null ? e.Args : null))
            {
                SetupCustomActions.RunPostInstallHelper(e.Args);
                Shutdown();
                return;
            }

            ApplyLanguageResource();
            ThemeManager.Instance.ApplyThemeToResources(this.Resources);

            bool canContinue = StandardUserRelaunchService.CanContinueStartup(
                StandardUserRelaunchService.ShouldRelaunchAsStandardUser(e),
                StandardUserRelaunchService.HasStartupArgument(e, StandardUserRelaunchService.ShellRelaunchArgument),
                StandardUserRelaunchService.TryRelaunchAsStandardUser,
                delegate
                {
                    AppLogger.LogError("App", "Standard-user relaunch failed; elevated startup was stopped.",
                        new InvalidOperationException("A standard-user process is required."));
                    MessageBox.Show(TryFindResource("ErrorStandardUserLaunch") as string ??
                        "KjTabBar could not start as a standard user. Please start it without administrator privileges.",
                        "KjTabBar", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            if (!canContinue)
            {
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            StartDiagnosticDispatcherTiming();

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
                Shutdown = RequestShutdownAfterFileOperations,
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
            _desktopRepeatedLaunch = new DesktopRepeatedLaunchService((ExplorerManager)_explorerService,
                _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget);

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

        private void RestoreHiddenExplorerWindowsAfterFatalException()
        {
            if (_hasRestoredHiddenExplorerWindowsAfterFatalException)
            {
                return;
            }

            _hasRestoredHiddenExplorerWindowsAfterFatalException = true;
            try
            {
                if (_windowTracking != null)
                {
                    _windowTracking.RestoreAllHiddenWindows();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App", "Failed to restore hidden explorer windows after fatal exception.", ex);
            }
        }

        /// <summary>
        /// EVENT_OBJECT_SHOW コールバック。
        /// 新規エクスプローラーウィンドウが表示された瞬間に非表示にし、
        /// タイマーTickでの吸収処理まで表示を抑制する。
        /// 既存タブバーがない場合は、次のTickを待たず監視処理を予約する。
        /// </summary>
        private void StartDiagnosticDispatcherTiming()
        {
            if (AppLogger.StartDiagnosticTiming() == null) return;
            System.Reflection.FieldInfo methodField = typeof(DispatcherOperation).GetField(
                "_method", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Dictionary<DispatcherOperation, Tuple<Stopwatch, string>> active =
                new Dictionary<DispatcherOperation, Tuple<Stopwatch, string>>();
            System.Collections.Concurrent.ConcurrentDictionary<DispatcherOperation, Tuple<Stopwatch, DispatcherPriority>> pending =
                new System.Collections.Concurrent.ConcurrentDictionary<DispatcherOperation, Tuple<Stopwatch, DispatcherPriority>>();
            Dispatcher.Hooks.OperationPosted += delegate (object sender, DispatcherHookEventArgs args)
            {
                pending[args.Operation] = Tuple.Create(Stopwatch.StartNew(), args.Operation.Priority);
            };
            Dispatcher.Hooks.OperationStarted += delegate (object sender, DispatcherHookEventArgs args)
            {
                string name = "unknown";
                try
                {
                    Delegate callback = methodField != null ? methodField.GetValue(args.Operation) as Delegate : null;
                    if (callback != null) name = callback.Method.DeclaringType.FullName + "." + callback.Method.Name;
                }
                catch { }
                Tuple<Stopwatch, DispatcherPriority> queued;
                if (pending.TryRemove(args.Operation, out queued) && queued.Item1.ElapsedMilliseconds >= 100)
                    AppLogger.LogDiagnosticTiming("DispatcherQueue." + queued.Item2 + "." + name, IntPtr.Zero, queued.Item1);
                active[args.Operation] = Tuple.Create(Stopwatch.StartNew(), name);
            };
            Dispatcher.Hooks.OperationCompleted += delegate (object sender, DispatcherHookEventArgs args)
            {
                Tuple<Stopwatch, string> entry;
                if (!active.TryGetValue(args.Operation, out entry)) return;
                active.Remove(args.Operation);
                if (entry.Item1.ElapsedMilliseconds >= 100)
                    AppLogger.LogDiagnosticTiming("Dispatcher." + entry.Item2, IntPtr.Zero, entry.Item1);
            };
            Dispatcher.Hooks.OperationAborted += delegate (object sender, DispatcherHookEventArgs args)
            {
                active.Remove(args.Operation);
                Tuple<Stopwatch, DispatcherPriority> queued;
                pending.TryRemove(args.Operation, out queued);
            };
            // Opt-in active probe: posting a message can change the observed wake-up timing.
            if (Environment.GetEnvironmentVariable("KJTB_DISPATCHER_PROBE") == "1")
                _diagnosticDispatcherProbe = new System.Threading.Timer(ProbeDiagnosticDispatcher, null, 1000, 1000);
        }

        private void ProbeDiagnosticDispatcher(object state)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Stopwatch timer = Stopwatch.StartNew();
            Stopwatch previous = System.Threading.Interlocked.CompareExchange(ref _pendingDiagnosticProbe, timer, null);
            if (previous != null)
            {
                AppLogger.LogDiagnosticTiming("DispatcherProbe.Pending", IntPtr.Zero, previous);
                return;
            }
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                {
                    AppLogger.LogDiagnosticTiming("DispatcherProbe.Completed", IntPtr.Zero, timer);
                    System.Threading.Interlocked.CompareExchange(ref _pendingDiagnosticProbe, null, timer);
                }));
            }
            catch (InvalidOperationException)
            {
                System.Threading.Interlocked.CompareExchange(ref _pendingDiagnosticProbe, null, timer);
            }
        }

        private void ShowEventCallback(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            Stopwatch eventTimer = null;
            bool isExplorerEvent = false;
            try
            {
                if (idObject != 0 || idChild != 0) return;
                if (eventType != NativeMethods.EVENT_OBJECT_CREATE && eventType != NativeMethods.EVENT_OBJECT_SHOW) return;
                eventTimer = AppLogger.StartDiagnosticTiming();
                if (eventTimer != null)
                {
                    StringBuilder eventClass = new StringBuilder(256);
                    NativeMethods.GetClassName(hwnd, eventClass, eventClass.Capacity);
                    if (eventClass.ToString() == "CabinetWClass")
                    {
                        isExplorerEvent = true;
                        uint deliveryMs = unchecked((uint)Environment.TickCount - dwmsEventTime);
                        AppLogger.LogDiagnostic("ReopenEvent", string.Format(
                            "event={0} hwnd={1} deliveryMs={2}", eventType, hwnd, deliveryMs));
                    }
                }
                if (eventType == NativeMethods.EVENT_OBJECT_CREATE)
                {
                    if (_desktopRepeatedLaunch != null && DesktopRepeatedLaunchService.ClassName(hwnd) == "CabinetWClass")
                        _desktopRepeatedLaunch.CancelForNewWindow();
                    _bootstrapResult.Services.ExplorerWindowMonitorCoordinator.HandleCreateEvent(
                        hwnd, _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget);
                    return;
                }
                if (eventType != NativeMethods.EVENT_OBJECT_SHOW) return;
                _bootstrapResult.Services.ExplorerWindowMonitorCoordinator.HandleShowEvent(
                    hwnd,
                    _bootstrapResult.Services.AppUiDispatcherAdapter.FindValidTabBarTarget,
                    _bootstrapResult.Services.ExplorerTabTargetResolver.HasActiveControlPanelTab,
                    delegate
                    {
                        _bootstrapResult.Services.AppMonitorCycleCoordinator.RequestImmediateCycle(
                            delegate (Action callback) { Dispatcher.BeginInvoke(DispatcherPriority.Background, callback); },
                            delegate
                            {
                                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                                {
                                    MonitorTimer_Tick(null, null);
                                }
                            });
                    });
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "Failed while hiding a pending explorer window.", ex);
            }
            finally
            {
                if (eventTimer != null && (isExplorerEvent || eventTimer.ElapsedMilliseconds >= 100))
                {
                    AppLogger.LogDiagnosticTiming("Event.Callback." + eventType, hwnd, eventTimer);
                    if (!isExplorerEvent)
                        AppLogger.LogDiagnostic("ReopenSlowChildEvent", string.Format(
                            "hwnd={0} root={1} event={2}", hwnd, NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT), eventType));
                }
            }
        }

        private async void ForegroundEventCallback(
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
                if (_desktopRepeatedLaunch != null) await _desktopRepeatedLaunch.OnForegroundAsync(hwnd);
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogError("App", "ForegroundEventCallback failed.", ex);
            }
        }

        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                MonitorTimer_TickCore();
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogErrorThrottled("App", "MonitorTimerTick", "MonitorTimer_Tick failed.", ex, TimeSpan.FromMinutes(5));
                // 例外が発生してもタイマーは継続
            }
            finally
            {
                Helpers.AppLogger.LogSlowOperation("App", "App.MonitorTimerTick", "MonitorTimer_Tick", stopwatch.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));
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
                                Dispatcher.BeginInvoke(new Action(async () =>
                                {
                                    try
                                    {
                                        bool isControlPanel = _explorerService.IsControlPanelPath(path);
                                        await _bootstrapResult.Services.ExplorerWindowProcessingCoordinator.ApplyOutcomeAsync(hwnd, 0,
                                            new ExplorerWindowEvaluationResult
                                            {
                                                Action = AbsorptionAction.Absorb,
                                                ResolvedPath = path,
                                                AllowSpecialPath = true,
                                                IsControlPanelPath = isControlPanel
                                            }, activeTabBarVM, null);
                                    }
                                    catch (Exception ex) { AppLogger.LogError("App", "Manual absorption failed.", ex); }
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
