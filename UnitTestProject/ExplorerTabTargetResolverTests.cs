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
    }
}
