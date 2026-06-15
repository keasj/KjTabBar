using System;
using System.IO;
using KjTabBar.Helpers;
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

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)200, targetViewModel, @"C:\Work", false, false, delegate (IntPtr hwnd) { });

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
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            };
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
                true,
                delegate (IntPtr hwnd) { ignoredHwnd = hwnd; });

            Assert.IsFalse(absorbed);
            Assert.AreEqual((IntPtr)300, ignoredHwnd);
            Assert.AreEqual(1, targetViewModel.Tabs.Count);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_ReusesExistingControlPanelTab_WhenEquivalentTabExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

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
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);
            targetViewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 2, true);
            targetViewModel.SelectTab(targetViewModel.Tabs[1]);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(3, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual((IntPtr)201, foregroundHwnd);
            Assert.AreEqual((IntPtr)201, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_AddsControlPanelTab_WhenEquivalentTabDoesNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

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
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)202, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(3, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[2].Path);
            Assert.AreEqual((IntPtr)202, foregroundHwnd);
            Assert.AreEqual((IntPtr)202, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_ReusesControlPanelTab_AndAlignsNewWindowToPreviousRect()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            IntPtr movedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect)
                {
                    movedHwnd = hwnd;
                    movedRect = rect;
                },
                delegate (TabBarViewModel viewModel, IntPtr hwnd)
                {
                    viewModel.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)205, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual((IntPtr)205, movedHwnd);
            Assert.AreEqual(0, movedRect.Left);
            Assert.AreEqual(0, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_PreservesBackgroundControlPanelTab_WhenActiveTabIsNotControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

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
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);
            targetViewModel.InsertTabWithPath(@"C:\Work", 2, false);
            targetViewModel.SelectTab(targetViewModel.Tabs[2]);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)203, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(4, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(@"C:\Work", targetViewModel.Tabs[2].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[3].Path);
            Assert.AreEqual((IntPtr)203, foregroundHwnd);
            Assert.AreEqual((IntPtr)203, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_PreservesControlPanelRoot_WhenPathNormalizesToControlPanelItem()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };
            explorerService.NormalizeKnownPathFunc = delegate (string path)
            {
                return path == @"C:\Windows\System32\powercfg.cpl" ? explorerService.PowerOptionsPath : path;
            };

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
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);

            bool absorbed = service.AbsorbExplorerWindow(
                (IntPtr)204,
                targetViewModel,
                @"C:\Windows\System32\powercfg.cpl",
                false,
                false,
                delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(3, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[2].Path);
            Assert.AreEqual((IntPtr)204, foregroundHwnd);
            Assert.AreEqual((IntPtr)204, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_CreatesControlPanelTab_WhenNoControlPanelTabExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

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
            Assert.AreEqual(1, targetViewModel.Tabs.Count);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual((IntPtr)201, foregroundHwnd);
            Assert.AreEqual((IntPtr)201, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_DoesNotLeaveTemporaryControlPanelTab_WhenRebindFails()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (TabBarViewModel viewModel, IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_PreservesSavedTabs_AndAddsExplicitInitialPath()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA", @"C:\SavedB" });

                MockExplorerService explorerService = new MockExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)400, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, @"C:\DesktopLaunch", true);

                Assert.AreEqual(3, viewModel.Tabs.Count);
                Assert.AreEqual(@"C:\SavedA", viewModel.Tabs[0].Path);
                Assert.AreEqual(@"C:\SavedB", viewModel.Tabs[1].Path);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.ActiveTab.Path);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.Tabs[2].Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_UsesExplicitInitialPath_WhenSavedTabsDoNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
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

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)400, new MockUserSettings(), explorerService);

            service.InitializeTabsForNewWindow(viewModel, @"C:\DesktopLaunch", true);

            Assert.AreEqual(2, viewModel.Tabs.Count);
            Assert.AreEqual(@"C:\DesktopLaunch", viewModel.ActiveTab.Path);
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_AddsControlPanelInitialPath_WhenSavedTabsExist()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".cp.tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA" });

                MockExplorerService explorerService = new MockExplorerService();
                explorerService.IsControlPanelPathFunc = delegate (string path)
                {
                    return path == explorerService.PowerOptionsPath;
                };

                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)401, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, explorerService.PowerOptionsPath, true);

                Assert.AreEqual(2, viewModel.Tabs.Count);
                Assert.AreEqual(explorerService.PowerOptionsPath, viewModel.ActiveTab.Path);
                Assert.AreEqual(explorerService.PowerOptionsPath, viewModel.Tabs[1].Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_PreservesCurrentExplorerPath_WhenSavedTabsExist_AndInitialPathOnlyIsFalse()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".existing-window.tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA", @"C:\SavedB" });

                MockExplorerService explorerService = new MockExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)402, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, @"E:\working", false);

                Assert.AreEqual(3, viewModel.Tabs.Count);
                Assert.AreEqual(@"C:\SavedA", viewModel.Tabs[0].Path);
                Assert.AreEqual(@"C:\SavedB", viewModel.Tabs[1].Path);
                Assert.AreEqual(@"E:\working", viewModel.Tabs[2].Path);
                Assert.AreEqual(@"E:\working", viewModel.ActiveTab.Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }
    }
}
