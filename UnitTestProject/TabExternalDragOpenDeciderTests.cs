using System.Windows;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class TabExternalDragOpenDeciderTests
    {
        [TestMethod]
        public void ShouldOpenInNewWindow_ReturnsTrue_WhenDropEffectIsNone_AndCursorIsOutsideWindow()
        {
            bool result = TabExternalDragOpenDecider.ShouldOpenInNewWindow(
                DragDropEffects.None,
                @"C:\Work",
                new NativeMethods.POINT { X = 50, Y = 50 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldOpenInNewWindow_ReturnsFalse_WhenCursorRemainsInsideWindow()
        {
            bool result = TabExternalDragOpenDecider.ShouldOpenInNewWindow(
                DragDropEffects.None,
                @"C:\Work",
                new NativeMethods.POINT { X = 150, Y = 150 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldOpenInNewWindow_ReturnsFalse_WhenDropEffectIsHandled()
        {
            bool result = TabExternalDragOpenDecider.ShouldOpenInNewWindow(
                DragDropEffects.Move,
                @"C:\Work",
                new NativeMethods.POINT { X = 50, Y = 50 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryOpenInNewWindowAndCloseSourceTab_ClosesTab_WhenWindowOpenSucceeds()
        {
            MockExplorerService explorerService = new MockExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel(System.IntPtr.Zero, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"C:\Work", 1);
            TabItemViewModel draggedTab = viewModel.Tabs[1];

            bool result = TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTab(
                DragDropEffects.None,
                draggedTab,
                draggedTab.Path,
                viewModel,
                explorerService.OpenInNewWindow,
                new NativeMethods.POINT { X = 50, Y = 50 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsTrue(result);
            Assert.AreEqual(@"C:\Work", explorerService.OpenedInNewWindowPath);
            Assert.AreEqual(1, viewModel.Tabs.Count);
            Assert.AreEqual(@"C:\MockPath", viewModel.Tabs[0].Path);
        }

        [TestMethod]
        public void TryOpenInNewWindowAndCloseSourceTab_LeavesTabOpen_WhenWindowOpenFails()
        {
            MockExplorerService explorerService = new MockExplorerService
            {
                OpenInNewWindowResult = false
            };
            TabBarViewModel viewModel = new TabBarViewModel(System.IntPtr.Zero, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"C:\Work", 1);
            TabItemViewModel draggedTab = viewModel.Tabs[1];

            bool result = TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTab(
                DragDropEffects.None,
                draggedTab,
                draggedTab.Path,
                viewModel,
                explorerService.OpenInNewWindow,
                new NativeMethods.POINT { X = 50, Y = 50 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsFalse(result);
            Assert.AreEqual(2, viewModel.Tabs.Count);
            Assert.AreEqual(@"C:\Work", viewModel.Tabs[1].Path);
        }

        [TestMethod]
        public void TryOpenInNewWindowAndCloseSourceTab_UsesDragStartPath_WhenTabPathChangesDuringDrag()
        {
            MockExplorerService explorerService = new MockExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel(System.IntPtr.Zero, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"E:\", 1);
            TabItemViewModel draggedTab = viewModel.Tabs[1];
            string draggedPath = draggedTab.Path;

            draggedTab.Path = explorerService.PowerOptionsPath;

            bool result = TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTab(
                DragDropEffects.None,
                draggedTab,
                draggedPath,
                viewModel,
                explorerService.OpenInNewWindow,
                new NativeMethods.POINT { X = 50, Y = 50 },
                new NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });

            Assert.IsTrue(result);
            Assert.AreEqual(@"E:\", explorerService.OpenedInNewWindowPath);
            Assert.AreEqual(1, viewModel.Tabs.Count);
        }
    }
}
