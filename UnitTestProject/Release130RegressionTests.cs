using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class Release130RegressionTests
    {
        [TestMethod]
        public void Persistence_RetriesIdenticalState_AfterWriteFailure()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.SaveRetry." + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "tabs.txt");
            try
            {
                Directory.CreateDirectory(path);
                using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService()))
                {
                    TabPersistenceService persistence = new TabPersistenceService(path);
                    persistence.SaveTabsIfChanged(vm, true);
                    Directory.Delete(path);
                    persistence.SaveTabsIfChanged(vm, true);
                    Assert.IsTrue(File.Exists(path), "A failed save must not mark the state as saved.");
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ReopenClosedTab_RestoresControlPanel()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                vm.InsertTabWithPath(explorer.AllControlPanelPath, 1, true);
                vm.CloseTab(vm.Tabs[1]);
                vm.ReopenClosedTab();
                Assert.IsNotNull(vm.FindTabByPath(explorer.AllControlPanelPath));
                Assert.IsFalse(vm.HasClosedTabs);
            }
        }

        [TestMethod]
        public void SamePathAbsorption_RestoresSelectedItems()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                vm.InsertTabWithPathAndSelect(@"C:\MockPath", 1, new List<string> { @"C:\MockPath\selected.txt" }, false);
                Assert.AreEqual(1, explorer.SelectItemsCallCount);
                CollectionAssert.AreEqual(new List<string> { @"C:\MockPath\selected.txt" }, explorer.LastSelectedItems);
            }
        }

        [TestMethod]
        public void NormalizeKnownPath_PreservesOrdinaryFolderContainingCplName()
        {
            ExplorerManager explorer = new ExplorerManager();
            Assert.AreEqual(@"C:\Work\appwiz.cpl_backup", explorer.NormalizeKnownPath(@"C:\Work\appwiz.cpl_backup"));
            Assert.AreEqual(@"C:\Microsoft.PowerOptions\data", explorer.NormalizeKnownPath(@"C:\Microsoft.PowerOptions\data"));
            Assert.AreEqual(@"\\server\powercfg.cpl\folder", explorer.NormalizeKnownPath(@"\\server\powercfg.cpl\folder"));
        }

        [TestMethod]
        public void DestroyQueuedBeforeRebind_DoesNotCloseNewHost()
        {
            RunSta(delegate
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                int closeCount = 0;
                using (TabBarWindowRuntimeCoordinator runtime = new TabBarWindowRuntimeCoordinator(
                    dispatcher, () => true, () => { }, () => Task.FromResult(0), () => closeCount++))
                {
                    typeof(TabBarWindowRuntimeCoordinator).GetField("_trackedExplorerHwnd", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(runtime, new IntPtr(-111));
                    runtime.HandleDestroyEvent(IntPtr.Zero, NativeMethods.EVENT_OBJECT_DESTROY, new IntPtr(-111), 0, 0, 0, 0);
                    runtime.RebindExplorer(new IntPtr(-222));
                    DispatcherFrame frame = new DispatcherFrame();
                    dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    Assert.AreEqual(0, closeCount);
                }
            });
        }

        [TestMethod]
        public void CloseTabsToRight_DoesNotRepeatedlyUpdateRemovedTitles()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                for (int i = 1; i < 50; i++)
                    vm.Tabs.Add(new TabItemViewModel(@"C:\Review\Folder" + i, "MockFolder", explorer));
                int notifications = 0;
                foreach (TabItemViewModel tab in vm.Tabs)
                    tab.PropertyChanged += (sender, args) => { if (args.PropertyName == "Title") notifications++; };
                vm.CloseTabsToRight(vm.Tabs[0]);
                Assert.AreEqual(1, vm.Tabs.Count);
                Assert.AreEqual(0, notifications, "Removed tabs must not publish intermediate titles.");
                vm.ReopenClosedTab();
                Assert.AreEqual(50, vm.Tabs.Count);
            }
        }

        [TestMethod]
        public void Absorption_IgnoredOrdinaryWindow_SkipsDesktopLookups()
        {
            int calls = 0;
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                HasValidTarget = true,
                CurrentPath = @"C:\Work",
                IsDesktopCandidate = false,
                IsDesktopShortcutTargetFunc = path => { calls++; return false; },
                IsDesktopShellItemPathFunc = path => { calls++; return false; }
            };
            bool special;
            string resolved;
            AbsorptionAction result = ExplorerAbsorptionDecisionMaker.Evaluate(context, new MockExplorerService(), out resolved, out special);
            Assert.AreEqual(AbsorptionAction.Ignore, result);
            Assert.AreEqual(0, calls);
        }

        [TestMethod]
        public void Persistence_RetriesAfterActiveSelectionWriteFails()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.ActiveRetry." + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "tabs.txt");
            string activePath = Path.Combine(directory, "tabs.active.txt");
            try
            {
                Directory.CreateDirectory(activePath);
                using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService()))
                {
                    TabPersistenceService persistence = new TabPersistenceService(path);
                    persistence.SaveTabsIfChanged(vm, true);
                    Assert.IsTrue(File.Exists(path));
                    Directory.Delete(activePath);
                    persistence.SaveTabsIfChanged(vm, true);
                    Assert.IsTrue(File.Exists(activePath));
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ReopenFailure_KeepsHistoryForRetry()
        {
            FailingFolderNameExplorer explorer = new FailingFolderNameExplorer();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                vm.InsertTabWithPath(@"C:\Other", 1);
                vm.CloseTab(vm.Tabs[1]);
                explorer.Fail = true;
                try { vm.ReopenClosedTab(); Assert.Fail("Expected failure."); }
                catch (IOException) { }
                Assert.IsTrue(vm.HasClosedTabs);
                explorer.Fail = false;
                vm.ReopenClosedTab();
                Assert.IsNotNull(vm.FindTabByPath(@"C:\Other"));
                Assert.IsFalse(vm.HasClosedTabs);
            }
        }

        private sealed class FailingFolderNameExplorer : MockExplorerService
        {
            internal bool Fail;
            public override string GetFolderName(string path)
            {
                if (Fail) throw new IOException("Metadata unavailable.");
                return base.GetFolderName(path);
            }
        }

        [TestMethod]
        public void HostPreparation_ContainsTimeoutException()
        {
            MockExplorerService explorer = new MockExplorerService();
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking, (vm, hwnd) => true, hwnd => { }, (hwnd, rect) => { }, hwnd => { },
                hwnd => true, () => new List<IntPtr>(), hwnd => { throw new TimeoutException("Blocked Shell."); },
                path => true, milliseconds => { });
            using (TabBarViewModel vm = new TabBarViewModel(new IntPtr(-101), new MockUserSettings(), explorer))
            {
                Assert.IsFalse(coordinator.PrepareForPathAsync(vm, explorer.AllControlPanelPath).GetAwaiter().GetResult());
                Assert.IsNull(explorer.OpenedInNewWindowPath);
            }
        }

        [TestMethod]
        public void Availability_PreservesRemoteAndRemovablePathsWithoutProbing()
        {
            foreach (uint driveType in new uint[] { 2, 4, 5, 0 })
            {
                bool result = ShellPathAvailabilityEvaluator.IsDirectoryAvailable(@"Z:\Folder",
                    root => driveType, path => { Assert.Fail("Should not probe an unavailable device."); return 0; });
                Assert.IsTrue(result);
            }
        }

        [TestMethod]
        public void Availability_DistinguishesDeletedChildFromAccessFailure()
        {
            Assert.IsFalse(ShellPathAvailabilityEvaluator.IsDirectoryAvailable(@"C:\Existing\Deleted", root => 3,
                path => { if (path.EndsWith("Deleted")) throw new DirectoryNotFoundException(); return FileAttributes.Directory; }));
            Assert.IsTrue(ShellPathAvailabilityEvaluator.IsDirectoryAvailable(@"C:\Protected", root => 3,
                path => { throw new UnauthorizedAccessException(); }));
            Assert.IsTrue(ShellPathAvailabilityEvaluator.IsDirectoryAvailable(@"C:\Unavailable", root => 3,
                path => { throw new IOException(); }));
            Assert.IsTrue(ShellPathAvailabilityEvaluator.IsDirectoryAvailable(@"C:\MissingParent\Child", root => 3,
                path => { throw new DirectoryNotFoundException(); }));
        }

        [TestMethod]
        public void BatchSelection_EnumeratesOnce_AndPreservesRequestedOrder()
        {
            int enumerations = 0;
            int releases = 0;
            ShellFolderItemSelectionHelper helper = new ShellFolderItemSelectionHelper(
                (item, property) => item,
                (collection, method, arguments) =>
                {
                    if (method != "Item") return null;
                    enumerations++;
                    return @"C:\Folder\Item" + arguments[0];
                },
                item => { if (item != null) releases++; },
                new ShellItemPathResolver(path => path),
                (source, key, message, exception, interval) => { });
            string[] paths = { @"C:\Folder\Item999", @"C:\Folder\Item998", @"C:\Folder\Item997" };
            object[] items = helper.FindFolderItemsByPaths(new object(), new object(), 1000, paths);
            Assert.AreEqual(1000, enumerations);
            Assert.AreEqual(997, releases);
            for (int i = 0; i < paths.Length; i++) Assert.AreEqual(paths[i], items[i]);
        }

        [TestMethod]
        public void MetadataCache_ReturnsImmediately_AndCoalescesRequests()
        {
            using (ManualResetEvent started = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent updated = new ManualResetEvent(false))
            {
                ShellMetadataCache cache = new ShellMetadataCache();
                int loads = 0;
                cache.Updated += (sender, args) => updated.Set();
                Func<string> load = delegate
                {
                    Interlocked.Increment(ref loads);
                    started.Set();
                    release.WaitOne();
                    return "Display title";
                };
                try
                {
                    Assert.AreEqual("Fallback", cache.Get("key", load, true, "Fallback"));
                    Assert.IsTrue(started.WaitOne(2000));
                    Assert.AreEqual("Fallback", cache.Get("key", load, true, "Fallback"));
                }
                finally { release.Set(); }
                Assert.IsTrue(updated.WaitOne(2000));
                Assert.AreEqual("Display title", cache.Get("key", load, true, "Fallback"));
                Assert.AreEqual(1, loads);
            }
        }

        [TestMethod]
        public void UpdateTitles_UnchangedDisambiguatedTitles_DoNotNotifyAgain()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                vm.InsertTabWithPath(@"C:\Another\Folder", 1);
                vm.UpdateTabTitles();
                int notifications = 0;
                foreach (TabItemViewModel tab in vm.Tabs)
                    tab.PropertyChanged += (sender, args) => { if (args.PropertyName == "Title") notifications++; };
                vm.UpdateTabTitles();
                Assert.AreEqual(0, notifications);
            }
        }

        [TestMethod]
        public void ReopenWhilePreparationPending_DoesNotDuplicateRestoration()
        {
            MockExplorerService explorer = new MockExplorerService();
            using (TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer))
            {
                vm.InsertTabWithPath(@"C:\Other", 1);
                vm.CloseTab(vm.Tabs[1]);
                TaskCompletionSource<bool> prepared = new TaskCompletionSource<bool>();
                Task first = vm.ReopenClosedTabAsync(path => prepared.Task, null);
                vm.ReopenClosedTabAsync(null, null).GetAwaiter().GetResult();
                prepared.SetResult(true);
                first.GetAwaiter().GetResult();
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
            }
        }

        [TestMethod]
        public void RestoredHistoryItem_DoesNotConsumeNewerBatch()
        {
            ClosedTabHistory history = new ClosedTabHistory();
            history.Record(@"C:\First", 0);
            ClosedTabInfo first = history.PeekLastBatch()[0];
            history.Record(@"C:\Second", 1);
            history.RemoveRestoredItem(first);
            Assert.AreEqual(@"C:\Second", history.PeekLastBatch()[0].Path);
            history.RemoveRestoredItem(history.PeekLastBatch()[0]);
            Assert.IsFalse(history.HasItems);
        }

        [TestMethod]
        public void StandardUserStartup_ContinuesWithoutRelaunch()
        {
            Assert.IsTrue(StandardUserRelaunchService.CanContinueStartup(false, false,
                () => { Assert.Fail("Standard users must not relaunch."); return false; },
                () => Assert.Fail("No error expected.")));
        }

        [TestMethod]
        public void ElevatedStartup_AlwaysExits_AndDoesNotRetryMarkedProcess()
        {
            foreach (bool launchSucceeded in new[] { false, true })
            {
                int launches = 0;
                int failures = 0;
                Assert.IsFalse(StandardUserRelaunchService.CanContinueStartup(true, false,
                    () => { launches++; return launchSucceeded; }, () => failures++));
                Assert.AreEqual(1, launches);
                Assert.AreEqual(launchSucceeded ? 0 : 1, failures);
            }
            Assert.IsFalse(StandardUserRelaunchService.CanContinueStartup(true, true,
                () => { Assert.Fail("Do not loop relaunches."); return true; }, () => { }));
        }

        internal static void RunSta(Action action)
        {
            Exception failure = null;
            Thread thread = new Thread(delegate()
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not finish.");
            if (failure != null) throw new Exception("STA test failed.", failure);
        }
    }
}
