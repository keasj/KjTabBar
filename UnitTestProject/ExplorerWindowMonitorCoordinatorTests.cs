using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowMonitorCoordinatorTests
    {
        [TestMethod]
        public void HandleShowEvent_HidesDesktopCandidateAndRegistersControlPanelCandidate()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                foregroundTracker,
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return (IntPtr)100; },
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            bool movedOffscreen = false;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                foregroundTracker,
                launchTracker,
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { movedOffscreen = true; },
                delegate { return new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc); });

            foregroundTracker.Update((IntPtr)50, "SHELLDLL_DefView");
            foregroundTracker.Update((IntPtr)100, "CabinetWClass");

            TabBarViewModel validTarget = new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService());

            coordinator.HandleShowEvent(
                (IntPtr)200,
                delegate { return validTarget; },
                delegate (TabBarViewModel vm) { return true; });

            Assert.IsTrue(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)200));
            Assert.IsTrue(trackingState.DesktopLaunchCandidates.Contains((IntPtr)200));
            Assert.IsTrue(trackingState.DesktopInteractiveLaunchCandidates.Contains((IntPtr)200));
            Assert.IsTrue(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)200));
            Assert.IsTrue(movedOffscreen);
        }

        [TestMethod]
        public void PrepareProcessRequests_ReevaluatesIgnoredWindow_WhenNoValidTarget()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.IgnoredWindows.Add((IntPtr)10);

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(),
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)10 },
                delegate { return null; });

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual((IntPtr)10, requests[0].ExplorerHwnd);
            Assert.IsFalse(trackingState.IgnoredWindows.Contains((IntPtr)10));
            Assert.IsTrue(trackingState.ProcessingExplorerWindows.Contains((IntPtr)10));
        }

        [TestMethod]
        public void PrepareProcessRequests_SkipsManagedAndAlreadyProcessingWindows()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.ProcessingExplorerWindows.Add((IntPtr)20);

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)20 },
                delegate { return null; });

            Assert.AreEqual(0, requests.Count);
        }

        private static ExplorerLaunchTracker CreateLaunchTracker(ExplorerWindowTrackingState trackingState)
        {
            return new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return IntPtr.Zero; },
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });
        }
    }
}
