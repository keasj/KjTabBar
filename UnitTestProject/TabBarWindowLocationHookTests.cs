using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class TabBarWindowLocationHookTests
    {
        [TestMethod]
        public void TabDrag_CapturesPointerAndReleasesOnMouseUpOrCaptureLoss()
        {
            Exception failure = null;
            bool dispatcherShutdown = false;
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                TabBarWindow window = null;
                try
                {
                    window = new TabBarWindow();
                    System.Windows.Controls.Button close = new System.Windows.Controls.Button();
                    System.Windows.Controls.Border tab = new System.Windows.Controls.Border
                    {
                        Width = 120, Height = 40, Child = close
                    };
                    System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    tab.PreviewMouseLeftButtonDown += (System.Windows.Input.MouseButtonEventHandler)Delegate.CreateDelegate(
                        typeof(System.Windows.Input.MouseButtonEventHandler), window,
                        typeof(TabBarWindow).GetMethod("Tab_MouseLeftButtonDown", flags));
                    tab.PreviewMouseLeftButtonUp += (System.Windows.Input.MouseButtonEventHandler)Delegate.CreateDelegate(
                        typeof(System.Windows.Input.MouseButtonEventHandler), window,
                        typeof(TabBarWindow).GetMethod("Tab_PreviewMouseLeftButtonUp", flags));
                    tab.LostMouseCapture += (System.Windows.Input.MouseEventHandler)Delegate.CreateDelegate(
                        typeof(System.Windows.Input.MouseEventHandler), window,
                        typeof(TabBarWindow).GetMethod("Tab_LostMouseCapture", flags));
                    System.Windows.Interop.HwndSourceParameters parameters = new System.Windows.Interop.HwndSourceParameters("Tab drag regression")
                    {
                        Width = 120, Height = 40, PositionX = -10000, PositionY = -10000,
                        WindowStyle = unchecked((int)0x90000000)
                    };
                    using (System.Windows.Interop.HwndSource host = new System.Windows.Interop.HwndSource(parameters))
                    {
                        host.RootVisual = tab;
                        tab.UpdateLayout();
                        System.Windows.Input.MouseButtonEventArgs down = new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonDownEvent };
                        tab.RaiseEvent(down);
                        Assert.AreSame(tab, System.Windows.Input.Mouse.Captured, "The pressed tab must keep receiving movement outside its bounds.");
                        System.Windows.Input.MouseButtonEventArgs up = new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonUpEvent };
                        tab.RaiseEvent(up);
                        Assert.IsFalse(tab.IsMouseCaptured);
                        Assert.IsNull(typeof(TabBarWindow).GetField("_dragSourceElement", flags).GetValue(window));

                        tab.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonDownEvent });
                        Assert.IsTrue(tab.IsMouseCaptured);
                        System.Windows.Input.Mouse.Capture(null);
                        Assert.IsNull(typeof(TabBarWindow).GetField("_dragSourceElement", flags).GetValue(window));

                        close.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonDownEvent });
                        Assert.IsNull(typeof(TabBarWindow).GetField("_dragSourceElement", flags).GetValue(window),
                            "The close button must not arm tab dragging.");
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    if (window != null) window.Close();
                    // Release WPF text-services COM objects while their STA is still alive.
                    System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    dispatcher.InvokeShutdown();
                    dispatcherShutdown = dispatcher.HasShutdownFinished;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
            Assert.IsTrue(dispatcherShutdown, "The WPF dispatcher must shut down before its STA exits.");
        }

        [TestMethod]
        public void TabDrag_RequiresLeavingPressedTabAndExceedingDragDistance()
        {
            System.Windows.Size size = new System.Windows.Size(120, 40);
            System.Windows.Point start = new System.Windows.Point(20, 20);
            Assert.IsFalse(TabBarWindow.ShouldStartTabDrag(start, new System.Windows.Point(100, 30), size));
            Assert.IsTrue(TabBarWindow.ShouldStartTabDrag(start, new System.Windows.Point(130, 20), size));
            Assert.IsTrue(TabBarWindow.ShouldStartTabDrag(start, new System.Windows.Point(20, -10), size));
            Assert.IsFalse(TabBarWindow.ShouldStartTabDrag(new System.Windows.Point(0, 20),
                new System.Windows.Point(-0.1, 20), size));
        }

        [TestMethod]
        public void ForegroundChange_RechecksAfterMessageLoop_AndCancelsStaleFollowup()
        {
            Exception failure = null;
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    int updates = 0;
                    IntPtr foreground = (IntPtr)100;
                    using (TabBarWindowRuntimeCoordinator coordinator = CreateCoordinator(() => true, () => updates++, () => Assert.Fail()))
                    {
                        coordinator.GetForegroundWindow = () => foreground;
                        coordinator.Start(foreground);
                        updates = 0;
                        coordinator.HandleForegroundChanged(foreground, foreground);
                        Assert.AreEqual(1, updates);
                        DrainForegroundFollowup();
                        Assert.AreEqual(2, updates, "Recheck Z-order after the foreground callback returns.");
                        coordinator.HandleForegroundChanged(foreground, foreground);
                        foreground = (IntPtr)999;
                        DrainForegroundFollowup();
                        Assert.AreEqual(3, updates, "Do not follow after the user switches apps.");
                        foreground = (IntPtr)100;
                        coordinator.HandleForegroundChanged(foreground, foreground);
                        coordinator.RebindExplorer((IntPtr)200);
                        int afterRebind = updates;
                        DrainForegroundFollowup();
                        Assert.AreEqual(afterRebind, updates);
                        foreground = (IntPtr)200;
                        coordinator.HandleForegroundChanged(foreground, foreground);
                        coordinator.Stop();
                        int afterStop = updates;
                        DrainForegroundFollowup();
                        Assert.AreEqual(afterStop, updates);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Foreground followup test did not finish.");
            if (failure != null) throw failure;
        }

        private static void DrainForegroundFollowup()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        [TestMethod]
        public void ForegroundChange_UpdatesImmediatelyWithoutTimerOrShellSync()
        {
            int updates = 0;
            int syncs = 0;
            using (TabBarWindowRuntimeCoordinator coordinator = new TabBarWindowRuntimeCoordinator(
                Dispatcher.CurrentDispatcher, () => true, () => updates++,
                () => { syncs++; return Task.FromResult(0); }, () => Assert.Fail("Must not close the bar.")))
            {
                coordinator.Start((IntPtr)100);
                updates = 0;
                coordinator.HandleForegroundChanged((IntPtr)100, (IntPtr)100);
                Assert.AreEqual(1, updates, "Must update before returning, without waiting for a timer.");
                Assert.AreEqual(0, syncs);
                coordinator.HandleForegroundChanged((IntPtr)100, (IntPtr)999);
                coordinator.HandleForegroundChanged((IntPtr)999, (IntPtr)999);
                coordinator.HandleForegroundChanged(IntPtr.Zero, IntPtr.Zero);
                Assert.AreEqual(1, updates, "Stale and unrelated events must not raise the bar.");
                coordinator.RebindExplorer((IntPtr)200);
                updates = 0;
                coordinator.HandleForegroundChanged((IntPtr)100, (IntPtr)100);
                Assert.AreEqual(0, updates);
                coordinator.HandleForegroundChanged((IntPtr)200, (IntPtr)200);
                Assert.AreEqual(1, updates);
                coordinator.Stop();
                coordinator.HandleForegroundChanged((IntPtr)200, (IntPtr)200);
                Assert.AreEqual(1, updates);
            }
        }

        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsTrue_ForTrackedExplorerWindowMove()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)10,
                0,
                (IntPtr)10);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsFalse_ForChildObjectMove()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)10,
                1,
                (IntPtr)10);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsFalse_ForDifferentExplorerWindow()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)11,
                0,
                (IntPtr)10);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void LocationUpdateQueue_CoalescesUntilPendingUpdateIsReleased()
        {
            int isQueued = 0;

            Assert.IsTrue(TabBarWindowRuntimeCoordinator.TryMarkLocationUpdateQueued(ref isQueued));
            Assert.IsFalse(TabBarWindowRuntimeCoordinator.TryMarkLocationUpdateQueued(ref isQueued));

            TabBarWindowRuntimeCoordinator.ClearLocationUpdateQueued(ref isQueued);

            Assert.IsTrue(TabBarWindowRuntimeCoordinator.TryMarkLocationUpdateQueued(ref isQueued));
        }

        [TestMethod]
        public void ShouldHandleDestroyEvent_ReturnsTrue_ForTrackedExplorerWindowDestroy()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleDestroyEvent(
                NativeMethods.EVENT_OBJECT_DESTROY,
                (IntPtr)10,
                0,
                (IntPtr)10);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldHandleDestroyEvent_ReturnsFalse_ForChildObjectDestroy()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleDestroyEvent(
                NativeMethods.EVENT_OBJECT_DESTROY,
                (IntPtr)10,
                1,
                (IntPtr)10);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldHandleDestroyEvent_ReturnsFalse_ForDifferentExplorerWindow()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldHandleDestroyEvent(
                NativeMethods.EVENT_OBJECT_DESTROY,
                (IntPtr)11,
                0,
                (IntPtr)10);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetPositionTimerInterval_ReturnsFastInterval_WhenLocationHookIsMissing()
        {
            TimeSpan result = TabBarWindowRuntimeCoordinator.GetPositionTimerInterval(IntPtr.Zero);

            Assert.AreEqual(TimeSpan.FromMilliseconds(100), result);
        }

        [TestMethod]
        public void GetPositionTimerInterval_ReturnsFallbackInterval_WhenLocationHookIsRegistered()
        {
            TimeSpan result = TabBarWindowRuntimeCoordinator.GetPositionTimerInterval((IntPtr)10);

            Assert.AreEqual(TimeSpan.FromSeconds(1), result);
        }

        [TestMethod]
        public void ShouldRepositionAfterRenderSizeChange_ReturnsTrue_WhenLoadedAndHeightChanged()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldRepositionAfterRenderSizeChange(true, true);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldRepositionAfterRenderSizeChange_ReturnsFalse_WhenHeightDidNotChange()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldRepositionAfterRenderSizeChange(true, false);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldRepositionAfterRenderSizeChange_ReturnsFalse_WhenNotLoaded()
        {
            bool result = TabBarWindowRuntimeCoordinator.ShouldRepositionAfterRenderSizeChange(false, true);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void HandlePositionTimerTick_ClosesWindow_WhenExplorerIsGone()
        {
            int closeCalls = 0;
            int updateCalls = 0;
            TabBarWindowRuntimeCoordinator coordinator = CreateCoordinator(
                delegate { return false; },
                delegate { updateCalls++; },
                delegate { closeCalls++; });

            coordinator.HandlePositionTimerTick();
            coordinator.HandlePositionTimerTick();

            Assert.AreEqual(1, closeCalls);
            Assert.AreEqual(0, updateCalls);
        }

        [TestMethod]
        public void HandlePositionTimerTick_DoesNotCloseWindow_OnFirstTransientExplorerMiss()
        {
            int closeCalls = 0;
            int updateCalls = 0;
            bool isAlive = false;
            TabBarWindowRuntimeCoordinator coordinator = CreateCoordinator(
                delegate { return isAlive; },
                delegate { updateCalls++; },
                delegate { closeCalls++; });

            coordinator.HandlePositionTimerTick();
            isAlive = true;
            coordinator.HandlePositionTimerTick();

            Assert.AreEqual(0, closeCalls);
            Assert.AreEqual(1, updateCalls);
        }

        [TestMethod]
        public void HandlePositionTimerTick_Repositions_WhenExplorerIsAlive()
        {
            int closeCalls = 0;
            int updateCalls = 0;
            TabBarWindowRuntimeCoordinator coordinator = CreateCoordinator(
                delegate { return true; },
                delegate { updateCalls++; },
                delegate { closeCalls++; });

            coordinator.HandlePositionTimerTick();

            Assert.AreEqual(0, closeCalls);
            Assert.AreEqual(1, updateCalls);
        }

        [TestMethod]
        public async Task HandleSyncTimerTickAsync_DoesNotOverlapConcurrentSyncs()
        {
            int syncCalls = 0;
            TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>();
            TabBarWindowRuntimeCoordinator coordinator = CreateCoordinator(
                delegate { return true; },
                delegate { },
                delegate { },
                async delegate
                {
                    syncCalls++;
                    await gate.Task;
                });

            Task first = coordinator.HandleSyncTimerTickAsync();
            Task second = coordinator.HandleSyncTimerTickAsync();

            await Task.Delay(10);
            gate.SetResult(true);
            await Task.WhenAll(first, second);

            Assert.AreEqual(1, syncCalls);
        }

        [TestMethod]
        public void ExecuteTabSelectionWithPendingReveal_CompletesReveal_AfterSuccessfulSelection()
        {
            int selectionCalls = 0;
            int revealCalls = 0;

            TabBarWindow.ExecuteTabSelectionWithPendingReveal(
                delegate { selectionCalls++; },
                delegate { revealCalls++; });

            Assert.AreEqual(1, selectionCalls);
            Assert.AreEqual(1, revealCalls);
        }

        [TestMethod]
        public void ExecuteTabSelectionWithPendingReveal_CompletesReveal_WhenSelectionThrows()
        {
            int revealCalls = 0;
            bool threw = false;

            try
            {
                TabBarWindow.ExecuteTabSelectionWithPendingReveal(
                    delegate { throw new InvalidOperationException("boom"); },
                    delegate { revealCalls++; });
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.IsTrue(threw);
            Assert.AreEqual(1, revealCalls);
        }

        [TestMethod]
        public void PersistCurrentTabState_PersistsViewModel_WhenWindowCloses()
        {
            TabBarViewModel persistedViewModel = null;
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)10, new MockUserSettings(), new MockExplorerService());

            TabBarWindow.PersistCurrentTabState(
                viewModel,
                delegate (TabBarViewModel currentViewModel)
                {
                    persistedViewModel = currentViewModel;
                });

            Assert.AreSame(viewModel, persistedViewModel);
        }

        [TestMethod]
        public void WindowMoveThreshold_DoesNotStartForClickOrSmallJitter_UsesBothDpiAxes()
        {
            int x = (int)Math.Ceiling(System.Windows.SystemParameters.MinimumHorizontalDragDistance * 2);
            int y = (int)Math.Ceiling(System.Windows.SystemParameters.MinimumVerticalDragDistance * 3);
            Assert.IsFalse(TabBarWindow.ShouldStartExplorerMove(0, 0, 2, 3));
            Assert.IsFalse(TabBarWindow.ShouldStartExplorerMove(x - 1, y - 1, 2, 3));
            Assert.IsTrue(TabBarWindow.ShouldStartExplorerMove(x, 0, 2, 3));
            Assert.IsTrue(TabBarWindow.ShouldStartExplorerMove(0, -y, 2, 3));
        }

        [TestMethod]
        public void WindowMoveBackground_AllowsAbsorptionZoneAndExcludesTabControls()
        {
            Exception failure = null;
            System.Threading.Thread thread = new System.Threading.Thread(delegate ()
            {
                try
                {
                    System.Windows.Controls.Border background = new System.Windows.Controls.Border();
                    System.Windows.Controls.Grid grid = new System.Windows.Controls.Grid();
                    background.Child = grid;
                    System.Windows.Controls.Border blank = new System.Windows.Controls.Border();
                    System.Windows.Controls.Border zone = new System.Windows.Controls.Border();
                    System.Windows.Controls.TextBlock zoneLabel = new System.Windows.Controls.TextBlock();
                    zone.Child = zoneLabel;
                    System.Windows.Controls.Button button = new System.Windows.Controls.Button();
                    System.Windows.Controls.Border tab = new System.Windows.Controls.Border();
                    tab.DataContext = new TabItemViewModel(null, "test", new MockExplorerService());
                    System.Windows.Controls.TextBlock tabLabel = new System.Windows.Controls.TextBlock();
                    tab.Child = tabLabel;
                    grid.Children.Add(blank);
                    grid.Children.Add(zone);
                    grid.Children.Add(button);
                    grid.Children.Add(tab);
                    Assert.IsTrue(TabBarWindow.IsWindowMoveBackground(background, background));
                    Assert.IsTrue(TabBarWindow.IsWindowMoveBackground(blank, background));
                    Assert.IsFalse(TabBarWindow.IsWindowMoveBackground(button, background));
                    Assert.IsFalse(TabBarWindow.IsWindowMoveBackground(tabLabel, background));
                    Assert.IsTrue(TabBarWindow.IsWindowMoveBackground(zone, background));
                    Assert.IsTrue(TabBarWindow.IsWindowMoveBackground(zoneLabel, background));
                    Assert.IsFalse(TabBarWindow.IsWindowMoveBackground(null, background));
                    Assert.IsFalse(TabBarWindow.IsWindowMoveBackground(new System.Windows.Controls.Border(), background));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "STA test did not finish.");
            if (failure != null) throw failure;
        }

        [TestMethod]
        public void DropVisibility_AllowsExposedPointAndRejectsCoveredPoint()
        {
            IntPtr target = (IntPtr)1;
            IntPtr app = (IntPtr)2;
            IntPtr dragged = (IntPtr)3;
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront(target, dragged,
                hwnd => hwnd == target ? app : hwnd == app ? dragged : IntPtr.Zero,
                hwnd => true, IntPtr.Zero, hwnd => true));
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront(target, dragged,
                hwnd => hwnd == target ? app : hwnd == app ? dragged : IntPtr.Zero,
                hwnd => true, IntPtr.Zero, hwnd => hwnd == dragged));
        }

        [TestMethod]
        public void DropVisibility_AllowsDraggedExplorerAndNonBlockingOverlays()
        {
            IntPtr target = (IntPtr)1;
            IntPtr app = (IntPtr)2;
            IntPtr dragged = (IntPtr)3;
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront(target, dragged,
                hwnd => hwnd == target ? dragged : hwnd == dragged ? app : IntPtr.Zero,
                hwnd => hwnd == dragged));
        }

        [TestMethod]
        public void DropVisibility_BackgroundDropRejectsCoveringWindowWithoutSourceExemption()
        {
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront((IntPtr)1, IntPtr.Zero,
                hwnd => hwnd == (IntPtr)1 ? (IntPtr)2 : IntPtr.Zero, hwnd => true));
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront((IntPtr)1, IntPtr.Zero,
                hwnd => IntPtr.Zero, hwnd => true));
        }

        [TestMethod]
        public void DropVisibility_RejectsMissingTargetAndChangingWindowOrder()
        {
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront(IntPtr.Zero, IntPtr.Zero,
                hwnd => IntPtr.Zero, hwnd => false));
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront((IntPtr)1, IntPtr.Zero,
                hwnd => hwnd == (IntPtr)1 ? (IntPtr)2 : (IntPtr)1, hwnd => false));
        }
        [TestMethod]
        public void DropVisibility_ObservedClockAndShellToolStylesDoNotBlockForegroundTarget()
        {
            // Styles observed on the user's clocks and Shell island window.
            Assert.IsTrue(NativeMethods.IsDropOverlayStyle(0x80108, true));
            Assert.IsTrue(NativeMethods.IsDropOverlayStyle(0x200088));
            Assert.IsFalse(NativeMethods.IsDropOverlayStyle(0x100));
            Assert.IsFalse(NativeMethods.IsDropOverlayStyle(0x80108, false));
            Assert.IsFalse(NativeMethods.IsDropOverlayStyle(0x108, true));
            IntPtr target = (IntPtr)1;
            IntPtr clock = (IntPtr)2;
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront(target, IntPtr.Zero,
                hwnd => hwnd == target ? clock : IntPtr.Zero,
                hwnd => !NativeMethods.IsDropOverlayStyle(0x80108, true)));
        }

        [TestMethod]
        public void DropVisibility_OleSourceCanRemainInFrontOfTarget()
        {
            IntPtr target = (IntPtr)1;
            IntPtr source = (IntPtr)2;
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront(target, source,
                hwnd => hwnd == target ? source : IntPtr.Zero, hwnd => true));
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront(target, source,
                hwnd => hwnd == target ? source : hwnd == source ? (IntPtr)3 : IntPtr.Zero,
                hwnd => true));
        }

        [TestMethod]
        public void DropVisibility_HostAboveUnownedBarDoesNotBlockButOtherAppStillDoes()
        {
            IntPtr target = (IntPtr)1;
            IntPtr host = (IntPtr)2;
            Assert.IsTrue(TabBarWindow.IsDropTargetInFront(target, IntPtr.Zero,
                hwnd => hwnd == target ? host : IntPtr.Zero, hwnd => true, host));
            Assert.IsFalse(TabBarWindow.IsDropTargetInFront(target, IntPtr.Zero,
                hwnd => hwnd == target ? host : hwnd == host ? (IntPtr)3 : IntPtr.Zero,
                hwnd => true, host));
        }
        private static TabBarWindowRuntimeCoordinator CreateCoordinator(
            Func<bool> isExplorerAlive,
            Action updatePosition,
            Action closeWindow)
        {
            return CreateCoordinator(
                isExplorerAlive,
                updatePosition,
                closeWindow,
                delegate { return Task.CompletedTask; });
        }

        private static TabBarWindowRuntimeCoordinator CreateCoordinator(
            Func<bool> isExplorerAlive,
            Action updatePosition,
            Action closeWindow,
            Func<Task> syncAsync)
        {
            return new TabBarWindowRuntimeCoordinator(
                Dispatcher.CurrentDispatcher,
                isExplorerAlive,
                updatePosition,
                syncAsync,
                closeWindow);
        }
    }
}

