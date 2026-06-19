using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.Services;

namespace KjTabBar.ViewModels
{
    public class TabBarViewModel : ViewModelBase, IDisposable
    {
        private IntPtr _explorerHwnd;
        private ObservableCollection<TabItemViewModel> _tabs;
        private TabItemViewModel _activeTab;
        private int _activeTabIndex;
        private readonly TabNavigationStateTracker _navigationTracker = new TabNavigationStateTracker();

        private System.Windows.Media.FontFamily _fontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        private double _fontSize = 11.5;
        private System.Windows.FontWeight _fontWeight = System.Windows.FontWeights.Normal;
        private System.Windows.FontStyle _fontStyle = System.Windows.FontStyles.Normal;
        private System.Windows.Visibility _windowVisibility = System.Windows.Visibility.Visible;
        private IUserSettings _userSettings;
        private IExplorerService _explorerService;
        private bool _isDisposed;

        private readonly TabBarExplorerSynchronizer _synchronizer;

        public IntPtr ExplorerHwnd
        {
            get { return _explorerHwnd; }
        }

        internal void SetExplorerHwnd(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr previousExplorerHwnd = _explorerHwnd;
            _explorerHwnd = explorerHwnd;
            if (previousExplorerHwnd != explorerHwnd)
            {
                _navigationTracker.NotifyExplorerHostChanged();
            }
            AppLogger.LogInfo(
                "TabBarViewModel",
                string.Format(
                    "SetExplorerHwnd previous={0} current={1} activeTab={2}",
                    previousExplorerHwnd,
                    _explorerHwnd,
                    ActiveTab != null ? ActiveTab.Path ?? string.Empty : string.Empty));
        }

        internal TabNavigationStateTracker NavigationTracker
        {
            get { return _navigationTracker; }
        }

        private ClosedTabHistory _closedTabHistory = new ClosedTabHistory();

        public bool HasClosedTabs
        {
            get { return _closedTabHistory.HasItems; }
        }

        private void StartHistoryBatch()
        {
            _closedTabHistory.StartBatch();
        }

        private void EndHistoryBatch()
        {
            if (_closedTabHistory.EndBatch())
            {
                OnPropertyChanged("HasClosedTabs");
            }
        }

        public ObservableCollection<TabItemViewModel> Tabs
        {
            get { return _tabs; }
        }

        public TabItemViewModel ActiveTab
        {
            get { return _activeTab; }
            set
            {
                if (_activeTab != null) _activeTab.IsActive = false;
                _activeTab = value;
                if (_activeTab != null) _activeTab.IsActive = true;
                OnPropertyChanged("ActiveTab");
            }
        }

        public int ActiveTabIndex
        {
            get { return _activeTabIndex; }
            set { _activeTabIndex = value; OnPropertyChanged("ActiveTabIndex"); }
        }

        public System.Windows.Media.FontFamily FontFamily
        {
            get { return _fontFamily; }
            set { _fontFamily = value; OnPropertyChanged("FontFamily"); }
        }

        public double FontSize
        {
            get { return _fontSize; }
            set { _fontSize = value; OnPropertyChanged("FontSize"); }
        }

        public System.Windows.FontWeight FontWeight
        {
            get { return _fontWeight; }
            set { _fontWeight = value; OnPropertyChanged("FontWeight"); }
        }

        public System.Windows.FontStyle FontStyle
        {
            get { return _fontStyle; }
            set { _fontStyle = value; OnPropertyChanged("FontStyle"); }
        }

        public System.Windows.Visibility WindowVisibility
        {
            get { return _windowVisibility; }
            set { _windowVisibility = value; OnPropertyChanged("WindowVisibility"); }
        }

        public TabBarViewModel(IntPtr explorerHwnd, IUserSettings userSettings, IExplorerService explorerService)
        {
            _userSettings = userSettings;
            _explorerService = explorerService;
            if (_userSettings != null)
            {
                _userSettings.SettingsChanged += UserSettings_SettingsChanged;
                ApplyUserSettings();
            }

            _explorerHwnd = explorerHwnd;
            _tabs = new ObservableCollection<TabItemViewModel>();

            string currentPath = _explorerService.GetCurrentPath(explorerHwnd);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = _explorerService.GetResolvedHomeFolderPath();
            }
            string title = _explorerService.GetFolderName(currentPath);
            TabItemViewModel firstTab = new TabItemViewModel(currentPath, title, _explorerService);
            _tabs.Add(firstTab);
            ActiveTab = firstTab;
            _activeTabIndex = 0;
            UpdateTabTitles();

            _synchronizer = new TabBarExplorerSynchronizer(this, _explorerService);
        }

