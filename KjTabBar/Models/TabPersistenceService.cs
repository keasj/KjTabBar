using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;

namespace KjTabBar.Models
{
    internal sealed class TabPersistenceService
    {
        private readonly string _tabsFilePath;
        private string _lastSavedTabs = "";
        private bool _tabsLoadFailed;

        public TabPersistenceService()
            : this(null)
        {
        }

        internal TabPersistenceService(string tabsFilePath)
        {
            _tabsFilePath = tabsFilePath;
        }

        public void LoadTabsTo(TabBarViewModel viewModel)
        {
            try
            {
                string file = GetTabsFilePathInstance();
                if (File.Exists(file))
                {
                    bool isProtectedFile = ProtectedTextStorage.IsProtectedFile(file);
                    string[] paths = ProtectedTextStorage.LoadLines(file);
                    _tabsLoadFailed = false;
                    viewModel.RestoreTabs(paths);
                    _lastSavedTabs = paths.Length > 0 ? string.Join("|", paths) + "|" : "";
                    if (!isProtectedFile && paths.Length > 0)
                    {
                        ProtectedTextStorage.SaveLines(file, paths);
                    }
                }
            }
            catch (Exception ex)
            {
                _tabsLoadFailed = true;
                AppLogger.LogError("TabPersistenceService", "Failed to load tabs.txt. Automatic tab saving is disabled to avoid overwriting existing data.", ex);
            }
        }

        public void SaveTabsIfChanged(TabBarViewModel viewModel)
        {
            try
            {
                if (viewModel == null || viewModel.Tabs.Count == 0) return;
                if (_tabsLoadFailed)
                {
                    AppLogger.LogInfo("TabPersistenceService", "Skipped saving tabs.txt because the previous load failed.");
                    return;
                }

                string currentTabsString = BuildCurrentTabsString(viewModel);
                if (_lastSavedTabs != currentTabsString)
                {
                    _lastSavedTabs = currentTabsString;

                    List<string> paths = BuildPersistablePathList(viewModel);
                    string file = GetTabsFilePathInstance();
                    string dir = Path.GetDirectoryName(file);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    ProtectedTextStorage.SaveLines(file, paths);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabPersistenceService", "Failed to save tabs.txt.", ex);
            }
        }

        private static string BuildCurrentTabsString(TabBarViewModel viewModel)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < viewModel.Tabs.Count; i++)
            {
                if (IsPathPersistable(viewModel.Tabs[i].Path))
                {
                    sb.Append(viewModel.Tabs[i].Path);
                    sb.Append("|");
                }
            }
            return sb.ToString();
        }

        private static List<string> BuildPersistablePathList(TabBarViewModel viewModel)
        {
            List<string> paths = new List<string>();
            for (int i = 0; i < viewModel.Tabs.Count; i++)
            {
                if (IsPathPersistable(viewModel.Tabs[i].Path))
                {
                    paths.Add(viewModel.Tabs[i].Path);
                }
            }
            return paths;
        }

        private static string GetTabsFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KjTabBar", "tabs.txt");
        }

        private string GetTabsFilePathInstance()
        {
            if (!string.IsNullOrEmpty(_tabsFilePath))
            {
                return _tabsFilePath;
            }

            return GetTabsFilePath();
        }

        private static bool IsPathPersistable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return true;
        }
    }
}
