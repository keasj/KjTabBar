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
        private static readonly TimeSpan SaveDebounceInterval = TimeSpan.FromSeconds(2);
        private readonly string _tabsFilePath;
        private string _lastSavedTabs = "";
        private string _lastObservedTabs = "";
        private DateTime _lastObservedTabsChangedUtc = DateTime.MinValue;
        private bool _tabsLoadFailed;

        public TabPersistenceService()
            : this(null)
        {
        }

        internal TabPersistenceService(string tabsFilePath)
        {
            _tabsFilePath = tabsFilePath;
        }

        public bool LoadTabsTo(TabBarViewModel viewModel)
        {
            try
            {
                string file = GetTabsFilePathInstance();
                if (File.Exists(file))
                {
                    bool isProtectedFile = ProtectedTextStorage.IsProtectedFile(file);
                    string[] paths = ProtectedTextStorage.LoadLines(file);
                    PersistedActiveTabSelection activeTabSelection = LoadActiveTabSelectionSafe();
                    _tabsLoadFailed = false;
                    viewModel.RestoreTabs(paths, activeTabSelection.Path, activeTabSelection.Index);
                    _lastSavedTabs = BuildPersistedStateString(paths, activeTabSelection.Path, activeTabSelection.Index);
                    if (!isProtectedFile && paths.Length > 0)
                    {
                        ProtectedTextStorage.SaveLines(file, paths);
                    }

                    return paths.Length > 0;
                }
            }
            catch (Exception ex)
            {
                _tabsLoadFailed = true;
                AppLogger.LogError("TabPersistenceService", "Failed to load tabs.txt. Automatic tab saving is disabled to avoid overwriting existing data.", ex);
            }

            return false;
        }

        public void SaveTabsIfChanged(TabBarViewModel viewModel, bool force = false)
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
                DateTime nowUtc = DateTime.UtcNow;
                if (_lastObservedTabs != currentTabsString)
                {
                    _lastObservedTabs = currentTabsString;
                    _lastObservedTabsChangedUtc = nowUtc;
                }

                if (_lastSavedTabs == currentTabsString)
                {
                    return;
                }

                if (!force &&
                    _lastObservedTabsChangedUtc != DateTime.MinValue &&
                    (nowUtc - _lastObservedTabsChangedUtc) < SaveDebounceInterval)
                {
                    return;
                }

                _lastSavedTabs = currentTabsString;

                List<string> paths = BuildPersistablePathList(viewModel);
                string activeTabPath = GetPersistableActiveTabPath(viewModel);
                int? activeTabIndex = GetPersistableActiveTabIndex(viewModel);
                string file = GetTabsFilePathInstance();
                string dir = Path.GetDirectoryName(file);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                ProtectedTextStorage.SaveLines(file, paths);
                SaveActiveTabSelection(activeTabIndex, activeTabPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabPersistenceService", "Failed to save tabs.txt.", ex);
            }
        }

        private static string BuildCurrentTabsString(TabBarViewModel viewModel)
        {
            List<string> paths = BuildPersistablePathList(viewModel);
            return BuildPersistedStateString(paths, GetPersistableActiveTabPath(viewModel), GetPersistableActiveTabIndex(viewModel));
        }

        private static string BuildPersistedStateString(IList<string> paths, string activeTabPath, int? activeTabIndex)
        {
            StringBuilder sb = new StringBuilder();
            if (paths != null)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    sb.Append(paths[i]);
                    sb.Append("|");
                }
            }

            sb.Append("activeIndex=");
            sb.Append(activeTabIndex.HasValue ? activeTabIndex.Value.ToString() : string.Empty);
            sb.Append("|");
            sb.Append("active=");
            sb.Append(activeTabPath ?? string.Empty);
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

        private static string GetPersistableActiveTabPath(TabBarViewModel viewModel)
        {
            if (viewModel == null || viewModel.ActiveTab == null)
            {
                return null;
            }

            return IsPathPersistable(viewModel.ActiveTab.Path) ? viewModel.ActiveTab.Path : null;
        }

        private static int? GetPersistableActiveTabIndex(TabBarViewModel viewModel)
        {
            if (viewModel == null || viewModel.ActiveTab == null)
            {
                return null;
            }

            if (!IsPathPersistable(viewModel.ActiveTab.Path))
            {
                return null;
            }

            for (int i = 0; i < viewModel.Tabs.Count; i++)
            {
                if (ReferenceEquals(viewModel.Tabs[i], viewModel.ActiveTab))
                {
                    return i;
                }
            }

            return null;
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

        private string GetActiveTabFilePathInstance()
        {
            string tabsFilePath = GetTabsFilePathInstance();
            string directory = Path.GetDirectoryName(tabsFilePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(tabsFilePath);
            string extension = Path.GetExtension(tabsFilePath);
            return Path.Combine(directory, fileNameWithoutExtension + ".active" + extension);
        }

        private PersistedActiveTabSelection LoadActiveTabSelectionSafe()
        {
            try
            {
                return LoadActiveTabSelection();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabPersistenceService", "Failed to load active tab selection. Tabs will be restored without persisted active selection.", ex);
                return new PersistedActiveTabSelection(null, null);
            }
        }

        private PersistedActiveTabSelection LoadActiveTabSelection()
        {
            string activeTabFilePath = GetActiveTabFilePathInstance();
            if (!File.Exists(activeTabFilePath))
            {
                return new PersistedActiveTabSelection(null, null);
            }

            string[] lines = ProtectedTextStorage.LoadLines(activeTabFilePath);
            if (lines.Length == 0)
            {
                return new PersistedActiveTabSelection(null, null);
            }

            if (lines[0].StartsWith("index=", StringComparison.Ordinal))
            {
                int index;
                int? parsedIndex = int.TryParse(lines[0].Substring("index=".Length), out index) ? (int?)index : null;
                string path = lines.Length > 1 ? lines[1] : null;
                return new PersistedActiveTabSelection(parsedIndex, path);
            }

            return new PersistedActiveTabSelection(null, lines[0]);
        }

        private void SaveActiveTabSelection(int? activeTabIndex, string activeTabPath)
        {
            string activeTabFilePath = GetActiveTabFilePathInstance();
            if (!IsPathPersistable(activeTabPath))
            {
                if (File.Exists(activeTabFilePath))
                {
                    File.Delete(activeTabFilePath);
                }
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("index=" + (activeTabIndex.HasValue ? activeTabIndex.Value.ToString() : string.Empty));
            lines.Add(activeTabPath);
            ProtectedTextStorage.SaveLines(activeTabFilePath, lines);
        }

        private static bool IsPathPersistable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return true;
        }

        private sealed class PersistedActiveTabSelection
        {
            public PersistedActiveTabSelection(int? index, string path)
            {
                Index = index;
                Path = path;
            }

            public int? Index { get; private set; }

            public string Path { get; private set; }
        }
    }
}