        private void ApplyUserSettings()
        {
            if (_userSettings == null) return;
            if (!string.IsNullOrEmpty(_userSettings.FontFamily))
            {
                try { FontFamily = new System.Windows.Media.FontFamily(_userSettings.FontFamily); } catch { }
            }
            if (_userSettings.FontSize > 0)
            {
                FontSize = _userSettings.FontSize;
            }
            FontWeight = _userSettings.IsBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            FontStyle = _userSettings.IsItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
        }

        private void UserSettings_SettingsChanged(object sender, EventArgs e)
        {
            if (_isDisposed) return;
            ApplyUserSettings();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (_userSettings != null)
            {
                _userSettings.SettingsChanged -= UserSettings_SettingsChanged;
            }
        }

        internal TabItemViewModel FindTabByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string normalizedPath = NormalizeTabPath(path).TrimEnd('\\');
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Path != null)
                {
                    string tabPath = NormalizeTabPath(_tabs[i].Path);
                    if (tabPath != null &&
                        string.Equals(tabPath.TrimEnd('\\'), normalizedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return _tabs[i];
                    }
                }
            }
            return null;
        }

        public void AddTab()
        {
            string currentPath = null;
            if (_activeTab != null && !string.IsNullOrEmpty(_activeTab.Path))
            {
                currentPath = NormalizeTabPath(_activeTab.Path);
            }
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = _explorerService.GetCurrentPath(_explorerHwnd);
            }
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = _explorerService.GetResolvedHomeFolderPath();
            }

            string title = _explorerService.GetFolderName(currentPath);
            TabItemViewModel newTab = new TabItemViewModel(currentPath, title, _explorerService);
            _tabs.Add(newTab);
            SelectTab(newTab);
            UpdateTabTitles();
        }

        public void AddTabWithPath(string path)
        {
            AddTabWithPath(path, false);
        }

        public void AddTabWithPath(string path, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return;

            int insertIndex = _activeTabIndex + 1;
            if (insertIndex > _tabs.Count) insertIndex = _tabs.Count;

            InsertTabWithPath(path, insertIndex, allowSpecialPath);
        }

        public void AddTabWithPathAndSelect(string path, System.Collections.Generic.List<string> selectedItems)
        {
            AddTabWithPathAndSelect(path, selectedItems, false);
        }

        public void AddTabWithPathAndSelect(string path, System.Collections.Generic.List<string> selectedItems, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return;
            _navigationTracker.PendingSelectedItems = selectedItems;
            AddTabWithPath(path, allowSpecialPath);
        }

        public void InsertTabWithPathAndSelect(string path, int index, System.Collections.Generic.List<string> selectedItems, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return;
            _navigationTracker.PendingSelectedItems = selectedItems;
            InsertTabWithPath(path, index, allowSpecialPath);
        }

        public void InsertTabWithPath(string path, int index)
        {
            InsertTabWithPath(path, index, false);
        }

        public void InsertTabWithPath(string path, int index, bool allowSpecialPath)
        {
            TryInsertTabWithPath(path, index, allowSpecialPath);
        }

        public bool TryInsertTabWithPath(string path, int index, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = NormalizeTabPath(path);
            if (!allowSpecialPath && _explorerService.IsControlPanelPath(path)) return false;

            string title = _explorerService.GetFolderName(path);
            TabItemViewModel newTab = new TabItemViewModel(path, title, _explorerService);

            if (index < 0) index = 0;
            if (index > _tabs.Count) index = _tabs.Count;
            _tabs.Insert(index, newTab);

            SelectTab(newTab);
            UpdateTabTitles();
            return true;
        }

        public void DuplicateTab(TabItemViewModel tab)
        {
            if (tab == null) return;
            string path = tab.Path;
            if (string.IsNullOrEmpty(path))
            {
                path = _explorerService.GetResolvedHomeFolderPath();
            }

            int index = GetTabIndex(tab);
            if (index < 0) return;

            int newIndex = index + 1;
            string title = _explorerService.GetFolderName(path);
            TabItemViewModel newTab = new TabItemViewModel(path, title, _explorerService);
            _tabs.Insert(newIndex, newTab);
            SelectTab(newTab);
            UpdateTabTitles();
        }

        public void MoveTab(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= _tabs.Count) return;
            if (newIndex < 0 || newIndex > _tabs.Count) return;

            if (newIndex == _tabs.Count) newIndex--;
            if (oldIndex == newIndex) return;

            _tabs.Move(oldIndex, newIndex);

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] == _activeTab)
                {
                    ActiveTabIndex = i;
                    break;
                }
            }
        }

        private int GetTabIndex(TabItemViewModel tab)
        {
            if (tab == null)
            {
                return -1;
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] == tab)
                {
                    return i;
                }
            }

            return -1;
        }

        internal void SetActiveTabOnly(TabItemViewModel tab)
        {
            if (tab == null)
            {
                return;
            }

            ActiveTab = tab;
            ActiveTabIndex = GetTabIndex(tab);
        }

        internal void ClearPendingNavigationTracking()
        {
            _navigationTracker.ClearPending();
        }

        internal void ClearCancelledNavigationTracking()
        {
            _navigationTracker.ClearCancelled();
        }

        internal bool IsCancelledNavigationMatch(string currentPath)
        {
            return _navigationTracker.IsCancelledNavigationMatch(currentPath, PathEquals);
        }

        internal void CancelPendingNavigation()
        {
            TabItemViewModel rollbackTab;
            int rollbackIndex;
            _navigationTracker.CancelNavigation(_activeTab, out rollbackTab, out rollbackIndex);

            int existingIndex = GetTabIndex(rollbackTab);
            if (existingIndex >= 0)
            {
                SetActiveTabOnly(rollbackTab);
                return;
            }

            if (rollbackIndex >= 0 && rollbackIndex < _tabs.Count)
            {
                SetActiveTabOnly(_tabs[rollbackIndex]);
            }
        }

        public void RestoreTabs(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;

            string initialPath = null;
            if (_tabs.Count > 0)
            {
                initialPath = NormalizeTabPath(_tabs[0].Path);
            }

            bool isFirstValidTab = true;
            for (int i = 0; i < paths.Length; i++)
            {
                string p = NormalizeTabPath(paths[i]);
                if (string.IsNullOrEmpty(p)) continue;
                if (!IsPersistedTabPathRestorable(p)) continue;

                if (isFirstValidTab)
                {
                    _tabs.Clear();
                    _activeTab = null;
                    _activeTabIndex = -1;
                    isFirstValidTab = false;
                }

                string title = _explorerService.GetFolderName(p);
                TabItemViewModel newTab = new TabItemViewModel(p, title, _explorerService);
                _tabs.Add(newTab);
            }

            if (!isFirstValidTab)
            {
                if (!string.IsNullOrEmpty(initialPath))
                {
                    TabItemViewModel targetTab = FindTabByPath(initialPath);
                    if (targetTab != null)
                    {
                        SelectTab(targetTab);
                    }
                    else if (_tabs.Count > 0)
                    {
                        SelectTab(_tabs[0]);
                    }
                }
                else if (_tabs.Count > 0)
                {
                    SelectTab(_tabs[0]);
                }
            }
            UpdateTabTitles();
        }

        private bool IsPersistedTabPathRestorable(string path)
        {
            return TabRestorationHelper.IsPersistedTabPathRestorable(path, _explorerService, NormalizeTabPath);
        }

        private string NormalizeTabPath(string path)
        {
            return _explorerService.NormalizeKnownPath(path);
        }

        public void CloseTab(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] == tab) { index = i; break; }
            }
            if (index < 0) return;

            RecordClosedTab(tab.Path, index);

            bool wasActiveTab = (tab == _activeTab);
            if (wasActiveTab)
            {
                ActiveTab = null;
                ActiveTabIndex = -1;
            }

            _tabs.RemoveAt(index);

            if (_tabs.Count == 0)
            {
                string defaultPath = _explorerService.GetResolvedHomeFolderPath();
                string title = _explorerService.GetFolderName(defaultPath);
                TabItemViewModel newTab = new TabItemViewModel(defaultPath, title, _explorerService);
                _tabs.Add(newTab);
                _activeTab = null;
                _activeTabIndex = -1;
                SelectTab(newTab);
                UpdateTabTitles();
                return;
            }

            if (wasActiveTab || _activeTab == null)
            {
                if (index >= _tabs.Count) index = _tabs.Count - 1;
                SelectTab(_tabs[index]);
            }
            else
            {
                if (index < _activeTabIndex)
                {
                    ActiveTabIndex = _activeTabIndex - 1;
                }
            }
            UpdateTabTitles();
        }

        private void RecordClosedTab(string path, int position)
        {
            if (_closedTabHistory.Record(path, position))
            {
                OnPropertyChanged("HasClosedTabs");
            }
        }

        public void ReopenClosedTab()
        {
            List<ClosedTabInfo> batch = _closedTabHistory.PopLastBatch();
            if (batch == null) return;

            OnPropertyChanged("HasClosedTabs");

            for (int i = batch.Count - 1; i >= 0; i--)
            {
                ClosedTabInfo info = batch[i];
                InsertTabWithPath(info.Path, info.Position);
            }
        }

        public void CloseTabsToRight(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index < 0) return;

            StartHistoryBatch();
            while (_tabs.Count > index + 1)
            {
                CloseTab(_tabs[index + 1]);
            }
            EndHistoryBatch();
        }

        public void CloseTabsToLeft(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index <= 0) return;

            StartHistoryBatch();
            for (int i = 0; i < index; i++)
            {
                CloseTab(_tabs[0]);
            }
            EndHistoryBatch();
        }

        public void SelectTab(TabItemViewModel tab)
        {
            if (tab == null) return;

            ClearCancelledNavigationTracking();

            bool shouldUpdateTitles = false;
            TabItemViewModel previousActiveTab = _activeTab;
            int previousActiveTabIndex = _activeTabIndex;

            string path = tab.Path;
            if (string.IsNullOrEmpty(path))
            {
                path = _explorerService.GetResolvedHomeFolderPath();
                tab.Path = path;
                tab.Title = _explorerService.GetFolderName(path);
                shouldUpdateTitles = true;
            }

            if (!_explorerService.IsTabPathCurrentlyAvailable(path))
            {
                CloseTab(tab);
                return;
            }

            if (_navigationTracker.NavigatingToPath != null && _navigationTracker.NavigatingToPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                if (shouldUpdateTitles)
                {
                    UpdateTabTitles();
                }
                return;
            }

            string currentPath = _explorerService.GetCurrentPath(_explorerHwnd);
            AppLogger.LogInfo(
                "TabBarViewModel",
                string.Format(
                    "SelectTab explorer={0} currentPath={1} targetPath={2} activeBefore={3}",
                    _explorerHwnd,
                    currentPath ?? string.Empty,
                    path ?? string.Empty,
                    previousActiveTab != null ? previousActiveTab.Path ?? string.Empty : string.Empty));
            if (PathEquals(currentPath, path))
            {
                if (tab != _activeTab)
                {
                    SetActiveTabOnly(tab);
                }
                ClearPendingNavigationTracking();
                if (shouldUpdateTitles)
                {
                    UpdateTabTitles();
                }
                return;
            }

            if (tab != _activeTab)
            {
                SetActiveTabOnly(tab);
            }

            if (_explorerService.Navigate(_explorerHwnd, path))
            {
                AppLogger.LogInfo(
                    "TabBarViewModel",
                    string.Format(
                        "SelectTab navigateStarted explorer={0} targetPath={1}",
                        _explorerHwnd,
                        path ?? string.Empty));
                _navigationTracker.StartNavigation(
                    NormalizeTabPath(path),
                    (previousActiveTab != null && previousActiveTab != tab) ? previousActiveTab : null,
                    (previousActiveTab != null && previousActiveTab != tab) ? previousActiveTabIndex : -1
                );
            }
            else
            {
                AppLogger.LogInfo(
                    "TabBarViewModel",
                    string.Format(
                        "SelectTab navigateRejected explorer={0} targetPath={1}",
                        _explorerHwnd,
                        path ?? string.Empty));
                _navigationTracker.ClearPending();

                if (previousActiveTab != null && previousActiveTab != tab)
                {
                    int previousIndex = GetTabIndex(previousActiveTab);
                    if (previousIndex >= 0)
                    {
                        SetActiveTabOnly(previousActiveTab);
                    }
                    else if (previousActiveTabIndex >= 0 && previousActiveTabIndex < _tabs.Count)
                    {
                        SetActiveTabOnly(_tabs[previousActiveTabIndex]);
                    }
                }
            }

            if (shouldUpdateTitles)
            {
                UpdateTabTitles();
            }
        }

        public async Task SyncWithExplorerAsync()
        {
            if (_synchronizer != null)
            {
                await _synchronizer.SyncWithExplorerAsync();
            }
        }

        internal bool PathEquals(string path1, string path2)
        {
            string normalizedPath1 = NormalizeTabPath(path1);
            string normalizedPath2 = NormalizeTabPath(path2);
            if (normalizedPath1 == null && normalizedPath2 == null) return true;
            if (normalizedPath1 == null || normalizedPath2 == null) return false;
            return string.Equals(normalizedPath1.TrimEnd('\\'), normalizedPath2.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        internal void UpdateTabTitles()
        {
            TabTitleDisambiguator.UpdateTitles(_tabs, _explorerService);
        }

        private string ShortenTitle(string title, int maxLen)
        {
            return TabTitleDisambiguator.ShortenTitle(title, maxLen);
        }
    }
}
