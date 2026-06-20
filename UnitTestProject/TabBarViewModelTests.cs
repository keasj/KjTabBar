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
        public void SyncWithExplorerAsync_UpdatesActiveControlPanelTab_InsteadOfJumpingToExistingControlPanelRootTab()
        {
            SynchronizerControlPanelExplorerService mockExplorer = new SynchronizerControlPanelExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(mockExplorer.AllControlPanelPath, 1, true);
            mockExplorer.CurrentPath = mockExplorer.PowerOptionsPath;
            vm.SelectTab(vm.Tabs[0]);

            mockExplorer.CurrentPath = mockExplorer.AllControlPanelPath;
            vm.SyncWithExplorerAsync().GetAwaiter().GetResult();

            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.ActiveTab.Path);
            Assert.AreEqual(vm.Tabs[0], vm.ActiveTab);
            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.Tabs[0].Path);
            Assert.AreEqual(mockExplorer.AllControlPanelPath, vm.Tabs[1].Path);
        }

        [TestMethod]
        public void SyncWithExplorerAsync_UpdatesActiveTabPath_InsteadOfJumpingToExistingMatchingTab()
        {
            SynchronizerControlPanelExplorerService mockExplorer = new SynchronizerControlPanelExplorerService();
            MockUserSettings mockSettings = new MockUserSettings();
            TabBarViewModel vm = new TabBarViewModel((IntPtr)123, mockSettings, mockExplorer);

            vm.InsertTabWithPath(@"C:\Data", 1, false);
            mockExplorer.CurrentPath = mockExplorer.PowerOptionsPath;
            vm.SelectTab(vm.Tabs[0]);

            mockExplorer.CurrentPath = @"C:\Data";
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
    }
}
