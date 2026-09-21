using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class Release136RegressionTests
    {
        [TestMethod]
        public void LateArrivalAfterGrace_PreservesOriginalTab_AndHostChangeClearsOldRequests()
        {
            string current = @"C:\A";
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = h => current;
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, current))
            {
                TabItemViewModel a = vm.ActiveTab, b = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(b); vm.SelectTab(b); vm.TimeoutPendingNavigation();
                Dictionary<string, DateTime> cancelled = (Dictionary<string, DateTime>)typeof(TabNavigationStateTracker)
                    .GetField("_cancelledNavigations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vm.NavigationTracker);
                foreach (string path in new List<string>(cancelled.Keys)) cancelled[path] = DateTime.UtcNow.AddDays(-1);
                current = b.Path; vm.NavigationTracker.InvalidateCache(); vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\A", a.Path); Assert.AreSame(b, vm.ActiveTab);
                Assert.IsFalse(vm.IsCancelledNavigationMatch(b.Path), "Consume observed arrival once.");
                vm.SelectTab(a); vm.TimeoutPendingNavigation();
                Assert.IsTrue(vm.IsCancelledNavigationMatch(a.Path));
                vm.SetExplorerHwnd((IntPtr)200);
                Assert.IsFalse(vm.IsCancelledNavigationMatch(a.Path));
            }
        }

        [TestMethod]
        public void PendingClose_PersistsConfirmedSnapshotUntilNavigationCompletes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string current = @"C:\A", file = Path.Combine(dir, "tabs.txt");
                MockExplorerService explorer = new MockExplorerService(); explorer.GetCurrentPathFunc = h => current;
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, current))
                {
                    TabItemViewModel a = vm.ActiveTab; vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                    TabPersistenceService store = new TabPersistenceService(file);
                    vm.CloseTab(a); store.SaveTabsIfChanged(vm, true);
                    CollectionAssert.AreEqual(new[] { @"C:\A", @"C:\B" }, ProtectedTextStorage.LoadLines(file));
                    using (TabBarViewModel restored = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, current))
                    {
                        new TabPersistenceService(file).LoadTabsTo(restored);
                        Assert.AreEqual(@"C:\A", restored.ActiveTab.Path);
                    }
                    vm.TimeoutPendingNavigation(); Assert.IsTrue(vm.Tabs.Contains(a));
                    vm.CloseTab(a); current = @"C:\B"; vm.NavigationTracker.InvalidateCache();
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult(); store.SaveTabsIfChanged(vm, true);
                    CollectionAssert.AreEqual(new[] { @"C:\B" }, ProtectedTextStorage.LoadLines(file));
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        [TestMethod]
        public void StartupRestore_InvalidatedDuringWait_DoesNotRebind()
        {
            VerifyInvalidatedRestore(false);
            VerifyInvalidatedRestore(true);
        }

        private static void VerifyInvalidatedRestore(bool dispose)
        {
            System.Threading.Tasks.TaskCompletionSource<bool> waiting = new System.Threading.Tasks.TaskCompletionSource<bool>();
            System.Threading.Tasks.TaskCompletionSource<bool> resume = new System.Threading.Tasks.TaskCompletionSource<bool>();
            bool candidate = false; int rebinds = 0;
            MockExplorerService explorer = new MockExplorerService(); explorer.GetCurrentPathFunc = h => @"C:\A";
            explorer.IsControlPanelPathFunc = p => p == explorer.PowerOptionsPath || p == explorer.AllControlPanelPath;
            explorer.IsControlPanelRootPathFunc = p => p == explorer.AllControlPanelPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(explorer, tracking,
                (vm, h) => { rebinds++; return false; }, delegate { }, delegate { }, delegate { }, h => true,
                () => candidate ? new List<IntPtr> { (IntPtr)100, (IntPtr)200 } : new List<IntPtr> { (IntPtr)100 },
                h => h == (IntPtr)200 ? explorer.AllControlPanelPath : @"C:\A", h => (NativeMethods.RECT?)null,
                p => true, null, ms => { waiting.TrySetResult(true); return resume.Task; });
            ExplorerWindowInteractionService interaction = new ExplorerWindowInteractionService(explorer, tracking,
                TestTabPersistenceFactory.Create(), h => "", delegate { }, delegate { }, delegate { },
                (vm, h) => false, delegate { }, () => null, delegate { }, null);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                vm.RestoreTabs(new[] { @"C:\A", explorer.PowerOptionsPath }, explorer.PowerOptionsPath, 1, true);
                TabItemViewModel saved = vm.ActiveTab;
                System.Threading.Tasks.Task restore = interaction.RestorePersistedSpecialActiveTabHostAsync(coordinator, vm, saved);
                Assert.IsTrue(waiting.Task.Wait(3000));
                if (dispose) vm.Dispose();
                else { vm.SelectTab(vm.Tabs[0]); vm.Tabs.Remove(saved); }
                candidate = true; resume.SetResult(true); restore.GetAwaiter().GetResult();
                Assert.AreEqual(0, rebinds); Assert.AreEqual((IntPtr)100, vm.ExplorerHwnd);
            }
        }

        [TestMethod]
        public void DetachCompletion_DistinguishesArrivalFromTimeout()
        {
            foreach (bool arrive in new[] { false, true })
            {
                string current = @"C:\A";
                MockExplorerService explorer = new MockExplorerService(); explorer.GetCurrentPathFunc = h => current;
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, current))
                {
                    TabItemViewModel a = vm.ActiveTab; vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer)); vm.CloseTab(a);
                    if (arrive) current = @"C:\B";
                    else typeof(TabNavigationStateTracker).GetField("_navigateStartTime", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                    TabDetachResult result = TabExternalDragOpenDecider.CompletePendingCloseAsync(vm, a).GetAwaiter().GetResult();
                    Assert.AreEqual(arrive ? TabDetachResult.Completed : TabDetachResult.WindowOpenedSourceRetained, result);
                    Assert.AreEqual(!arrive, vm.Tabs.Contains(a));
                }
            }
        }

        [TestMethod]
        public void FileOperationShutdown_WaitsForCopyAndMove_AndRejectsNewWork()
        {
            FileOperationTracker tracker = new FileOperationTracker();
            IDisposable first = tracker.TryBegin(), second = tracker.TryBegin();
            System.Threading.Tasks.Task stopping = tracker.StopAndWaitAsync();
            Assert.IsFalse(stopping.IsCompleted); Assert.IsNull(tracker.TryBegin());
            string dir = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "source"), "verified payload");
                File.Copy(Path.Combine(dir, "source"), Path.Combine(dir, "copy"));
                first.Dispose(); first.Dispose(); Assert.IsFalse(stopping.IsCompleted);
                File.Move(Path.Combine(dir, "copy"), Path.Combine(dir, "moved"));
                second.Dispose(); Assert.IsTrue(stopping.Wait(3000));
                Assert.AreEqual("verified payload", File.ReadAllText(Path.Combine(dir, "moved")));
                Assert.IsNull(tracker.TryBegin());
            }
            finally { first.Dispose(); second.Dispose(); Directory.Delete(dir, true); }
        }

        private sealed class QueuedExplorer : MockExplorerService
        {
            internal string Current = @"C:\A";
            internal readonly Queue<string> Requests = new Queue<string>();
            internal int Mode, Calls;
            public override string GetCurrentPath(IntPtr hwnd) { return Current; }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                Calls++;
                if (Mode == 1 && Calls % 2 == 0) return false;
                Requests.Enqueue(path);
                if (Mode == 2 && Calls % 2 == 0) throw new TimeoutException("Uncertain completion");
                return true;
            }
        }

        [TestMethod]
        public void SixStepSelectionSequences_PreserveTabIdentityAcrossRejectedAndDelayedNavigation()
        {
            FieldInfo startTime = typeof(TabNavigationStateTracker).GetField("_navigateStartTime", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int mode = 0; mode < 3; mode++)
            for (int encoded = 0; encoded < 46656; encoded++)
            {
                QueuedExplorer explorer = new QueuedExplorer { Mode = mode };
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)123, new MockUserSettings(), explorer, @"C:\A"))
                {
                    TabItemViewModel[] tabs = { vm.ActiveTab, new TabItemViewModel(@"C:\B", "B", explorer), new TabItemViewModel(@"C:\C", "C", explorer) };
                    vm.Tabs.Add(tabs[1]); vm.Tabs.Add(tabs[2]);
                    int code = encoded;
                    for (int step = 0; step < 6; step++)
                    {
                        int action = code % 6; code /= 6;
                        if (action < 3) vm.SelectTab(tabs[action]);
                        else if (action == 3)
                        {
                            if (explorer.Requests.Count > 0) explorer.Current = explorer.Requests.Dequeue();
                        }
                        else if (action == 4 || vm.NavigationTracker.NavigatingToPath != null)
                        {
                            if (action == 5) startTime.SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                            vm.NavigationTracker.InvalidateCache();
                            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                        }
                        for (int i = 0; i < 3; i++)
                            Assert.AreEqual(@"C:\" + (char)('A' + i), tabs[i].Path, "mode={0}, sequence={1}, step={2}", mode, encoded, step);
                        Assert.IsNotNull(vm.ActiveTab);
                        Assert.IsTrue(vm.Tabs.Contains(vm.ActiveTab));
                        Assert.AreEqual(vm.Tabs.IndexOf(vm.ActiveTab), vm.ActiveTabIndex);
                    }
                }
            }
        }

        [TestMethod]
        public void FailedSpecialHostPreparation_PreservesSavedTabAndSelectsActualFolder()
        {
            VerifyFailedRestore(@"C:\A", @"C:\A");
            VerifyFailedRestore(@"C:\Other", @"C:\Other");
            VerifyFailedRestore(null, @"C:\A");
        }

        private static void VerifyFailedRestore(string currentPath, string expectedSource)
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = h => currentPath;
            explorer.IsControlPanelPathFunc = p => p == explorer.AllControlPanelPath || p == explorer.PowerOptionsPath;
            explorer.IsControlPanelRootPathFunc = p => p == explorer.AllControlPanelPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(explorer, tracking,
                (vm, h) => { vm.SetExplorerHwnd(h); return true; }, delegate { }, delegate { }, delegate { },
                h => true, () => new List<IntPtr> { (IntPtr)100 }, h => currentPath ?? @"C:\A",
                h => (NativeMethods.RECT?)null, p => false, delegate { });
            ExplorerWindowInteractionService interaction = new ExplorerWindowInteractionService(explorer, tracking,
                TestTabPersistenceFactory.Create(), h => "", delegate { }, delegate { }, delegate { },
                (vm, h) => false, delegate { }, () => null, delegate { }, null);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                vm.RestoreTabs(new[] { @"C:\A", explorer.PowerOptionsPath }, explorer.PowerOptionsPath, 1, true);
                TabItemViewModel saved = vm.ActiveTab;
                interaction.RestorePersistedSpecialActiveTabHostAsync(coordinator, vm, saved).GetAwaiter().GetResult();
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(explorer.PowerOptionsPath, saved.Path);
                Assert.IsTrue(vm.Tabs.Contains(saved));
                Assert.AreEqual(expectedSource, vm.ActiveTab.Path);
                Assert.IsFalse(vm.IsRestoringControlPanelHost);
            }
        }

        [TestMethod]
        public void PartialSave_UsesActivePathWhenSavedIndexBelongsToAnotherTab()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "tabs.txt");
            try
            {
                MockExplorerService explorer = new MockExplorerService();
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
                {
                    TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                    vm.Tabs.Add(b); vm.SetActiveTabOnly(b);
                    TabPersistenceService store = new TabPersistenceService(file);
                    store.SaveTabsIfChanged(vm, true);
                    vm.Tabs.Move(1, 0); vm.SetActiveTabOnly(b);
                    using (FileStream locked = new FileStream(Path.Combine(directory, "tabs.active.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
                        store.SaveTabsIfChanged(vm, true);
                    using (TabBarViewModel restored = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
                    {
                        Assert.IsTrue(new TabPersistenceService(file).LoadTabsTo(restored));
                        Assert.AreEqual(@"C:\B", restored.ActiveTab.Path);
                        Assert.AreEqual(0, restored.ActiveTabIndex);
                    }
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void RestoreDuplicatePaths_PreservesSelectedDuplicateIndex()
        {
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), new MockExplorerService(), @"C:\A"))
            {
                vm.RestoreTabs(new[] { @"C:\A", @"C:\B", @"C:\B" }, @"C:\B", 2);
                Assert.AreEqual(2, vm.ActiveTabIndex);
            }
        }

        [TestMethod]
        public void SavedSettings_SubscriberExceptionDoesNotReportDiskFailureOrSkipOtherSubscribers()
        {
            string file = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".xml");
            FieldInfo current = typeof(UserSettings).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = current.GetValue(null);
            try
            {
                UserSettings settings = new UserSettings { FontSize = 19 };
                bool notified = false;
                settings.SettingsChanged += (s, e) => { throw new InvalidOperationException("Notification failed"); };
                settings.SettingsChanged += (s, e) => notified = true;
                string error;
                Assert.IsTrue(settings.TrySaveToPath(file, out error));
                Assert.IsNull(error);
                Assert.IsTrue(notified);
                Assert.AreEqual(19.0, UserSettings.LoadFromPath(file).FontSize);
                Assert.AreSame(settings, UserSettings.Current);
            }
            finally { current.SetValue(null, previous); if (File.Exists(file)) File.Delete(file); }
        }

        [TestMethod]
        public void DetachPendingNavigation_IsNotReportedAsCompletedAndTimeoutPreservesSource()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = h => @"C:\A";
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel source = vm.ActiveTab;
                vm.Tabs.Add(new TabItemViewModel(@"C:\B", "B", explorer));
                int opened = 0;
                TabDetachResult result = TabExternalDragOpenDecider.TryOpenInNewWindowAndCloseSourceTabAsync(
                    System.Windows.DragDropEffects.None, source, source.Path, vm, p => { opened++; return true; },
                    new NativeMethods.POINT { X = 0, Y = 0 }, new NativeMethods.RECT { Left = 10, Top = 10, Right = 100, Bottom = 100 },
                    null, null).GetAwaiter().GetResult();
                Assert.AreEqual(TabDetachResult.ClosePending, result);
                vm.TimeoutPendingNavigation();
                Assert.AreEqual(1, opened);
                Assert.IsTrue(vm.Tabs.Contains(source));
                Assert.AreEqual(@"C:\A", source.Path);
            }
        }
    }
}
