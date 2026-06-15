using System;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerTabTargetResolverTests
    {
        [TestMethod]
        public void IgnoreExplorerWindow_MarksIgnoredAndRestoresHiddenState()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.HiddenPendingAbsorb[(IntPtr)10] = DateTime.UtcNow;

            ExplorerTabTargetResolver resolver = new ExplorerTabTargetResolver(
                new TabBarRegistry(),
                trackingState,
                new ControlPanelTabSearch(new MockExplorerService()));

            resolver.IgnoreExplorerWindow((IntPtr)10);

            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)10));
            Assert.IsFalse(trackingState.HiddenPendingAbsorb.ContainsKey((IntPtr)10));
        }

        [TestMethod]
        public void HasEquivalentControlPanelTab_ReturnsUnderlyingSearchResult()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path) { return path == "cp"; };
            ExplorerTabTargetResolver resolver = new ExplorerTabTargetResolver(
                new TabBarRegistry(),
                new ExplorerWindowTrackingState(),
                new ControlPanelTabSearch(explorerService));
            TabBarViewModel viewModel = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath("cp", 1, true);

            Assert.IsTrue(resolver.HasEquivalentControlPanelTab(viewModel, "cp"));
        }

        [TestMethod]
        public void FindTarget_ReturnsForegroundControlPanelHost_WhenEquivalentTabDoesNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == "cp-item";
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel foregroundHost = new TabBarViewModel((IntPtr)10, new MockUserSettings(), explorerService);
            foregroundHost.InsertTabWithPath("cp-root", 1, true);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { foregroundHost },
                "cp-item",
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)10; },
                delegate (IntPtr hwnd) { return false; });

            Assert.AreSame(foregroundHost, result);
        }

        [TestMethod]
        public void FindTarget_DoesNotReturnBackgroundControlPanelHost_WhenEquivalentTabDoesNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == "cp-item";
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel backgroundHost = new TabBarViewModel((IntPtr)20, new MockUserSettings(), explorerService);
            backgroundHost.InsertTabWithPath("cp-root", 1, true);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { backgroundHost },
                "cp-item",
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; });

            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindTarget_DoesNotReturnForegroundControlPanelHost_ForControlPanelRootPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == "cp-item";
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel foregroundHost = new TabBarViewModel((IntPtr)30, new MockUserSettings(), explorerService);
            foregroundHost.InsertTabWithPath("cp-root", 1, true);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { foregroundHost },
                "cp-root",
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)30; },
                delegate (IntPtr hwnd) { return false; });

            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindTarget_ReturnsPreviousForegroundControlPanelHost_WhenEquivalentTabDoesNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == "cp-item";
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel previousForegroundHost = new TabBarViewModel((IntPtr)40, new MockUserSettings(), explorerService);
            previousForegroundHost.InsertTabWithPath("cp-root", 1, true);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { previousForegroundHost },
                "cp-item",
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)40; });

            Assert.AreSame(previousForegroundHost, result);
        }

        [TestMethod]
        public void FindTarget_DoesNotReturnEquivalentBackgroundControlPanelHost_WithoutForegroundRelation()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == "cp-item";
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel backgroundHost = new TabBarViewModel((IntPtr)50, new MockUserSettings(), explorerService);
            backgroundHost.InsertTabWithPath("cp-item", 1, true);
            backgroundHost.SelectTab(backgroundHost.Tabs[1]);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { backgroundHost },
                "cp-item",
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; });

            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindTarget_ReturnsSoleControlPanelHost_ForControlPanelItem()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "cp-root" || path == explorerService.PowerOptionsPath;
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "cp-root"; };
            explorerService.NormalizeShellNamespacePathFunc = delegate (string path) { return path; };

            ControlPanelTabSearch search = new ControlPanelTabSearch(explorerService);
            TabBarViewModel soleHost = new TabBarViewModel((IntPtr)60, new MockUserSettings(), explorerService);
            soleHost.InsertTabWithPath("cp-root", 1, true);

            TabBarViewModel result = search.FindTarget(
                new System.Collections.Generic.List<TabBarViewModel> { soleHost },
                explorerService.PowerOptionsPath,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; });

            Assert.AreSame(soleHost, result);
        }
    }
}
