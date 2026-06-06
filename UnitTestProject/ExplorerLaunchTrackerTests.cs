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
        public void UpdateForegroundState_RegistersControlPanelLaunchCandidate_OnWindowTransition()
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

            Assert.IsTrue(windowTracking.ControlPanelTabLaunchCandidates.Contains((IntPtr)20));
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
