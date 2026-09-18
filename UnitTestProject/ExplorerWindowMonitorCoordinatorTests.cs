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
        public void PrepareProcessRequests_EvaluatesParkedHostReshownFromDesktop()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            tracking.RememberParkedExplorerOrigin((IntPtr)20, (IntPtr)10);
            tracking.DesktopLaunchCandidates.Add((IntPtr)10);
            tracking.HiddenPendingAbsorb[(IntPtr)10] = DateTime.UtcNow;
            TabBarViewModel target = new TabBarViewModel((IntPtr)20, new MockUserSettings(), new MockExplorerService());
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => hwnd, hwnd => null, delegate { },
                null, () => DateTime.UtcNow);

            List<ExplorerWindowProcessRequest> requests = coordinator.PrepareProcessRequests(
                new List<IntPtr> { (IntPtr)10, (IntPtr)20 }, () => target);

            Assert.IsTrue(requests.Exists(request => request.ExplorerHwnd == (IntPtr)10),
                "A desktop-reshown parked host must not remain hidden and excluded from processing.");
        }

        [TestMethod]
        public void HandleShowEvent_RehidesRegisteredHostOnlyWhileControlPanelRestoreIsPending()
        {
            IntPtr host = (IntPtr)123;
            TabBarViewModel vm = new TabBarViewModel(host, new MockUserSettings(), new MockExplorerService());
            vm.WindowVisibility = System.Windows.Visibility.Hidden;
            vm.IsRestoringControlPanelHost = true;
            TabBarWindow window = new TabBarWindow();
            window.DataContext = vm;
            TabBarRegistry registry = new TabBarRegistry();
            registry.Add(host, window);
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => null);
            int hides = 0;
            int requests = 0;
            bool visible = true;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                registry, tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => host, hwnd => null,
                hwnd => Assert.Fail("Registered restore must not enter absorption again."),
                null, () => DateTime.UtcNow, hwnd => visible,
                hwnd => { Assert.AreEqual(host, hwnd); hides++; visible = false; });
            try
            {
                coordinator.HandleShowEvent(host, () => vm, null, () => requests++);
                Assert.AreEqual(1, hides, "Windows showed Home again after the restore had hidden it.");
                coordinator.HandleShowEvent(host, () => vm, null, () => requests++);
                Assert.AreEqual(1, hides, "A delayed notification for an already hidden host does nothing.");
                vm.IsRestoringControlPanelHost = false;
                visible = true;
                coordinator.HandleShowEvent(host, () => vm, null, () => requests++);
                Assert.AreEqual(1, hides, "Completed restoration must permit the host to be shown.");
                Assert.AreEqual(0, requests);
                Assert.AreEqual(0, tracking.HiddenPendingAbsorb.Count);
            }
            finally
            {
                registry.ClearAndCloseAll();
            }
        }

        [TestMethod]
        public void PrepareProcessRequests_RecoversVisibleFirstHostBeforeDelayedShowCallback()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => null);
            bool visible = false;
            int moves = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => hwnd,
                hwnd => new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 },
                hwnd => moves++, null, () => DateTime.UtcNow, hwnd => visible);
            IntPtr host = (IntPtr)123;
            List<IntPtr> windows = new List<IntPtr> { host };
            coordinator.HandleCreateEvent(host, () => null);
            Assert.AreEqual(0, coordinator.PrepareProcessRequests(windows, () => null).Count);

            visible = true; // Windows has shown it; its out-of-context callback has not arrived.
            Assert.AreEqual(1, coordinator.PrepareProcessRequests(windows, () => null).Count);
            Assert.AreEqual(2, moves, "Reapply offscreen placement before starting restoration.");
            Assert.AreEqual(100, tracking.HiddenOriginalRects[host].Left);
            int immediateRequests = 0;
            coordinator.HandleShowEvent(host, () => null, null, () => immediateRequests++);
            Assert.AreEqual(0, immediateRequests);
            Assert.AreEqual(0, coordinator.PrepareProcessRequests(windows, () => null).Count,
                "Late notifications must not start a duplicate restoration.");
        }

        [TestMethod]
        public void PrepareProcessRequests_DoesNotTakeVisibleInternalReplacementHost()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => null);
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => hwnd,
                hwnd => new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 },
                hwnd => { }, null, () => DateTime.UtcNow, hwnd => true);
            tracking.RegisterInternalHostSwitchLaunchRequest();
            coordinator.HandleCreateEvent((IntPtr)123, () => null);
            Assert.AreEqual(0, coordinator.PrepareProcessRequests(new List<IntPtr> { (IntPtr)123 }, () => null).Count);
            Assert.IsTrue(tracking.InternalHostSwitchLaunchWindows.Contains((IntPtr)123));
        }

        [TestMethod]
        public void CreateEvent_PreparesOffscreen_ButWaitsForShowBeforeProcessing()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            int moves = 0;
            int requests = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => hwnd,
                hwnd => new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 },
                hwnd => moves++, null, () => DateTime.UtcNow);
            coordinator.HandleCreateEvent((IntPtr)123, () => null);
            Assert.AreEqual(1, moves);
            Assert.AreEqual(0, coordinator.PrepareProcessRequests(new List<IntPtr> { (IntPtr)123 }, () => null).Count);
            coordinator.HandleShowEvent((IntPtr)123, () => null, null, () => requests++);
            Assert.AreEqual(2, moves);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(100, tracking.HiddenOriginalRects[(IntPtr)123].Left);
            Assert.AreEqual(1, coordinator.PrepareProcessRequests(new List<IntPtr> { (IntPtr)123 }, () => null).Count);
        }

        [TestMethod]
        public void CreateEvent_PreservesIndependentLaunch_AndConsumesInternalLaunchOnce()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            int moves = 0;
            int requests = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, new DesktopForegroundTracker(), CreateLaunchTracker(tracking),
                hwnd => "CabinetWClass", (hwnd, flags) => hwnd,
                hwnd => new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 },
                hwnd => moves++, null, () => DateTime.UtcNow);
            tracking.RegisterExplicitIndependentLaunchRequest();
            coordinator.HandleCreateEvent((IntPtr)123, () => null);
            Assert.AreEqual(0, moves);
            Assert.IsTrue(tracking.ExplicitIndependentLaunchWindows.Contains((IntPtr)123));
            tracking.RegisterInternalHostSwitchLaunchRequest();
            coordinator.HandleCreateEvent((IntPtr)456, () => null);
            Assert.IsFalse(tracking.HasPendingInternalHostSwitchLaunchRequest());
            coordinator.HandleShowEvent((IntPtr)456, () => null, null, () => requests++);
            Assert.AreEqual(2, moves);
            Assert.AreEqual(0, requests);
            Assert.IsTrue(tracking.InternalHostSwitchLaunchWindows.Contains((IntPtr)456));
        }

        [TestMethod]
        public void Registry_Keeps_Unloaded_TabBar_While_Live_Host_Is_Hidden()
        {
            System.Windows.Interop.HwndSourceParameters parameters =
                new System.Windows.Interop.HwndSourceParameters("HiddenRestoreTest");
            parameters.WindowStyle = 0;
            parameters.Width = 1;
            parameters.Height = 1;
            using (System.Windows.Interop.HwndSource host = new System.Windows.Interop.HwndSource(parameters))
            {
                TabBarViewModel vm = new TabBarViewModel(host.Handle, new MockUserSettings(), new MockExplorerService());
                vm.WindowVisibility = System.Windows.Visibility.Hidden;
                TabBarWindow window = new TabBarWindow();
                window.DataContext = vm;
                TabBarRegistry registry = new TabBarRegistry();
                registry.Add(host.Handle, window);
                try
                {
                    Assert.IsFalse(window.IsLoaded);
                    registry.RemoveInvalidWindows(new List<IntPtr>());
                    Assert.IsTrue(registry.Contains(host.Handle));
                    Assert.IsTrue(window.IsExplorerAlive());
                }
                finally
                {
                    registry.ClearAndCloseAll();
                }
            }
        }

        [TestMethod]
        public void HandleShowEvent_HidesFirstHostUntilRestorationAndRequestsImmediateCycle()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foreground = new DesktopForegroundTracker();
            ExplorerLaunchTracker launches = new ExplorerLaunchTracker(
                foreground, tracking,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return IntPtr.Zero; },
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });
            int moved = 0;
            int requested = 0;
            ExplorerWindowMonitorCoordinator coordinator = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, foreground, launches,
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)99 ? "OtherWindow" : "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 }; },
                delegate (IntPtr hwnd) { moved++; }, null,
                delegate { return DateTime.UtcNow; });
            Action request = delegate { requested++; };

            coordinator.HandleShowEvent((IntPtr)10, delegate { return null; }, null, request);
            Assert.AreEqual(1, requested);
            Assert.AreEqual(1, moved);
            Assert.AreEqual(1, tracking.HiddenPendingAbsorb.Count);

            tracking.HiddenPendingAbsorb.Clear();
            tracking.HiddenOriginalRects.Clear();
            tracking.ProcessingExplorerWindows.Add((IntPtr)10);
            tracking.AbsorbPathRetryCounts[(IntPtr)11] = 1;
            tracking.IgnoredWindows.Add((IntPtr)12);
            coordinator.HandleShowEvent((IntPtr)10, delegate { return null; }, null, request);
            coordinator.HandleShowEvent((IntPtr)11, delegate { return null; }, null, request);
            coordinator.HandleShowEvent((IntPtr)12, delegate { return null; }, null, request);
            coordinator.HandleShowEvent((IntPtr)99, delegate { return null; }, null, request);
            coordinator.HandleShowEvent(IntPtr.Zero, delegate { return null; }, null, request);
            Assert.AreEqual(1, requested);

            tracking.RegisterExplicitIndependentLaunchRequest();
            coordinator.HandleShowEvent((IntPtr)20, delegate { return null; }, null, request);
            Assert.IsTrue(tracking.ExplicitIndependentLaunchWindows.Contains((IntPtr)20));
            tracking.RegisterInternalHostSwitchLaunchRequest();
            coordinator.HandleShowEvent((IntPtr)21, delegate { return null; }, null, request);
            Assert.IsTrue(tracking.InternalHostSwitchLaunchWindows.Contains((IntPtr)21));
            Assert.AreEqual(1, requested);

            TabBarViewModel existing = new TabBarViewModel(
                (IntPtr)30, new MockUserSettings(), new MockExplorerService());
            coordinator.HandleShowEvent((IntPtr)31, delegate { return existing; }, null, request);
            Assert.AreEqual(1, requested, "Existing host absorption must retain its timing.");
        }

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
