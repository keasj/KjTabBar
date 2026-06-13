using System;
using System.Collections.Generic;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class AppMonitorCycleCoordinatorTests
    {
        [TestMethod]
        public void RunCycle_SavesTabsAndRestoresExpiredHiddenWindows()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.HiddenPendingAbsorb[(IntPtr)10] = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return IntPtr.Zero; },
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            ExplorerWindowMonitorCoordinator monitorCoordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(),
                trackingState,
                new DesktopForegroundTracker(),
                launchTracker,
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                delegate { return DateTime.UtcNow; });

            TabPersistenceService tabPersistence = TestTabPersistenceFactory.Create();
            MemoryMaintenanceService memoryMaintenance = new MemoryMaintenanceService(explorerService);
            AppMonitorCycleCoordinator coordinator = new AppMonitorCycleCoordinator(
                explorerService,
                launchTracker,
                monitorCoordinator,
                trackingState,
                tabPersistence,
                memoryMaintenance,
                TimeSpan.FromSeconds(2));

            TabBarViewModel viewModel = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorerService);

            List<ExplorerWindowProcessRequest> requests = coordinator.RunCycle(
                delegate { return viewModel; },
                new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc));

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual((IntPtr)10, requests[0].ExplorerHwnd);
            Assert.IsFalse(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)10));
        }
    }
}
