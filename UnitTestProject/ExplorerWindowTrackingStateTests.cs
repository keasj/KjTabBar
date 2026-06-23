using System;
using System.Collections.Generic;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowTrackingStateTests
    {
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
