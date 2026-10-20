using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class AuditRegressionTests
    {
        [TestMethod]
        public void SynchronousReopen_RejectsUiContext_AsyncReopenRemainsResponsive()
        {
            Exception failure = null;
            Thread thread = new Thread(delegate ()
            {
                try
                {
                    using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService()))
                    {
                        vm.InsertTabWithPath(@"C:\One", 1);
                        vm.InsertTabWithPath(@"C:\Two", 2);
                        vm.CloseTab(vm.Tabs[1]);
                        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                        bool rejected = false;
                        try { vm.ReopenClosedTab(); }
                        catch (InvalidOperationException) { rejected = true; }
                        Assert.IsTrue(rejected);
                        Assert.IsTrue(vm.HasClosedTabs);
                        Task operation = vm.ReopenClosedTabAsync();
                        DispatcherFrame frame = new DispatcherFrame();
                        bool responsive = false;
                        dispatcher.BeginInvoke(new Action(() => responsive = true));
                        operation.ContinueWith(t => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                        DispatcherTimer timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                        timeout.Tick += (sender, args) => frame.Continue = false;
                        timeout.Start();
                        Dispatcher.PushFrame(frame);
                        timeout.Stop();
                        Assert.IsTrue(responsive);
                        Assert.IsTrue(operation.IsCompleted, "Async restoration did not complete.");
                        operation.GetAwaiter().GetResult();
                        Assert.AreEqual(3, vm.Tabs.Count);
                        Assert.IsFalse(vm.HasClosedTabs);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { SynchronizationContext.SetSynchronizationContext(null); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(8000), "UI test did not return.");
            if (failure != null) throw failure;
        }

        [TestMethod]
        public void SetupShutdownSignal_WaitsForOwnedFileOperation()
        {
            FileOperationTracker tracker = new FileOperationTracker();
            IDisposable operation = tracker.TryBegin();
            ManualResetEventSlim requested = new ManualResetEventSlim();
            Task completion = null;
            using (SetupShutdownService listener = new SetupShutdownService(delegate
            {
                completion = tracker.StopAndWaitAsync();
                requested.Set();
            }))
            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
            {
                try
                {
                    Assert.IsTrue(SetupShutdownService.TryRequest(process.Id));
                    Assert.IsTrue(requested.Wait(2000));
                    Assert.IsFalse(completion.IsCompleted);
                    Assert.IsNull(tracker.TryBegin());
                    operation.Dispose();
                    Assert.IsTrue(completion.Wait(2000));
                }
                finally { operation.Dispose(); }
            }
        }

        [TestMethod]
        public async Task BackgroundSave_CapturesImmutableState_AndForcedSaveCannotBeOverwritten()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjtbAudit-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(directory, "tabs.txt");
            ManualResetEventSlim entered = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            int calls = 0;
            TabPersistenceService store = new TabPersistenceService(file, (path, lines) =>
            {
                if (Interlocked.Increment(ref calls) == 1) { entered.Set(); release.Wait(); }
                ProtectedTextStorage.SaveLines(path, lines);
            });
            try
            {
                using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService(), @"C:\Before"))
                {
                    Task save = store.SaveTabsInBackground(vm, true);
                    Assert.IsTrue(entered.Wait(2000));
                    Assert.IsFalse(save.IsCompleted);
                    vm.ActiveTab.Path = @"C:\After";
                    Assert.AreSame(save, store.SaveTabsInBackground(vm));
                    release.Set();
                    await save;
                    string[] state = ProtectedTextStorage.LoadLines(store.GetSnapshotFilePathInstance());
                    Assert.AreEqual(@"C:\Before", state[4]);
                    store.SaveTabsIfChanged(vm, true);
                    state = ProtectedTextStorage.LoadLines(store.GetSnapshotFilePathInstance());
                    Assert.AreEqual(@"C:\After", state[4]);
                }
            }
            finally { release.Set(); store.FlushPendingSave(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task BackgroundSave_RetriesIdenticalStateAfterFailure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjtbRetry-" + Guid.NewGuid().ToString("N"));
            bool fail = true;
            TabPersistenceService store = new TabPersistenceService(Path.Combine(directory, "tabs.txt"), (path, lines) =>
            {
                if (fail) throw new IOException("Injected write failure.");
                ProtectedTextStorage.SaveLines(path, lines);
            });
            try
            {
                using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService(), @"C:\Retry"))
                {
                    await store.SaveTabsInBackground(vm, true);
                    Assert.IsFalse(File.Exists(store.GetSnapshotFilePathInstance()));
                    fail = false;
                    await store.SaveTabsInBackground(vm, true);
                    Assert.IsTrue(File.Exists(store.GetSnapshotFilePathInstance()));
                }
            }
            finally { store.FlushPendingSave(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestMethod]
        public async Task Icons_CancelUnusedQueue_WithoutCancellingSharedConsumer()
        {
            ManualResetEventSlim entered = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            Task blocked = ComThreadService.Instance.InvokeAsync(() => { entered.Set(); release.Wait(); });
            Assert.IsTrue(entered.Wait(2000));
            List<TabItemViewModel> tabs = new List<TabItemViewModel>();
            using (ExplorerManager manager = new ExplorerManager(true))
            {
                int before = TabItemViewModel.PendingIconRequestCount;
                string prefix = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                try
                {
                    for (int i = 0; i < 300; i++) tabs.Add(new TabItemViewModel(prefix + i, "tab", manager));
                    TabItemViewModel duplicate = new TabItemViewModel(prefix + "0", "shared", manager);
                    foreach (TabItemViewModel tab in tabs) tab.StopIconUpdates();
                    Assert.AreEqual(before + 1, TabItemViewModel.PendingIconRequestCount);
                    duplicate.StopIconUpdates();
                    Assert.AreEqual(before, TabItemViewModel.PendingIconRequestCount);
                    release.Set();
                    await blocked;
                }
                finally { foreach (TabItemViewModel tab in tabs) tab.StopIconUpdates(); release.Set(); }
            }
        }

        [TestMethod]
        public void DropDirectoryProbe_DistinguishesFileFolderAndMissingThroughWorker()
        {
            string file = Path.GetTempFileName();
            try
            {
                using (ExplorerManager manager = new ExplorerManager(true))
                {
                    Assert.IsTrue(manager.IsDirectoryForDrop(Path.GetDirectoryName(file)));
                    Assert.IsFalse(manager.IsDirectoryForDrop(file));
                    Assert.IsFalse(manager.IsDirectoryForDrop(file + ".missing"));
                }
            }
            finally { File.Delete(file); }
        }
    }
}
