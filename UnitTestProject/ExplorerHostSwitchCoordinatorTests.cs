using System;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerHostSwitchCoordinatorTests
    {
        [TestMethod]
        public void Overlap_RestoredActiveTabThrowPreservesSavedPath() { VerifyFailedHostNavigation(false, true, false, false, false, true); }
        [TestMethod]
        public void Overlap_RestoredActiveTabRejectionPreservesSavedPath() { VerifyFailedHostNavigation(false, false, false, false, false, true); }
        [TestMethod]
        public void Overlap_RestoredActiveTabTimeoutPreservesSavedPath() { VerifyFailedHostNavigation(false, false, false, false, true, true); }
        [TestMethod]
        public void FullReview_HostTimeoutRestoresOriginal() { VerifyFailedHostNavigation(false, false, false, false, true); }
        [TestMethod]
        public void FullReview_CloseAndHostTimeoutRestoresTabsAndHost() { VerifyFailedHostNavigation(false, false, true, false, true); }
        [TestMethod]
        public void FullReview_ParkedHostTimeoutRestoresOriginal() { VerifyFailedHostNavigation(true, false, false, false, true); }
        [TestMethod]
        public void Recheck2_CloseAfterHostSwitchFailurePreservesHistory() { VerifyFailedHostNavigation(false, true, true); }
        [TestMethod]
        public void Recheck2_FailedRollbackPreservesPathsUntilRetry() { VerifyFailedHostNavigation(false, true, false, true); }
        [TestMethod]
        public void Recheck2_FreshHostNavigationThrows() { VerifyFailedHostNavigation(false, true); }
        [TestMethod]
        public void Recheck2_FreshHostNavigationRejected() { VerifyFailedHostNavigation(false, false); }
        [TestMethod]
        public void Recheck2_ParkedHostNavigationThrows() { VerifyFailedHostNavigation(true, true); }
        [TestMethod]
        public void Recheck2_ParkedHostNavigationRejected() { VerifyFailedHostNavigation(true, false); }

        private void VerifyFailedHostNavigation(bool parked, bool throws, bool close = false, bool rejectRollback = false, bool timeout = false, bool restoredSelection = false)
        {
            FailedHostNavigationExplorer explorer = new FailedHostNavigationExplorer { Throws = throws, AllowNavigate = timeout };
            explorer.IsControlPanelPathFunc = path => path == explorer.AllControlPanelPath || path == explorer.PowerOptionsPath;
            explorer.IsControlPanelRootPathFunc = path => path == explorer.AllControlPanelPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            if (parked) tracking.RememberParkedExplorerOrigin((IntPtr)100, (IntPtr)200);
            bool launched = parked;
            IntPtr shown = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking, (vm, hwnd) => { if (rejectRollback && hwnd == (IntPtr)100) return false; vm.SetExplorerHwnd(hwnd); return true; },
                hwnd => shown = hwnd, delegate { }, delegate { }, hwnd => true,
                () => launched ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                explorer.GetCurrentPath, hwnd => (NativeMethods.RECT?)null,
                path => { launched = true; return true; }, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\A"))
            {
                TabItemViewModel original = vm.ActiveTab;
                TabItemViewModel target = new TabItemViewModel(explorer.PowerOptionsPath, "Power", explorer);
                vm.Tabs.Add(target);
                if (restoredSelection) vm.SetActiveTabOnly(target);
                Func<string, System.Threading.Tasks.Task<bool>> prepare = path => coordinator.PrepareForPathAsync(vm, path, vm.IsPreparedTabOperationCurrent);
                if (close) vm.CloseTabsAsync(0, 1, prepare, coordinator.CompletePendingReveal).GetAwaiter().GetResult();
                else vm.SelectTabAsync(target, prepare, coordinator.CompletePendingReveal).GetAwaiter().GetResult();
                if (timeout)
                {
                    Assert.AreEqual((IntPtr)200, vm.ExplorerHwnd);
                    typeof(TabNavigationStateTracker).GetField("_navigateStartTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .SetValue(vm.NavigationTracker, DateTime.UtcNow.AddSeconds(-6));
                    vm.NavigationTracker.InvalidateCache();
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                }
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.IsFalse(vm.HasClosedTabs);
                if (rejectRollback)
                {
                    Assert.AreEqual((IntPtr)200, vm.ExplorerHwnd);
                    Assert.IsTrue(vm.IsRestoringControlPanelHost);
                    vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                    Assert.AreEqual(@"C:\A", original.Path);
                    explorer.AllowNavigate = true;
                    vm.SelectTab(target);
                    Assert.IsFalse(vm.IsRestoringControlPanelHost);
                    Assert.AreSame(target, vm.ActiveTab);
                    return;
                }
                Assert.AreEqual((IntPtr)100, vm.ExplorerHwnd);
                Assert.AreEqual((IntPtr)100, shown);
                Assert.AreSame(original, vm.ActiveTab);
                IntPtr parkedHwnd;
                Assert.IsTrue(tracking.TryGetParkedExplorerOrigin((IntPtr)100, out parkedHwnd));
                Assert.AreEqual((IntPtr)200, parkedHwnd);
                System.Threading.Thread.Sleep(800);
                vm.SyncWithExplorerAsync().GetAwaiter().GetResult();
                Assert.AreEqual(@"C:\A", original.Path);
                Assert.AreEqual(explorer.PowerOptionsPath, target.Path);
                Assert.IsFalse(vm.IsTabOperationPending);
            }
        }

        private sealed class FailedHostNavigationExplorer : MockExplorerService
        {
            internal bool Throws;
            internal bool AllowNavigate;
            public override string GetCurrentPath(IntPtr hwnd) { return hwnd == (IntPtr)100 ? @"C:\A" : AllControlPanelPath; }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                if (AllowNavigate) return true;
                if (Throws) throw new TimeoutException("Simulated navigation timeout");
                return false;
            }
        }
        [TestMethod]
        public void PrepareForPath_UnregisteredHost_RebindsWithoutLosingTabs()
        {
            VerifyRegistrationRecovery(false, true, true);
        }

        [TestMethod]
        public void PrepareForPath_RegisteredHost_DoesNotLaunchReplacement()
        {
            VerifyRegistrationRecovery(true, true, true);
        }

        [TestMethod]
        public void PrepareForPath_RecoveryLaunchFails_PreservesOriginalHostAndTabs()
        {
            VerifyRegistrationRecovery(false, false, true);
        }

        [TestMethod]
        public void PrepareForPath_ReplacementNotRegistered_DoesNotRebind()
        {
            VerifyRegistrationRecovery(false, true, false);
        }

        [TestMethod]
        public void PrepareForPath_RegistrationQueryFails_DoesNotReplaceHost()
        {
            VerifyRegistrationRecovery(false, true, true, true);
        }

        [TestMethod]
        public void Review_PreparationCanceledBeforeRebind_PreservesOriginalHost()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsExplorerWindowRegisteredFunc = hwnd => hwnd != (IntPtr)100;
            bool current = true;
            int launches = 0;
            int rebinds = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, new ExplorerWindowTrackingState(),
                (vm, hwnd) => { rebinds++; vm.SetExplorerHwnd(hwnd); return true; },
                delegate { }, delegate { }, delegate { }, hwnd => true,
                () => launches > 0
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                hwnd => { current = false; return @"C:\Work"; },
                hwnd => (NativeMethods.RECT?)null,
                path => { launches++; return true; }, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work"))
            {
                Assert.IsFalse(coordinator.PrepareForPathAsync(vm, @"C:\Work", () => current).GetAwaiter().GetResult());
                Assert.AreEqual(1, launches);
                Assert.AreEqual(0, rebinds);
                Assert.AreEqual((IntPtr)100, vm.ExplorerHwnd);
                coordinator.CompletePendingReveal();
            }
        }

        [TestMethod]
        public void Recheck_CanceledLaunch_StopsPollingImmediately()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsExplorerWindowRegisteredFunc = hwnd => hwnd != (IntPtr)100;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            bool current = true;
            int enumerations = 0, delay = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking, (vm, hwnd) => { Assert.Fail("Must not rebind"); return false; },
                delegate { }, delegate { }, delegate { }, hwnd => true,
                () => { enumerations++; return new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                hwnd => @"C:\Other", hwnd => (NativeMethods.RECT?)null,
                path => { current = false; return true; }, milliseconds => delay += milliseconds);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work"))
            {
                Assert.IsFalse(coordinator.PrepareForPathAsync(vm, @"C:\Work", () => current).GetAwaiter().GetResult());
                Assert.AreEqual(1, enumerations);
                Assert.AreEqual(0, delay);
                Assert.IsFalse(tracking.HasPendingInternalHostSwitchLaunchRequest());
            }
        }

        [TestMethod]
        public void Recheck_CanceledLaunch_ReleasesOnlyItsOwnHiddenWindow()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsExplorerWindowRegisteredFunc = hwnd => false;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            tracking.MarkInternalHostSwitchLaunchWindow((IntPtr)300);
            tracking.HiddenPendingAbsorb[(IntPtr)300] = DateTime.UtcNow;
            bool current = true;
            IntPtr revealed = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking, (vm, hwnd) => false, hwnd => revealed = hwnd,
                delegate { }, delegate { }, hwnd => true,
                () => new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                hwnd => null, hwnd => (NativeMethods.RECT?)null,
                path =>
                {
                    tracking.TryConsumeInternalHostSwitchLaunchRequest();
                    tracking.MarkInternalHostSwitchLaunchWindow((IntPtr)200);
                    tracking.HiddenPendingAbsorb[(IntPtr)200] = DateTime.UtcNow;
                    current = false;
                    return true;
                }, delegate { });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work"))
            {
                Assert.IsFalse(coordinator.PrepareForPathAsync(vm, @"C:\Work", () => current).GetAwaiter().GetResult());
                Assert.AreEqual((IntPtr)200, revealed);
                Assert.IsFalse(tracking.HiddenPendingAbsorb.ContainsKey((IntPtr)200));
                Assert.IsFalse(tracking.InternalHostSwitchLaunchWindows.Contains((IntPtr)200));
                Assert.IsTrue(tracking.HiddenPendingAbsorb.ContainsKey((IntPtr)300));
                Assert.IsTrue(tracking.InternalHostSwitchLaunchWindows.Contains((IntPtr)300));
            }
        }

        [TestMethod]
        public void Recheck_HostPolling_StopsOnElapsedTimeBeforeAttemptLimit()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsExplorerWindowRegisteredFunc = hwnd => false;
            int enumerations = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, new ExplorerWindowTrackingState(), (vm, hwnd) => false,
                delegate { }, delegate { }, delegate { }, hwnd => true,
                () => { enumerations++; return new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                hwnd => null, hwnd => (NativeMethods.RECT?)null, path => true,
                milliseconds => System.Threading.Thread.Sleep(30));
            coordinator.NewHostWaitTimeout = TimeSpan.FromMilliseconds(20);
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work"))
            {
                Assert.IsFalse(coordinator.PrepareForPathAsync(vm, @"C:\Work").GetAwaiter().GetResult());
                Assert.IsTrue(enumerations <= 2);
            }
        }

        private static void VerifyRegistrationRecovery(bool registered, bool launchSucceeds, bool replacementRegistered, bool queryFails = false)
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsExplorerWindowRegisteredFunc = hwnd =>
            {
                if (queryFails) throw new InvalidOperationException("Shell query failed.");
                return hwnd == (IntPtr)100 ? registered : replacementRegistered;
            };
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            int launches = 0;
            int rebinds = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                (vm, hwnd) => { rebinds++; vm.SetExplorerHwnd(hwnd); return true; },
                delegate { }, delegate { }, delegate { }, hwnd => true,
                () => launches > 0 && launchSucceeds
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                hwnd => @"C:\Work",
                hwnd => (NativeMethods.RECT?)null,
                path => { launches++; return launchSucceeds; }, delegate { });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work");
            viewModel.InsertTabWithPath(@"C:\Other", viewModel.Tabs.Count, false);
            int count = viewModel.Tabs.Count;
            TabItemViewModel active = viewModel.ActiveTab;
            bool recovered = !queryFails && !registered && launchSucceeds && replacementRegistered;
            Assert.AreEqual(registered || recovered, coordinator.PrepareForPath(viewModel, @"C:\Work"));
            Assert.AreEqual(registered || queryFails ? 0 : 1, launches);
            Assert.AreEqual(recovered ? 1 : 0, rebinds);
            Assert.AreEqual(recovered ? (IntPtr)200 : (IntPtr)100, viewModel.ExplorerHwnd);
            Assert.AreEqual(count, viewModel.Tabs.Count);
            Assert.AreSame(active, viewModel.ActiveTab);
            coordinator.CompletePendingReveal();
        }

        [TestMethod]
        public void FolderLaunchFromControlPanel_RestoresMaximizedParkedHost()
        {
            VerifyFolderLaunchRestoreState(true, 2, 2, 3);
        }

        [TestMethod]
        public void FolderLaunchFromControlPanel_RestoresMaximizedFreshHost()
        {
            VerifyFolderLaunchRestoreState(false, 2, 2, 3);
        }

        [TestMethod]
        public void FolderLaunchFromControlPanel_RestoresNormalMinimizedHost()
        {
            VerifyFolderLaunchRestoreState(true, 2, 0, 1);
        }

        [TestMethod]
        public void FolderLaunchFromControlPanel_PreservesRestoredNormalState()
        {
            VerifyFolderLaunchRestoreState(true, 1, 2, 1);
        }

        private static void VerifyFolderLaunchRestoreState(bool parked, uint showCommand, uint flags, uint expected)
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsControlPanelPathFunc = path => path == explorer.PowerOptionsPath;
            explorer.GetCurrentPathFunc = hwnd => hwnd == (IntPtr)100 ? explorer.PowerOptionsPath : @"C:\Work";
            NativeMethods.RECT normal = new NativeMethods.RECT { Left = 200, Top = 150, Right = 1200, Bottom = 850 };
            NativeMethods.WINDOWPLACEMENT source = new NativeMethods.WINDOWPLACEMENT
                { showCmd = showCommand, flags = flags, rcNormalPosition = normal };
            int writes = 0;
            bool rebound = false;
            bool launched = false;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { },
                hwnd => hwnd == (IntPtr)100 ? source : (NativeMethods.WINDOWPLACEMENT?)null,
                (hwnd, placement) =>
                {
                    Assert.IsTrue(rebound);
                    Assert.AreEqual((IntPtr)200, hwnd);
                    Assert.AreEqual(expected, placement.showCmd);
                    Assert.AreEqual(normal, placement.rcNormalPosition);
                    writes++;
                    return true;
                });
            if (parked) tracking.RememberParkedExplorerOrigin((IntPtr)100, (IntPtr)200);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking, (vm, hwnd) => { vm.SetExplorerHwnd(hwnd); rebound = true; return true; },
                hwnd => { Assert.AreEqual(1, writes); }, delegate { }, delegate { }, hwnd => true,
                () => launched ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 },
                hwnd => hwnd == (IntPtr)100 ? explorer.PowerOptionsPath : @"C:\Work",
                hwnd => new NativeMethods.RECT { Left = -32000, Top = -32000, Right = -31840, Bottom = -31972 },
                path => { launched = true; return true; }, delegate { });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.PowerOptionsPath);
            Assert.IsTrue(coordinator.PrepareForPath(viewModel, @"C:\Work"));
            Assert.AreEqual(0, writes, "Preparation must not reveal a host before tab navigation completes.");
            ExplorerWindowInteractionService interaction = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(), delegate { return string.Empty; },
                delegate { }, delegate { Assert.AreEqual(1, writes, "Apply the inherited state before foreground activation."); },
                delegate { }, delegate { return null; }, delegate { });
            Assert.IsTrue(interaction.AbsorbExplorerWindow((IntPtr)300, viewModel, @"C:\Work", false, false, delegate { }));
            coordinator.CompletePendingReveal();
            Assert.AreEqual(1, writes);
        }

        [TestMethod]
        public void PrepareForPath_ExposesDestinationHostTypeBeforeRebind_AndRestoresFolderType()
        {
            VerifyHostTypeDuringRebind(0);
        }

        [TestMethod]
        public void PrepareForPath_RestoresHostTypeWhenRebindIsRejected()
        {
            VerifyHostTypeDuringRebind(1);
        }

        [TestMethod]
        public void PrepareForPath_RestoresHostTypeWhenRebindThrows()
        {
            VerifyHostTypeDuringRebind(2);
        }

        private static void VerifyHostTypeDuringRebind(int failureMode)
        {
            const string target = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            const string folder = @"C:\Work";
            ExplorerManager paths = new ExplorerManager();
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsControlPanelPathFunc = paths.IsControlPanelPath;
            explorer.IsControlPanelRootPathFunc = paths.IsControlPanelRootPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            bool launched = false;
            int rebindCalls = 0;
            ExplorerHostSwitchCoordinator coordinator = null;
            coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalls++;
                    Assert.AreEqual((bool?)(hwnd == (IntPtr)200), coordinator.CurrentHostIsControlPanel,
                        "Owner selection must see the destination before the view model is rebound.");
                    if (hwnd == (IntPtr)100 && failureMode == 1) return false;
                    if (hwnd == (IntPtr)100 && failureMode == 2) throw new InvalidOperationException("Rejected rebind");
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate { }, delegate { }, delegate { }, delegate { return true; },
                delegate { return launched
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)200 ? target : folder; },
                delegate { launched = true; return true; }, delegate { });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, folder);

            Assert.IsNull(coordinator.CurrentHostIsControlPanel);
            Assert.IsTrue(coordinator.PrepareForPath(viewModel, target));
            Assert.AreEqual((bool?)true, coordinator.CurrentHostIsControlPanel);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            // Navigation commits the selected path after the host preparation callback.
            viewModel.ActiveTab.Path = target;

            Assert.AreEqual(failureMode == 0, coordinator.PrepareForPath(viewModel, folder));
            Assert.AreEqual(2, rebindCalls);
            Assert.AreEqual((bool?)(failureMode != 0), coordinator.CurrentHostIsControlPanel);
            Assert.AreEqual(failureMode == 0 ? (IntPtr)100 : (IntPtr)200, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_AcceptsHostThatAppearsAfterFiveSeconds()
        {
            VerifySlowHostLaunch(false);
        }

        [TestMethod]
        public void PrepareForPath_StopsWaitingWhenOriginalWindowCloses()
        {
            VerifySlowHostLaunch(true);
        }

        private static void VerifySlowHostLaunch(bool closeOriginal)
        {
            const string target = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            ExplorerManager paths = new ExplorerManager();
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsControlPanelPathFunc = paths.IsControlPanelPath;
            explorer.IsControlPanelRootPathFunc = paths.IsControlPanelRootPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            int delays = 0;
            int launches = 0;
            IntPtr shown = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                delegate (IntPtr hwnd) { shown = hwnd; }, delegate { }, delegate { },
                delegate { return !closeOriginal || delays < 3; },
                delegate { return delays >= 50
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)200 ? target : explorer.HomeFolderPath; },
                delegate { launches++; return true; },
                delegate (int milliseconds)
                {
                    Assert.AreEqual(100, milliseconds);
                    Assert.AreEqual(IntPtr.Zero, shown);
                    delays++;
                });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);

            bool prepared = coordinator.PrepareForPath(viewModel, target);
            coordinator.CompletePendingReveal();

            Assert.AreEqual(!closeOriginal, prepared);
            Assert.AreEqual(closeOriginal ? 3 : 50, delays);
            Assert.AreEqual(1, launches);
            Assert.AreEqual(closeOriginal ? IntPtr.Zero : (IntPtr)200, shown);
            if (closeOriginal) Assert.IsFalse(tracking.HasPendingInternalHostSwitchLaunchRequest());
        }

        [TestMethod]
        public void PrepareForPath_CreatesParentHistory_ForPowerOptions()
        {
            VerifyControlPanelParentHistory("PowerOptionsPath");
        }

        [TestMethod]
        public void PrepareForPath_CreatesParentHistory_ForStorageSpaces()
        {
            VerifyControlPanelParentHistory("StorageSpacesPath");
        }

        [TestMethod]
        public void PrepareForPath_MatchesControlPanelRootWithDifferentGuid()
        {
            VerifyControlPanelRootWindow("::{21EC2020-3AEA-1069-A2DD-08002B30309D}", true);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotMatchControlPanelChildAsRoot()
        {
            VerifyControlPanelRootWindow("::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}", false);
        }

        private static void VerifyControlPanelRootWindow(string observedPath, bool expectedMatch)
        {
            const string targetPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            ExplorerManager pathService = new ExplorerManager();
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsControlPanelPathFunc = pathService.IsControlPanelPath;
            explorer.IsControlPanelRootPathFunc = pathService.IsControlPanelRootPath;
            explorer.NormalizeKnownPathFunc = pathService.NormalizeKnownPath;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            bool launched = false;
            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                delegate (TabBarViewModel vm, IntPtr hwnd) { reboundHwnd = hwnd; vm.SetExplorerHwnd(hwnd); return true; },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; }, delegate { }, delegate { },
                delegate { return true; },
                delegate { return launched
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr>(); },
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)200 ? observedPath : explorer.HomeFolderPath; },
                delegate (string path) { Assert.AreEqual(targetPath, path); launched = true; return true; },
                delegate { });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);

            Assert.AreEqual(expectedMatch, coordinator.PrepareForPath(viewModel, targetPath));
            coordinator.CompletePendingReveal();
            Assert.AreEqual(expectedMatch ? (IntPtr)200 : IntPtr.Zero, reboundHwnd);
            Assert.AreEqual(expectedMatch ? (IntPtr)200 : IntPtr.Zero, shownHwnd);
            Assert.AreEqual(expectedMatch ? (IntPtr)200 : (IntPtr)100, viewModel.ExplorerHwnd);
        }

        private static void VerifyControlPanelParentHistory(string itemPath)
        {
            ControlPanelHistoryExplorerService explorer = new ControlPanelHistoryExplorerService(itemPath);
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            string launchedPath = null;
            int launchCount = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                delegate { }, delegate { }, delegate { },
                delegate { return true; },
                delegate { return launchedPath == null
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }; },
                delegate (IntPtr hwnd) { return explorer.GetCurrentPath(hwnd); },
                delegate (string path) { launchCount++; launchedPath = path; explorer.NewWindowPath = path; return true; },
                delegate { });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);
            viewModel.RestoreTabs(new string[] { itemPath }, itemPath, 0, true);

            Assert.IsTrue(coordinator.PrepareForPath(viewModel, itemPath));
            viewModel.SelectTab(viewModel.ActiveTab);
            coordinator.CompletePendingReveal();

            Assert.AreEqual(explorer.AllControlPanelPath, launchedPath, "Open the parent before navigating to the saved item.");
            Assert.AreEqual(itemPath, explorer.NewWindowPath);
            Assert.AreEqual(explorer.AllControlPanelPath, explorer.PreviousPath, "Back must lead to the Control Panel parent.");
            Assert.AreEqual(1, launchCount);
            Assert.AreEqual(1, viewModel.Tabs.Count);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }

        private sealed class ControlPanelHistoryExplorerService : MockExplorerService
        {
            public string NewWindowPath { get; set; }
            public string PreviousPath { get; private set; }

            public ControlPanelHistoryExplorerService(string itemPath)
            {
                IsControlPanelPathFunc = path => path == AllControlPanelPath || path == itemPath;
                IsControlPanelRootPathFunc = path => path == AllControlPanelPath;
            }

            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return explorerHwnd == (IntPtr)200 ? NewWindowPath : HomeFolderPath;
            }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                Assert.AreEqual((IntPtr)200, explorerHwnd, "Do not navigate the ordinary host to a Control Panel item.");
                PreviousPath = NewWindowPath;
                NewWindowPath = path;
                return true;
            }
        }

        [TestMethod]
        public void PrepareForPath_RestoresParkedExplorerHost_ForNormalPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)100)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            NativeMethods.RECT rectAtRebind = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    rectAtRebind = movedRect;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 30,
                            Top = 40,
                            Right = 830,
                            Bottom = 640
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(30, rectAtRebind.Left, "Align the host before rebinding updates the tab bar position.");
            Assert.AreEqual((IntPtr)100, reboundHwnd);
            Assert.AreEqual((IntPtr)100, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, viewModel.ExplorerHwnd);
            Assert.AreEqual(30, movedRect.Left);
            Assert.AreEqual(40, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)100]);
        }

        [TestMethod]
        public void CompletePendingReveal_DoesNotMoveParkedHostToOffscreenCurrentRect()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                return hwnd == (IntPtr)200 ? explorerService.PowerOptionsPath : @"C:\Work";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            IntPtr shownHwnd = IntPtr.Zero;
            int moveCallCount = 0;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { moveCallCount++; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = -32000,
                            Top = -32000,
                            Right = -31839,
                            Bottom = -31757
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)100, shownHwnd);
            Assert.AreEqual(0, moveCallCount);
            Assert.AreEqual((IntPtr)100, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_DoesNothing_ForControlPanelPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.IsTrue(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotSwitchToFreshExplorerHost_WhenCurrentPathIsNormalButActiveTabIsControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                new ExplorerWindowTrackingState(),
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Users\Test");

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.IsNull(explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotQueryCurrentPath_WhenActiveTabAlreadyMatchesNormalTarget()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            int getCurrentPathCallCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                getCurrentPathCallCount++;
                return @"C:\Initial";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            bool rebindCalled = false;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rebindCalled = true;
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(@"C:\Work", 1, false);
            viewModel.SelectTab(viewModel.Tabs[1]);
            getCurrentPathCallCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                Assert.Fail("GetCurrentPath should not be called when the active tab already indicates a normal host.");
                return null;
            };

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Users\Test");

            Assert.IsTrue(prepared);
            Assert.IsFalse(rebindCalled);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, getCurrentPathCallCount);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesToFreshExplorerHost_WhenCurrentHostIsControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)300)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 50,
                            Top = 60,
                            Right = 850,
                            Bottom = 660
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(@"C:\Work", explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
            Assert.AreEqual(0, movedRect.Width);
            Assert.AreEqual(0, movedRect.Height);
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)300]);
        }

        [TestMethod]
        public void CompletePendingReveal_RestoresCurrentHostRect_ForFreshHostHiddenOffscreen()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.HiddenPendingAbsorb[(IntPtr)300] = DateTime.UtcNow;
            trackingState.HiddenOriginalRects[(IntPtr)300] = new NativeMethods.RECT
            {
                Left = 123,
                Top = 234,
                Right = 923,
                Bottom = 834
            };

            IntPtr shownHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            NativeMethods.RECT rectAtRebind = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    rectAtRebind = movedRect;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)200)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 10,
                            Top = 20,
                            Right = 810,
                            Bottom = 620
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(10, rectAtRebind.Left, "Align the host before rebinding updates the tab bar position.");
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(10, movedRect.Left);
            Assert.AreEqual(20, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
        }

        [TestMethod]
        public void CompletePendingReveal_FallsBackToHiddenRect_WhenCurrentHostRectUnavailable()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.HiddenPendingAbsorb[(IntPtr)300] = DateTime.UtcNow;
            trackingState.HiddenOriginalRects[(IntPtr)300] = new NativeMethods.RECT
            {
                Left = 123,
                Top = 234,
                Right = 923,
                Bottom = 834
            };

            IntPtr shownHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd) { return null; },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual(123, movedRect.Left);
            Assert.AreEqual(234, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesToFreshExplorerHost_WhenTargetPathIsControlPanelAndNoParkedOrigin()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)300)
                {
                    return explorerService.AllControlPanelPath;
                }

                return @"C:\MockPath";
            };

            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(explorerService.AllControlPanelPath, explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)200, trackingState.ParkedExplorerOrigins[(IntPtr)300]);
        }

        [TestMethod]
        public void PrepareForPath_SwitchesBackToParkedControlPanelHost_ForControlPanelPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)300)
                {
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)300, (IntPtr)200);

            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (IntPtr hwnd)
                {
                    if (hwnd == (IntPtr)300)
                    {
                        return new NativeMethods.RECT
                        {
                            Left = 50,
                            Top = 60,
                            Right = 850,
                            Bottom = 660
                        };
                    }

                    return null;
                },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)300, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[0]);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);
            Assert.AreEqual(IntPtr.Zero, shownHwnd);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual((IntPtr)200, reboundHwnd);
            Assert.AreEqual((IntPtr)200, shownHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            Assert.AreEqual(50, movedRect.Left);
            Assert.AreEqual(60, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
            Assert.AreEqual((IntPtr)300, trackingState.ParkedExplorerOrigins[(IntPtr)200]);
        }

        [TestMethod]
        public void PrepareForPath_DoesNotQueryParkedHostPath_WhenParkedOriginExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

            int currentHostPathQueryCount = 0;
            int parkedHostPathQueryCount = 0;
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)300)
                {
                    currentHostPathQueryCount++;
                    return @"C:\Work";
                }

                if (hwnd == (IntPtr)200)
                {
                    parkedHostPathQueryCount++;
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.RememberParkedExplorerOrigin((IntPtr)300, (IntPtr)200);

            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)300, new MockUserSettings(), explorerService);
            currentHostPathQueryCount = 0;
            parkedHostPathQueryCount = 0;

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.PowerOptionsPath);

            Assert.IsTrue(prepared);
            Assert.AreEqual(1, currentHostPathQueryCount);
            Assert.AreEqual(0, parkedHostPathQueryCount);
            Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
        }
        [TestMethod]
        public void PrepareForPath_CancelsInternalHostSwitchLaunchRequest_WhenNewWindowIsNotFound()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                return @"C:\MockPath";
            };
            explorerService.FindExplorerWindowsFunc = delegate
            {
                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd) { return true; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, @"C:\Work");

            Assert.IsFalse(prepared);
            Assert.IsFalse(trackingState.TryConsumeInternalHostSwitchLaunchRequest());
        }

        [TestMethod]
        public void PrepareForPath_MatchesResolvedHomePath_WhenTargetIsHomeShellPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.HomeFolderPath = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetCurrentPathFunc = delegate (IntPtr hwnd)
            {
                if (hwnd == (IntPtr)200)
                {
                    return explorerService.PowerOptionsPath;
                }

                if (hwnd == (IntPtr)300)
                {
                    return explorerService.GetResolvedHomeFolderPath();
                }

                return @"C:\MockPath";
            };
            int findExplorerWindowsCallCount = 0;
            explorerService.FindExplorerWindowsFunc = delegate
            {
                findExplorerWindowsCallCount++;
                if (findExplorerWindowsCallCount == 1)
                {
                    return new System.Collections.Generic.List<IntPtr> { (IntPtr)200 };
                }

                return new System.Collections.Generic.List<IntPtr> { (IntPtr)200, (IntPtr)300 };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr reboundHwnd = IntPtr.Zero;
            IntPtr shownHwnd = IntPtr.Zero;
            ExplorerHostSwitchCoordinator coordinator = new ExplorerHostSwitchCoordinator(
                explorerService,
                trackingState,
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    reboundHwnd = hwnd;
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { shownHwnd = hwnd; },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { return true; },
                delegate { return explorerService.FindExplorerWindows(); },
                delegate (IntPtr hwnd) { return explorerService.GetCurrentPath(hwnd); },
                delegate (string path) { return explorerService.OpenInNewWindow(path); },
                delegate (int millisecondsTimeout) { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)200, new MockUserSettings(), explorerService);
            viewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 1, true);
            viewModel.SelectTab(viewModel.Tabs[1]);

            bool prepared = coordinator.PrepareForPath(viewModel, explorerService.HomeFolderPath);
            coordinator.CompletePendingReveal();

            Assert.IsTrue(prepared);
            Assert.AreEqual(explorerService.HomeFolderPath, explorerService.OpenedInNewWindowPath);
            Assert.AreEqual((IntPtr)300, reboundHwnd);
            Assert.AreEqual((IntPtr)300, shownHwnd);
            Assert.AreEqual((IntPtr)300, viewModel.ExplorerHwnd);
        }
    }
}


