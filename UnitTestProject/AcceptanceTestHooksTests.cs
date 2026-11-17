using System;
using System.IO;
using System.Threading.Tasks;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class AcceptanceTestHooksTests
    {
        private string _root, _oldRoot, _oldDiagnostics;
        [TestInitialize]
        public void Initialize()
        {
            _oldRoot = Environment.GetEnvironmentVariable("KJTB_ACCEPTANCE_DIR");
            _oldDiagnostics = Environment.GetEnvironmentVariable("KJTB_DIAGNOSTICS");
            _root = Path.Combine(Path.GetTempPath(), "KjTabBar.Acceptance." + Guid.NewGuid().ToString("N"));
            // Initialize the cached diagnostics flag before changing the test environment.
            KjTabBar.Helpers.AppLogger.SetLogDirectoryOverrideForTests(Path.Combine(_root, "Logs"));
            Environment.SetEnvironmentVariable("KJTB_ACCEPTANCE_DIR", _root);
            Environment.SetEnvironmentVariable("KJTB_DIAGNOSTICS", "1");
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("KJTB_ACCEPTANCE_DIR", _oldRoot);
            Environment.SetEnvironmentVariable("KJTB_DIAGNOSTICS", _oldDiagnostics);
            KjTabBar.Helpers.AppLogger.Flush();
            KjTabBar.Helpers.AppLogger.SetLogDirectoryOverrideForTests(null);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void RequireAcceptanceBuild()
        {
            if (!AcceptanceTestHooks.Available) Assert.Inconclusive("Requires /p:KjtbAcceptanceHooks=true.");
            Directory.CreateDirectory(AcceptanceTestHooks.CommandDirectory);
        }

        private string Put(string name, string content)
        {
            string path = Path.Combine(AcceptanceTestHooks.CommandDirectory, name);
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public async Task NoOptIn_UsesRealNavigationWithoutConsumingCommands()
        {
            string directory;
            using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
                directory = Path.Combine(_root, p.Id.ToString());
            Directory.CreateDirectory(directory);
            string command = Path.Combine(directory, "navigate.txt");
            File.WriteAllText(command, "reject");
            Environment.SetEnvironmentVariable("KJTB_DIAGNOSTICS", null);
            int calls = 0;
            Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(() => { calls++; return Task.FromResult(true); }, () => true));
            Assert.AreEqual(1, calls);
            Assert.IsTrue(File.Exists(command));
            Assert.IsFalse(AcceptanceTestHooks.RejectRollback());
            Assert.IsFalse(AcceptanceTestHooks.MarkerActive("sync-pause"));
            if (!AcceptanceTestHooks.Available)
            {
                Environment.SetEnvironmentVariable("KJTB_DIAGNOSTICS", "1");
                Assert.IsNull(AcceptanceTestHooks.CommandDirectory);
                Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(() => Task.FromResult(true), () => true));
                Assert.IsTrue(File.Exists(command), "Normal build must ignore even explicitly armed commands.");
            }
        }

        [TestMethod]
        public async Task Commands_AreOneShotValidatedAndExpire()
        {
            RequireAcceptanceBuild();
            int calls = 0;
            Func<Task<bool>> real = () => { calls++; return Task.FromResult(true); };
            Put("navigate.txt", "reject");
            Assert.IsFalse(await AcceptanceTestHooks.NavigateAsync(real, () => true));
            Assert.AreEqual(0, calls);
            Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(real, () => true));
            Put("navigate.txt", "drop");
            Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(real, () => true));
            Assert.AreEqual(1, calls);
            Put("navigate.txt", "throw");
            try { await AcceptanceTestHooks.NavigateAsync(real, () => true); Assert.Fail(); }
            catch (TimeoutException) { }
            Put("navigate.txt", "delay:30001");
            Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(real, () => true));
            string stale = Put("navigate.txt", "reject");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-2));
            Assert.IsTrue(await AcceptanceTestHooks.NavigateAsync(real, () => true));
            Assert.AreEqual(3, calls);
            Put("rollback.txt", "reject");
            Assert.IsTrue(AcceptanceTestHooks.RejectRollback());
            Assert.IsFalse(AcceptanceTestHooks.RejectRollback());
            string pause = Put("sync-pause.flag", "");
            Assert.IsTrue(AcceptanceTestHooks.MarkerActive("sync-pause"));
            File.SetLastWriteTimeUtc(pause, DateTime.UtcNow.AddSeconds(-31));
            Assert.IsFalse(AcceptanceTestHooks.MarkerActive("sync-pause"));
        }

        [TestMethod]
        public async Task Delay_SkipsInvalidHostAndLateThrowObservesRealRequest()
        {
            RequireAcceptanceBuild();
            int calls = 0;
            Put("navigate.txt", "delay:20");
            Assert.IsFalse(await AcceptanceTestHooks.NavigateAsync(() => { calls++; return Task.FromResult(true); }, () => false));
            Assert.AreEqual(0, calls);
            TaskCompletionSource<bool> arrived = new TaskCompletionSource<bool>();
            Put("navigate.txt", "late-throw:20");
            try
            {
                await AcceptanceTestHooks.NavigateAsync(() => { arrived.SetResult(true); return Task.FromResult(true); }, () => true);
                Assert.Fail();
            }
            catch (TimeoutException) { }
            Assert.AreSame(arrived.Task, await Task.WhenAny(arrived.Task, Task.Delay(2000)));
        }

        [TestMethod]
        public async Task ClickPath_LateArrivalInSyntheticGraceCommitsSelection()
        {
            RequireAcceptanceBuild();
            string actual = @"C:\Unconfirmed";
            MockExplorerService explorer = new MockExplorerService { GetCurrentPathFunc = h => actual,
                NavigateFunc = (h, p) => { actual = p; return true; } };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(b);
                vm.SuspendSynchronizationAfterHostRollbackFailure();
                Put("navigate.txt", "late-throw:50");
                await vm.SelectTabAsync(b, null, null);
                Assert.IsTrue(vm.LastNavigationFailed);
                Assert.AreEqual(0, vm.ActiveTabIndex);
                for (int i = 0; i < 100 && actual != b.Path; i++) await Task.Delay(10);
                Assert.AreEqual(b.Path, actual);
                Put("grace.flag", "");
                vm.NavigationTracker.InvalidateCache();
                await vm.SyncWithExplorerAsync();
                Assert.AreSame(b, vm.ActiveTab);
                Assert.IsFalse(vm.IsHostLocationUnconfirmed);
                Assert.AreEqual(@"C:\A", vm.Tabs[0].Path);
            }
        }

        [TestMethod]
        public async Task HostRoundTrip_DiscardsDelayedRequest()
        {
            RequireAcceptanceBuild();
            int calls = 0;
            MockExplorerService explorer = new MockExplorerService { GetCurrentPathFunc = h => @"C:\A",
                NavigateFunc = (h, p) => { calls++; return true; } };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel b = new TabItemViewModel(@"C:\B", "B", explorer);
                vm.Tabs.Add(b);
                string command = Put("navigate.txt", "delay:1000");
                Task click = vm.SelectTabAsync(b, null, null);
                // Availability checks precede navigation. Switch only after the hook has claimed its command.
                for (int i = 0; i < 200 && File.Exists(command); i++) await Task.Delay(5);
                Assert.IsFalse(File.Exists(command), "The click must reach the navigation hook first.");
                vm.SetExplorerHwnd((IntPtr)200);
                vm.SetExplorerHwnd((IntPtr)100);
                await click;
                Assert.AreEqual(0, calls);
            }
        }

        [TestMethod]
        public async Task RealPreparationPath_InjectedRollbackFailureProtectsSavedTabs()
        {
            RequireAcceptanceBuild();
            bool opened = false;
            MockExplorerService explorer = new MockExplorerService {
                GetCurrentPathFunc = h => h == (IntPtr)100 ? @"C:\A" : "AllControlPanelPath",
                IsControlPanelPathFunc = p => p == "AllControlPanelPath" || p == "ProgramsAndFeaturesPath" };
            KjTabBar.Models.ExplorerWindowTrackingState tracking = new KjTabBar.Models.ExplorerWindowTrackingState();
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel b = new TabItemViewModel("ProgramsAndFeaturesPath", "B", explorer);
                vm.Tabs.Add(b);
                ExplorerHostSwitchCoordinator host = new ExplorerHostSwitchCoordinator(explorer, tracking,
                    (target, h) => { target.SetExplorerHwnd(h); return true; }, h => {}, (h, r) => {}, h => {}, h => true,
                    () => opened ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                        : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                    explorer.GetCurrentPath, p => { opened = true; return true; }, ms => {});
                Put("navigate.txt", "reject");
                Put("rollback.txt", "reject");
                await vm.SelectTabAsync(b, p => host.PrepareForPathAsync(vm, p, vm.IsPreparedTabOperationCurrent), host.CompletePendingReveal);
                Assert.IsTrue(host.HostRollbackFailed);
                Assert.IsTrue(vm.IsHostLocationUnconfirmed);
                Assert.AreEqual((IntPtr)200, vm.ExplorerHwnd);
                Assert.AreEqual(@"C:\A", vm.Tabs[0].Path);
                Assert.AreEqual("ProgramsAndFeaturesPath", vm.Tabs[1].Path);
            }
        }

        [TestMethod]
        public async Task SyncPause_ClickConfirmsArrivalBeforeNextObservedMove()
        {
            RequireAcceptanceBuild();
            string actual = @"C:\B";
            MockExplorerService explorer = new MockExplorerService { GetCurrentPathFunc = h => actual };
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel b = new TabItemViewModel(actual, "B", explorer);
                vm.Tabs.Add(b);
                vm.SuspendSynchronizationAfterHostRollbackFailure();
                string pause = Put("sync-pause.flag", "");
                await vm.SelectTabAsync(b, null, null);
                Assert.IsFalse(vm.IsHostLocationUnconfirmed);
                actual = @"C:\C";
                await vm.SyncWithExplorerAsync();
                Assert.AreEqual(@"C:\B", b.Path);
                File.Delete(pause);
                vm.NavigationTracker.InvalidateCache();
                await vm.SyncWithExplorerAsync();
                Assert.AreEqual(actual, b.Path);
                Assert.AreEqual(@"C:\A", vm.Tabs[0].Path);
            }
        }
    }
}
