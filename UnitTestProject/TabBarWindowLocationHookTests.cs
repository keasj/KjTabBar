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

