using System;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowInteractionServiceTests
    {
        [TestMethod]
        public void GetDesktopVirtualPathFromWindowTitle_ReturnsMappedPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return "Control Panel"; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            string result = service.GetDesktopVirtualPathFromWindowTitle((IntPtr)1);

            Assert.AreEqual("Control Panel", result);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_InsertsTabAndMarksAbsorbed()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)200, targetViewModel, @"C:\Work", false, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(@"C:\Work", targetViewModel.ActiveTab.Path);
            Assert.AreEqual((IntPtr)100, foregroundHwnd);
            Assert.AreEqual((IntPtr)200, closedHwnd);
            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)200));
        }

        [TestMethod]
        public void AbsorbExplorerWindow_RejectsControlPanelPathAndCallsIgnore()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path) { return true; };
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            IntPtr ignoredHwnd = IntPtr.Zero;

            bool absorbed = service.AbsorbExplorerWindow(
                (IntPtr)300,
                targetViewModel,
                "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                false,
                delegate (IntPtr hwnd) { ignoredHwnd = hwnd; });

            Assert.IsFalse(absorbed);
            Assert.AreEqual((IntPtr)300, ignoredHwnd);
            Assert.AreEqual(1, targetViewModel.Tabs.Count);
        }
    }
}
