using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
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
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { movedOffscreen = true; },
                null,
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
        public void HandleShowEvent_Ignores_ExplicitIndependentLaunchRequest()
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
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { movedOffscreen = true; },
                null,
                delegate { return new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc); });

            trackingState.RegisterExplicitIndependentLaunchRequest();

            coordinator.HandleShowEvent(
                (IntPtr)210,
                delegate { return new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService()); },
                delegate (TabBarViewModel vm) { return true; });

            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)210));
            Assert.IsTrue(trackingState.ExplicitIndependentLaunchWindows.Contains((IntPtr)210));
            Assert.IsFalse(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)210));
            Assert.IsFalse(trackingState.DesktopLaunchCandidates.Contains((IntPtr)210));
            Assert.IsFalse(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)210));
            Assert.IsFalse(movedOffscreen);
        }

        [TestMethod]
        public void HandleShowEvent_HidesAndSkips_InternalHostSwitchLaunchWindow()
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
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { movedOffscreen = true; },
                null,
                delegate { return new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc); });

            trackingState.RegisterInternalHostSwitchLaunchRequest();

            coordinator.HandleShowEvent(
                (IntPtr)240,
                delegate { return null; },
                delegate (TabBarViewModel vm) { return false; });

            Assert.IsTrue(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)240));
            Assert.IsTrue(trackingState.InternalHostSwitchLaunchWindows.Contains((IntPtr)240));
            Assert.IsTrue(movedOffscreen);

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)240 },
                delegate { return new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService()); });

            Assert.AreEqual(0, requests.Count);
            Assert.IsFalse(trackingState.ProcessingExplorerWindows.Contains((IntPtr)240));
        }

        [TestMethod]
        public void HandleShowEvent_RegistersControlPanelCandidate_WhenManagedWindowWasPreviousForeground()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            IntPtr currentForeground = (IntPtr)200;
            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                foregroundTracker,
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return currentForeground; },
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                foregroundTracker,
                launchTracker,
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc); });

            foregroundTracker.Update((IntPtr)50, "SHELLDLL_DefView");
            foregroundTracker.Update((IntPtr)100, "CabinetWClass");
            foregroundTracker.Update((IntPtr)200, "CabinetWClass");

            TabBarViewModel validTarget = new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService());

            coordinator.HandleShowEvent(
                (IntPtr)220,
                delegate { return validTarget; },
                delegate (TabBarViewModel vm) { return true; });

            Assert.IsTrue(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)220));
        }

        [TestMethod]
        public void HandleShowEvent_DoesNotRegisterControlPanelCandidate_WithoutDesktopForeground()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            IntPtr currentForeground = (IntPtr)200;
            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                foregroundTracker,
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return currentForeground; },
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                foregroundTracker,
                launchTracker,
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc); });

            foregroundTracker.Update((IntPtr)100, "CabinetWClass");
            foregroundTracker.Update((IntPtr)200, "CabinetWClass");

            TabBarViewModel validTarget = new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService());

            coordinator.HandleShowEvent(
                (IntPtr)230,
                delegate { return validTarget; },
                delegate (TabBarViewModel vm) { return true; });

            Assert.IsFalse(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)230));
            Assert.IsFalse(trackingState.DesktopLaunchCandidates.Contains((IntPtr)230));
            Assert.IsFalse(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)230));
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
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                null,
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
        public void PrepareProcessRequests_DoesNotReevaluateIgnoredWindow_WhenOnlyControlPanelCandidateArrivesLater()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.IgnoredWindows.Add((IntPtr)11);
            trackingState.ControlPanelTabLaunchCandidates.Add((IntPtr)11);

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(),
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)11 },
                delegate { return new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService()); });

            Assert.AreEqual(0, requests.Count);
            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)11));
            Assert.IsFalse(trackingState.ProcessingExplorerWindows.Contains((IntPtr)11));
        }

        [TestMethod]
        public void PrepareProcessRequests_DoesNotReevaluate_ExplicitIndependentLaunchWindow_WhenNoValidTarget()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.IgnoreExplicitIndependentLaunchWindow((IntPtr)12);

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(),
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)12 },
                delegate { return null; });

            Assert.AreEqual(0, requests.Count);
            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)12));
            Assert.IsTrue(trackingState.ExplicitIndependentLaunchWindows.Contains((IntPtr)12));
            Assert.IsFalse(trackingState.ProcessingExplorerWindows.Contains((IntPtr)12));
        }

        [TestMethod]
        public void PrepareProcessRequests_SkipsParkedExplorerOriginValue()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)20, (IntPtr)10);

            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(),
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)10, (IntPtr)20 },
                delegate { return null; });

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual((IntPtr)20, requests[0].ExplorerHwnd);
            Assert.IsFalse(trackingState.ProcessingExplorerWindows.Contains((IntPtr)10));
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
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                null,
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)20 },
                delegate { return null; });

            Assert.AreEqual(0, requests.Count);
        }

        [TestMethod]
        public void HandleShowEvent_UsesRootWindow_WhenChildWindowShowIsReported()
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

            IntPtr movedHwnd = IntPtr.Zero;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                foregroundTracker,
                launchTracker,
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)300)
                    {
                        return "CabinetWClass";
                    }

                    return "DirectUIHWND";
                },
                delegate (IntPtr hwnd, uint flags)
                {
                    if (hwnd == (IntPtr)301)
                    {
                        return (IntPtr)300;
                    }

                    return hwnd;
                },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT(); },
                delegate (IntPtr hwnd) { movedHwnd = hwnd; },
                null,
                delegate { return new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc); });

            foregroundTracker.Update((IntPtr)50, "SHELLDLL_DefView");
            foregroundTracker.Update((IntPtr)100, "CabinetWClass");

            TabBarViewModel validTarget = new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService());

            coordinator.HandleShowEvent(
                (IntPtr)301,
                delegate { return validTarget; },
                delegate (TabBarViewModel vm) { return true; });

            Assert.IsTrue(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)300));
            Assert.IsTrue(trackingState.DesktopLaunchCandidates.Contains((IntPtr)300));
            Assert.IsTrue(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)300));
            Assert.AreEqual((IntPtr)300, movedHwnd);
            Assert.IsFalse(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)301));
        }

        [TestMethod]
        public void PrepareProcessRequests_PersistsState_BeforeClosing_InvalidTabBarWindow()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            MockExplorerService explorerService = new MockExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)500, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"C:\Desktop", 1, false);

            TabBarWindow window = new TabBarWindow();
            window.DataContext = viewModel;
            tabBars.Add((IntPtr)500, window);

            int persistedCount = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                delegate (TabBarViewModel vm)
                {
                    if (ReferenceEquals(vm, viewModel))
                    {
                        persistedCount++;
                    }
                },
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr>(),
                delegate { return null; });

            Assert.AreEqual(0, requests.Count);
            Assert.AreEqual(1, persistedCount);
            Assert.IsFalse(tabBars.Contains((IntPtr)500));
        }

        [TestMethod]
        public void PrepareProcessRequests_DoesNotClose_TabBar_WhenRegistryKeyIsStaleButCurrentHostIsEnumerated()
        {
            TabBarRegistry tabBars = new TabBarRegistry();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            MockExplorerService explorerService = new MockExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)500, new MockUserSettings(), explorerService);
            viewModel.SetExplorerHwnd((IntPtr)600);

            TabBarWindow window = new TabBarWindow();
            window.DataContext = viewModel;

            tabBars.Add((IntPtr)500, window);

            int persistedCount = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                tabBars,
                trackingState,
                new DesktopForegroundTracker(),
                CreateLaunchTracker(trackingState),
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return null; },
                delegate (IntPtr hwnd) { },
                delegate (TabBarViewModel vm)
                {
                    if (ReferenceEquals(vm, viewModel))
                    {
                        persistedCount++;
                    }
                },
                delegate { return DateTime.UtcNow; });

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)600 },
                delegate { return viewModel; });

            Assert.AreEqual(1, requests.Count);
            Assert.AreEqual((IntPtr)600, requests[0].ExplorerHwnd);
            Assert.AreSame(viewModel, requests[0].ValidTarget);
            Assert.AreEqual(0, persistedCount);
            Assert.IsTrue(tabBars.Contains((IntPtr)500));
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
