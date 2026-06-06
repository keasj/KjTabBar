using System;
using System.Windows.Threading;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    internal sealed class AppServiceBundle
    {
        public MemoryMaintenanceService MemoryMaintenance { get; set; }
        public ControlPanelTabSearch ControlPanelTabSearch { get; set; }
        public ExplorerTabTargetResolver ExplorerTabTargetResolver { get; set; }
        public DesktopPathClassifier DesktopPathClassifier { get; set; }
        public ExplorerLaunchTracker ExplorerLaunchTracker { get; set; }
        public AppUiDispatcherAdapter AppUiDispatcherAdapter { get; set; }
        public ExplorerWindowEvaluationService ExplorerWindowEvaluationService { get; set; }
        public ExplorerWindowInteractionService ExplorerWindowInteractionService { get; set; }
        public ExplorerWindowMonitorCoordinator ExplorerWindowMonitorCoordinator { get; set; }
        public ExplorerWindowOutcomeCoordinator ExplorerWindowOutcomeCoordinator { get; set; }
        public ExplorerWindowProcessingCoordinator ExplorerWindowProcessingCoordinator { get; set; }
        public AppMonitorCycleCoordinator AppMonitorCycleCoordinator { get; set; }
    }

    internal sealed class AppServiceFactory
    {
        public AppServiceBundle Create(
            IExplorerService explorerService,
            TabBarRegistry tabBars,
            ExplorerWindowTrackingState windowTracking,
            DesktopForegroundTracker desktopForegroundTracker,
            TabPersistenceService tabPersistence,
            Dispatcher dispatcher,
            Func<IUserSettings> getUserSettings,
            Action<IntPtr, Views.TabBarWindow> registerTabBar,
            TimeSpan maxHiddenDuration)
        {
            AppServiceBundle bundle = new AppServiceBundle();
            bundle.MemoryMaintenance = new MemoryMaintenanceService(explorerService);
            bundle.ControlPanelTabSearch = new ControlPanelTabSearch(explorerService);
            bundle.ExplorerTabTargetResolver = new ExplorerTabTargetResolver(tabBars, windowTracking, bundle.ControlPanelTabSearch);
            bundle.DesktopPathClassifier = new DesktopPathClassifier(explorerService);
            bundle.ExplorerLaunchTracker = new ExplorerLaunchTracker(
                desktopForegroundTracker,
                windowTracking,
                bundle.ExplorerTabTargetResolver.IsManagedControlPanelLaunchSource,
                tabBars.Contains);
            bundle.AppUiDispatcherAdapter = new AppUiDispatcherAdapter(
                dispatcher,
                bundle.ExplorerTabTargetResolver,
                bundle.ExplorerLaunchTracker.IsForegroundRelatedWindow);
            bundle.ExplorerWindowEvaluationService = new ExplorerWindowEvaluationService(explorerService, bundle.DesktopPathClassifier);
            bundle.ExplorerWindowInteractionService = new ExplorerWindowInteractionService(explorerService, windowTracking, tabPersistence);
            bundle.ExplorerWindowMonitorCoordinator = new ExplorerWindowMonitorCoordinator(tabBars, windowTracking, desktopForegroundTracker, bundle.ExplorerLaunchTracker);
            bundle.ExplorerWindowOutcomeCoordinator = new ExplorerWindowOutcomeCoordinator(
                windowTracking,
                bundle.ExplorerWindowInteractionService,
                bundle.ExplorerTabTargetResolver.IgnoreExplorerWindow,
                getUserSettings,
                registerTabBar);
            bundle.ExplorerWindowProcessingCoordinator = new ExplorerWindowProcessingCoordinator(
                windowTracking,
                bundle.ExplorerLaunchTracker,
                bundle.ExplorerWindowEvaluationService,
                bundle.ExplorerWindowInteractionService,
                bundle.ExplorerWindowOutcomeCoordinator);
            bundle.AppMonitorCycleCoordinator = new AppMonitorCycleCoordinator(
                explorerService,
                bundle.ExplorerLaunchTracker,
                bundle.ExplorerWindowMonitorCoordinator,
                windowTracking,
                tabPersistence,
                bundle.MemoryMaintenance,
                maxHiddenDuration);
            return bundle;
        }
    }
}
