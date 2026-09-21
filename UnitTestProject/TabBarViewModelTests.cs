using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using KjTabBar.ViewModels;
using System.IO;
using KjTabBar.Models;

namespace UnitTestProject
{
    [TestClass]
    public class TabBarViewModelTests
    {
        [TestMethod]
        public void Overlap_CloseThenNewRequestTimeoutRetainsCloseRollback()
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel a = vm.ActiveTab;
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                TabItemViewModel c = new TabItemViewModel(@"C:\C", "C", explorer);
                vm.Tabs.Add(b); vm.Tabs.Add(c);
                vm.CloseTab(a); vm.SelectTab(c);
                vm.TimeoutPendingNavigation();
                vm.NavigationTracker.InvalidateCache(); vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(a, vm.ActiveTab);
                Assert.AreEqual(3, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
                Assert.AreEqual(@"C:\B", b.Path);
                Assert.AreEqual(@"C:\C", c.Path);
            }
        }

        [TestMethod]
        public void Overlap_ObservedIntermediateArrivalBecomesRollbackSource()
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                TabItemViewModel c = new TabItemViewModel(@"C:\C", "C", explorer);
                vm.Tabs.Add(b); vm.Tabs.Add(c); vm.SelectTab(b);
                explorer.Current = b.Path;
                vm.NavigationTracker.InvalidateCache(); vm.SelectTab(c);
                vm.TimeoutPendingNavigation();
                Assert.AreSame(b, vm.ActiveTab);
                Assert.AreEqual(@"C:\B", b.Path);
                Assert.AreEqual(@"C:\C", c.Path);
            }
        }
        [TestMethod]
        public void Overlap_AcceptedRequestsTimeoutRestoresActualSource()
        {
            VerifyOverlappingRequests(false);
        }

        [TestMethod]
        public void Overlap_ReturnToSourceStillTracksLateArrival()
        {
            VerifyOverlappingRequests(true);
        }

        private void VerifyOverlappingRequests(bool returnToSource)
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel a = vm.ActiveTab;
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                TabItemViewModel c = new TabItemViewModel(@"C:\C", "C", explorer);
                vm.Tabs.Add(b); vm.Tabs.Add(c);
                vm.SelectTab(b); vm.SelectTab(returnToSource ? a : c);
                if (!returnToSource)
                {
                    typeof(TabNavigationStateTracker).GetField("_navigateStartTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreSame(a, vm.ActiveTab);
                }
                explorer.Current = b.Path;
                vm.NavigationTracker.InvalidateCache(); vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(b, vm.ActiveTab);
                Assert.AreEqual(@"C:\A", a.Path);
                Assert.AreEqual(@"C:\B", b.Path);
                if (!returnToSource)
                {
                    explorer.Current = c.Path;
                    vm.NavigationTracker.InvalidateCache(); vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreSame(c, vm.ActiveTab);
                    Assert.AreEqual(@"C:\B", b.Path);
                }
            }
        }
        [TestMethod]
        public void FullReview_CloseTimeoutRestoresSourceAndPreservesTarget() { VerifyCloseTimeout(1); }
        [TestMethod]
        public void FullReview_CloseRangeTimeoutRestoresRemovedTabs() { VerifyCloseTimeout(2); }
        [TestMethod]
        public void FullReview_CloseLastTimeoutRestoresSource() { VerifyCloseTimeout(0); }

        private void VerifyCloseTimeout(int count)
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel source = vm.ActiveTab;
                if (count == 2) vm.Tabs.Add(new TabItemViewModel(@"C:\X", "X", explorer));
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer);
                if (count > 0) vm.Tabs.Add(target);
                int before = vm.Tabs.Count;
                vm.CloseTabsAsync(0, Math.Max(count, 1), null, null).GetAwaiter().GetResult();
                Assert.IsFalse(vm.Tabs.Contains(source));
                typeof(TabNavigationStateTracker).GetField("_navigateStartTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                vm.NavigationTracker.InvalidateCache();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(source, vm.ActiveTab);
                Assert.AreEqual(before, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
                Assert.AreEqual(@"C:\A", source.Path);
                if (count > 0)
                {
                    Assert.AreEqual(@"C:\B", target.Path);
                    explorer.Current = target.Path;
                    vm.NavigationTracker.InvalidateCache();
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreSame(target, vm.ActiveTab);
                    Assert.AreEqual(@"C:\A", source.Path);
                }
                else
                {
                    explorer.Current = explorer.GetResolvedHomeFolderPath();
                    vm.NavigationTracker.InvalidateCache();
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreNotSame(source, vm.ActiveTab);
                    Assert.AreEqual(explorer.Current, vm.ActiveTab.Path);
                    Assert.AreEqual(@"C:\A", source.Path);
                }
            }
        }

        [TestMethod]
        public void FullReview_CompletedCloseDoesNotUndoLater()
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel source = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(target); vm.CloseTab(source);
                explorer.Current = target.Path;
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                vm.TimeoutPendingNavigation();
                Assert.IsFalse(vm.Tabs.Contains(source));
                Assert.IsTrue(vm.HasClosedTabs);
            }
        }

        [TestMethod]
        public void FullReview_LateControlPanelRootSelectsItsOwnTab()
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer { Throws = true };
            explorer.IsControlPanelRootPathFunc = path => path == explorer.AllControlPanelPath;
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel source = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(explorer.AllControlPanelPath, "CP", explorer);
                vm.Tabs.Add(target); explorer.FailPath = target.Path;
                vm.SelectTab(target);
                explorer.Current = target.Path;
                vm.NavigationTracker.InvalidateCache();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(target, vm.ActiveTab);
                Assert.AreEqual(@"C:\A", source.Path);
            }
        }

        [TestMethod]
        public void FullReview_TruncatedEncryptedStateIsNotOverwritten()
        {
            string dir = Path.Combine(Path.GetTempPath(), "KjTabBar-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "tabs.txt");
            try
            {
                File.WriteAllText(path, "kjtb-dpapi-v1:");
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), new MockExplorerService(), @"C:\A"))
                {
                    TabPersistenceService persistence = new TabPersistenceService(path);
                    Assert.IsFalse(persistence.LoadTabsTo(vm));
                    persistence.SaveTabsIfChanged(vm, true);
                    Assert.AreEqual("kjtb-dpapi-v1:", File.ReadAllText(path));
                }
                string empty = KjTabBar.Helpers.ProtectedTextStorage.SerializeLines(new string[0]);
                Assert.AreEqual(0, KjTabBar.Helpers.ProtectedTextStorage.DeserializeLines(empty).Length);
            }
            finally { File.Delete(path); Directory.Delete(dir); }
        }

        [TestMethod]
        public void FullReview_DragEffectUsesAllowedOperation()
        {
            Assert.AreEqual(System.Windows.DragDropEffects.Copy,
                KjTabBar.Views.TabBarWindowDragDropHandler.GetFileDropEffect(System.Windows.DragDropKeyStates.None,
                    System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move));
            Assert.AreEqual(System.Windows.DragDropEffects.None,
                KjTabBar.Views.TabBarWindowDragDropHandler.GetFileDropEffect(System.Windows.DragDropKeyStates.ShiftKey, System.Windows.DragDropEffects.Copy));
            Assert.AreEqual(System.Windows.DragDropEffects.Move,
                KjTabBar.Views.TabBarWindowDragDropHandler.GetFileDropEffect(System.Windows.DragDropKeyStates.ShiftKey, System.Windows.DragDropEffects.Move));
            Assert.AreEqual(System.Windows.DragDropEffects.Copy,
                KjTabBar.Views.TabBarWindowDragDropHandler.GetFileDropEffect(System.Windows.DragDropKeyStates.RightMouseButton, System.Windows.DragDropEffects.Copy));
        }
        [TestMethod]
        public void Recheck2_ClosePreparationChecksAvailabilityOnceBeforeAwait()
        {
            CountingAvailabilityExplorer explorer = new CountingAvailabilityExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                explorer.Calls = 0;
                bool prepared = false;
                vm.CloseTabsAsync(0, 1, path =>
                {
                    prepared = true;
                    Assert.AreEqual(1, explorer.Calls);
                    return System.Threading.Tasks.Task.FromResult(false);
                }, null).GetAwaiter().GetResult();
                Assert.IsTrue(prepared);
                Assert.AreEqual(2, vm.Tabs.Count);
            }
        }

        private sealed class CountingAvailabilityExplorer : MockExplorerService
        {
            internal int Calls;
            public override bool IsTabPathCurrentlyAvailable(string path) { Calls++; return true; }
        }
        [TestMethod]
        public void Recheck2_SecondNavigationThrows() { VerifySecondNavigationFailure(true); }
        [TestMethod]
        public void Recheck2_SecondNavigationRejected() { VerifySecondNavigationFailure(false); }
        [TestMethod]
        public void Recheck2_CloseNavigationThrows() { VerifyCloseNavigationFailure(true, 1); }
        [TestMethod]
        public void Recheck2_CloseNavigationRejected() { VerifyCloseNavigationFailure(false, 1); }
        [TestMethod]
        public void Recheck2_CloseRangeNavigationThrows() { VerifyCloseNavigationFailure(true, 2); }
        [TestMethod]
        public void Recheck2_CloseRangeNavigationRejected() { VerifyCloseNavigationFailure(false, 2); }
        [TestMethod]
        public void Recheck2_CloseLastNavigationThrows() { VerifyCloseNavigationFailure(true, 0); }
        [TestMethod]
        public void Recheck2_CloseLastNavigationRejected() { VerifyCloseNavigationFailure(false, 0); }
        private void VerifySecondNavigationFailure(bool throws)
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer { Throws = throws };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                TabItemViewModel c = new TabItemViewModel(@"C:\C", "C", explorer);
                vm.Tabs.Add(b); vm.Tabs.Add(c);
                vm.SelectTab(b);
                DateTime started = vm.NavigationTracker.NavigateStartTime;
                explorer.FailPath = c.Path;
                vm.SelectTab(c);
                Assert.AreSame(b, vm.ActiveTab);
                Assert.AreEqual(b.Path, vm.NavigationTracker.NavigatingToPath);
                Assert.AreEqual(started, vm.NavigationTracker.NavigateStartTime);
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\B", b.Path);
                explorer.Current = b.Path;
                vm.NavigationTracker.InvalidateCache();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
                Assert.AreEqual(@"C:\A", original.Path);
                Assert.AreEqual(@"C:\C", c.Path);
            }
        }

        private void VerifyCloseNavigationFailure(bool throws, int count)
        {
            SequentialFailureExplorer explorer = new SequentialFailureExplorer { Throws = throws };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                if (count == 2) vm.Tabs.Add(new TabItemViewModel(@"C:\X", "X", explorer));
                if (count > 0) vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                int before = vm.Tabs.Count;
                explorer.FailPath = count == 0 ? explorer.GetResolvedHomeFolderPath() : @"C:\B";
                vm.CloseTabsAsync(0, Math.Max(1, count), null, null).GetAwaiter().GetResult();
                Assert.AreEqual(before, vm.Tabs.Count);
                Assert.AreSame(original, vm.ActiveTab);
                Assert.IsFalse(vm.HasClosedTabs);
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\A", original.Path);
                if (count > 0) Assert.AreEqual(@"C:\B", vm.Tabs[vm.Tabs.Count - 1].Path);
            }
        }

        private sealed class SequentialFailureExplorer : MockExplorerService
        {
            internal string Current = @"C:\A";
            internal string FailPath;
            internal bool Throws;
            public override string GetCurrentPath(IntPtr hwnd) { return Current; }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                if (path != FailPath) return true;
                if (Throws) throw new TimeoutException("Simulated navigation timeout");
                return false;
            }
        }
        [TestMethod]
        public void Review_SelectTab_RejectsRemovedTab()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel active = vm.ActiveTab;
                TabItemViewModel removed = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(removed);
                vm.CloseTab(removed);
                vm.SelectTab(removed);
                Assert.AreSame(active, vm.ActiveTab);
                Assert.AreEqual(0, vm.ActiveTabIndex);
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
            }
        }

        [TestMethod]
        public void Review_CloseTab_UnavailableDestination_PreservesSelectionAndHistory()
        {
            VerifyUnavailableCloseDestination(false);
        }

        [TestMethod]
        public void Review_CloseRange_UnavailableDestination_PreservesSelectionAndHistory()
        {
            VerifyUnavailableCloseDestination(true);
        }

        private static void VerifyUnavailableCloseDestination(bool range)
        {
            UnavailablePathExplorerService explorer = new UnavailablePathExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel active = vm.ActiveTab;
                vm.Tabs.Add(new TabItemViewModel(@"Z:\SleepingDrive", "Unavailable", explorer));
                if (range) vm.CloseTabsToLeft(vm.Tabs[1]);
                else vm.CloseTab(active);
                Assert.AreSame(active, vm.ActiveTab);
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
            }
        }

        [TestMethod]
        public void Review_Sync_DiscardsPathAfterSelectionChanges()
        {
            BlockingPathExplorerService explorer = new BlockingPathExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel next = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(next);
                System.Threading.Tasks.Task syncing = vm.SyncWithExplorerAsync();
                try
                {
                    Assert.IsTrue(explorer.WaitUntilPathReadStarts(2000));
                    vm.SetActiveTabOnly(next);
                }
                finally { explorer.ReleasePathRead(); }
                syncing.GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\B", next.Path);
                Assert.IsNull(vm.NavigationTracker.CachedExplorerPath);
            }
        }

        [TestMethod]
        public void Review_Sync_DiscardsAvailabilityAfterSelectionChanges()
        {
            VerifyStaleAvailability(false);
        }

        [TestMethod]
        public void Review_Sync_DiscardsAvailabilityAfterSelectionChangesBack()
        {
            VerifyStaleAvailability(true);
        }

        private static void VerifyStaleAvailability(bool switchBack)
        {
            BlockingAvailabilityExplorerService explorer = new BlockingAvailabilityExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel next = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(next);
                System.Threading.Tasks.Task syncing = vm.SyncWithExplorerAsync();
                try
                {
                    Assert.IsTrue(explorer.Entered.Wait(2000));
                    vm.SetActiveTabOnly(next);
                    if (switchBack) vm.SetActiveTabOnly(original);
                }
                finally { explorer.Release.Set(); }
                syncing.GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\A", original.Path);
                Assert.AreEqual(@"C:\B", next.Path);
                Assert.IsTrue(vm.NavigationTracker.ShouldPoll(DateTime.UtcNow, false));
            }
        }

        private sealed class BlockingAvailabilityExplorerService : MockExplorerService
        {
            internal readonly System.Threading.ManualResetEventSlim Entered = new System.Threading.ManualResetEventSlim();
            internal readonly System.Threading.ManualResetEventSlim Release = new System.Threading.ManualResetEventSlim();
            public override string GetCurrentPath(IntPtr hwnd) { return @"C:\Stale"; }
            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                Entered.Set();
                if (!Release.Wait(3000)) throw new TimeoutException();
                return true;
            }
        }

        [TestMethod]
        public void Review_SelectAsync_RemovedWhilePreparing_DoesNotSelectAndAlwaysReveals()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel active = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(target);
                System.Threading.Tasks.TaskCompletionSource<bool> ready = new System.Threading.Tasks.TaskCompletionSource<bool>();
                bool revealed = false;
                System.Threading.Tasks.Task selecting = vm.SelectTabAsync(target, path => ready.Task, () => revealed = true);
                vm.Tabs.Remove(target);
                ready.SetResult(true);
                selecting.GetAwaiter().GetResult();
                Assert.AreSame(active, vm.ActiveTab);
                Assert.IsTrue(revealed);
                Assert.IsFalse(vm.IsTabOperationPending);
            }
        }

        [TestMethod]
        public void Review_PreparingSelection_BlocksOverlappingUiCloseAndSynchronization()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = hwnd => { Assert.Fail("Sync must wait for preparation."); return null; };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(target);
                System.Threading.Tasks.TaskCompletionSource<bool> ready = new System.Threading.Tasks.TaskCompletionSource<bool>();
                System.Threading.Tasks.Task selecting = vm.SelectTabAsync(target, path => ready.Task, delegate { });
                vm.CloseTabsAsync(1, 1, null, null).GetAwaiter().GetResult();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(2, vm.Tabs.Count);
                ready.SetResult(false);
                selecting.GetAwaiter().GetResult();
                Assert.AreSame(original, vm.ActiveTab);
                Assert.IsFalse(vm.IsTabOperationPending);
            }
        }

        [TestMethod]
        public void Review_InsertAsync_PreparesHostBeforeAddingControlPanelTab()
        {
            VerifyPreparedInsertion(false, true);
        }

        [TestMethod]
        public void Review_DuplicateAsync_PreparesHostBeforeDuplicatingInactiveControlPanelTab()
        {
            VerifyPreparedInsertion(true, true);
        }

        [TestMethod]
        public void Review_InsertAsync_FailedPreparation_PreservesTabs()
        {
            VerifyPreparedInsertion(false, false);
        }

        [TestMethod]
        public void Review_DuplicateAsync_FailedPreparation_PreservesTabs()
        {
            VerifyPreparedInsertion(true, false);
        }

        private static void VerifyPreparedInsertion(bool duplicate, bool prepareSucceeds)
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = hwnd => explorer.AllControlPanelPath;
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel source = new TabItemViewModel(explorer.AllControlPanelPath, "Control Panel", explorer);
                if (duplicate) vm.Tabs.Add(source);
                int before = vm.Tabs.Count;
                bool prepared = false;
                bool revealed = false;
                Func<string, System.Threading.Tasks.Task<bool>> prepare = path =>
                {
                    Assert.AreEqual(explorer.AllControlPanelPath, path);
                    Assert.AreSame(original, vm.ActiveTab);
                    Assert.AreEqual(before, vm.Tabs.Count);
                    prepared = true;
                    if (prepareSucceeds) vm.SetExplorerHwnd((IntPtr)456);
                    return System.Threading.Tasks.Task.FromResult(prepareSucceeds);
                };
                Action reveal = () => revealed = true;
                System.Threading.Tasks.Task operation = duplicate
                    ? vm.DuplicateTabAsync(source, prepare, reveal)
                    : vm.InsertTabWithPathAsync(source.Path, before, prepare, reveal);
                operation.GetAwaiter().GetResult();
                Assert.IsTrue(prepared);
                Assert.IsTrue(revealed);
                Assert.AreEqual(before + (prepareSucceeds ? 1 : 0), vm.Tabs.Count);
                if (prepareSucceeds)
                {
                    Assert.AreEqual(explorer.AllControlPanelPath, vm.ActiveTab.Path);
                    Assert.AreEqual((IntPtr)456, vm.ExplorerHwnd);
                }
                else Assert.AreSame(original, vm.ActiveTab);
            }
        }

        [TestMethod]
        public void Recheck_NavigateException_RestoresSelectionAndPreservesTargetPath()
        {
            ThrowingNavigationExplorer explorer = new ThrowingNavigationExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(target);
                vm.SelectTab(target);
                Assert.AreSame(original, vm.ActiveTab);
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\B", target.Path);
                explorer.Current = @"C:\B";
                vm.NavigationTracker.InvalidateCache();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(target, vm.ActiveTab);
                Assert.AreEqual(@"C:\A", original.Path);
                Assert.AreEqual(1, explorer.NavigateCalls);
            }
        }

        [TestMethod]
        public void Recheck_ClosePendingDuplicate_SelectsRemainingTab()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel first = new TabItemViewModel(@"C:\B", "B1", explorer);
                TabItemViewModel second = new TabItemViewModel(@"C:\B", "B2", explorer);
                vm.Tabs.Add(first); vm.Tabs.Add(second);
                vm.SelectTab(first);
                vm.CloseTabsAsync(1, 1, path => System.Threading.Tasks.Task.FromResult(true), delegate { }).GetAwaiter().GetResult();
                Assert.AreSame(second, vm.ActiveTab);
                Assert.AreEqual(1, vm.ActiveTabIndex);
                Assert.AreEqual(@"C:\B", vm.NavigationTracker.NavigatingToPath);
            }
        }

        [TestMethod]
        public void Recheck_ReopenDisposedWhilePreparing_PreservesTabsAndHistory()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel closed = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(closed); vm.CloseTab(closed);
                System.Threading.Tasks.TaskCompletionSource<bool> ready = new System.Threading.Tasks.TaskCompletionSource<bool>();
                bool revealed = false;
                System.Threading.Tasks.Task reopening = vm.ReopenClosedTabAsync(path => ready.Task,
                    action => { try { action(); } finally { revealed = true; } });
                vm.Dispose(); ready.SetResult(true);
                reopening.GetAwaiter().GetResult();
                Assert.AreEqual(1, vm.Tabs.Count);
                Assert.IsTrue(vm.HasClosedTabs);
                Assert.IsTrue(revealed);
                Assert.IsFalse(vm.IsTabOperationPending);
            }
        }

        [TestMethod]
        public void Recheck_DragUnavailableDestination_DoesNotLaunchOrReportSuccess()
        {
            UnavailablePathExplorerService explorer = new UnavailablePathExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                vm.Tabs.Add(new TabItemViewModel(@"Z:\SleepingDrive", "Unavailable", explorer));
                bool opened = false;
                bool result = KjTabBar.Views.TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTab(
                    System.Windows.DragDropEffects.None, original, original.Path, vm,
                    path => { opened = true; return true; },
                    new KjTabBar.Helpers.NativeMethods.POINT { X = 0, Y = 0 },
                    new KjTabBar.Helpers.NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 });
                Assert.IsFalse(result);
                Assert.IsFalse(opened);
                Assert.IsTrue(vm.Tabs.Contains(original));
            }
        }

        private sealed class ThrowingNavigationExplorer : MockExplorerService
        {
            internal string Current = @"C:\A";
            internal int NavigateCalls;
            public override string GetCurrentPath(IntPtr hwnd) { return Current; }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                NavigateCalls++;
                throw new TimeoutException("Simulated Shell worker timeout");
            }
        }

        [TestMethod]
        public void Recheck_DragAsync_PreparesRemainingHostBeforeClosingSource()
        {
            VerifyAsyncDetach(true, true);
        }

        [TestMethod]
        public void Recheck_DragAsync_LaunchFailure_DoesNotSwitchHost()
        {
            VerifyAsyncDetach(false, true);
        }

        [TestMethod]
        public void Recheck_DragAsync_PreparationFailure_KeepsSourceAndReturnsFalse()
        {
            VerifyAsyncDetach(true, false);
        }

        private static void VerifyAsyncDetach(bool launchSucceeds, bool prepareSucceeds)
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, explorer.AllControlPanelPath))
            {
                TabItemViewModel source = vm.ActiveTab;
                TabItemViewModel remaining = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(remaining);
                int prepares = 0, reveals = 0;
                KjTabBar.Views.TabDetachResult result = KjTabBar.Views.TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTabAsync(
                    System.Windows.DragDropEffects.None, source, source.Path, vm, path => launchSucceeds,
                    new KjTabBar.Helpers.NativeMethods.POINT { X = 0, Y = 0 },
                    new KjTabBar.Helpers.NativeMethods.RECT { Left = 100, Top = 100, Right = 300, Bottom = 300 },
                    path =>
                    {
                        prepares++;
                        Assert.AreEqual(remaining.Path, path);
                        Assert.AreSame(source, vm.ActiveTab);
                        Assert.IsTrue(vm.IsPreparedTabOperationCurrent());
                        if (prepareSucceeds) vm.SetExplorerHwnd((IntPtr)456);
                        return System.Threading.Tasks.Task.FromResult(prepareSucceeds);
                    }, () => reveals++).GetAwaiter().GetResult();
                bool closed = result == KjTabBar.Views.TabDetachResult.Completed || result == KjTabBar.Views.TabDetachResult.ClosePending;
                Assert.AreEqual(launchSucceeds && prepareSucceeds, closed);
                if (!launchSucceeds) Assert.AreEqual(KjTabBar.Views.TabDetachResult.NotOpened, result);
                else if (!prepareSucceeds) Assert.AreEqual(KjTabBar.Views.TabDetachResult.WindowOpenedSourceRetained, result);
                Assert.AreEqual(launchSucceeds ? 1 : 0, prepares);
                Assert.AreEqual(prepares, reveals);
                Assert.AreEqual(!closed, vm.Tabs.Contains(source));
                Assert.AreSame(closed ? remaining : source, vm.ActiveTab);
            }
        }

        private TabBarViewModel CreateViewModel()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            MockExplorerService mockExplorer = new MockExplorerService();
            return new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);
        }

        [TestMethod]
        public void Load_TabBarViewModel_Applies_UserSettings()
        {
            MockUserSettings mockSettings = new MockUserSettings
            {
                FontFamily = "Consolas",
                FontSize = 20.0,
                IsBold = true,
                IsItalic = true
            };

            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            Assert.AreEqual("Consolas", vm.FontFamily.Source);
            Assert.AreEqual(20.0, vm.FontSize);
            Assert.AreEqual(System.Windows.FontWeights.Bold, vm.FontWeight);
            Assert.AreEqual(System.Windows.FontStyles.Italic, vm.FontStyle);
        }

        [TestMethod]
        public void Constructor_Uses_InitialPath_Without_Querying_ExplorerPath()
        {
            CountingInitialPathExplorerService mockExplorer = new CountingInitialPathExplorerService();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), mockExplorer, @"C:\Resolved");

            Assert.AreEqual(0, mockExplorer.GetCurrentPathCallCount);
            Assert.AreEqual(@"C:\Resolved", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\Resolved", vm.ActiveTab.Path);
        }

        [TestMethod]
        public void SettingsChanged_Updates_TabBarViewModel()
        {
            MockUserSettings mockSettings = new MockUserSettings
            {
                FontFamily = "Arial",
                FontSize = 12.0,
                IsBold = false,
                IsItalic = false
            };

            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            mockSettings.FontFamily = "Courier New";
            mockSettings.FontSize = 16.0;
            mockSettings.IsBold = true;
            mockSettings.TriggerChange();

            Assert.AreEqual("Courier New", vm.FontFamily.Source);
            Assert.AreEqual(16.0, vm.FontSize);
            Assert.AreEqual(System.Windows.FontWeights.Bold, vm.FontWeight);
        }

        [TestMethod]
        public void Dispose_Stops_Reacting_To_SettingsChanged()
        {
            MockUserSettings mockSettings = new MockUserSettings
            {
                FontFamily = "Arial",
                FontSize = 12.0,
                IsBold = false,
                IsItalic = false
            };

            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.Dispose();

            mockSettings.FontFamily = "Courier New";
            mockSettings.FontSize = 16.0;
            mockSettings.IsBold = true;
            mockSettings.TriggerChange();

            Assert.AreEqual("Arial", vm.FontFamily.Source);
            Assert.AreEqual(12.0, vm.FontSize);
            Assert.AreEqual(System.Windows.FontWeights.Normal, vm.FontWeight);
        }

        [TestMethod]
        public void CloseTabs_PreparesNormalHostBeforeClosingControlPanel()
        {
            VerifyCloseTabsHostSwitch(false, false);
        }

        [TestMethod]
        public void CloseTabs_LastControlPanelTab_PreparesHomeHost()
        {
            VerifyCloseTabsHostSwitch(true, false);
        }

        [TestMethod]
        public void CloseTabs_FailedHostSwitch_KeepsTabAndHistory()
        {
            VerifyCloseTabsHostSwitch(false, true);
        }

        [TestMethod]
        public void CloseTabs_Range_PreparesRemainingTabAndRecordsBatch()
        {
            MockExplorerService explorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Remaining");
            vm.InsertTabWithPath(@"C:\Other", 1);
            vm.InsertTabWithPath(explorer.AllControlPanelPath, 2, true);
            bool prepared = false;
            vm.CloseTabsAsync(1, 2, path =>
            {
                Assert.AreEqual(@"C:\Remaining", path);
                Assert.AreEqual(explorer.AllControlPanelPath, vm.ActiveTab.Path);
                prepared = true;
                return System.Threading.Tasks.Task.FromResult(true);
            }, null).GetAwaiter().GetResult();
            Assert.IsTrue(prepared);
            Assert.AreEqual(1, vm.Tabs.Count);
            vm.ReopenClosedTab();
            Assert.AreEqual(3, vm.Tabs.Count);
        }

        [TestMethod]
        public void CloseTabs_InactiveTab_DoesNotSwitchHost()
        {
            TabBarViewModel vm = CreateViewModel();
            TabItemViewModel closedTab = vm.ActiveTab;
            vm.InsertTabWithPath(@"C:\Selected", 1);
            TabItemViewModel activeTab = vm.ActiveTab;
            vm.CloseTabsAsync(0, 1, path =>
            {
                Assert.Fail("Closing an inactive tab must not switch the host.");
                return System.Threading.Tasks.Task.FromResult(false);
            }, () => Assert.Fail("No host needs revealing.")).GetAwaiter().GetResult();
            Assert.AreSame(activeTab, vm.ActiveTab);
            Assert.IsFalse(vm.Tabs.Contains(closedTab));
        }

        [TestMethod]
        public void CloseTabs_ChangedCollectionWhilePreparing_DoesNotCloseAnotherTab()
        {
            TabBarViewModel vm = CreateViewModel();
            TabItemViewModel closedTab = vm.ActiveTab;
            System.Threading.Tasks.TaskCompletionSource<bool> prepared = new System.Threading.Tasks.TaskCompletionSource<bool>();
            bool revealed = false;
            System.Threading.Tasks.Task closing = vm.CloseTabsAsync(0, 1, path => prepared.Task, () => revealed = true);
            vm.InsertTabWithPath(@"C:\AddedWhileWaiting", 0);
            prepared.SetResult(true);
            closing.GetAwaiter().GetResult();
            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.IsTrue(vm.Tabs.Contains(closedTab));
            Assert.IsFalse(vm.HasClosedTabs);
            Assert.IsTrue(revealed);
        }

        [TestMethod]
        public void CloseTabs_ControlPanel_UsesParkedNormalExplorerForNavigation()
        {
            CloseHostExplorerService explorer = new CloseHostExplorerService();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorer, explorer.AllControlPanelPath);
            vm.RestoreTabs(new string[] { explorer.AllControlPanelPath, @"C:\Remaining" }, explorer.AllControlPanelPath, 0, true);
            KjTabBar.Models.ExplorerWindowTrackingState tracking = new KjTabBar.Models.ExplorerWindowTrackingState();
            tracking.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);
            IntPtr shownHwnd = IntPtr.Zero;
            KjTabBar.Services.ExplorerHostSwitchCoordinator coordinator = new KjTabBar.Services.ExplorerHostSwitchCoordinator(
                explorer, tracking,
                (model, hwnd) => { model.SetExplorerHwnd(hwnd); return true; },
                hwnd => shownHwnd = hwnd, delegate { }, delegate { },
                delegate { return true; }, () => new System.Collections.Generic.List<IntPtr>(),
                hwnd => explorer.GetCurrentPath(hwnd), delegate { return false; }, delegate { });
            vm.CloseTabsAsync(0, 1, path => coordinator.PrepareForPathAsync(vm, path),
                coordinator.CompletePendingReveal).GetAwaiter().GetResult();
            Assert.AreEqual((IntPtr)100, explorer.NavigatedHwnd);
            Assert.AreEqual((IntPtr)100, shownHwnd);
            Assert.AreEqual(@"C:\Remaining", vm.ActiveTab.Path);
        }

        private sealed class CloseHostExplorerService : MockExplorerService
        {
            public IntPtr NavigatedHwnd;
            public override string GetCurrentPath(IntPtr hwnd)
            {
                return hwnd == (IntPtr)200 ? AllControlPanelPath : @"C:\Previous";
            }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                NavigatedHwnd = hwnd;
                return true;
            }
        }

        private static void VerifyCloseTabsHostSwitch(bool lastTab, bool reject)
        {
            MockExplorerService explorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.AllControlPanelPath);
            TabItemViewModel closedTab = vm.ActiveTab;
            if (!lastTab)
            {
                vm.InsertTabWithPath(@"C:\Remaining", 1);
                vm.SelectTab(closedTab);
            }
            int originalCount = vm.Tabs.Count;
            bool prepared = false;
            bool revealed = false;
            vm.CloseTabsAsync(0, 1, path =>
            {
                Assert.AreSame(closedTab, vm.ActiveTab, "Prepare while the original host tab is still active.");
                Assert.AreEqual(originalCount, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
                Assert.AreEqual(lastTab ? explorer.GetResolvedHomeFolderPath() : @"C:\Remaining", path);
                prepared = true;
                if (!reject) vm.SetExplorerHwnd((IntPtr)200);
                return System.Threading.Tasks.Task.FromResult(!reject);
            }, () => revealed = true).GetAwaiter().GetResult();

            Assert.IsTrue(prepared, "Closing must prepare the destination Explorer host.");
            if (reject)
            {
                Assert.AreEqual(originalCount, vm.Tabs.Count);
                Assert.AreSame(closedTab, vm.ActiveTab);
                Assert.IsFalse(vm.HasClosedTabs);
            }
            else
            {
                Assert.AreEqual((IntPtr)200, vm.ExplorerHwnd);
                Assert.AreEqual(lastTab ? explorer.GetResolvedHomeFolderPath() : @"C:\Remaining", vm.ActiveTab.Path);
                Assert.IsFalse(vm.Tabs.Contains(closedTab));
                Assert.IsTrue(vm.HasClosedTabs);
                Assert.IsTrue(revealed);
            }
        }

        [TestMethod]
        public void CloseTabsToRight_Closes_All_Tabs_To_The_Right_Of_Specified_Tab()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            // Initially has 1 tab (C:\MockPath from GetCurrentPath)
            vm.InsertTabWithPath(@"C:\Tab1", 1);
            vm.InsertTabWithPath(@"C:\Tab2", 2);
            vm.InsertTabWithPath(@"C:\Tab3", 3);
            vm.InsertTabWithPath(@"C:\Tab4", 4);
            // Tabs: [C:\MockPath, C:\Tab1, C:\Tab2, C:\Tab3, C:\Tab4]

            TabItemViewModel targetTab = vm.Tabs[2]; // C:\Tab2
            vm.CloseTabsToRight(targetTab);

            Assert.AreEqual(3, vm.Tabs.Count);
            Assert.AreEqual(@"C:\MockPath", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\Tab1", vm.Tabs[1].Path);
            Assert.AreEqual(@"C:\Tab2", vm.Tabs[2].Path);
        }

        [TestMethod]
        public void CloseTabsToLeft_Closes_All_Tabs_To_The_Left_Of_Specified_Tab()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\Tab1", 1);
            vm.InsertTabWithPath(@"C:\Tab2", 2);
            vm.InsertTabWithPath(@"C:\Tab3", 3);
            vm.InsertTabWithPath(@"C:\Tab4", 4);
            // Tabs: [C:\MockPath, C:\Tab1, C:\Tab2, C:\Tab3, C:\Tab4]

            TabItemViewModel targetTab = vm.Tabs[2]; // C:\Tab2
            vm.CloseTabsToLeft(targetTab);

            Assert.AreEqual(3, vm.Tabs.Count);
            Assert.AreEqual(@"C:\Tab2", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\Tab3", vm.Tabs[1].Path);
            Assert.AreEqual(@"C:\Tab4", vm.Tabs[2].Path);
        }

        [TestMethod]
        public void ReopenClosedTab_Restores_Last_Closed_Tab()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\Tab1", 1);
            vm.InsertTabWithPath(@"C:\Tab2", 2);
            // Tabs: [C:\MockPath, C:\Tab1, C:\Tab2]

            TabItemViewModel tabToClose = vm.Tabs[1]; // C:\Tab1
            vm.CloseTab(tabToClose);
            // Tabs: [C:\MockPath, C:\Tab2]

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.IsTrue(vm.HasClosedTabs);

            vm.ReopenClosedTab();
            // Tabs: [C:\MockPath, C:\Tab1, C:\Tab2]

            Assert.AreEqual(3, vm.Tabs.Count);
            Assert.AreEqual(@"C:\Tab1", vm.Tabs[1].Path);
            Assert.IsFalse(vm.HasClosedTabs);
        }

        [TestMethod]
        public void ReopenClosedTab_Batch_Restores_Multiple_Tabs_From_RightClose()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            MockExplorerService mockExplorer = new MockExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\Tab1", 1);
            vm.InsertTabWithPath(@"C:\Tab2", 2);
            vm.InsertTabWithPath(@"C:\Tab3", 3);
            vm.InsertTabWithPath(@"C:\Tab4", 4);
            // Tabs: [C:\MockPath, C:\Tab1, C:\Tab2, C:\Tab3, C:\Tab4]

            TabItemViewModel targetTab = vm.Tabs[1]; // C:\Tab1
            vm.CloseTabsToRight(targetTab);
            // Tabs: [C:\MockPath, C:\Tab1]

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.IsTrue(vm.HasClosedTabs);

            vm.ReopenClosedTab();
            // Should restore all 3 tabs: Tab2, Tab3, Tab4

            Assert.AreEqual(5, vm.Tabs.Count);
            Assert.AreEqual(@"C:\Tab2", vm.Tabs[2].Path);
            Assert.AreEqual(@"C:\Tab3", vm.Tabs[3].Path);
            Assert.AreEqual(@"C:\Tab4", vm.Tabs[4].Path);
        }

        [TestMethod]
        public void UpdateTabTitles_Disambiguates_Duplicate_Folder_Names_By_Parent_Folder()
        {
            CustomMockExplorerService mockExplorer = new CustomMockExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.Tabs.Clear();
            vm.InsertTabWithPath(@"C:\ProjectA\Work", 0);
            vm.InsertTabWithPath(@"C:\ProjectB\Work", 1);

            Assert.AreEqual(@"ProjectA\Work", vm.Tabs[0].Title);
            Assert.AreEqual(@"ProjectB\Work", vm.Tabs[1].Title);
        }

        [TestMethod]
        public void UpdateTabTitles_Further_Disambiguates_If_Parent_Is_Also_Same()
        {
            CustomMockExplorerService mockExplorer = new CustomMockExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.Tabs.Clear();
            vm.InsertTabWithPath(@"C:\Client1\Project\Sub", 0);
            vm.InsertTabWithPath(@"C:\Client2\Project\Sub", 1);

            Assert.AreEqual(@"Client1\Project\Sub", vm.Tabs[0].Title);
            Assert.AreEqual(@"Client2\Project\Sub", vm.Tabs[1].Title);
        }

        [TestMethod]
        public void UpdateTitles_Reuses_Existing_BaseTitles()
        {
            CountingFolderNameExplorerService explorerService = new CountingFolderNameExplorerService();
            System.Collections.ObjectModel.ObservableCollection<TabItemViewModel> tabs =
                new System.Collections.ObjectModel.ObservableCollection<TabItemViewModel>
                {
                    new TabItemViewModel(@"C:\ProjectA\Work", "Work", explorerService),
                    new TabItemViewModel(@"C:\ProjectB\Other", "Other", explorerService)
                };

            TabTitleDisambiguator.UpdateTitles(tabs, explorerService);
            TabTitleDisambiguator.UpdateTitles(tabs, explorerService);

            Assert.AreEqual(0, explorerService.GetFolderNameCallCount);
        }

        [TestMethod]
        public void ShortenTitle_Handles_Non_Absolute_Path_Without_Losing_Parent()
        {
            var vm = CreateViewModel();
            // 10文字制限で、15文字の親 + \ + 4文字の子 = 20文字 (短縮あり)
            string title20 = "ParentFolderABC\\Work";
            string result20 = (string)typeof(TabBarViewModel).GetMethod("ShortenTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(vm, new object[] { title20, 10 });

            // 修正前は "\...\Work" になっていた
            // 修正後は中央省略形式（例: "Par...Work"）になるはず
            Assert.AreNotEqual("\\...\\Work", result20, "Should not use root-anchored shortening for non-absolute path.");
            Assert.IsTrue(result20.StartsWith("Par"), $"Shortened title '{result20}' should preserve the start of the title.");
        }

        [TestMethod]
        public void RestoreTabs_DoesNotAppendInitialExplorerPath_WhenSavedLayoutExists()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            RestoreTabsExplorerService mockExplorer = new RestoreTabsExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.RestoreTabs(new string[] { @"C:\SavedA", @"C:\SavedB" });

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.AreEqual(@"C:\SavedA", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\SavedB", vm.Tabs[1].Path);
            Assert.AreEqual(@"C:\SavedA", vm.ActiveTab.Path);
            Assert.AreEqual(@"C:\SavedA", mockExplorer.LastNavigatedPath);
        }

        [TestMethod]
        public void RestoreTabs_Restores_Unc_Path_From_Persisted_Data()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            RestoreTabsExplorerService mockExplorer = new RestoreTabsExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.RestoreTabs(new string[]
            {
                @"\\server\share\folder",
                @"C:\SavedB"
            });

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.AreEqual(@"\\server\share\folder", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\SavedB", vm.Tabs[1].Path);
        }

        [TestMethod]
        public void RestoreTabs_Selects_Persisted_Active_Path_When_Provided()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            RestoreTabsExplorerService mockExplorer = new RestoreTabsExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.RestoreTabs(new string[] { @"C:\SavedA", @"C:\SavedB" }, @"C:\SavedB");

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.AreEqual(@"C:\SavedB", vm.ActiveTab.Path);
            Assert.AreEqual(@"C:\SavedB", mockExplorer.LastNavigatedPath);
        }

        [TestMethod]
        public void RestoreTabs_Selects_Persisted_Active_Index_When_Duplicate_Paths_Exist()
        {
            MockUserSettings mockSettings = new MockUserSettings();
            RestoreTabsExplorerService mockExplorer = new RestoreTabsExplorerService();
            TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, mockSettings, mockExplorer);

            vm.RestoreTabs(
                new string[] { @"C:\Desktop", @"C:\Work", @"C:\Desktop" },
                @"C:\Desktop",
                2);

            Assert.AreEqual(3, vm.Tabs.Count);
            Assert.AreSame(vm.Tabs[2], vm.ActiveTab);
            Assert.AreEqual(@"C:\Desktop", vm.ActiveTab.Path);
            Assert.AreEqual(@"C:\Desktop", mockExplorer.LastNavigatedPath);
        }

        [TestMethod]
        public void NavigationTracker_ActivatesExplorerHostSwitchGracePeriod()
        {
            TabNavigationStateTracker tracker = new TabNavigationStateTracker();

            tracker.NotifyExplorerHostChanged();

            Assert.IsTrue(tracker.IsExplorerHostSwitchGraceActive(DateTime.UtcNow));
            Assert.IsFalse(tracker.IsExplorerHostSwitchGraceActive(DateTime.UtcNow.AddSeconds(1)));
        }

        [TestMethod]
        public void SelectTab_DoesNotNavigate_When_ControlPanelItemDiffers_Only_By_EmbeddedNullSuffix()
        {
            ControlPanelAliasExplorerService mockExplorer = new ControlPanelAliasExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(mockExplorer.PowerOptionsPath, 1, true);
            mockExplorer.NavigateCallCount = 0;

            vm.SelectTab(vm.Tabs[1]);

            Assert.AreEqual(0, mockExplorer.NavigateCallCount);
            Assert.AreEqual(mockExplorer.PowerOptionsPath, vm.ActiveTab.Path);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_SelectsPendingItemsBeforeClearingNavigation()
        {
            MockExplorerService mockExplorer = new MockExplorerService();
            string currentPath = @"C:\Original";
            mockExplorer.GetCurrentPathFunc = delegate (IntPtr hwnd) { return currentPath; };
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), mockExplorer, currentPath);
            System.Collections.Generic.List<string> selectedItems = new System.Collections.Generic.List<string>
            {
                @"C:\Target\Selected.txt"
            };

            vm.AddTabWithPathAndSelect(@"C:\Target", selectedItems);
            currentPath = @"C:\Target";
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, mockExplorer.SelectItemsCallCount);
            Assert.AreEqual((IntPtr)123, mockExplorer.LastSelectItemsHwnd);
            CollectionAssert.AreEqual(selectedItems, mockExplorer.LastSelectedItems);
            Assert.IsNull(vm.NavigationTracker.PendingSelectedItems);
            Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_DiscardsPathFromPreviousExplorerHost()
        {
            BlockingPathExplorerService mockExplorer = new BlockingPathExplorerService();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), mockExplorer, @"C:\Initial");
            System.Threading.Tasks.Task syncTask = vm.SyncWithExplorerAsync();

            try
            {
                Assert.IsTrue(mockExplorer.WaitUntilPathReadStarts(1000));
                vm.SetExplorerHwnd((IntPtr)456);
            }
            finally
            {
                mockExplorer.ReleasePathRead();
            }

            syncTask.GetAwaiter().GetResult();

            Assert.AreEqual((IntPtr)123, mockExplorer.RequestedExplorerHwnd);
            Assert.AreEqual(@"C:\Initial", vm.ActiveTab.Path);
            Assert.IsNull(vm.NavigationTracker.CachedExplorerPath);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_KeepsItemNavigationPending_WhileControlPanelParentIsStillDisplayed()
        {
            SynchronizerControlPanelExplorerService explorer = new SynchronizerControlPanelExplorerService();
            explorer.CurrentPath = explorer.AllControlPanelPath;
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer);
            viewModel.RestoreTabs(new string[] { explorer.PowerOptionsPath }, explorer.PowerOptionsPath, 0, true);
            viewModel.SelectTab(viewModel.ActiveTab);

            viewModel.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(explorer.PowerOptionsPath, viewModel.ActiveTab.Path);
            Assert.AreEqual(explorer.PowerOptionsPath, viewModel.NavigationTracker.NavigatingToPath);
            explorer.CurrentPath = explorer.PowerOptionsPath;
            viewModel.SyncWithExplorerAsync().GetAwaiter().GetResult();
            Assert.AreEqual(explorer.PowerOptionsPath, viewModel.ActiveTab.Path);
            Assert.IsNull(viewModel.NavigationTracker.NavigatingToPath);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_UpdatesActiveControlPanelItemTab_WhenSeparateRootTabExists()
        {
            SynchronizerControlPanelExplorerService mockExplorer = new SynchronizerControlPanelExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(mockExplorer.AllControlPanelPath, 1, true);
            // Complete the preceding request before simulating ordinary Explorer navigation.
            mockExplorer.CurrentPath = vm.Tabs[1].Path;
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
            mockExplorer.CurrentPath = mockExplorer.PowerOptionsPath;
            vm.NavigationTracker.InvalidateCache();
            vm.SelectTab(vm.Tabs[0]);

            mockExplorer.CurrentPath = mockExplorer.AllControlPanelPath;
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.ActiveTab.Path);
            Assert.AreEqual(vm.Tabs[0], vm.ActiveTab);
            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.Tabs[0].Path);
            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.Tabs[1].Path);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_UpdatesActiveControlPanelRootTab_WhenSeparateItemTabExists()
        {
            SynchronizerControlPanelExplorerService mockExplorer = new SynchronizerControlPanelExplorerService();
            mockExplorer.CurrentPath = mockExplorer.AllControlPanelPath;
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(mockExplorer.PowerOptionsPath, 1, true);
            // Complete the preceding request before simulating ordinary Explorer navigation.
            mockExplorer.CurrentPath = vm.Tabs[1].Path;
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            // Revert active tab back to Tabs[0] (AllControlPanelPath)
            mockExplorer.CurrentPath = mockExplorer.AllControlPanelPath;
            vm.NavigationTracker.InvalidateCache();
            vm.SelectTab(vm.Tabs[0]);

            mockExplorer.CurrentPath = mockExplorer.PowerOptionsPath;
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(mockExplorer.PowerOptionsPath, vm.ActiveTab.Path);
            Assert.AreEqual(vm.Tabs[0], vm.ActiveTab);
            Assert.AreEqual(mockExplorer.PowerOptionsPath, vm.Tabs[0].Path);
            Assert.AreEqual(mockExplorer.PowerOptionsPath, vm.Tabs[1].Path);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_UpdatesActiveTabPath_InsteadOfJumpingToExistingMatchingTab()
        {
            SynchronizerControlPanelExplorerService mockExplorer = new SynchronizerControlPanelExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\Data", 1, false);
            // Complete the preceding request before simulating ordinary Explorer navigation.
            mockExplorer.CurrentPath = vm.Tabs[1].Path;
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
            mockExplorer.CurrentPath = mockExplorer.PowerOptionsPath;
            vm.NavigationTracker.InvalidateCache();
            vm.SelectTab(vm.Tabs[0]);

            mockExplorer.CurrentPath = @"C:\Data";
            vm.NavigationTracker.InvalidateCache();
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(@"C:\Data", vm.ActiveTab.Path);
            Assert.AreEqual(vm.Tabs[0], vm.ActiveTab);
            Assert.AreEqual(@"C:\Data", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\Data", vm.Tabs[1].Path);
        }

        [TestMethod]
        public void SelectTab_UsesRecentCachedExplorerPath_WithoutRefreshingCurrentPath()
        {
            CachedPathExplorerService mockExplorer = new CachedPathExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\CachedTarget", 1, false);
            mockExplorer.GetCurrentPathCallCount = 0;

            object tracker = typeof(TabBarViewModel)
                .GetProperty("NavigationTracker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(vm, null);
            tracker.GetType()
                .GetMethod("UpdateCache", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Invoke(tracker, new object[] { @"C:\CachedTarget", DateTime.UtcNow });

            vm.SelectTab(vm.Tabs[1]);

            Assert.AreEqual(0, mockExplorer.GetCurrentPathCallCount);
            Assert.AreEqual(1, mockExplorer.NavigateCallCount);
        }

        [TestMethod]
        public void SelectTab_DoesNotCloseTab_When_PathIsTemporarilyUnavailable()
        {
            UnavailablePathExplorerService mockExplorer = new UnavailablePathExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"Z:\SleepingDrive", 1, false);
            TabItemViewModel originalActiveTab = vm.ActiveTab;
            TabItemViewModel unavailableTab = vm.Tabs[1];

            vm.SelectTab(unavailableTab);

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.AreSame(unavailableTab, vm.Tabs[1]);
            Assert.AreSame(originalActiveTab, vm.ActiveTab);
            Assert.AreEqual(0, mockExplorer.NavigateCallCount);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_Removes_Unavailable_Inactive_Tab()
        {
            DeletedInactiveTabExplorerService mockExplorer = new DeletedInactiveTabExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\DeletedFolder", 1, false);
            vm.InsertTabWithPath(@"C:\OtherAlive", 2, false);
            vm.SelectTab(vm.Tabs[0]);

            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(2, vm.Tabs.Count);
            Assert.AreEqual(@"C:\Alive", vm.ActiveTab.Path);
            Assert.AreEqual(@"C:\Alive", vm.Tabs[0].Path);
            Assert.AreEqual(@"C:\OtherAlive", vm.Tabs[1].Path);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_Removes_Unavailable_Active_Tab_And_Selects_Existing_Matching_Tab()
        {
            DeletedActiveTabExplorerService mockExplorer = new DeletedActiveTabExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.Tabs.Clear();
            vm.InsertTabWithPath(@"C:\Desktop", 0, false);
            vm.InsertTabWithPath(@"C:\DeletedFolder", 1, false);
            vm.SelectTab(vm.Tabs[1]);

            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, vm.Tabs.Count);
            Assert.AreEqual(@"C:\Desktop", vm.ActiveTab.Path);
            Assert.AreEqual(@"C:\Desktop", vm.Tabs[0].Path);
        }

        private class CustomMockExplorerService : MockExplorerService
        {
            public override string GetFolderName(string path)
            {
                if (string.IsNullOrEmpty(path)) return "Home";
                if (path.StartsWith("::{")) return "Special";
                try
                {
                    return Path.GetFileName(path.TrimEnd('\\')) ?? path;
                }
                catch
                {
                    return "MockFolder";
                }
            }
        }

        private sealed class CountingFolderNameExplorerService : MockExplorerService
        {
            public int GetFolderNameCallCount { get; private set; }

            public override string GetFolderName(string path)
            {
                GetFolderNameCallCount++;
                return path;
            }
        }

        private sealed class CountingInitialPathExplorerService : MockExplorerService
        {
            public int GetCurrentPathCallCount { get; private set; }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                GetCurrentPathCallCount++;
                return @"C:\SlowCurrentPath";
            }

            public override string GetFolderName(string path)
            {
                return path;
            }
        }

        private sealed class RestoreTabsExplorerService : MockExplorerService
        {
            public string LastNavigatedPath { get; private set; }

            public override string GetFolderName(string path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return "Home";
                }

                return path;
            }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return @"C:\InitialOnly";
            }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                LastNavigatedPath = path;
                return true;
            }
        }

        private sealed class ControlPanelAliasExplorerService : MockExplorerService
        {
            private readonly ShellPathNormalizer _normalizer;

            public ControlPanelAliasExplorerService()
            {
                AllControlPanelPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
                HomeFolderPath = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
                ProgramsAndFeaturesPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}";
                PowerOptionsPath = "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";

                ShellLocationNameResolver locationResolver = new ShellLocationNameResolver(
                    AllControlPanelPath,
                    HomeFolderPath,
                    ProgramsAndFeaturesPath,
                    PowerOptionsPath,
                    delegate (string title) { return null; });
                _normalizer = new ShellPathNormalizer(
                    AllControlPanelPath,
                    HomeFolderPath,
                    ProgramsAndFeaturesPath,
                    PowerOptionsPath,
                    delegate { return "コントロール パネル"; },
                    delegate { return "ホーム"; },
                    delegate { return "ネットワーク"; },
                    delegate { return "ごみ箱"; },
                    delegate { return "PC"; },
                    delegate { return @"C:\Users\TestUser"; },
                    locationResolver,
                    delegate (string path) { return null; });

                IsControlPanelPathFunc = delegate (string path) { return _normalizer.IsControlPanelPath(path); };
                NormalizeKnownPathFunc = delegate (string path) { return _normalizer.NormalizeKnownPath(path); };
            }

            public int NavigateCallCount { get; set; }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return PowerOptionsPath + '\0' + "\\::{00000000-0000-0000-0000-000000000000}";
            }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                NavigateCallCount++;
                return true;
            }

            public override string GetFolderName(string path)
            {
                return "Power Options";
            }
        }

        private sealed class BlockingPathExplorerService : MockExplorerService
        {
            private readonly System.Threading.ManualResetEventSlim _pathReadStarted = new System.Threading.ManualResetEventSlim(false);
            private readonly System.Threading.ManualResetEventSlim _releasePathRead = new System.Threading.ManualResetEventSlim(false);

            public IntPtr RequestedExplorerHwnd { get; private set; }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                RequestedExplorerHwnd = explorerHwnd;
                _pathReadStarted.Set();
                _releasePathRead.Wait();
                return @"C:\Stale";
            }

            public bool WaitUntilPathReadStarts(int millisecondsTimeout)
            {
                return _pathReadStarted.Wait(millisecondsTimeout);
            }

            public void ReleasePathRead()
            {
                _releasePathRead.Set();
            }
        }

        private sealed class SynchronizerControlPanelExplorerService : MockExplorerService
        {
            public SynchronizerControlPanelExplorerService()
            {
                AllControlPanelPath = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
                PowerOptionsPath = "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";
                CurrentPath = PowerOptionsPath;
                IsControlPanelPathFunc = delegate (string path)
                {
                    return string.Equals(path, AllControlPanelPath, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(path, PowerOptionsPath, StringComparison.OrdinalIgnoreCase);
                };
                IsControlPanelRootPathFunc = delegate (string path)
                {
                    return string.Equals(path, AllControlPanelPath, StringComparison.OrdinalIgnoreCase);
                };
                NormalizeKnownPathFunc = delegate (string path) { return path; };
            }

            public string CurrentPath { get; set; }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return CurrentPath;
            }

            public override string GetFolderName(string path)
            {
                if (string.Equals(path, AllControlPanelPath, StringComparison.OrdinalIgnoreCase))
                {
                    return "Control Panel";
                }

                if (string.Equals(path, PowerOptionsPath, StringComparison.OrdinalIgnoreCase))
                {
                    return "Power Options";
                }

                return path;
            }
        }

        private sealed class CachedPathExplorerService : MockExplorerService
        {
            public int GetCurrentPathCallCount { get; set; }
            public int NavigateCallCount { get; set; }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                GetCurrentPathCallCount++;
                return @"C:\CurrentFromExplorer";
            }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                NavigateCallCount++;
                return true;
            }

            public override string GetFolderName(string path)
            {
                return path;
            }
        }

        private sealed class UnavailablePathExplorerService : MockExplorerService
        {
            public int NavigateCallCount { get; set; }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                NavigateCallCount++;
                return true;
            }

            public override string GetFolderName(string path)
            {
                return path;
            }

            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                return !string.Equals(path, @"Z:\SleepingDrive", StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class DeletedInactiveTabExplorerService : MockExplorerService
        {
            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return @"C:\Alive";
            }

            public override string GetFolderName(string path)
            {
                return path;
            }

            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                return !string.Equals(path, @"C:\DeletedFolder", StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class DeletedActiveTabExplorerService : MockExplorerService
        {
            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return @"C:\Desktop";
            }

            public override string GetFolderName(string path)
            {
                return path;
            }

            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                return !string.Equals(path, @"C:\DeletedFolder", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
