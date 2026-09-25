using System;
using System.IO;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowInteractionServiceTests
    {
        [TestMethod]
        public void DesktopLaunch_ReusesActiveOrLeftmostMatch_AndDragStillAdds()
        {
            string[] paths = new string[]
            {
                @"C:\Work",
                "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
                "::{645FF040-5081-101B-9F08-00AA002F954E}",
                "AllControlPanelPath", "PowerOptionsPath", "ProgramsAndFeaturesPath"
            };
            foreach (string path in paths)
            {
                MockExplorerService explorer = new MockExplorerService();
                explorer.IsControlPanelPathFunc = p => p == explorer.AllControlPanelPath ||
                    p == explorer.PowerOptionsPath || p == explorer.ProgramsAndFeaturesPath;
                string current = @"C:\Other";
                explorer.GetCurrentPathFunc = h => current;
                explorer.NavigateFunc = (h, p) => { current = p; return true; };
                ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
                int foreground = 0;
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorer, tracking, TestTabPersistenceFactory.Create(), h => "",
                    delegate { }, h => foreground++, delegate { }, () => null, delegate { });
                using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, current))
                {
                    vm.InsertTabWithPath(path, vm.Tabs.Count, true);
                    TabItemViewModel first = vm.ActiveTab;
                    vm.DuplicateTab(first);
                    TabItemViewModel second = vm.ActiveTab;
                    Assert.AreEqual(3, vm.Tabs.Count, "Explicit duplication must create a tab.");
                    bool controlPanel = explorer.IsControlPanelPath(path);
                    Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)200, vm, path, true, controlPanel, null, reuseExistingTab: true), path);
                    Assert.AreSame(second, vm.ActiveTab, "Prefer the active duplicate: " + path);
                    Assert.AreEqual(3, vm.Tabs.Count);
                    vm.SelectTab(vm.Tabs[0]);
                    Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)300, vm, path, true, controlPanel, null, reuseExistingTab: true), path);
                    Assert.AreSame(first, vm.ActiveTab, "Otherwise select the leftmost match: " + path);
                    Assert.AreEqual(3, vm.Tabs.Count);
                    Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)400, vm, path, true, controlPanel, null), path);
                    Assert.AreEqual(4, vm.Tabs.Count, "Dragging must add a duplicate: " + path);
                    Assert.AreEqual(3, foreground);
                }
            }
        }

        [TestMethod]
        public void DesktopControlPanel_DifferentItemIsNotReplaced_AndFailedRebindKeepsTabs()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.IsControlPanelPathFunc = p => p == explorer.AllControlPanelPath || p == explorer.PowerOptionsPath;
            bool canRebind = true;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorer, new ExplorerWindowTrackingState(), TestTabPersistenceFactory.Create(),
                (vm, hwnd) => { if (!canRebind) return false; vm.SetExplorerHwnd(hwnd); return true; });
            using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.AllControlPanelPath))
            {
                TabItemViewModel root = vm.ActiveTab;
                Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)200, vm, explorer.PowerOptionsPath, true, true, null, reuseExistingTab: true));
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.AreEqual(explorer.AllControlPanelPath, root.Path);
                TabItemViewModel power = vm.ActiveTab;
                canRebind = false;
                Assert.IsFalse(service.AbsorbExplorerWindow((IntPtr)300, vm, explorer.AllControlPanelPath, true, true, null, reuseExistingTab: true));
                Assert.AreEqual(2, vm.Tabs.Count);
                Assert.AreSame(power, vm.ActiveTab);
                Assert.AreEqual(explorer.AllControlPanelPath, root.Path);
            }
        }

        [TestMethod]
        public void DesktopLaunch_RestoredTabsReuseMatchingFolderOrSpecialItem()
        {
            string[] paths = new string[] { @"C:\Work", "::{679F85CB-0220-4080-B29B-5540CC05AAB6}", "AllControlPanelPath",
                "PowerOptionsPath", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "::{645FF040-5081-101B-9F08-00AA002F954E}" };
            foreach (string path in paths)
            {
                string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tabs.txt");
                try
                {
                    ProtectedTextStorage.SaveLines(file, new string[] { @"C:\Other", path, path });
                    MockExplorerService explorer = new MockExplorerService();
                    explorer.IsControlPanelPathFunc = p => p == explorer.AllControlPanelPath || p == explorer.PowerOptionsPath;
                    explorer.GetCurrentPathFunc = h => path;
                    explorer.HomeFolderPath = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
                    ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                        explorer, new ExplorerWindowTrackingState(), new TabPersistenceService(file));
                    using (TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, path))
                    {
                        service.InitializeTabsForNewWindow(vm, path, true, true);
                        Assert.AreEqual(3, vm.Tabs.Count, path);
                        Assert.AreSame(vm.Tabs[1], vm.ActiveTab, path);
                    }
                }
                finally { if (File.Exists(file)) File.Delete(file); }
            }
        }
        [TestMethod]
        public void SuccessiveControlPanelShortcuts_KeepOriginalFolderHost()
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            System.Collections.Generic.List<IntPtr> closed = new System.Collections.Generic.List<IntPtr>();
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(), delegate { return string.Empty; },
                delegate { }, delegate { }, hwnd => closed.Add(hwnd), delegate { return null; }, delegate { });
            TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, @"C:\Work");
            Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)200, vm, explorer.AllControlPanelPath, true, true, delegate { }));
            Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)300, vm, explorer.PowerOptionsPath, true, true, delegate { }));
            Assert.AreEqual((IntPtr)100, tracking.ParkedExplorerOrigins[(IntPtr)300],
                "Return to the original folder Explorer, preserving its collapsed ribbon.");
            Assert.IsFalse(tracking.ParkedExplorerOrigins.ContainsKey((IntPtr)200));
            CollectionAssert.AreEqual(new IntPtr[] { (IntPtr)200 }, closed);
            Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)400, vm, explorer.AllControlPanelPath, true, true, delegate { }));
            Assert.AreEqual((IntPtr)100, tracking.ParkedExplorerOrigins[(IntPtr)400]);
            CollectionAssert.AreEqual(new IntPtr[] { (IntPtr)200, (IntPtr)300 }, closed);
        }

        [TestMethod]
        public void ControlPanelLaunch_RestoresMaximizedStateAfterMinimize()
        {
            VerifyControlPanelRestoreState(2, 2, 3);
        }

        [TestMethod]
        public void ControlPanelLaunch_RestoresNormalStateAfterMinimize()
        {
            VerifyControlPanelRestoreState(2, 0, 1);
        }

        [TestMethod]
        public void ControlPanelLaunch_DoesNotReapplyOldMaximizeFlagToNormalWindow()
        {
            VerifyControlPanelRestoreState(1, 2, 1);
        }

        private static void VerifyControlPanelRestoreState(uint showCommand, uint flags, uint expectedShowCommand)
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            NativeMethods.RECT normal = new NativeMethods.RECT { Left = 100, Top = 100, Right = 1100, Bottom = 800 };
            NativeMethods.WINDOWPLACEMENT source = new NativeMethods.WINDOWPLACEMENT
                { showCmd = showCommand, flags = flags, rcNormalPosition = normal };
            int writes = 0;
            bool rebound = false;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { },
                hwnd => hwnd == (IntPtr)100 ? source : (NativeMethods.WINDOWPLACEMENT?)null,
                (hwnd, placement) =>
                {
                    Assert.IsTrue(rebound, "Do not reveal the replacement before rebinding succeeds.");
                    Assert.AreEqual((IntPtr)200, hwnd);
                    Assert.AreEqual(expectedShowCommand, placement.showCmd);
                    Assert.AreEqual(normal, placement.rcNormalPosition);
                    writes++;
                    return true;
                });
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(), delegate { return string.Empty; },
                delegate { }, delegate { Assert.AreEqual(1, writes, "Restore state before foreground activation."); },
                delegate { }, (vm, hwnd) => { vm.SetExplorerHwnd(hwnd); rebound = true; return true; },
                delegate { }, delegate { return null; }, delegate { }, null);
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.AllControlPanelPath);
            Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)200, viewModel, explorer.PowerOptionsPath, true, true, delegate { }));
            Assert.AreEqual(1, writes);
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_DoesNotNavigateToSavedTabBeforeExplicitShortcut()
        {
            string tabsFilePath = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".shortcut.tabs.txt");
            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedFolder" });
                PendingShortcutExplorerService explorer = new PendingShortcutExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorer, new ExplorerWindowTrackingState(), new TabPersistenceService(tabsFilePath),
                    delegate { return string.Empty; }, delegate { }, delegate { }, delegate { },
                    delegate { return null; }, delegate { });
                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)403, new MockUserSettings(), explorer, @"C:\Assets");

                service.InitializeTabsForNewWindow(viewModel, @"C:\Assets", true);

                Assert.AreEqual(@"C:\Assets", viewModel.ActiveTab.Path);
                Assert.IsNull(explorer.PendingPath,
                    "A saved-folder navigation must not remain queued after selecting the already-open shortcut target.");
                Assert.AreEqual(2, viewModel.Tabs.Count);
            }
            finally
            {
                if (File.Exists(tabsFilePath)) File.Delete(tabsFilePath);
            }
        }

        private sealed class PendingShortcutExplorerService : MockExplorerService
        {
            public string PendingPath { get; private set; }
            public override string GetCurrentPath(IntPtr hwnd) { return @"C:\Assets"; }
            public override bool Navigate(IntPtr hwnd, string path)
            {
                // Shell accepts navigation before its current location changes.
                PendingPath = path;
                return true;
            }
        }

        [TestMethod]
        public void ReopenControlPanel_KeepsSavedNormalStateUntilFinalReveal()
        {
            VerifySavedPlacementAtReveal(1);
        }

        [TestMethod]
        public void ReopenControlPanel_KeepsMaximizedStateAndSeparateNormalBounds()
        {
            VerifySavedPlacementAtReveal(3);
        }

        [TestMethod]
        public void ReopenControlPanel_AfterOneMinute_KeepsSavedNormalPlacement()
        {
            VerifySavedPlacementAtReveal(1, 60);
        }

        [TestMethod]
        public void ReopenControlPanel_AfterOneMinute_KeepsMaximizedPlacement()
        {
            VerifySavedPlacementAtReveal(3, 60);
        }

        private static void VerifySavedPlacementAtReveal(uint savedShowCommand, int closedAgeSeconds = 0)
        {
            NativeMethods.RECT normal = new NativeMethods.RECT { Left = 220, Top = 140, Right = 1220, Bottom = 840 };
            NativeMethods.RECT maximized = new NativeMethods.RECT { Left = -8, Top = -8, Right = 1928, Bottom = 1040 };
            NativeMethods.WINDOWPLACEMENT saved = new NativeMethods.WINDOWPLACEMENT
                { length = 44, showCmd = savedShowCommand, rcNormalPosition = normal };
            NativeMethods.WINDOWPLACEMENT current = new NativeMethods.WINDOWPLACEMENT
                { length = 44, showCmd = 3, rcNormalPosition = maximized };
            int writes = 0;
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => current,
                (hwnd, placement) => { current = placement; writes++; return true; });
            tracking.AddHiddenPendingWindow((IntPtr)100, maximized, DateTime.UtcNow);
            tracking.RememberRecentClosedManagedExplorerRect(savedShowCommand == 3 ? maximized : normal, DateTime.UtcNow.AddSeconds(-closedAgeSeconds), saved);
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                new PowerOptionsExplorerService(), tracking, TestTabPersistenceFactory.Create(),
                delegate { return string.Empty; }, delegate { Assert.Fail("Home must stay hidden"); }, delegate { },
                delegate { }, delegate { return true; }, delegate { }, delegate { return null; }, delegate { }, null,
                delegate { });

            service.RestorePreparedExplorerWindowForCreate((IntPtr)100, true);
            Assert.AreEqual(0, writes, "Placement can show a window and must wait for the final reveal.");
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(savedShowCommand, current.showCmd);
            Assert.AreEqual(normal, current.rcNormalPosition, "A maximized frame must not become the normal restore bounds.");
            tracking.RestoreNormalPositionBeforeShow((IntPtr)100);
            Assert.AreEqual(1, writes, "Saved placement must be consumed only once.");
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ReopenControlPanel_KeepsHomeHidden_AfterSuccessfulSwitch()
        {
            await VerifyReopenVisibility(true);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ReopenControlPanel_RestoresHome_WhenHostLaunchFails()
        {
            await VerifyReopenVisibility(false);
        }

        private static async System.Threading.Tasks.Task VerifyReopenVisibility(bool launchSucceeds)
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            System.Collections.Generic.List<string> events = new System.Collections.Generic.List<string>();
            NativeMethods.RECT original = new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState(
                hwnd => true, hwnd => { }, hwnd => { }, hwnd => null,
                (hwnd, placement) =>
                {
                    Assert.AreEqual((uint)1, placement.showCmd);
                    Assert.AreEqual(original, placement.rcNormalPosition);
                    events.Add("placement:" + hwnd);
                    return true;
                });
            tracking.RememberRecentClosedManagedExplorerRect(original, DateTime.UtcNow,
                new NativeMethods.WINDOWPLACEMENT { length = 44, showCmd = 1, rcNormalPosition = original });
            tracking.AddHiddenPendingWindow((IntPtr)100, original, DateTime.UtcNow);
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(),
                delegate { return string.Empty; },
                delegate (IntPtr hwnd) { events.Add("show:" + hwnd); },
                delegate { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { events.Add("move:" + hwnd); Assert.AreEqual(100, rect.Left); },
                delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                delegate { }, delegate { return null; }, delegate { }, null,
                delegate (IntPtr hwnd) { events.Add("hide:" + hwnd); });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);
            viewModel.RestoreTabs(new string[] { explorer.PowerOptionsPath }, explorer.PowerOptionsPath, 0, true);

            service.RestorePreparedExplorerWindowForCreate((IntPtr)100, viewModel.IsRestoringControlPanelHost);
            CollectionAssert.AreEqual(new string[] { "hide:100" }, events);
            Assert.AreEqual(original, tracking.DeferredOriginRestoreRects[(IntPtr)100], "Preserve bounds without moving Home onscreen.");
            Assert.AreEqual(0, tracking.HiddenPendingAbsorb.Count);
            Assert.AreEqual(0, tracking.HiddenOriginalRects.Count);

            bool opened = false;
            explorer.GetCurrentPathFunc = hwnd => hwnd == (IntPtr)200 ? explorer.AllControlPanelPath : explorer.HomeFolderPath;
            ExplorerHostSwitchCoordinator host = new ExplorerHostSwitchCoordinator(
                explorer, tracking,
                delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                delegate (IntPtr hwnd) { events.Add("show:" + hwnd); },
                delegate (IntPtr hwnd, NativeMethods.RECT rect)
                {
                    Assert.AreEqual((IntPtr)200, hwnd);
                    Assert.AreEqual(original, rect, "The CP host must receive Home's saved onscreen bounds.");
                    events.Add("move:" + hwnd);
                }, delegate { }, delegate { return true; },
                delegate { return opened
                    ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                    : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                delegate (IntPtr hwnd) { return explorer.GetCurrentPath(hwnd); },
                delegate (string path)
                {
                    Assert.AreEqual(explorer.AllControlPanelPath, path, "Keep the native Back history.");
                    Assert.IsFalse(events.Contains("show:100"), "Home must remain hidden throughout preparation.");
                    opened = launchSucceeds;
                    return launchSucceeds;
                }, delegate { });

            await service.RestorePersistedSpecialActiveTabHostAsync(host, viewModel, viewModel.ActiveTab, delegate
            {
                Assert.IsFalse(viewModel.IsRestoringControlPanelHost);
                Assert.IsTrue(events.Contains(launchSucceeds ? "show:200" : "show:100"));
                events.Add("show:tabbar");
            });
            string restoredHost = launchSucceeds ? "200" : "100";
            Assert.IsTrue(events.IndexOf("placement:" + restoredHost) >= 0);
            Assert.IsTrue(events.IndexOf("placement:" + restoredHost) < events.IndexOf("show:" + restoredHost));
            Assert.AreEqual("show:tabbar", events[events.Count - 1]);
            Assert.IsFalse(viewModel.IsRestoringControlPanelHost);
            Assert.AreEqual(launchSucceeds ? (IntPtr)200 : (IntPtr)100, viewModel.ExplorerHwnd);
            Assert.AreEqual(!launchSucceeds, events.Contains("show:100"));
            Assert.AreEqual(launchSucceeds, events.Contains("show:200"));
            Assert.AreEqual(!launchSucceeds, events.Contains("move:100"), "Home stays offscreen unless restoration fails.");
            Assert.AreEqual(launchSucceeds, tracking.DeferredOriginRestoreRects.ContainsKey((IntPtr)100));
        }

        [TestMethod]
        public void ReopenNormalFolder_RestoresOriginalPositionBeforeShowing()
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            System.Collections.Generic.List<string> events = new System.Collections.Generic.List<string>();
            tracking.AddHiddenPendingWindow((IntPtr)100,
                new NativeMethods.RECT { Left = 120, Top = 100, Right = 900, Bottom = 700 }, DateTime.UtcNow);
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(), delegate { return string.Empty; },
                delegate { events.Add("show"); }, delegate { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { Assert.AreEqual(120, rect.Left); events.Add("move"); },
                delegate { return true; }, delegate { }, delegate { return null; }, delegate { }, null,
                delegate { Assert.Fail("Normal host must not stay hidden."); });

            service.RestorePreparedExplorerWindowForCreate((IntPtr)100, false);
            CollectionAssert.AreEqual(new string[] { "move", "show" }, events);
            Assert.AreEqual(0, tracking.HiddenPendingAbsorb.Count);
        }

        [TestMethod]
        public void PowerOptionsRegression_ReusesSourceTab_WithoutNavigatingBackToControlPanel()
        {
            VerifyManagedPowerOptionsNavigation(false);
        }

        [TestMethod]
        public void PowerOptionsRegression_PreservesSourceTab_WhenEquivalentBackgroundTabExists()
        {
            VerifyManagedPowerOptionsNavigation(true);
        }

        private static void VerifyManagedPowerOptionsNavigation(bool addEquivalentBackgroundTab)
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.AllControlPanelPath);
            TabItemViewModel sourceTab = viewModel.ActiveTab;
            if (addEquivalentBackgroundTab)
            {
                viewModel.Tabs.Add(new TabItemViewModel(explorer.PowerOptionsPath, "Power Options", explorer));
            }
            int initialCount = viewModel.Tabs.Count;
            ExplorerWindowInteractionService service = CreatePowerOptionsInteraction(explorer, new ExplorerWindowTrackingState(), TestTabPersistenceFactory.Create());

            Assert.IsTrue(service.AbsorbExplorerWindow((IntPtr)200, viewModel, explorer.PowerOptionsPath, true, true, delegate { }, true));

            Assert.AreEqual(initialCount, viewModel.Tabs.Count);
            Assert.AreSame(sourceTab, viewModel.ActiveTab);
            Assert.AreEqual(explorer.PowerOptionsPath, sourceTab.Path);
            Assert.AreEqual(0, explorer.NavigationCount, "Adopting an already open page must not navigate back to the old page.");
            Assert.IsNull(viewModel.NavigationTracker.NavigatingToPath);
        }

        [TestMethod]
        public void PowerOptionsRegression_Restoration_OpensOnlyOnePowerOptionsWindow()
        {
            string tabsPath = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".tabs.txt");
            string activePath = Path.Combine(Path.GetDirectoryName(tabsPath), Path.GetFileNameWithoutExtension(tabsPath) + ".active.txt");
            try
            {
                PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
                ProtectedTextStorage.SaveLines(tabsPath, new string[] { @"C:\Saved", explorer.PowerOptionsPath });
                ProtectedTextStorage.SaveLines(activePath, new string[] { "index=1", explorer.PowerOptionsPath });
                ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);
                ExplorerWindowInteractionService service = CreatePowerOptionsInteraction(explorer, tracking, new TabPersistenceService(tabsPath));
                bool opened = false;
                string openedPath = null;
                explorer.GetCurrentPathFunc = hwnd => hwnd == (IntPtr)200 ? openedPath : explorer.HomeFolderPath;
                int explicitOpens = 0;
                ExplorerHostSwitchCoordinator host = new ExplorerHostSwitchCoordinator(
                    explorer, tracking,
                    delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                    delegate { }, delegate { }, delegate { },
                    delegate { return true; },
                    delegate { return opened
                        ? new System.Collections.Generic.List<IntPtr> { (IntPtr)100, (IntPtr)200 }
                        : new System.Collections.Generic.List<IntPtr> { (IntPtr)100 }; },
                    delegate (IntPtr hwnd) { return explorer.GetCurrentPath(hwnd); },
                    delegate (string path) { explicitOpens++; openedPath = path; opened = true; return true; },
                    delegate { });

                service.InitializeTabsForNewWindow(viewModel, explorer.HomeFolderPath, false);
                Assert.AreEqual(explorer.PowerOptionsPath, viewModel.ActiveTab.Path);
                Assert.IsTrue(host.PrepareForPath(viewModel, viewModel.ActiveTab.Path));
                viewModel.SelectTab(viewModel.ActiveTab);
                host.CompletePendingReveal();

                Assert.AreEqual(explorer.AllControlPanelPath, openedPath);
                Assert.AreEqual(1, explorer.NavigationCount);
                Assert.AreEqual(1, explicitOpens + explorer.PowerOptionsLaunchCount,
                    "Restoration must not navigate the normal host before opening the dedicated Control Panel host.");
                Assert.AreEqual(2, viewModel.Tabs.Count);
                Assert.AreEqual(1, viewModel.ActiveTabIndex);
                Assert.AreEqual((IntPtr)200, viewModel.ExplorerHwnd);
            }
            finally
            {
                if (File.Exists(tabsPath)) File.Delete(tabsPath);
                if (File.Exists(activePath)) File.Delete(activePath);
            }
        }

        [TestMethod]
        public async System.Threading.Tasks.Task PowerOptionsRegression_RemembersLaunchSource_AfterForegroundChanges()
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            DesktopForegroundTracker foreground = new DesktopForegroundTracker();
            IntPtr currentForeground = (IntPtr)100;
            ExplorerLaunchTracker launch = new ExplorerLaunchTracker(
                foreground, tracking,
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)100; },
                delegate (IntPtr hwnd) { return hwnd == (IntPtr)100; },
                delegate { return currentForeground; },
                delegate { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate { return true; });
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.AllControlPanelPath);
            ExplorerWindowMonitorCoordinator monitor = new ExplorerWindowMonitorCoordinator(
                new TabBarRegistry(), tracking, foreground, launch,
                delegate { return "CabinetWClass"; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate { return new NativeMethods.RECT(); }, delegate { }, null, delegate { return DateTime.UtcNow; });
            foreground.Update(currentForeground, "CabinetWClass");
            monitor.HandleShowEvent((IntPtr)200, delegate { return viewModel; }, delegate { return true; });
            currentForeground = (IntPtr)200;
            foreground.Update(currentForeground, "CabinetWClass");
            foreground.Update(currentForeground, "CabinetWClass");
            Assert.IsFalse(launch.WasManagedControlPanelLaunchSource());

            ExplorerWindowInteractionService interaction = CreatePowerOptionsInteraction(explorer, tracking, TestTabPersistenceFactory.Create());
            ExplorerWindowEvaluationResult observed = null;
            ExplorerWindowProcessingCoordinator processing = new ExplorerWindowProcessingCoordinator(
                tracking, launch, new ExplorerWindowEvaluationService(explorer, new DesktopPathClassifier(explorer)), interaction,
                new ExplorerWindowOutcomeCoordinator(tracking, interaction, delegate { }, delegate { return new MockUserSettings(); }, delegate { }),
                delegate (Func<ExplorerWindowEvaluationResult> callback)
                {
                    observed = callback();
                    return System.Threading.Tasks.Task.FromResult(observed);
                });
            await processing.ProcessAsync((IntPtr)200, viewModel,
                delegate { return viewModel; }, delegate { return viewModel; },
                delegate (TabBarViewModel vm, string path) { return vm.FindTabByPath(path) != null; },
                delegate { return true; }, delegate { return false; }, delegate { return false; });

            Assert.IsTrue(observed.WasManagedControlPanelLaunchSource, "The show event must retain the origin through delayed processing.");
            Assert.AreEqual(1, viewModel.Tabs.Count);
            Assert.AreEqual(explorer.PowerOptionsPath, viewModel.ActiveTab.Path);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task PowerOptionsRegression_KeepsRestoredTab_WhileHostPreparationIsPending()
        {
            PowerOptionsExplorerService explorer = new PowerOptionsExplorerService();
            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer, explorer.HomeFolderPath);
            viewModel.RestoreTabs(new string[] { @"C:\Saved", explorer.PowerOptionsPath }, explorer.PowerOptionsPath, 1, true);

            await viewModel.SyncWithExplorerAsync();

            Assert.AreEqual(explorer.PowerOptionsPath, viewModel.ActiveTab.Path);
            Assert.AreEqual(1, viewModel.ActiveTabIndex);
            Assert.AreEqual(0, explorer.NavigationCount);

            viewModel.SetExplorerHwnd((IntPtr)200);
            viewModel.SelectTab(viewModel.ActiveTab);
            Assert.IsFalse(viewModel.IsRestoringControlPanelHost);
            int pathQueries = 0;
            explorer.GetCurrentPathFunc = delegate { pathQueries++; return explorer.PowerOptionsPath; };
            viewModel.NavigationTracker.UpdateCache(null, DateTime.MinValue);
            await viewModel.SyncWithExplorerAsync();
            Assert.AreEqual(1, pathQueries, "Synchronization must resume after restoration.");
        }

        private static ExplorerWindowInteractionService CreatePowerOptionsInteraction(
            PowerOptionsExplorerService explorer, ExplorerWindowTrackingState tracking, TabPersistenceService persistence)
        {
            return new ExplorerWindowInteractionService(explorer, tracking, persistence,
                delegate { return string.Empty; }, delegate { }, delegate { }, delegate { },
                delegate (TabBarViewModel vm, IntPtr hwnd) { vm.SetExplorerHwnd(hwnd); return true; },
                delegate { }, delegate { return null; }, delegate { });
        }

        private sealed class PowerOptionsExplorerService : MockExplorerService
        {
            public int NavigationCount { get; private set; }
            public int PowerOptionsLaunchCount { get; private set; }

            public PowerOptionsExplorerService()
            {
                IsControlPanelPathFunc = path => path == AllControlPanelPath || path == PowerOptionsPath;
                IsControlPanelRootPathFunc = path => path == AllControlPanelPath;
                GetCurrentPathFunc = hwnd => hwnd == (IntPtr)200 ? PowerOptionsPath : HomeFolderPath;
            }

            public override bool Navigate(IntPtr explorerHwnd, string path)
            {
                NavigationCount++;
                if (explorerHwnd == (IntPtr)100 && path == PowerOptionsPath) PowerOptionsLaunchCount++;
                return true;
            }
        }

        [TestMethod]
        public void CleanupClosedWindows_PreservesParkedExplorerOrigin_WhenParkedWindowIsStillAliveButNotEnumerated()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState(
                delegate (IntPtr hwnd)
                {
                    return hwnd == (IntPtr)100 || hwnd == (IntPtr)200;
                });
            trackingState.RememberParkedExplorerOrigin((IntPtr)200, (IntPtr)100);

            trackingState.CleanupClosedWindows(new System.Collections.Generic.List<IntPtr> { (IntPtr)200 });

            IntPtr parkedOrigin;
            bool found = trackingState.TryGetParkedExplorerOrigin((IntPtr)200, out parkedOrigin);

            Assert.IsTrue(found);
            Assert.AreEqual((IntPtr)100, parkedOrigin);
        }

        [TestMethod]
        public void GetDesktopVirtualPathFromWindowTitle_ReturnsMappedPath()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return "Control Panel"; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            string result = service.GetDesktopVirtualPathFromWindowTitle((IntPtr)1);

            Assert.AreEqual("Control Panel", result);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_InsertsTabAndMarksAbsorbed()
        {
            MockExplorerService explorerService = new MockExplorerService();
            string current = @"C:\MockPath";
            explorerService.GetCurrentPathFunc = h => current;
            explorerService.NavigateFunc = (h, path) => { current = path; return true; };
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)200, targetViewModel, @"C:\Work", false, false, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(@"C:\Work", targetViewModel.ActiveTab.Path);
            Assert.AreEqual((IntPtr)100, foregroundHwnd);
            Assert.AreEqual((IntPtr)200, closedHwnd);
            Assert.IsTrue(trackingState.IgnoredWindows.Contains((IntPtr)200));
        }

        [TestMethod]
        public void AbsorbExplorerWindow_RejectsControlPanelPathAndCallsIgnore()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            };
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            IntPtr ignoredHwnd = IntPtr.Zero;

            bool absorbed = service.AbsorbExplorerWindow(
                (IntPtr)300,
                targetViewModel,
                "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                false,
                true,
                delegate (IntPtr hwnd) { ignoredHwnd = hwnd; });

            Assert.IsFalse(absorbed);
            Assert.AreEqual((IntPtr)300, ignoredHwnd);
            Assert.AreEqual(1, targetViewModel.Tabs.Count);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_AddsControlPanelTab_WhenEquivalentTabAlreadyExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);
            targetViewModel.InsertTabWithPath(explorerService.PowerOptionsPath, 2, true);
            targetViewModel.SelectTab(targetViewModel.Tabs[1]);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(4, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[2].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[3].Path);
            Assert.AreEqual((IntPtr)201, foregroundHwnd);
            Assert.AreEqual((IntPtr)201, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)201));
        }

        [TestMethod]
        public void AbsorbExplorerWindow_AddsControlPanelTab_WhenEquivalentTabDoesNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)202, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(3, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[2].Path);
            Assert.AreEqual((IntPtr)202, foregroundHwnd);
            Assert.AreEqual((IntPtr)202, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)202));
        }

        [TestMethod]
        public void AbsorbExplorerWindow_UsesHiddenOriginalRect_WhenManagedHostRectIsOffscreen()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };
            explorerService.GetExplorerWindowRectFunc = delegate (IntPtr hwnd)
            {
                return new NativeMethods.RECT
                {
                    Left = -32000,
                    Top = -32000,
                    Right = -31839,
                    Bottom = -31757
                };
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.HiddenPendingAbsorb[(IntPtr)200] = DateTime.UtcNow;
            trackingState.HiddenOriginalRects[(IntPtr)200] = new NativeMethods.RECT
            {
                Left = 100,
                Top = 200,
                Right = 900,
                Bottom = 800
            };

            NativeMethods.RECT movedRect = default(NativeMethods.RECT);
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { movedRect = rect; },
                delegate (TabBarViewModel vm, IntPtr hwnd)
                {
                    vm.SetExplorerHwnd(hwnd);
                    return true;
                },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate (KjTabBar.Views.TabBarWindow window) { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);

            bool absorbed = service.AbsorbExplorerWindow(
                (IntPtr)200,
                targetViewModel,
                explorerService.PowerOptionsPath,
                true,
                true,
                delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(100, movedRect.Left);
            Assert.AreEqual(200, movedRect.Top);
            Assert.AreEqual(800, movedRect.Width);
            Assert.AreEqual(600, movedRect.Height);
        }



        [TestMethod]
        public void AbsorbExplorerWindow_PreservesBackgroundControlPanelTab_WhenActiveTabIsNotControlPanel()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);
            targetViewModel.InsertTabWithPath(@"C:\Work", 2, false);
            targetViewModel.SelectTab(targetViewModel.Tabs[2]);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)203, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(4, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(@"C:\Work", targetViewModel.Tabs[2].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[3].Path);
            Assert.AreEqual((IntPtr)203, foregroundHwnd);
            Assert.AreEqual((IntPtr)203, targetViewModel.ExplorerHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, trackingState.ParkedExplorerOrigins[(IntPtr)203]);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_PreservesControlPanelRoot_WhenPathNormalizesToControlPanelItem()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };
            explorerService.NormalizeKnownPathFunc = delegate (string path)
            {
                return path == @"C:\Windows\System32\powercfg.cpl" ? explorerService.PowerOptionsPath : path;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            targetViewModel.InsertTabWithPath(explorerService.AllControlPanelPath, 1, true);

            bool absorbed = service.AbsorbExplorerWindow(
                (IntPtr)204,
                targetViewModel,
                @"C:\Windows\System32\powercfg.cpl",
                false,
                false,
                delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(3, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual(explorerService.AllControlPanelPath, targetViewModel.Tabs[1].Path);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.Tabs[2].Path);
            Assert.AreEqual((IntPtr)204, foregroundHwnd);
            Assert.AreEqual((IntPtr)204, targetViewModel.ExplorerHwnd);
            Assert.AreEqual((IntPtr)100, closedHwnd);
            Assert.IsFalse(trackingState.ParkedExplorerOrigins.ContainsKey((IntPtr)204));
        }

        [TestMethod]
        public void AbsorbExplorerWindow_CreatesControlPanelTab_WhenNoControlPanelTabExists()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            IntPtr foregroundHwnd = IntPtr.Zero;
            IntPtr closedHwnd = IntPtr.Zero;
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { foregroundHwnd = hwnd; },
                delegate (IntPtr hwnd) { closedHwnd = hwnd; },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);
            Assert.AreEqual(1, targetViewModel.Tabs.Count);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
            Assert.AreEqual((IntPtr)201, foregroundHwnd);
            Assert.AreEqual((IntPtr)201, targetViewModel.ExplorerHwnd);
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, trackingState.ParkedExplorerOrigins[(IntPtr)201]);
        }

        [TestMethod]
        public void AbsorbExplorerWindow_DoesNotLeaveTemporaryControlPanelTab_WhenRebindFails()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd, NativeMethods.RECT rect) { },
                delegate (TabBarViewModel viewModel, IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel targetViewModel = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorerService);

            bool absorbed = service.AbsorbExplorerWindow((IntPtr)201, targetViewModel, explorerService.PowerOptionsPath, true, true, delegate (IntPtr hwnd) { });

            Assert.IsFalse(absorbed);
            Assert.AreEqual(1, targetViewModel.Tabs.Count);
            Assert.AreEqual(@"C:\MockPath", targetViewModel.ActiveTab.Path);
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_PreservesSavedTabs_AndAddsExplicitInitialPath()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA", @"C:\SavedB" });

                MockExplorerService explorerService = new MockExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)400, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, @"C:\DesktopLaunch", true);

                Assert.AreEqual(3, viewModel.Tabs.Count);
                Assert.AreEqual(@"C:\SavedA", viewModel.Tabs[0].Path);
                Assert.AreEqual(@"C:\SavedB", viewModel.Tabs[1].Path);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.ActiveTab.Path);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.Tabs[2].Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_UsesExplicitInitialPath_WhenSavedTabsDoNotExist()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                explorerService,
                new ExplorerWindowTrackingState(),
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate { });

            TabBarViewModel viewModel = new TabBarViewModel((IntPtr)400, new MockUserSettings(), explorerService);

            service.InitializeTabsForNewWindow(viewModel, @"C:\DesktopLaunch", true);

            Assert.AreEqual(2, viewModel.Tabs.Count);
            Assert.AreEqual(@"C:\DesktopLaunch", viewModel.ActiveTab.Path);
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_AddsExplicitInitialPathAsNewTab_WhenItAlreadyExistsInSavedTabs()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".existing-initial.tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA", @"C:\DesktopLaunch" });

                MockExplorerService explorerService = new MockExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)403, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, @"C:\DesktopLaunch", true);

                Assert.AreEqual(3, viewModel.Tabs.Count);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.Tabs[1].Path);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.Tabs[2].Path);
                Assert.AreSame(viewModel.Tabs[2], viewModel.ActiveTab);
                Assert.AreEqual(@"C:\DesktopLaunch", viewModel.ActiveTab.Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_AddsControlPanelInitialPath_WhenSavedTabsExist()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".cp.tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA" });

                MockExplorerService explorerService = new MockExplorerService();
                explorerService.IsControlPanelPathFunc = delegate (string path)
                {
                    return path == explorerService.PowerOptionsPath;
                };

                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)401, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, explorerService.PowerOptionsPath, true);

                Assert.AreEqual(2, viewModel.Tabs.Count);
                Assert.AreEqual(explorerService.PowerOptionsPath, viewModel.ActiveTab.Path);
                Assert.AreEqual(explorerService.PowerOptionsPath, viewModel.Tabs[1].Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }

        [TestMethod]
        public void InitializeTabsForNewWindow_PreservesCurrentExplorerPath_WhenSavedTabsExist_AndInitialPathOnlyIsFalse()
        {
            string tabsFilePath = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".existing-window.tabs.txt");

            try
            {
                ProtectedTextStorage.SaveLines(tabsFilePath, new string[] { @"C:\SavedA", @"C:\SavedB" });

                MockExplorerService explorerService = new MockExplorerService();
                ExplorerWindowInteractionService service = new ExplorerWindowInteractionService(
                    explorerService,
                    new ExplorerWindowTrackingState(),
                    new TabPersistenceService(tabsFilePath),
                    delegate (IntPtr hwnd) { return string.Empty; },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate (IntPtr hwnd) { },
                    delegate { return null; },
                    delegate { });

                TabBarViewModel viewModel = new TabBarViewModel((IntPtr)402, new MockUserSettings(), explorerService);

                service.InitializeTabsForNewWindow(viewModel, @"E:\working", false);

                Assert.AreEqual(3, viewModel.Tabs.Count);
                Assert.AreEqual(@"C:\SavedA", viewModel.Tabs[0].Path);
                Assert.AreEqual(@"C:\SavedB", viewModel.Tabs[1].Path);
                Assert.AreEqual(@"E:\working", viewModel.Tabs[2].Path);
                Assert.AreEqual(@"E:\working", viewModel.ActiveTab.Path);
            }
            finally
            {
                if (File.Exists(tabsFilePath))
                {
                    File.Delete(tabsFilePath);
                }
            }
        }
    }
}
