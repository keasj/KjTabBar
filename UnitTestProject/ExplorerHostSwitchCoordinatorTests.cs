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
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 30,
                            Top = 40,
                            Right = 830,
                            Bottom = 640
                        };
                    }

                    return null;
                },
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
            Assert.AreEqual(30, movedRect.Left);
            Assert.AreEqual(40, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)100]);
        }

        [TestMethod]
        public void CompletePendingReveal_DoesNotMoveParkedHostToOffscreenCurrentRect()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                return hwnd == (IntPtr)200 ? explorerService.PowerOptionsPath : @"C:\Work";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            IntPtr shownHwnd = IntPtr.Zero;
            int moveCallCount = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { moveCallCount++; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = -32000,
                            Top = -32000,
                            Right = -31839,
                            Bottom = -31757
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)100, shownHwnd);
            Assert.AreEqual(0, moveCallCount);
            Assert.AreEqual((IntPtr)100, viewModel.ExplorerHwnd);
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
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)300)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 50,
                            Top = 60,
                            Right = 850,
                            Bottom = 660
                        };
                    }

                    return null;
                },
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
            Assert.AreEqual(0, movedRect.Width);
            Assert.AreEqual(0, movedRect.Height);
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)300]);
        }

        [TestMethod]
        public void CompletePendingReveal_RestoresCurrentHostRect_ForFreshHostHiddenOffscreen()
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
            trackingState.HiddenPendingAbsorb[(IntPtr)300] = DateTime.UtcNow;
            trackingState.HiddenOriginalRects[(IntPtr)300] = new NativeMethods.RECT
            {
                Left = 123,
                Top = 234,
                Right = 923,
                Bottom = 834
            };

            IntPtr shownHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 10,
                            Top = 20,
                            Right = 810,
                            Bottom = 620
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(10, movedRect.Left);
            Assert.AreEqual(20, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
        }

        [TestMethod]
        public void CompletePendingReveal_FallsBackToHiddenRect_WhenCurrentHostRectUnavailable()
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
            trackingState.HiddenPendingAbsorb[(IntPtr)300] = DateTime.UtcNow;
            trackingState.HiddenOriginalRects[(IntPtr)300] = new NativeMethods.RECT
            {
                Left = 123,
                Top = 234,
                Right = 923,
                Bottom = 834
            };

            IntPtr shownHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd) { return null; },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(123, movedRect.Left);
            Assert.AreEqual(234, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
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
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)300)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 50,
                            Top = 60,
                            Right = 850,
                            Bottom = 660
                        };
                    }

                    return null;
                },
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
            Assert.AreEqual(50, movedRect.Left);
            Assert.AreEqual(60, movedRect.Top);
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

        [TestMethod]
        public void PrepareForPath_MatchesResolvedHomePath_WhenTargetIsHomeShellPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.HomeFolderPath = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
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
                    return explorerService.GetResolvedHomeFolderPath();
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
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.HomeFolderPath);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(explorerService.HomeFolderPath, explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
        }
    }
}


