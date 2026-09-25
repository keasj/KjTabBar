using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class Release137RegressionTests
    {
        private sealed class AsyncExplorer : MockExplorerService, IAsyncExplorerService
        {
            internal string Current = @"C:\A";
            internal Func<IntPtr, Task<string>> Read;
            internal Func<string, Task<bool>> Move;
            internal int Moves;
            public override string GetCurrentPath(IntPtr hwnd) { return Current; }
            public Task<string> GetCurrentPathAsync(IntPtr hwnd) { return Read != null ? Read(hwnd) : Task.FromResult(Current); }
            public Task<bool> NavigateAsync(IntPtr hwnd, string path)
            {
                Moves++;
                if (Move != null) return Move(path);
                Current = path;
                return Task.FromResult(true);
            }
            public Task SelectItemsAsync(IntPtr hwnd, List<string> items) { SelectItems(hwnd, items); return Task.CompletedTask; }
        }

        private static ExplorerWindowInteractionService Interaction(IExplorerService explorer, Action<IntPtr> close, Action<IntPtr> show)
        {
            return new ExplorerWindowInteractionService(explorer, new ExplorerWindowTrackingState(h => true, delegate { }, delegate { }),
                TestTabPersistenceFactory.Create(), h => "", show, delegate { }, close, () => null, delegate { });
        }

        [TestMethod]
        public async Task Outcome_DesktopFlagReusesButDragOutcomeAdds()
        {
            foreach (bool asynchronous in new[] { false, true })
            {
                AsyncExplorer explorer = new AsyncExplorer();
                ExplorerWindowInteractionService interaction = Interaction(explorer, delegate { }, delegate { });
                ExplorerWindowOutcomeCoordinator outcome = new ExplorerWindowOutcomeCoordinator(
                    new ExplorerWindowTrackingState(), interaction, delegate { }, () => new MockUserSettings(), delegate { });
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
                {
                    TabItemViewModel original = vm.ActiveTab;
                    ExplorerWindowEvaluationResult result = new ExplorerWindowEvaluationResult
                    {
                        Action = AbsorptionAction.Absorb, ResolvedPath = explorer.Current, ReuseExistingTab = true
                    };
                    if (asynchronous) await outcome.ApplyOutcomeAsync((IntPtr)200, 0, result, vm, null);
                    else outcome.ApplyOutcome((IntPtr)200, 0, result, vm, null);
                    Assert.AreEqual(1, vm.Tabs.Count);
                    Assert.AreSame(original, vm.ActiveTab);
                    result.ReuseExistingTab = false;
                    if (asynchronous) await outcome.ApplyOutcomeAsync((IntPtr)300, 0, result, vm, null);
                    else outcome.ApplyOutcome((IntPtr)300, 0, result, vm, null);
                    Assert.AreEqual(2, vm.Tabs.Count);
                    Assert.AreNotSame(original, vm.ActiveTab);
                }
            }
        }
        [TestMethod]
        public async Task DesktopReuse_NavigationFailurePreservesExistingTabAndSource()
        {
            AsyncExplorer explorer = new AsyncExplorer();
            int closed = 0, shown = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, h => shown++);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                vm.InsertTabWithPath(@"C:\B", 1);
                TabItemViewModel match = vm.ActiveTab;
                vm.SelectTab(vm.Tabs[0]);
                explorer.Move = p => Task.FromResult(false);
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null, reuseExistingTab: true));
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.AreSame(match, vm.Tabs[1]);
                Assert.AreSame(vm.Tabs[0], vm.ActiveTab);
                Assert.AreEqual(0, closed);
                Assert.AreEqual(1, shown);
            }
        }

        [TestMethod]
        public async Task DesktopReuse_WaitsForArrivalBeforeClosingSource()
        {
            TaskCompletionSource<string> arrival = new TaskCompletionSource<string>();
            AsyncExplorer explorer = new AsyncExplorer();
            int closed = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                vm.InsertTabWithPath(@"C:\B", 1);
                TabItemViewModel match = vm.ActiveTab;
                vm.SelectTab(vm.Tabs[0]);
                explorer.Move = p => { explorer.Read = h => arrival.Task; return Task.FromResult(true); };
                Task<bool> operation = service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null, reuseExistingTab: true);
                Assert.IsFalse(operation.IsCompleted);
                Assert.AreEqual(0, closed);
                Assert.AreEqual(2, vm.Tabs.Count);
                arrival.SetResult(@"C:\B");
                Assert.IsTrue(await operation);
                Assert.AreSame(match, vm.ActiveTab);
                Assert.AreEqual(1, closed);
            }
        }

        [TestMethod]
        public async Task DropAndDuplicate_KeepCreatingTabsForExistingLocations()
        {
            string[] paths = new string[] { Path.GetTempPath(), "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "::{645FF040-5081-101B-9F08-00AA002F954E}",
                "::{26EE0668-A00A-44D7-9371-BEB064C98683}" };
            foreach (string path in paths)
            {
                AsyncExplorer explorer = new AsyncExplorer { Current = path };
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, path))
                {
                    TabItemViewModel original = vm.ActiveTab;
                    Assert.IsTrue(await TabBarWindowDragDropHandler.InsertDroppedPathsAsync(vm, 1, new[] { path }, explorer, p => Task.FromResult(true), delegate { }));
                    Assert.AreEqual(2, vm.Tabs.Count, path);
                    Assert.AreNotSame(original, vm.ActiveTab);
                    await vm.DuplicateTabAsync(original, p => Task.FromResult(true), delegate { });
                    Assert.AreEqual(3, vm.Tabs.Count, path);
                    Assert.AreNotSame(original, vm.ActiveTab);
                }
            }
        }
        [TestMethod]
        public async Task Absorption_RejectedNavigation_KeepsSourceWindow()
        {
            AsyncExplorer explorer = new AsyncExplorer { Move = p => Task.FromResult(false) };
            int closed = 0, shown = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, h => shown++);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null));
                Assert.AreEqual(0, closed); Assert.AreEqual(1, shown);
                Assert.AreEqual(@"C:\A", vm.ActiveTab.Path);
            }
        }

        [TestMethod]
        public async Task Absorption_NavigationThrows_KeepsSourceWindow()
        {
            AsyncExplorer explorer = new AsyncExplorer { Move = p => Task.FromException<bool>(new IOException("simulated")) };
            int closed = 0, shown = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, h => shown++);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null));
                Assert.AreEqual(0, closed); Assert.AreEqual(1, shown);
            }
        }

        [TestMethod]
        public async Task Absorption_Timeout_KeepsSourceAndRollsBackSelection()
        {
            AsyncExplorer explorer = new AsyncExplorer { Move = p => Task.FromResult(true) };
            int closed = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, delegate { });
            service.AbsorptionTimeout = TimeSpan.Zero;
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null));
                Assert.AreEqual(0, closed); Assert.AreEqual(@"C:\A", vm.ActiveTab.Path);
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
            }
        }

        [TestMethod]
        public async Task Absorption_WaitsForArrivalBeforeClosing_AndRejectsDuplicateWork()
        {
            TaskCompletionSource<string> arrived = new TaskCompletionSource<string>();
            AsyncExplorer explorer = new AsyncExplorer();
            explorer.Move = p => { explorer.Read = h => arrived.Task; return Task.FromResult(true); };
            int closed = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Task<bool> pending = service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null);
                Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0, closed);
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null));
                Assert.AreEqual(2, vm.Tabs.Count);
                arrived.SetResult(@"C:\B");
                Assert.IsTrue(await pending); Assert.AreEqual(1, closed);
            }
        }

        [TestMethod]
        public async Task Absorption_ConcurrentOtherSource_DoesNotDisturbPendingOperation()
        {
            TaskCompletionSource<string> arrived = new TaskCompletionSource<string>();
            AsyncExplorer explorer = new AsyncExplorer();
            explorer.Move = p => { explorer.Read = h => arrived.Task; return Task.FromResult(true); };
            List<IntPtr> closed = new List<IntPtr>();
            ExplorerWindowInteractionService service = Interaction(explorer, closed.Add, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Task<bool> first = service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null);
                Assert.IsFalse(await service.AbsorbExplorerWindowAsync((IntPtr)300, vm, @"C:\C", false, false, null));
                Assert.IsTrue(vm.IsTabOperationPending);
                arrived.SetResult(@"C:\B"); Assert.IsTrue(await first);
                CollectionAssert.AreEqual(new[] { (IntPtr)200 }, closed);
                Assert.IsFalse(vm.IsTabOperationPending);
            }
        }

        [TestMethod]
        public async Task Absorption_DisposedDuringArrival_DoesNotCloseSource()
        {
            TaskCompletionSource<string> arrived = new TaskCompletionSource<string>();
            AsyncExplorer explorer = new AsyncExplorer();
            explorer.Move = p => { explorer.Read = h => arrived.Task; return Task.FromResult(true); };
            int closed = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, delegate { });
            TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current);
            Task<bool> pending = service.AbsorbExplorerWindowAsync((IntPtr)200, vm, @"C:\B", false, false, null);
            vm.Dispose(); arrived.SetResult(@"C:\B");
            Assert.IsFalse(await pending); Assert.AreEqual(0, closed);
        }

        [TestMethod]
        public async Task Absorption_ReusedHost_IsNeverClosed()
        {
            AsyncExplorer explorer = new AsyncExplorer(); int closed = 0;
            ExplorerWindowInteractionService service = Interaction(explorer, h => closed++, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsTrue(await service.AbsorbExplorerWindowAsync((IntPtr)100, vm, @"C:\B", false, false, null));
                Assert.AreEqual(0, closed);
            }
        }

        [TestMethod]
        public async Task Selection_PathReadYields_AndDropsResultAfterTabRemoval()
        {
            TaskCompletionSource<string> read = new TaskCompletionSource<string>();
            AsyncExplorer explorer = new AsyncExplorer { Read = h => read.Task };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer); vm.Tabs.Add(target);
                Task pending = vm.SelectTabAsync(target, null, null);
                Assert.IsFalse(pending.IsCompleted); Assert.AreEqual(0, explorer.Moves);
                vm.Tabs.Remove(target); read.SetResult(explorer.Current); await pending;
                Assert.AreEqual(0, explorer.Moves); Assert.AreEqual(@"C:\A", vm.ActiveTab.Path);
            }
        }

        [TestMethod]
        public async Task Selection_NavigateYields_AndLateResultDoesNotReplaceNewSelection()
        {
            TaskCompletionSource<bool> accepted = new TaskCompletionSource<bool>();
            AsyncExplorer explorer = new AsyncExplorer { Move = p => accepted.Task };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                TabItemViewModel source = vm.ActiveTab, target = new TabItemViewModel(@"C:\B", "B", explorer); vm.Tabs.Add(target);
                Task pending = vm.SelectTabCoreAsync(target); Assert.IsFalse(pending.IsCompleted);
                vm.SetActiveTabOnly(source); accepted.SetResult(true); await pending;
                Assert.AreSame(source, vm.ActiveTab); Assert.IsTrue(vm.IsCancelledNavigationMatch(target.Path));
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
            }
        }

        [TestMethod]
        public async Task Closing_WaitsForNavigationAcceptance_BeforeRemovingSource()
        {
            TaskCompletionSource<bool> accepted = new TaskCompletionSource<bool>();
            AsyncExplorer explorer = new AsyncExplorer { Move = p => accepted.Task };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                TabItemViewModel source = vm.ActiveTab; vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                Task pending = vm.CloseTabsAsync(0, 1, null, null);
                Assert.IsFalse(pending.IsCompleted); Assert.IsTrue(vm.Tabs.Contains(source));
                accepted.SetResult(false); await pending;
                Assert.IsTrue(vm.Tabs.Contains(source)); Assert.AreSame(source, vm.ActiveTab);
            }
        }

        [TestMethod]
        public async Task CloseAll_MaintainsOneHomeTab()
        {
            AsyncExplorer explorer = new AsyncExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                await vm.CloseTabsAsync(0, 2, null, null);
                Assert.AreEqual(1, vm.Tabs.Count); Assert.AreEqual(explorer.GetResolvedHomeFolderPath(), vm.ActiveTab.Path);
            }
        }

        [TestMethod]
        public async Task BackgroundDrop_PreparesSpecialHost_BeforeNavigation()
        {
            AsyncExplorer explorer = new AsyncExplorer();
            string cp = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            explorer.IsControlPanelPathFunc = p => p == cp;
            int prepared = 0, revealed = 0;
            explorer.Move = p => { Assert.AreEqual(1, prepared); explorer.Current = p; return Task.FromResult(true); };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsTrue(await TabBarWindowDragDropHandler.InsertDroppedPathsAsync(vm, 1, new[] { cp }, explorer,
                    p => { Assert.AreEqual(cp, p); prepared++; return Task.FromResult(true); }, () => revealed++));
                Assert.AreEqual(cp, vm.ActiveTab.Path); Assert.AreEqual(1, revealed);
            }
        }

        [TestMethod]
        public async Task BackgroundDrop_PreparationFailure_DoesNotInsertOrNavigate()
        {
            AsyncExplorer explorer = new AsyncExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                Assert.IsFalse(await TabBarWindowDragDropHandler.InsertDroppedPathsAsync(vm, 1, new[] { "shell:ControlPanelFolder" }, explorer,
                    p => Task.FromResult(false), delegate { }));
                Assert.AreEqual(1, vm.Tabs.Count); Assert.AreEqual(0, explorer.Moves);
            }
        }

        [TestMethod]
        public async Task Reopen_NavigationFailure_DoesNotDuplicateTabOnRetry()
        {
            AsyncExplorer explorer = new AsyncExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
            {
                TabItemViewModel closed = new TabItemViewModel(@"C:\B", "B", explorer); vm.Tabs.Add(closed); vm.CloseTab(closed);
                explorer.Move = p => Task.FromResult(false);
                await vm.ReopenClosedTabAsync(null, null);
                Assert.AreEqual(1, vm.Tabs.Count); Assert.IsTrue(vm.HasClosedTabs);
                explorer.Move = p => { explorer.Current = p; return Task.FromResult(true); };
                await vm.ReopenClosedTabAsync(null, null);
                Assert.AreEqual(2, vm.Tabs.Count); Assert.IsFalse(vm.HasClosedTabs);
            }
        }

        [TestMethod]
        public void DragEffect_DriveModifiersAndAllowedEffects()
        {
            DragDropEffects all = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
            string[] local = { @"C:\Source\file" };
            Assert.AreEqual(DragDropEffects.Move, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.None, all, local, @"C:\Dest"));
            Assert.AreEqual(DragDropEffects.Copy, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.None, all, local, @"D:\Dest"));
            Assert.AreEqual(DragDropEffects.Copy, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.ControlKey, all, local, @"C:\Dest"));
            Assert.AreEqual(DragDropEffects.Move, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.ShiftKey, all, local, @"D:\Dest"));
            Assert.AreEqual(DragDropEffects.Link, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey, all, local, @"C:\Dest"));
            Assert.AreEqual(DragDropEffects.None, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.ShiftKey, DragDropEffects.Copy, local, @"C:\Dest"));
            Assert.AreEqual(DragDropEffects.Move, TabBarWindowDragDropHandler.GetFileDropEffect(DragDropKeyStates.None, DragDropEffects.Move, local, @"C:\Dest"));
        }

        [TestMethod]
        public async Task Icons_RecoverAfterQueueFull_AndShareResult()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.Icon137." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            ManualResetEventSlim entered = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            Task blocked = ComThreadService.Instance.InvokeAsync(() => { entered.Set(); release.Wait(); });
            Assert.IsTrue(entered.Wait(2000));
            try
            {
                for (int i = 0; i < 64; i++) _ = ComThreadService.Instance.InvokeAsync(delegate { });
                using (ExplorerManager manager = new ExplorerManager(true))
                {
                    TabItemViewModel first = new TabItemViewModel(directory, "A", manager);
                    TabItemViewModel second = new TabItemViewModel(directory, "B", manager);
                    ImageSource fallback = first.IconSource;
                    Assert.IsNotNull(fallback);
                    release.Set(); await blocked;
                    for (int i = 0; i < 60 && (ReferenceEquals(fallback, first.IconSource) || ReferenceEquals(fallback, second.IconSource)); i++) await Task.Delay(50);
                    Assert.AreNotSame(fallback, first.IconSource);
                    Assert.AreSame(first.IconSource, second.IconSource);
                }
            }
            finally { release.Set(); Directory.Delete(directory); }
        }

        [TestMethod]
        public void Snapshot_PreservesTabsAndDuplicateSelection_WhenLegacyMirrorIsLocked()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.Snapshot137." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "tabs.txt");
            MockExplorerService explorer = new MockExplorerService();
            try
            {
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
                {
                    vm.Tabs.Add(new TabItemViewModel(@"C:\A", "duplicate", explorer));
                    TabPersistenceService store = new TabPersistenceService(file);
                    store.SaveTabsIfChanged(vm, true);
                    vm.SetActiveTabOnly(vm.Tabs[1]);
                    using (FileStream held = new FileStream(Path.Combine(directory, "tabs.active.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
                        store.SaveTabsIfChanged(vm, true);
                    using (TabBarViewModel restored = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
                    {
                        Assert.IsTrue(new TabPersistenceService(file).LoadTabsTo(restored));
                        Assert.AreEqual(2, restored.Tabs.Count);
                        Assert.AreEqual(1, restored.ActiveTabIndex);
                    }
                    string committed = File.ReadAllText(store.GetSnapshotFilePathInstance());
                    vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                    using (FileStream held = new FileStream(store.GetSnapshotFilePathInstance(), FileMode.Open, FileAccess.Read, FileShare.Read))
                        store.SaveTabsIfChanged(vm, true);
                    Assert.AreEqual(committed, File.ReadAllText(store.GetSnapshotFilePathInstance()));
                    store.SaveTabsIfChanged(vm, true);
                    using (TabBarViewModel restored = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
                    {
                        Assert.IsTrue(new TabPersistenceService(file).LoadTabsTo(restored));
                        Assert.AreEqual(3, restored.Tabs.Count);
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        private sealed class FailingSettings : IUserSettings
        {
            internal bool Succeeds;
            public string FontFamily { get; set; }
            public double FontSize { get; set; } = 14;
            public bool IsBold { get; set; }
            public bool IsItalic { get; set; }
            public event EventHandler SettingsChanged { add { } remove { } }
            public void Save() { throw new Exception("Use TrySave"); }
            public bool TrySave(out string error) { error = Succeeds ? null : "disk failure"; return Succeeds; }
        }

        [TestMethod]
        public void Selection_KeepsDispatcherResponsive_AndAppliesOnUiThread()
        {
            Exception failure = null;
            Thread thread = new Thread(delegate ()
            {
                System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                dispatcher.BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        int uiThread = Thread.CurrentThread.ManagedThreadId;
                        TaskCompletionSource<bool> accepted = new TaskCompletionSource<bool>();
                        AsyncExplorer explorer = new AsyncExplorer { Move = p => accepted.Task };
                        using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.Current))
                        {
                            TabItemViewModel target = new TabItemViewModel(@"C:\B", "B", explorer); vm.Tabs.Add(target);
                            vm.PropertyChanged += (sender, args) => Assert.AreEqual(uiThread, Thread.CurrentThread.ManagedThreadId);
                            Task selecting = vm.SelectTabAsync(target, null, null);
                            bool processed = false;
                            _ = dispatcher.BeginInvoke(new Action(() => { processed = true; accepted.SetResult(true); }));
                            await selecting;
                            Assert.IsTrue(processed); Assert.AreSame(target, vm.ActiveTab);
                            Assert.AreEqual(uiThread, Thread.CurrentThread.ManagedThreadId);
                        }
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dispatcher.InvokeShutdown(); }
                }));
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
            Assert.IsTrue(thread.Join(10000), "Dispatcher stalled.");
            if (failure != null) throw failure;
        }

        [TestMethod]
        public void WheelFontSize_SaveFailure_RollsBackAndCanRetry()
        {
            FailingSettings settings = new FailingSettings(); string error;
            Assert.IsFalse(TabBarWindow.TryChangeFontSize(settings, 18, out error));
            Assert.AreEqual("disk failure", error); Assert.AreEqual(14.0, settings.FontSize);
            settings.Succeeds = true;
            Assert.IsTrue(TabBarWindow.TryChangeFontSize(settings, 18, out error)); Assert.AreEqual(18.0, settings.FontSize);
        }
    }
}
