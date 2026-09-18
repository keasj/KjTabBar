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
        public void RequestImmediateCycle_DefersAndCoalescesNotifications_ThenAcceptsNextReopen()
        {
            AppMonitorCycleCoordinator coordinator = new AppMonitorCycleCoordinator(
                null, null, null, null, null, null, TimeSpan.FromSeconds(2));
            List<Action> queued = new List<Action>();
            int cycles = 0;
            Action runCycle = delegate
            {
                cycles++;
                coordinator.RequestImmediateCycle(queued.Add, delegate { Assert.Fail("Reentrant cycle"); });
            };

            coordinator.RequestImmediateCycle(queued.Add, runCycle);
            coordinator.RequestImmediateCycle(queued.Add, runCycle);
            Assert.AreEqual(0, cycles, "The show callback must not run the monitor inline.");
            Assert.AreEqual(1, queued.Count);
            queued[0]();
            Assert.AreEqual(1, cycles);
            Assert.AreEqual(1, queued.Count, "Notifications during the cycle must also coalesce.");

            coordinator.RequestImmediateCycle(queued.Add, runCycle);
            Assert.AreEqual(2, queued.Count);
            queued[1]();
            Assert.AreEqual(2, cycles);
        }

        [TestMethod]
        public void RequestImmediateCycle_RecoversAfterQueueOrCycleFailure()
        {
            AppMonitorCycleCoordinator coordinator = new AppMonitorCycleCoordinator(
                null, null, null, null, null, null, TimeSpan.FromSeconds(2));
            List<Action> queued = new List<Action>();
            try
            {
                coordinator.RequestImmediateCycle(
                    delegate (Action callback) { throw new InvalidOperationException("queue"); },
                    delegate { });
                Assert.Fail("Expected queue failure.");
            }
            catch (InvalidOperationException) { }

            coordinator.RequestImmediateCycle(
                queued.Add, delegate { throw new InvalidOperationException("cycle"); });
            Assert.AreEqual(1, queued.Count);
            try
            {
                queued[0]();
                Assert.Fail("Expected cycle failure.");
            }
            catch (InvalidOperationException) { }

            int cycles = 0;
            coordinator.RequestImmediateCycle(queued.Add, delegate { cycles++; });
            Assert.AreEqual(2, queued.Count);
            queued[1]();
            Assert.AreEqual(1, cycles);
        }

        [TestMethod]
        public void GetMonitorTimerInterval_Uses_Background_Polling_Interval()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(1), AppRuntimeCoordinator.GetMonitorTimerInterval());
        }

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
                null,
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
