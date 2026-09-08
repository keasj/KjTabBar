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
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, trackingState.ParkedExplorerOrigins[(IntPtr)201]);
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
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, trackingState.ParkedExplorerOrigins[(IntPtr)202]);
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
            Assert.AreEqual(IntPtr.Zero, closedHwnd);
            Assert.AreEqual((IntPtr)100, trackingState.ParkedExplorerOrigins[(IntPtr)204]);
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

            Assert.IsTrue(absorbed);
            Assert.AreEqual(2, targetViewModel.Tabs.Count);
            Assert.AreEqual(explorerService.PowerOptionsPath, targetViewModel.ActiveTab.Path);
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
