using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class AppMonitorCycleCoordinator
    {
        private readonly IExplorerService _explorerService;
        private readonly ExplorerLaunchTracker _explorerLaunchTracker;
        private readonly ExplorerWindowMonitorCoordinator _monitorCoordinator;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly TabPersistenceService _tabPersistence;
        private readonly MemoryMaintenanceService _memoryMaintenance;
        private readonly TimeSpan _maxHiddenDuration;

        public AppMonitorCycleCoordinator(
            IExplorerService explorerService,
            ExplorerLaunchTracker explorerLaunchTracker,
            ExplorerWindowMonitorCoordinator monitorCoordinator,
            ExplorerWindowTrackingState windowTracking,
            TabPersistenceService tabPersistence,
            MemoryMaintenanceService memoryMaintenance,
            TimeSpan maxHiddenDuration)
        {
            _explorerService = explorerService;
            _explorerLaunchTracker = explorerLaunchTracker;
            _monitorCoordinator = monitorCoordinator;
            _windowTracking = windowTracking;
            _tabPersistence = tabPersistence;
            _memoryMaintenance = memoryMaintenance;
            _maxHiddenDuration = maxHiddenDuration;
        }

        public List<ExplorerWindowProcessRequest> RunCycle(
            Func<TabBarViewModel> findValidTarget,
            DateTime nowUtc)
        {
            _explorerLaunchTracker.UpdateForegroundState();

            List<IntPtr> explorerWindows = _explorerService.FindExplorerWindows();
            List<ExplorerWindowProcessRequest> requests = _monitorCoordinator.PrepareProcessRequests(explorerWindows, findValidTarget);

            TabBarViewModel saveTarget = findValidTarget != null ? findValidTarget() : null;
            if (saveTarget != null)
            {
                _tabPersistence.SaveTabsIfChanged(saveTarget);
            }

            List<IntPtr> hiddenToRestore = _windowTracking.GetHiddenWindowsToRestore(_maxHiddenDuration, nowUtc);
            for (int i = 0; i < hiddenToRestore.Count; i++)
            {
                _windowTracking.RestoreHiddenWindow(hiddenToRestore[i]);
            }

            if (_memoryMaintenance != null)
            {
                _memoryMaintenance.PerformIfDue();
            }

            return requests;
        }
    }
}
