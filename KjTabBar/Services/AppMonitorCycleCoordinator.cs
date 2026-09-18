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
        private bool _immediateCyclePending;

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

        // Called on the UI thread. Coalesce show events and avoid running inside the hook.
        public void RequestImmediateCycle(Action<Action> enqueue, Action runCycle)
        {
            if (_immediateCyclePending) return;

            _immediateCyclePending = true;
            try
            {
                enqueue(delegate
                {
                    try
                    {
                        runCycle();
                    }
                    finally
                    {
                        _immediateCyclePending = false;
                    }
                });
            }
            catch
            {
                _immediateCyclePending = false;
                throw;
            }
        }

        public List<ExplorerWindowProcessRequest> RunCycle(
            Func<TabBarViewModel> findValidTarget,
            DateTime nowUtc)
        {
            System.Diagnostics.Stopwatch cycleTimer = AppLogger.StartDiagnosticTiming();
            AppLogger.LogDiagnosticTiming("Cycle.Begin", IntPtr.Zero, cycleTimer);
            _explorerLaunchTracker.UpdateForegroundState();

            List<IntPtr> explorerWindows = _explorerService.FindExplorerWindows();
            AppLogger.LogDiagnosticTiming("Cycle.Enumerated", IntPtr.Zero, cycleTimer);
            List<ExplorerWindowProcessRequest> requests = _monitorCoordinator.PrepareProcessRequests(explorerWindows, findValidTarget);
            AppLogger.LogDiagnosticTiming("Cycle.Prepared", IntPtr.Zero, cycleTimer);

            TabBarViewModel saveTarget = findValidTarget != null ? findValidTarget() : null;
            AppLogger.LogDiagnosticTiming("Cycle.TargetFound", IntPtr.Zero, cycleTimer);
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

            AppLogger.LogDiagnosticTiming("Cycle.Completed", IntPtr.Zero, cycleTimer);
            return requests;
        }
    }
}
