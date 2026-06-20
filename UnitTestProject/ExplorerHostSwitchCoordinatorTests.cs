using System;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerHostSwitchCoordinatorTests
    {
        [TestMethod]
        public void PrepareForPath_RestoresParkedExplorerHost_ForNormalPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)100)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)100, reboundHwnd);
            Assert.AreEqual((IntPtr)100, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, movedRect.Left);
            Assert.AreEqual(0, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)100]);
        }

        [TestMethod]
        public void PrepareForPath_DoesNothing_ForControlPanelPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.IsTrue(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotSwitchToFreshExplorerHost_WhenCurrentPathIsNormalButActiveTabIsControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                new ExplorerWindowTrackingState(),
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Users\Test");

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.IsNull(explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotQueryCurrentPath_WhenActiveTabAlreadyMatchesNormalTarget()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            int getCurrentPathCallCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                getCurrentPathCallCount++;
                return @"C:\Initial";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"C:\Work", 1, false);
            viewModel.SelectTab(viewModel.Tabs[1]);
            getCurrentPathCallCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                Assert.Fail("GetCurrentPath should not be called when the active tab already indicates a normal host.");
                return null;
            };

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Users\Test");

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, getCurrentPathCallCount);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesToFreshExplorerHost_WhenCurrentHostIsControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(@"C:\Work", explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, movedRect.Left);
            Assert.AreEqual(0, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)300]);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesToFreshExplorerHost_WhenTargetPathIsControlPanelAndNoParkedOrigin()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)300)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(explorerService.PowerOptionsPath, explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)300]);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesBackToParkedControlPanelHost_ForControlPanelPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)300, (IntPtr)200);

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)300, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[0]);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)200, reboundHwnd);
            Assert.AreEqual((IntPtr)200, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, movedRect.Left);
            Assert.AreEqual(0, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.AreEqual((IntPtr)300, trackingState.ParkedExplorerOrigins[(IntPtr)200]);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotQueryParkedHostPath_WhenParkedOriginExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

            int currentHostPathQueryCount = 0;
            int parkedHostPathQueryCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)300)
                {
                    currentHostPathQueryCount++;
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)200)
                {
                    parkedHostPathQueryCount++;
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)300, (IntPtr)200);

            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)300, new MockUserSettings(), explorerService);
            currentHostPathQueryCount = 0;
            parkedHostPathQueryCount = 0;

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);

            Assert.IsTrue(prepared);
            Assert.AreEqual(1, currentHostPathQueryCount);
            Assert.AreEqual(0, parkedHostPathQueryCount);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }
        [TestMethod]
        public void PrepareForPath_CancelsInternalHostSwitchLaunchRequest_WhenNewWindowIsNotFound()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };
            explorerService.FindExplorerWindowsFunc = delegate
            {
                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd) { return true; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");

            Assert.IsFalse(prepared);
            Assert.IsFalse(trackingState.TryConsumeInternalHostSwitchLaunchRequest());
        }
    }
}
