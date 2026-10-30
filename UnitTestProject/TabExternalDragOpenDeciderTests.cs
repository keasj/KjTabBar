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
        public void Detach_RequiresCursorOutsideBothTabBarAndExplorer()
        {
            NativeMethods.RECT bar = new NativeMethods.RECT { Left = 100, Top = 80, Right = 300, Bottom = 120 };
            NativeMethods.RECT explorer = new NativeMethods.RECT { Left = 100, Top = 100, Right = 500, Bottom = 500 };
            Assert.IsFalse(TabExternalDragOpenDecider.ShouldOpenInNewWindow(DragDropEffects.None, @"C:\Work",
                new NativeMethods.POINT { X = 200, Y = 300 }, bar, explorer), "Explorer content is not a detach target.");
            Assert.IsFalse(TabExternalDragOpenDecider.ShouldOpenInNewWindow(DragDropEffects.None, @"C:\Work",
                new NativeMethods.POINT { X = 200, Y = 90 }, bar, explorer), "The tab bar is not a detach target.");
            Assert.IsTrue(TabExternalDragOpenDecider.ShouldOpenInNewWindow(DragDropEffects.None, @"C:\Work",
                new NativeMethods.POINT { X = 600, Y = 300 }, bar, explorer));
            Assert.IsFalse(TabExternalDragOpenDecider.ShouldOpenInNewWindow(DragDropEffects.Move, @"C:\Work",
                new NativeMethods.POINT { X = 600, Y = 300 }, bar, explorer));
        }

        [TestMethod]
        public void DragCancellation_IsDetectedForOleCancelAndEscape_AndDoesNotLeakToNextDrag()
        {
            System.Exception failure = null;
            bool dispatcherShutdown = false;
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Windows.Controls.Border element = new System.Windows.Controls.Border();
                    System.Windows.Interop.HwndSourceParameters parameters = new System.Windows.Interop.HwndSourceParameters("Cancel drag regression")
                    {
                        Width = 120, Height = 40, PositionX = -10000, PositionY = -10000,
                        WindowStyle = unchecked((int)0x90000000)
                    };
                    using (System.Windows.Interop.HwndSource host = new System.Windows.Interop.HwndSource(parameters))
                    {
                        host.RootVisual = element;
                        // Exercise actual WPF callbacks without entering the test runner's modal OLE drag loop.
                        System.Type sourceType = typeof(DragDrop).Assembly.GetType("System.Windows.OleDragSource", true);
                        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                        object source = sourceType.GetConstructors(flags)[0].Invoke(new object[] { element });
                        System.Reflection.MethodInfo query = System.Array.Find(sourceType.GetMethods(flags), method => method.Name.EndsWith("OleQueryContinueDrag"));
                        QueryContinueDragEventHandler cancel = (sender, e) => { e.Action = DragAction.Cancel; e.Handled = true; };
                        element.QueryContinueDrag += cancel;
                        bool cancelled;
                        DragDropEffects effect = TabBarWindow.RunTabDrag(element, () =>
                        {
                            Assert.AreEqual(0x40101, (int)query.Invoke(source, new object[] { 0, 1 }));
                            return DragDropEffects.None;
                        }, out cancelled);
                        element.QueryContinueDrag -= cancel;
                        Assert.IsTrue(cancelled);

                        effect = TabBarWindow.RunTabDrag(element, () =>
                        {
                            int result = (int)query.Invoke(source, new object[] { 1, 1 });
                            Assert.AreEqual(0x40101, result, "DRAGDROP_S_CANCEL");
                            return DragDropEffects.None;
                        }, out cancelled);
                        Assert.IsTrue(cancelled, "ESC must suppress detachment even before WPF sets the default action.");

                        effect = TabBarWindow.RunTabDrag(element, () => DragDropEffects.None, out cancelled);
                        Assert.AreEqual(DragDropEffects.None, effect);
                        Assert.IsFalse(cancelled, "A later unhandled drop must still detach.");
                    }
                }
                catch (System.Exception ex) { failure = ex; }
                finally
                {
                    // Release WPF text-services COM objects while their STA is still alive.
                    System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    dispatcher.InvokeShutdown();
                    dispatcherShutdown = dispatcher.HasShutdownFinished;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.IsTrue(thread.Join(10000), "Drag cancellation did not complete.");
            if (failure != null) throw failure;
            Assert.IsTrue(dispatcherShutdown, "The WPF dispatcher must shut down before its STA exits.");
        }

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
