using System;
using System.Collections.Generic;
using KjTabBar.Models;
using KjTabBar.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowTrackingStateTests
    {
        [TestMethod]
        public void RestoreNormalPositionBeforeShow_RestoresOffscreenNormalWindow_AndRetriesFailure()
        {
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 483, Top = 208, Right = 1924, Bottom = 898 };
            NativeMethods.WINDOWPLACEMENT current = new NativeMethods.WINDOWPLACEMENT { showCmd = 1, rcNormalPosition = original };
            int writes = 0;
            bool succeed = false;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => current,
                (hwnd, placement) =>
                {
                    Assert.AreEqual(original, placement.rcNormalPosition);
                    Assert.AreEqual((uint)1, placement.showCmd);
                    writes++;
                    return succeed;
                });
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            current.rcNormalPosition = new NativeMethods.RECT { Left = -32000, Top = -32000, Right = -30559, Bottom = -31310 };
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(1, writes);
            succeed = true;
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(2, writes);
        }

        [TestMethod]
        public void DeferredOriginBounds_SurviveHiddenEnumeration_AndExpireAfterClose()
        {
            bool alive = true;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                window => alive, window => { }, window => { });
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1000, Bottom = 800 };
            tracking.DeferredOriginRestoreRects[(IntPtr)100] = original;
            tracking.CleanupClosedWindows(new List<IntPtr>());
            Assert.AreEqual(original, tracking.DeferredOriginRestoreRects[(IntPtr)100]);
            alive = false;
            tracking.CleanupClosedWindows(new List<IntPtr>());
            Assert.AreEqual(0, tracking.DeferredOriginRestoreRects.Count);
        }
        [TestMethod]
        public void PendingPlacement_SurvivesHiddenHostMissingFromVisibleEnumeration()
        {
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                window => true, window => { }, window => { }, window => null,
                (window, placement) => { writes++; return true; });
            tracking.StageRestorePlacement((IntPtr)100, new NativeMethods.WINDOWPLACEMENT
                { showCmd = 1, rcNormalPosition = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1000, Bottom = 800 } });
            tracking.CleanupClosedWindows(new List<IntPtr>());
            tracking.TransferRestorePlacement((IntPtr)100, (IntPtr)200);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)200);
            Assert.AreEqual(1, writes);
        }

        [TestMethod]
        public void SavedPlacement_ExpiresAndDoesNotLeakIntoNextReopen()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            NativeMethods.RECT rect = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1000, Bottom = 800 };
            NativeMethods.WINDOWPLACEMENT saved = new NativeMethods.WINDOWPLACEMENT { showCmd = 3, rcNormalPosition = rect };
            DateTime now = DateTime.UtcNow;
            tracking.RememberRecentClosedManagedExplorerRect(rect, now.AddSeconds(-11), saved);
            NativeMethods.RECT result;
            NativeMethods.WINDOWPLACEMENT? placement;
            Assert.IsFalse(tracking.TryTakeRecentClosedManagedExplorerRect(now, out result, out placement));
            Assert.IsFalse(placement.HasValue);
            tracking.RememberRecentClosedManagedExplorerRect(rect, now);
            Assert.IsTrue(tracking.TryTakeRecentClosedManagedExplorerRect(now, out result, out placement));
            Assert.IsFalse(placement.HasValue);
        }

        [TestMethod]
        public void PendingPlacement_TransfersOnceAndRetriesFailedApplication()
        {
            int writes = 0;
            bool succeed = false;
            NativeMethods.RECT normal = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1000, Bottom = 800 };
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                window => true, window => { }, window => { }, window => null,
                (window, placement) => { Assert.AreEqual((IntPtr)200, window); Assert.AreEqual(normal, placement.rcNormalPosition); writes++; return succeed; });
            tracking.StageRestorePlacement((IntPtr)100, new NativeMethods.WINDOWPLACEMENT { showCmd = 3, rcNormalPosition = normal });
            tracking.TransferRestorePlacement((IntPtr)100, (IntPtr)200);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(0, writes);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)200);
            succeed = true;
            tracking.RestoreNormalPositionBeforeShow((IntPtr)200);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)200);
            Assert.AreEqual(2, writes);
        }

        [TestMethod]
        public void PendingPlacement_DropsClosedWindowAndRejectsOffscreenSnapshot()
        {
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                window => false, window => { }, window => { }, window => null,
                (window, placement) => { writes++; return true; });
            NativeMethods.WINDOWPLACEMENT saved = new NativeMethods.WINDOWPLACEMENT
                { showCmd = 1, rcNormalPosition = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1000, Bottom = 800 } };
            tracking.StageRestorePlacement((IntPtr)100, saved);
            tracking.CleanupClosedWindows(new List<IntPtr>());
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            saved.rcNormalPosition = new NativeMethods.RECT { Left = -32000, Top = -32000, Right = -31000, Bottom = -31300 };
            tracking.StageRestorePlacement((IntPtr)200, saved);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)200);
            Assert.AreEqual(0, writes);
        }

        [TestMethod]
        public void GetHiddenWindowsToRestore_KeepsInternalHostOffscreenDuringLaunchGracePeriod()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => null);
            IntPtr internalHost = (IntPtr)100;
            IntPtr ordinaryHost = (IntPtr)200;
            DateTime hiddenAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
            NativeMethods.RECT rect = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1100, Bottom = 800 };
            tracking.AddHiddenPendingWindow(internalHost, rect, hiddenAt);
            tracking.MarkInternalHostSwitchLaunchWindow(internalHost);
            tracking.AddHiddenPendingWindow(ordinaryHost, rect, hiddenAt);

            CollectionAssert.AreEqual(new[] { ordinaryHost },
                tracking.GetHiddenWindowsToRestore(TimeSpan.FromSeconds(2), hiddenAt.AddSeconds(3)));
            Assert.IsFalse(tracking.GetHiddenWindowsToRestore(TimeSpan.FromSeconds(2), hiddenAt.AddSeconds(8)).Contains(internalHost));
            Assert.IsTrue(tracking.GetHiddenWindowsToRestore(TimeSpan.FromSeconds(2), hiddenAt.AddSeconds(11)).Contains(internalHost),
                "An abandoned internal host must still have a bounded recovery path.");
        }

        [TestMethod]
        public void GetHiddenWindowsToRestore_IgnoredInternalHostDoesNotWaitForLaunchTimeout()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                window => true, window => { }, window => { }, window => null);
            IntPtr hwnd = (IntPtr)100;
            DateTime hiddenAt = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
            tracking.AddHiddenPendingWindow(hwnd, new NativeMethods.RECT { Right = 1000, Bottom = 700 }, hiddenAt);
            tracking.MarkInternalHostSwitchLaunchWindow(hwnd);
            tracking.IgnoredWindows.Add(hwnd);
            CollectionAssert.AreEqual(new[] { hwnd },
                tracking.GetHiddenWindowsToRestore(TimeSpan.FromSeconds(2), hiddenAt.AddMilliseconds(100)));
        }

        [TestMethod]
        public void RestoreNormalPositionBeforeShow_PreservesMaximizedHostRestoreBounds()
        {
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 200, Top = 100, Right = 1200, Bottom = 800 };
            NativeMethods.WINDOWPLACEMENT current = new NativeMethods.WINDOWPLACEMENT { length = 44, showCmd = 1, rcNormalPosition = original };
            NativeMethods.WINDOWPLACEMENT restored = default(NativeMethods.WINDOWPLACEMENT);
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => current,
                (hwnd, placement) => { restored = placement; writes++; return true; });
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            current.rcNormalPosition = new NativeMethods.RECT { Left = -32000, Top = -32000, Right = -31000, Bottom = -31300 };
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            current.showCmd = 3;
            current.ptMaxPosition = new NativeMethods.POINT { X = -8, Y = -8 };

            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);

            Assert.AreEqual(1, writes);
            Assert.AreEqual(original, restored.rcNormalPosition);
            Assert.AreEqual((uint)3, restored.showCmd);
            Assert.AreEqual(current.ptMaxPosition, restored.ptMaxPosition);
        }

        [TestMethod]
        public void RestoreNormalPositionBeforeShow_DoesNotOverwriteValidNewPosition()
        {
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 200, Top = 100, Right = 1200, Bottom = 800 };
            NativeMethods.WINDOWPLACEMENT current = new NativeMethods.WINDOWPLACEMENT { showCmd = 1, rcNormalPosition = original };
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => current,
                (hwnd, placement) => { writes++; return true; });
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            current.showCmd = 3;
            current.rcNormalPosition = new NativeMethods.RECT { Left = 300, Top = 200, Right = 1500, Bottom = 900 };
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(0, writes);
        }

        [TestMethod]
        public void RestoreNormalPositionBeforeShow_DropsClosedWindowCapture()
        {
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 200, Top = 100, Right = 1200, Bottom = 800 };
            NativeMethods.WINDOWPLACEMENT current = new NativeMethods.WINDOWPLACEMENT { showCmd = 1, rcNormalPosition = original };
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => current,
                (hwnd, placement) => { writes++; return true; });
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            tracking.CleanupClosedWindows(new List<IntPtr>());
            current.showCmd = 3;
            current.rcNormalPosition = new NativeMethods.RECT { Left = -32000, Top = -32000, Right = -31000, Bottom = -31300 };
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(0, writes);
        }

        [TestMethod]
        public void RestoreAllHiddenWindows_RestoresParkedExplorerWindows_ByDefault()
        {
            List<IntPtr> shownWindows = new List<IntPtr>();
            List<IntPtr> closedWindows = new List<IntPtr>();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState(
                delegate (IntPtr hwnd) { return true; },
                delegate (IntPtr hwnd) { shownWindows.Add(hwnd); },
                delegate (IntPtr hwnd) { closedWindows.Add(hwnd); });

            trackingState.RememberParkedExplorerOrigin((IntPtr)100, (IntPtr)200);
            trackingState.RestoreAllHiddenWindows();

            CollectionAssert.AreEqual(new[] { (IntPtr)200 }, shownWindows);
            Assert.AreEqual(0, closedWindows.Count);
            Assert.AreEqual(0, trackingState.ParkedExplorerOrigins.Count);
        }

        [TestMethod]
        public void CloseParkedExplorerOrigin_ClosesAndClearsCascadingOrigins_WhenChainExists()
        {
            List<IntPtr> shownWindows = new List<IntPtr>();
            List<IntPtr> closedWindows = new List<IntPtr>();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState(
                delegate (IntPtr hwnd) { return true; },
                delegate (IntPtr hwnd) { shownWindows.Add(hwnd); },
                delegate (IntPtr hwnd) { closedWindows.Add(hwnd); });

            trackingState.RememberParkedExplorerOrigin((IntPtr)100, (IntPtr)200);
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)300);

            trackingState.CloseParkedExplorerOrigin((IntPtr)100);

            CollectionAssert.AreEqual(new[] { (IntPtr)200, (IntPtr)300 }, closedWindows);
            Assert.AreEqual(0, trackingState.ParkedExplorerOrigins.Count);
        }
    }
}
