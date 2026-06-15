using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerLaunchTrackerTests
    {
        [TestMethod]
        public void IsForegroundRelatedWindow_ReturnsTrue_WhenForegroundRootMatches()
        {
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                new ExplorerWindowTrackingState(),
                hwnd => false,
                hwnd => false,
                () => (IntPtr)200,
                hwnd => string.Empty,
                (hwnd, flags) => hwnd == (IntPtr)200 ? (IntPtr)100 : hwnd,
                hwnd => true);

            Assert.IsTrue(tracker.IsForegroundRelatedWindow((IntPtr)100));
            Assert.IsFalse(tracker.IsForegroundRelatedWindow((IntPtr)300));
        }

        [TestMethod]
        public void UpdateForegroundState_DoesNotRegisterControlPanelLaunchCandidate_OnWindowTransitionAlone()
        {
            ExplorerWindowTrackingState windowTracking = new ExplorerWindowTrackingState();
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                windowTracking,
                hwnd => hwnd == (IntPtr)10,
                hwnd => false,
                () => IntPtr.Zero,
                hwnd => hwnd == (IntPtr)20 ? "CabinetWClass" : string.Empty,
                (hwnd, flags) => hwnd,
                hwnd => hwnd == (IntPtr)20);

            tracker.UpdateForegroundState((IntPtr)10, "CabinetWClass");
            tracker.UpdateForegroundState((IntPtr)20, "CabinetWClass");

            Assert.IsFalse(windowTracking.ControlPanelTabLaunchCandidates.Contains((IntPtr)20));
        }

        [TestMethod]
        public void WasForegroundRelatedWindow_ReturnsTrue_For_PreviousForegroundRoot()
        {
            IntPtr currentForeground = (IntPtr)20;
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                new ExplorerWindowTrackingState(),
                hwnd => false,
                hwnd => false,
                () => currentForeground,
                hwnd => "CabinetWClass",
                (hwnd, flags) => hwnd,
                hwnd => true);

            tracker.UpdateForegroundState((IntPtr)10, "CabinetWClass");
            tracker.UpdateForegroundState((IntPtr)20, "CabinetWClass");

            Assert.IsTrue(tracker.WasForegroundRelatedWindow((IntPtr)10));
            Assert.IsFalse(tracker.WasForegroundRelatedWindow((IntPtr)30));
        }

        [TestMethod]
        public void WasManagedControlPanelLaunchSource_ReturnsTrue_For_PreviousManagedControlPanelWindow()
        {
            ExplorerWindowTrackingState windowTracking = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                foregroundTracker,
                windowTracking,
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)10; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return (IntPtr)20; },
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            tracker.UpdateForegroundState((IntPtr)10, "CabinetWClass");
            tracker.UpdateForegroundState((IntPtr)20, "CabinetWClass");

            Assert.IsTrue(tracker.WasManagedControlPanelLaunchSource());
        }

        [TestMethod]
        public void WasManagedControlPanelLaunchSource_ReturnsTrue_For_LastManagedControlPanelWindow()
        {
            ExplorerWindowTrackingState windowTracking = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                foregroundTracker,
                windowTracking,
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)10; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return (IntPtr)20; },
                delegate (IntPtr hwnd) { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });

            tracker.UpdateForegroundState((IntPtr)10, "CabinetWClass");

            Assert.IsTrue(tracker.WasManagedControlPanelLaunchSource());
        }

        [TestMethod]
        public void TryRegisterDesktopLaunchCandidate_AddsInteractiveCandidate_AfterDesktopTransition()
        {
            ExplorerWindowTrackingState windowTracking = new ExplorerWindowTrackingState();
            ExplorerLaunchTracker tracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                windowTracking,
                hwnd => false,
                hwnd => false,
                () => IntPtr.Zero,
                hwnd => string.Empty,
                (hwnd, flags) => hwnd,
                hwnd => true);

            tracker.UpdateForegroundState((IntPtr)10, "SHELLDLL_DefView");
            tracker.UpdateForegroundState((IntPtr)20, "CabinetWClass");

            bool result = tracker.TryRegisterDesktopLaunchCandidate((IntPtr)30);

            Assert.IsTrue(result);
            Assert.IsTrue(windowTracking.DesktopLaunchCandidates.Contains((IntPtr)30));
            Assert.IsTrue(windowTracking.DesktopInteractiveLaunchCandidates.Contains((IntPtr)30));
        }
    }
}
