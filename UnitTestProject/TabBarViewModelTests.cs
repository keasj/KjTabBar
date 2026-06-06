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
    }
}
