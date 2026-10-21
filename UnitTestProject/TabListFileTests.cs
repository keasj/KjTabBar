using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class TabListFileTests
    {
        [TestMethod]
        public void RoundTripPreservesZeroBasedSelectionOrderDuplicatesAndUnicode()
        {
            string[] paths = { @"C:\日本語", @"\\offline\share", "shell:RecycleBinFolder", @"C:\日本語" };
            string text = TabListFile.Serialize(paths, 3);
            Assert.IsTrue(text.StartsWith("active=3\r\n", StringComparison.Ordinal));
            TabListFile list = TabListFile.Parse(text);
            CollectionAssert.AreEqual(paths, list.Paths);
            Assert.AreEqual(3, list.ActiveIndex);
        }

        [TestMethod]
        public void UserExampleSelectsSecondTab()
        {
            TabListFile list = TabListFile.Parse("active=1\nC:\\Users\nD:\\Documents\nshell:RecycleBinFolder");
            Assert.AreEqual(1, list.ActiveIndex);
            Assert.AreEqual(3, list.Paths.Length);
        }

        [TestMethod]
        public void RejectsMalformedLists()
        {
            foreach (string text in new[] { "", "active=-1\nC:\\A", "active=1\nC:\\A", "active=0", "active=x\nC:\\A", "active=0\n\nC:\\A", "active=0\nA\0B" })
            {
                try { TabListFile.Parse(text); Assert.Fail("Accepted malformed list."); }
                catch (FormatException) { }
            }
        }

        [TestMethod]
        public void Utf8FileSupportsBomAndAtomicOverwrite()
        {
            string file = Path.GetTempFileName();
            try
            {
                string[] paths = { @"C:\日本語" };
                TabListFile.Write(file, paths, 0);
                byte[] bytes = File.ReadAllBytes(file);
                Assert.AreEqual((byte)'a', bytes[0]);
                CollectionAssert.AreEqual(paths, TabListFile.Read(file).Paths);
                File.WriteAllText(file, TabListFile.Serialize(paths, 0), new UTF8Encoding(true));
                CollectionAssert.AreEqual(paths, TabListFile.Read(file).Paths);
                File.WriteAllBytes(file, new byte[] { 0xff, 0xfe, 0x61, 0x00 });
                try { TabListFile.Read(file); Assert.Fail("Accepted UTF-16."); }
                catch (DecoderFallbackException) { }
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void InvalidExportLeavesExistingFileUntouched()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "original");
                try { TabListFile.Write(file, new[] { "bad\npath" }, 0); Assert.Fail("Accepted newline."); }
                catch (FormatException) { }
                Assert.AreEqual("original", File.ReadAllText(file));
            }
            finally { File.Delete(file); }
        }

        private sealed class DisplayNameExplorer : MockExplorerService
        {
            internal const string ControlPanel = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            internal const string LongName = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            public override string GetFolderName(string path)
            {
                if (path == ControlPanel) return "コントロール パネル";
                if (path == "shell:RecycleBinFolder") return "ごみ箱";
                return Path.GetFileName(path);
            }
            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                throw new AssertFailedException("Import must not probe availability for display names.");
            }
        }

        [TestMethod]
        public void ImportResolvesSpecialFolderNamesWithoutChangingPaths()
        {
            DisplayNameExplorer explorer = new DisplayNameExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                string[] paths = { DisplayNameExplorer.ControlPanel, "shell:RecycleBinFolder", @"C:\Users" };
                vm.ImportTabsAsync(TabListFile.Parse(TabListFile.Serialize(paths, 2)), null, null).GetAwaiter().GetResult();
                Assert.AreEqual("コントロール パネル", vm.Tabs[0].Title);
                Assert.AreEqual("ごみ箱", vm.Tabs[1].Title);
                Assert.AreEqual("Users", vm.Tabs[2].Title);
                for (int i = 0; i < paths.Length; i++) Assert.AreEqual(paths[i], vm.Tabs[i].Path);
            }
        }

        [TestMethod]
        public void ImportUsesExistingLongNameAbbreviation()
        {
            DisplayNameExplorer explorer = new DisplayNameExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                string path = @"C:\Parent\" + DisplayNameExplorer.LongName;
                vm.ImportTabsAsync(TabListFile.Parse(TabListFile.Serialize(new[] { path }, 0)), null, null).GetAwaiter().GetResult();
                Assert.AreEqual(DisplayNameExplorer.LongName, vm.ActiveTab.BaseTitle);
                Assert.AreEqual(TabTitleDisambiguator.ShortenTitle(DisplayNameExplorer.LongName, 30), vm.ActiveTab.Title);
                Assert.IsTrue(vm.ActiveTab.Title.Contains("..."));
                Assert.AreEqual(path, vm.ActiveTab.Path);
            }
        }

        [TestMethod]
        public void ImportUsesExistingDuplicateNameDisambiguation()
        {
            DisplayNameExplorer explorer = new DisplayNameExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                string[] paths = { @"C:\One\Same", @"C:\Two\Same", @"C:\One\Same" };
                vm.ImportTabsAsync(TabListFile.Parse(TabListFile.Serialize(paths, 2)), null, null).GetAwaiter().GetResult();
                Assert.AreEqual("(1)C:\\One\\Same", vm.Tabs[0].Title);
                Assert.AreEqual("Two\\Same", vm.Tabs[1].Title);
                Assert.AreEqual("(2)C:\\One\\Same", vm.Tabs[2].Title);
                Assert.AreSame(vm.Tabs[2], vm.ActiveTab);
            }
        }

        [TestMethod]
        public void ImportDoesNotBreakSubsequentExplorerNavigation()
        {
            string current = @"C:\Original";
            MockExplorerService explorer = new MockExplorerService
            {
                GetCurrentPathFunc = hwnd => current,
                NavigateFunc = (hwnd, path) => { current = path; return true; }
            };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, current))
            {
                vm.ImportTabsAsync(TabListFile.Parse("active=0\nC:\\Imported\nC:\\Second"), null, null).GetAwaiter().GetResult();
                TabBarExplorerSynchronizer synchronizer = new TabBarExplorerSynchronizer(vm, explorer);
                synchronizer.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\Imported", vm.ActiveTab.Path);
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
                vm.SelectTabAsync(vm.Tabs[1], null, null).GetAwaiter().GetResult();
                synchronizer.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreSame(vm.Tabs[1], vm.ActiveTab);
                Assert.AreEqual(@"C:\Second", current);
                current = @"C:\NavigatedInExplorer";
                vm.NavigationTracker.UpdateCache(current, DateTime.UtcNow);
                synchronizer.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(current, vm.ActiveTab.Path);
                Assert.AreEqual(@"C:\Imported", vm.Tabs[0].Path);
            }
        }

        [TestMethod]
        public void ImportKeepsListWhenHostPreparationFails()
        {
            NoProbeExplorer explorer = new NoProbeExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                int completed = 0;
                TabListFile list = TabListFile.Parse("active=1\nC:\\One\nshell:RecycleBinFolder");
                Assert.IsTrue(vm.ImportTabsAsync(list, path => Task.FromResult(false), () => completed++).GetAwaiter().GetResult());
                Assert.AreEqual(1, completed);
                Assert.IsFalse(vm.IsTabOperationPending);
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.AreSame(vm.Tabs[1], vm.ActiveTab);
                Assert.AreEqual(list.Paths[1], vm.ActiveTab.Path);
            }
        }

        [TestMethod]
        public void ProductionMetadataResolvesControlPanelIdentifier()
        {
            using (KjTabBar.Models.ExplorerManager explorer = new KjTabBar.Models.ExplorerManager(true))
            {
                string title = explorer.GetFolderName(DisplayNameExplorer.ControlPanel);
                Assert.IsFalse(string.IsNullOrWhiteSpace(title));
                Assert.IsFalse(title.Contains("26EE0668"), "Control Panel metadata remained an identifier: " + title);
                Assert.IsFalse(title.StartsWith("::{", StringComparison.Ordinal));
            }
        }

        private sealed class ImportReadFailureExplorer : MockExplorerService, IAsyncExplorerService
        {
            internal int Reads;
            internal readonly TaskCompletionSource<string> PendingRead = new TaskCompletionSource<string>();
            public Task<string> GetCurrentPathAsync(IntPtr hwnd)
            {
                return ++Reads == 2 ? PendingRead.Task : Task.FromResult(@"C:\Original");
            }
            public Task<bool> NavigateAsync(IntPtr hwnd, string path) { return Task.FromResult(false); }
            public Task SelectItemsAsync(IntPtr hwnd, System.Collections.Generic.List<string> items) { return Task.CompletedTask; }
        }

        [TestMethod]
        public async Task ImportReadExceptionPreservesEntriesAndLaterUserNavigation()
        {
            ImportReadFailureExplorer explorer = new ImportReadFailureExplorer();
            explorer.GetCurrentPathFunc = hwnd => @"C:\Original";
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                TabListFile list = TabListFile.Parse("active=1\nZ:\\Missing\nZ:\\Missing\nC:\\Second");
                Task<bool> import = vm.ImportTabsAsync(list, null, null);
                TabItemViewModel selected = vm.Tabs[1];
                explorer.PendingRead.SetException(new TimeoutException("Injected read timeout"));
                try { await import; Assert.Fail("The read failure must remain observable."); }
                catch (TimeoutException) { }
                Assert.IsFalse(vm.IsTabOperationPending);
                TabBarExplorerSynchronizer sync = new TabBarExplorerSynchronizer(vm, explorer);
                await sync.SyncWithExplorerAsync();
                Assert.AreSame(selected, vm.ActiveTab);
                Assert.AreEqual(1, vm.ActiveTabIndex);
                for (int i = 0; i < list.Paths.Length; i++) Assert.AreEqual(list.Paths[i], vm.Tabs[i].Path);
                explorer.GetCurrentPathFunc = hwnd => @"C:\UserNavigation";
                vm.NavigationTracker.UpdateCache(@"C:\UserNavigation", DateTime.UtcNow);
                await sync.SyncWithExplorerAsync();
                Assert.AreEqual(@"C:\UserNavigation", selected.Path);
                Assert.AreEqual(@"Z:\Missing", vm.Tabs[0].Path);
            }
        }

        [TestMethod]
        public async Task ImportReadExceptionDoesNotGuardNewSelectionHostOrDisposedModel()
        {
            foreach (string change in new[] { "selection", "host", "disposed" })
            {
                ImportReadFailureExplorer explorer = new ImportReadFailureExplorer();
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
                {
                    Task<bool> import = vm.ImportTabsAsync(TabListFile.Parse("active=0\nZ:\\Missing\nC:\\Second"), null, null);
                    if (change == "selection") vm.SetActiveTabOnly(vm.Tabs[1]);
                    else if (change == "host") vm.SetExplorerHwnd((IntPtr)456);
                    else vm.Dispose();
                    explorer.PendingRead.SetException(new TimeoutException("Injected read timeout"));
                    try { await import; Assert.Fail("The read failure must remain observable."); }
                    catch (TimeoutException) { }
                    Assert.IsFalse(vm.ShouldKeepImportedSelection(@"C:\Original"), change);
                    if (change == "selection") Assert.AreSame(vm.Tabs[1], vm.ActiveTab);
                }
            }
        }
        private sealed class NoProbeExplorer : MockExplorerService
        {
            public override bool IsTabPathCurrentlyAvailable(string path)
            {
                throw new AssertFailedException("Import must not probe path availability.");
            }
        }

        [TestMethod]
        public void ImportReplacesListWithoutAvailabilityChecks()
        {
            NoProbeExplorer explorer = new NoProbeExplorer();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                TabListFile list = TabListFile.Parse("active=1\nZ:\\Missing\n\\\\offline\\share\nZ:\\Missing\nshell:RecycleBinFolder");
                Assert.IsTrue(vm.ImportTabsAsync(list, null, null).GetAwaiter().GetResult());
                Assert.AreEqual(4, vm.Tabs.Count);
                for (int i = 0; i < list.Paths.Length; i++) Assert.AreEqual(list.Paths[i], vm.Tabs[i].Path);
                Assert.AreSame(vm.Tabs[1], vm.ActiveTab);
                Assert.AreEqual(1, vm.ActiveTabIndex);
            }
        }

        [TestMethod]
        public void RejectedNavigationKeepsImportedSelectionAndPaths()
        {
            NoProbeExplorer explorer = new NoProbeExplorer { NavigateFunc = (hwnd, path) => false };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                TabListFile list = TabListFile.Parse("active=0\nZ:\\Missing\nC:\\MockPath");
                vm.ImportTabsAsync(list, null, null).GetAwaiter().GetResult();
                Assert.AreSame(vm.Tabs[0], vm.ActiveTab);
                new TabBarExplorerSynchronizer(vm, explorer).SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.IsTrue(vm.ShouldKeepImportedSelection(@"C:\MockPath"));
                Assert.AreEqual(list.Paths[0], vm.ActiveTab.Path);
                Assert.IsFalse(vm.ShouldKeepImportedSelection(list.Paths[0]));
            }
        }


        [TestMethod]
        public void ImportWithUnknownSourcePreservesEntriesUntilExplorerMoves()
        {
            foreach (string unavailable in new string[] { null, string.Empty })
            {
                string current = unavailable;
                MockExplorerService explorer = new MockExplorerService
                {
                    GetCurrentPathFunc = hwnd => current,
                    NavigateFunc = (hwnd, path) => false
                };
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
                {
                    vm.ImportTabsAsync(TabListFile.Parse("active=0\nZ:\\Missing\nC:\\Second"), null, null).GetAwaiter().GetResult();
                    TabBarExplorerSynchronizer sync = new TabBarExplorerSynchronizer(vm, explorer);
                    sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    current = @"C:\Original";
                    vm.NavigationTracker.UpdateCache(current, DateTime.UtcNow);
                    sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreEqual(@"Z:\Missing", vm.ActiveTab.Path);
                    current = @"C:\UserNavigation";
                    vm.NavigationTracker.UpdateCache(current, DateTime.UtcNow);
                    sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreEqual(current, vm.ActiveTab.Path);
                }
            }
        }

        [TestMethod]
        public void ImportFailureDoesNotDisableSubsequentCloseRollback()
        {
            foreach (bool closeAll in new[] { false, true })
            {
                MockExplorerService explorer = new MockExplorerService
                {
                    GetCurrentPathFunc = hwnd => @"C:\Original",
                    NavigateFunc = (hwnd, path) => false
                };
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
                {
                    vm.ImportTabsAsync(TabListFile.Parse("active=0\nZ:\\Missing\nC:\\Second"), null, null).GetAwaiter().GetResult();
                    TabItemViewModel first = vm.Tabs[0];
                    TabItemViewModel second = vm.Tabs[1];
                    explorer.NavigateFunc = (hwnd, path) => true;
                    vm.CloseTabsAsync(0, closeAll ? 2 : 1, null, null).GetAwaiter().GetResult();
                    typeof(TabNavigationStateTracker).GetField("_navigateStartTime",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                    new TabBarExplorerSynchronizer(vm, explorer).SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreEqual(2, vm.Tabs.Count);
                    Assert.AreSame(first, vm.Tabs[0]);
                    Assert.AreSame(second, vm.Tabs[1]);
                    Assert.AreSame(first, vm.ActiveTab);
                }
            }
        }


        [TestMethod]
        public void ExportShutdownWaitsForWriteAndRejectsNewWork()
        {
            FileOperationTracker tracker = new FileOperationTracker();
            using (System.Threading.ManualResetEventSlim started = new System.Threading.ManualResetEventSlim())
            using (System.Threading.ManualResetEventSlim release = new System.Threading.ManualResetEventSlim())
            {
                Task<bool> write = tracker.RunAsync(() => { started.Set(); release.Wait(); });
                try
                {
                    Assert.IsTrue(started.Wait(3000));
                    Task shutdown = tracker.StopAndWaitAsync();
                    Assert.IsFalse(shutdown.IsCompleted);
                    Assert.IsFalse(tracker.RunAsync(() => Assert.Fail("Started during shutdown.")).GetAwaiter().GetResult());
                    release.Set();
                    Assert.IsTrue(write.GetAwaiter().GetResult());
                    Assert.IsTrue(shutdown.Wait(3000));
                }
                finally { release.Set(); write.GetAwaiter().GetResult(); }
            }
        }

        [TestMethod]
        public void ExportWriteFailureReleasesShutdownWait()
        {
            FileOperationTracker tracker = new FileOperationTracker();
            Task<bool> write = tracker.RunAsync(() => { throw new IOException("Test failure"); });
            try { write.GetAwaiter().GetResult(); Assert.Fail("Failure was hidden."); }
            catch (IOException) { }
            Assert.IsTrue(tracker.StopAndWaitAsync().Wait(3000));
        }

        [TestMethod]
        public void StreamingImportPreservesLargeListsAndRejectsInvalidUtf8Tail()
        {
            string file = Path.GetTempFileName();
            try
            {
                string[] paths = new string[10000];
                for (int i = 0; i < paths.Length; i++) paths[i] = @"C:\日本語\" + i;
                TabListFile.Write(file, paths, paths.Length - 1);
                TabListFile list = TabListFile.Read(file);
                CollectionAssert.AreEqual(paths, list.Paths);
                Assert.AreEqual(paths.Length - 1, list.ActiveIndex);
                using (FileStream stream = new FileStream(file, FileMode.Append))
                    stream.WriteByte(0xff);
                try { TabListFile.Read(file); Assert.Fail("Accepted invalid UTF-8 tail."); }
                catch (DecoderFallbackException) { }
            }
            finally { File.Delete(file); }
        }

        [TestMethod]
        public void ImportTimeoutKeepsSelectionAndLaterArrivalResumesSynchronization()
        {
            string current = @"C:\Original";
            MockExplorerService explorer = new MockExplorerService
            {
                GetCurrentPathFunc = hwnd => current,
                NavigateFunc = (hwnd, path) => true
            };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, current))
            {
                vm.ImportTabsAsync(TabListFile.Parse("active=0\nC:\\Imported\nC:\\Second"), null, null).GetAwaiter().GetResult();
                typeof(TabNavigationStateTracker).GetField("_navigateStartTime",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                TabBarExplorerSynchronizer sync = new TabBarExplorerSynchronizer(vm, explorer);
                sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\Imported", vm.ActiveTab.Path);
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
                current = @"C:\Imported";
                vm.NavigationTracker.UpdateCache(current, DateTime.UtcNow);
                sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                current = @"C:\Later";
                vm.NavigationTracker.UpdateCache(current, DateTime.UtcNow);
                sync.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(current, vm.ActiveTab.Path);
            }
        }


        private sealed class CountingNameExplorer : MockExplorerService
        {
            internal int Reads;
            public override string GetFolderName(string path)
            {
                Reads++;
                return Path.GetFileName(path);
            }
        }

        [TestMethod]
        public void MetadataNotificationsDoNotReloadTabsBeyondCacheCapacity()
        {
            Exception failure = null;
            System.Threading.Thread thread = new System.Threading.Thread(() =>
            {
                try
                {
                    CountingNameExplorer explorer = new CountingNameExplorer();
                    using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Initial"))
                    {
                        vm.Tabs.Clear();
                        for (int i = 0; i < 300; i++)
                            vm.Tabs.Add(new TabItemViewModel(@"C:\Folder" + i, "Old" + i, explorer));
                        // A duplicate must receive the same updated base name and keep disambiguation.
                        vm.Tabs.Add(new TabItemViewModel(@"C:\Folder0", "Old0", explorer));
                        System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        typeof(TabBarViewModel).GetField("_metadataDispatcher", flags).SetValue(vm, dispatcher);
                        System.Reflection.MethodInfo notify = typeof(TabBarViewModel).GetMethod("FolderTitlesChanged", flags);
                        int before = explorer.Reads;
                        for (int i = 0; i < 300; i++)
                            notify.Invoke(vm, new object[] { null, new ShellMetadataChangedEventArgs("FolderName:" + @"C:\Folder" + i, "New" + i) });
                        notify.Invoke(vm, new object[] { null, new ShellMetadataChangedEventArgs("FolderName:" + @"C:\Folder0", "Latest") });
                        System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
                        dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                            new Action(() => frame.Continue = false));
                        System.Windows.Threading.Dispatcher.PushFrame(frame);
                        Assert.AreEqual(before, explorer.Reads, "A notification triggered fresh folder-name requests.");
                        Assert.AreEqual("Latest", vm.Tabs[0].BaseTitle);
                        Assert.AreEqual("Latest", vm.Tabs[300].BaseTitle);
                        Assert.AreEqual("New299", vm.Tabs[299].BaseTitle);
                        Assert.AreNotEqual(vm.Tabs[0].Title, vm.Tabs[300].Title);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(10000), "Metadata notification test timed out.");
            if (failure != null) throw failure;
        }

        [TestMethod]
        public void MetadataRefreshNotificationIncludesResolvedKeyAndValue()
        {
            ShellMetadataCache cache = new ShellMetadataCache();
            using (System.Threading.ManualResetEventSlim updated = new System.Threading.ManualResetEventSlim())
            {
                ShellMetadataChangedEventArgs received = null;
                cache.Updated += (sender, args) => { received = args as ShellMetadataChangedEventArgs; updated.Set(); };
                cache.Get("FolderName:test", () => "Resolved", true, "Fallback");
                Assert.IsTrue(updated.Wait(3000));
                Assert.IsNotNull(received);
                Assert.AreEqual("FolderName:test", received.Key);
                Assert.AreEqual("Resolved", received.Value);
            }
        }

        [TestMethod]
        public void ImportUnknownSourceTimeoutAllowsRetry()
        {
            int navigations = 0;
            MockExplorerService explorer = new MockExplorerService
            {
                GetCurrentPathFunc = hwnd => null,
                NavigateFunc = (hwnd, path) => { navigations++; return true; }
            };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\Original"))
            {
                vm.ImportTabsAsync(TabListFile.Parse("active=0\nC:\\Imported"), null, null).GetAwaiter().GetResult();
                typeof(TabNavigationStateTracker).GetField("_navigateStartTime",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                new TabBarExplorerSynchronizer(vm, explorer).SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.IsNull(vm.NavigationTracker.NavigatingToPath);
                Assert.AreEqual(@"C:\Imported", vm.ActiveTab.Path);
                vm.SelectTabAsync(vm.ActiveTab, null, null).GetAwaiter().GetResult();
                Assert.AreEqual(2, navigations);
            }
        }

        [TestMethod]
        public void BusyImportLeavesExistingTabsUntouched()
        {
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), new NoProbeExplorer(), @"C:\Original"))
            {
                TabItemViewModel original = vm.ActiveTab;
                Assert.IsTrue(vm.TryBeginExternalTabOperation());
                try
                {
                    Assert.IsFalse(vm.ImportTabsAsync(TabListFile.Parse("active=0\nZ:\\Missing"), null, null).GetAwaiter().GetResult());
                    Assert.AreSame(original, vm.ActiveTab);
                    Assert.AreEqual(1, vm.Tabs.Count);
                }
                finally { vm.EndExternalTabOperation(); }
            }
        }
    }
}
