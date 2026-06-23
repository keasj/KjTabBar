using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    internal sealed class AppBootstrapContext
    {
        public IExplorerService ExplorerService { get; set; }
        public TabBarRegistry TabBars { get; set; }
        public ExplorerWindowTrackingState WindowTracking { get; set; }
        public DesktopForegroundTracker DesktopForegroundTracker { get; set; }
        public TabPersistenceService TabPersistence { get; set; }
        public Dispatcher Dispatcher { get; set; }
        public Func<IUserSettings> GetUserSettings { get; set; }
        public Action<IntPtr, Views.TabBarWindow> RegisterTabBar { get; set; }
        public TimeSpan MaxHiddenDuration { get; set; }
        public TrayIconService TrayIconService { get; set; }
        public Func<object, object> TryFindResource { get; set; }
        public Action Shutdown { get; set; }
        public NativeMethods.WinEventDelegate ForegroundEventCallback { get; set; }
        public NativeMethods.WinEventDelegate ShowEventCallback { get; set; }
        public NativeMethods.WinEventDelegate MoveSizeEndEventCallback { get; set; }
        public EventHandler MonitorTickHandler { get; set; }
    }

    internal sealed class AppBootstrapResult
    {
        public Mutex Mutex { get; set; }
        public AppServiceBundle Services { get; set; }
        public DispatcherTimer MonitorTimer { get; set; }
        public WinEventHookRegistration ForegroundEventHook { get; set; }
        public WinEventHookRegistration ShowEventHook { get; set; }
        public WinEventHookRegistration MoveSizeEndEventHook { get; set; }
    }

    internal sealed class AppBootstrapper
    {
        private readonly AppRuntimeCoordinator _runtimeCoordinator;
        private readonly AppServiceFactory _serviceFactory;

        public AppBootstrapper(AppRuntimeCoordinator runtimeCoordinator, AppServiceFactory serviceFactory)
        {
            _runtimeCoordinator = runtimeCoordinator ?? throw new ArgumentNullException(nameof(runtimeCoordinator));
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        }

        public AppBootstrapResult Initialize(AppBootstrapContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Mutex mutex;
            if (!_runtimeCoordinator.TryAcquireSingleInstanceMutex("KjTabBar_Application_Mutex", out mutex))
            {
                return null;
            }

            AppServiceBundle services = _serviceFactory.Create(
                context.ExplorerService,
                context.TabBars,
                context.WindowTracking,
                context.DesktopForegroundTracker,
                context.TabPersistence,
                context.Dispatcher,
                context.GetUserSettings,
                context.RegisterTabBar,
                context.MaxHiddenDuration);

            context.TrayIconService.Initialize(context.TryFindResource, context.Shutdown);

            return new AppBootstrapResult
            {
                Mutex = mutex,
                Services = services,
                ForegroundEventHook = _runtimeCoordinator.TryRegisterWinEventHook(
                    "foreground",
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    context.ForegroundEventCallback,
                    "Failed to set up foreground hook. Falling back to polling."),
                ShowEventHook = _runtimeCoordinator.TryRegisterWinEventHook(
                    "show",
                    NativeMethods.EVENT_OBJECT_SHOW,
                    NativeMethods.EVENT_OBJECT_SHOW,
                    context.ShowEventCallback,
                    "Failed to set up show hook."),
                MoveSizeEndEventHook = _runtimeCoordinator.TryRegisterWinEventHook(
                    "movesizeend",
                    NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                    NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                    context.MoveSizeEndEventCallback,
                    "Failed to set up movesizeend hook."),
                MonitorTimer = _runtimeCoordinator.CreateMonitorTimer(
                    TimeSpan.FromMilliseconds(500),
                    context.MonitorTickHandler)
            };
        }
    }
}
