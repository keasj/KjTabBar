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
        private System.Windows.Threading.Dispatcher _metadataDispatcher;
        private int _metadataUpdateQueued;
        private bool _isReopeningClosedTabs;
        private bool _isClosingTabs;
        private bool _isPreparingTabOperation;
        private int _selectionsInFlight;
        private bool _externalTabOperation;
        private Func<bool> _canApplyPreparedTabOperation;
        internal bool IsPreparedTabOperationCurrent() => _canApplyPreparedTabOperation != null && _canApplyPreparedTabOperation();
        internal bool LastNavigationFailed { get; private set; }
        internal long NavigationFailureVersion { get; private set; }
        private Action _pendingCloseRollback;
        private string[] _pendingClosePaths;
        private string _pendingCloseActivePath;
        private int? _pendingCloseActiveIndex;
        internal string[] PendingClosePaths => _pendingCloseRollback != null ? _pendingClosePaths : null;
        internal string PendingCloseActivePath => _pendingCloseActivePath;
        internal int? PendingCloseActiveIndex => _pendingCloseActiveIndex;
        internal bool IsDisposed => _isDisposed;
        internal Action PendingHostRollback { get; set; }
        private long _selectionVersion;
        internal long SynchronizationVersion { get; private set; }
        internal bool IsTabOperationPending => _isPreparingTabOperation || _isClosingTabs || _isReopeningClosedTabs || _selectionsInFlight > 0 || _externalTabOperation;

        internal bool TryBeginExternalTabOperation()
        {
            if (_isDisposed || IsTabOperationPending) return false;
            _externalTabOperation = true;
            return true;
        }

        internal void EndExternalTabOperation() { _externalTabOperation = false; }
        internal bool IsExternalOperationCurrent(long version)
        {
            return !_isDisposed && _externalTabOperation && SynchronizationVersion == version;
        }

        internal bool IsSynchronizationCurrent(long version)
        {
            return !_isDisposed && !IsTabOperationPending && SynchronizationVersion == version;
        }

        private void InvalidateSelection()
        {
            _selectionVersion++;
            SynchronizationVersion++;
        }

        private readonly TabBarExplorerSynchronizer _synchronizer;

        internal bool IsRestoringControlPanelHost { get; set; }
        private string _persistedRestoreSourcePath;

        internal void CancelPersistedHostRestoration(string currentPath)
        {
            if (_isDisposed || !IsRestoringControlPanelHost) return;
            string path = string.IsNullOrEmpty(currentPath) ? _persistedRestoreSourcePath : currentPath;
            if (string.IsNullOrEmpty(path)) return;
            TabItemViewModel source = FindTabByPath(path);
            if (source == null)
            {
                source = new TabItemViewModel(path, _explorerService.GetFolderName(path), _explorerService);
                _tabs.Add(source);
            }
            ClearPendingNavigationTracking();
            SetActiveTabOnly(source);
            _navigationTracker.InvalidateCache();
        }

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
                SynchronizationVersion++;
                _navigationTracker.NotifyExplorerHostChanged();
            }
            AppLogger.LogDiagnostic(
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
                InvalidateSelection();
                _navigationTracker.InvalidateCache();
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
            : this(explorerHwnd, userSettings, explorerService, null)
        {
        }

        internal TabBarViewModel(IntPtr explorerHwnd, IUserSettings userSettings, IExplorerService explorerService, string initialPath)
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

            string currentPath = initialPath;
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = _explorerService.GetCurrentPath(explorerHwnd);
            }
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
            ExplorerManager manager = _explorerService as ExplorerManager;
            if (manager != null && manager.UsesShellWorker)
            {
                _metadataDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                manager.FolderTitlesChanged += FolderTitlesChanged;
            }
        }

        private void FolderTitlesChanged(object sender, EventArgs e)
        {
            if (_isDisposed || _metadataDispatcher == null || _metadataDispatcher.HasShutdownStarted ||
                System.Threading.Interlocked.Exchange(ref _metadataUpdateQueued, 1) != 0) return;
            _metadataDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate
            {
                System.Threading.Interlocked.Exchange(ref _metadataUpdateQueued, 0);
                if (_isDisposed) return;
                foreach (TabItemViewModel tab in _tabs) tab.BaseTitle = _explorerService.GetFolderName(tab.Path);
                UpdateTabTitles();
            }));
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
            foreach (TabItemViewModel tab in _tabs) tab.StopIconUpdates();
            ExplorerManager manager = _explorerService as ExplorerManager;
            if (manager != null) manager.FolderTitlesChanged -= FolderTitlesChanged;
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
            if (_isDisposed || string.IsNullOrEmpty(path)) return false;
            path = NormalizeTabPath(path);
            if (!allowSpecialPath && _explorerService.IsControlPanelPath(path)) return false;
            ObserveSelection(InsertTabCoreAsync(path, index, allowSpecialPath));
            return true;
        }

        internal async Task<bool> InsertTabCoreAsync(string path, int index, bool allowSpecialPath, bool removeOnFailure = false)
        {
            if (_isDisposed || string.IsNullOrEmpty(path)) return false;
            path = NormalizeTabPath(path);
            if (!allowSpecialPath && _explorerService.IsControlPanelPath(path)) return false;
            TabItemViewModel previous = _activeTab;
            TabItemViewModel newTab = new TabItemViewModel(path, _explorerService.GetFolderName(path), _explorerService);
            _tabs.Insert(Math.Max(0, Math.Min(index, _tabs.Count)), newTab);
            bool selected = false;
            try
            {
                await SelectTabCoreAsync(newTab);
                selected = !LastNavigationFailed && _activeTab == newTab && !_isDisposed;
                return selected;
            }
            finally
            {
                if (removeOnFailure && !selected)
                {
                    _tabs.Remove(newTab);
                    if (_activeTab == newTab && _tabs.Contains(previous)) SetActiveTabOnly(previous);
                }
                UpdateTabTitles();
            }
        }

        public void DuplicateTab(TabItemViewModel tab)
        {
            ObserveSelection(DuplicateTabCoreAsync(tab));
        }

        private async Task DuplicateTabCoreAsync(TabItemViewModel tab)
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
            await SelectTabCoreAsync(newTab);
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
            _pendingCloseRollback = null;
            PendingHostRollback = null;
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

        internal void TimeoutPendingNavigation()
        {
            if (_navigationTracker.NavigatingToPath == null) return;
            Action restoreTabs = _pendingCloseRollback;
            Action restoreHost = PendingHostRollback;
            _pendingCloseRollback = null;
            PendingHostRollback = null;
            if (restoreTabs != null) restoreTabs();
            CancelPendingNavigation();
            if (restoreHost != null) restoreHost();
        }

        private void RegisterCloseRollback(List<TabItemViewModel> before, List<ClosedTabInfo> previousRecords,
            TabItemViewModel previousActive)
        {
            if (_navigationTracker.NavigatingToPath == null || previousActive == null || _tabs.Contains(previousActive)) return;
            List<TabItemViewModel> removed = before.FindAll(tab => !_tabs.Contains(tab));
            List<TabItemViewModel> added = new List<TabItemViewModel>();
            foreach (TabItemViewModel tab in _tabs) if (!before.Contains(tab)) added.Add(tab);
            List<ClosedTabInfo> records = _closedTabHistory.GetRecordedItems();
            records.RemoveAll(previousRecords.Contains);
            if (_pendingCloseRollback == null)
            {
                _pendingClosePaths = before.ConvertAll(tab => tab.Path).ToArray();
                _pendingCloseActivePath = previousActive.Path;
                _pendingCloseActiveIndex = before.IndexOf(previousActive);
            }
            Action previousRollback = _pendingCloseRollback;
            _pendingCloseRollback = delegate
            {
                foreach (TabItemViewModel tab in added) _tabs.Remove(tab);
                foreach (TabItemViewModel tab in removed)
                    if (!_tabs.Contains(tab)) _tabs.Insert(Math.Min(before.IndexOf(tab), _tabs.Count), tab);
                foreach (ClosedTabInfo info in records) _closedTabHistory.RemoveRestoredItem(info);
                if (previousRollback != null) previousRollback();
                OnPropertyChanged("HasClosedTabs");
                UpdateTabTitles();
            };
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
            RestoreTabs(paths, null, null);
        }

        public void RestoreTabs(string[] paths, string activePath)
        {
            RestoreTabs(paths, activePath, null);
        }

        public void RestoreTabs(string[] paths, string activePath, int? activeIndex)
        {
            RestoreTabs(paths, activePath, activeIndex, false);
        }

        internal void RestoreTabs(string[] paths, string activePath, int? activeIndex, bool deferControlPanelNavigation, bool deferNavigation = false)
        {
            if (paths == null || paths.Length == 0) return;

            string initialPath = null;
            if (_tabs.Count > 0)
            {
                initialPath = NormalizeTabPath(_tabs[0].Path);
            }

            _persistedRestoreSourcePath = initialPath;
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

            if (!isFirstValidTab && !deferNavigation)
            {
                TabItemViewModel activeTab = null;
                if (activeIndex.HasValue && activeIndex.Value >= 0 && activeIndex.Value < _tabs.Count)
                {
                    TabItemViewModel indexedTab = _tabs[activeIndex.Value];
                    if (string.IsNullOrEmpty(activePath) || PathEquals(indexedTab.Path, activePath))
                        activeTab = indexedTab;
                }

                if (activeTab == null && !string.IsNullOrEmpty(activePath))
                {
                    activeTab = FindTabByPath(activePath);
                }

                if (activeTab != null)
                {
                    SelectRestoredTab(activeTab, deferControlPanelNavigation);
                }
                else if (!string.IsNullOrEmpty(initialPath))
                {
                    TabItemViewModel targetTab = FindTabByPath(initialPath);
                    if (targetTab != null)
                    {
                        SelectRestoredTab(targetTab, deferControlPanelNavigation);
                    }
                    else if (_tabs.Count > 0)
                    {
                        SelectRestoredTab(_tabs[0], deferControlPanelNavigation);
                    }
                }
                else if (_tabs.Count > 0)
                {
                    SelectRestoredTab(_tabs[0], deferControlPanelNavigation);
                }
            }
            UpdateTabTitles();
        }

        private void SelectRestoredTab(TabItemViewModel tab, bool deferControlPanelNavigation)
        {
            if (deferControlPanelNavigation && _explorerService.IsControlPanelPath(tab.Path))
            {
                // The interaction service prepares the Control Panel host before navigation.
                IsRestoringControlPanelHost = true;
                ClearPendingNavigationTracking();
                SetActiveTabOnly(tab);
                return;
            }

            SelectTab(tab);
        }

        private bool IsPersistedTabPathRestorable(string path)
        {
            return TabRestorationHelper.IsPersistedTabPathRestorable(path, _explorerService, NormalizeTabPath);
        }

        private string NormalizeTabPath(string path)
        {
            return _explorerService.NormalizeKnownPath(path);
        }

        internal async Task CloseTabsAsync(int startIndex, int count,
            Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            if (_isDisposed || IsTabOperationPending || startIndex < 0 || count <= 0 || startIndex > _tabs.Count - count) return;
            _isClosingTabs = true;
            bool preparingHost = false;
            try
            {
                int activeIndex = GetTabIndex(_activeTab);
                bool changesSelection = _activeTab == null ||
                    (activeIndex >= startIndex && activeIndex < startIndex + count);
                if (changesSelection && !_explorerService.IsTabPathCurrentlyAvailable(GetCloseSelectionPath(startIndex, count))) return;
                if (changesSelection && preparePath != null)
                {
                    // Prepare before removing the active tab: host detection needs its old path.
                    string targetPath = GetCloseSelectionPath(startIndex, count);
                    List<TabItemViewModel> originalTabs = new List<TabItemViewModel>(_tabs);
                    TabItemViewModel originalActiveTab = _activeTab;
                    long selectionVersion = _selectionVersion;
                    _canApplyPreparedTabOperation = () => !_isDisposed && _selectionVersion == selectionVersion &&
                        _activeTab == originalActiveTab && _tabs.Count == originalTabs.Count &&
                        System.Linq.Enumerable.SequenceEqual(_tabs, originalTabs) &&
                        PathEquals(targetPath, GetCloseSelectionPath(startIndex, count));
                    preparingHost = true;
                    if (!await preparePath(targetPath)) return;

                    // An asynchronous host launch must not close tabs changed in the meantime.
                    if (!IsPreparedTabOperationCurrent()) return;
                }

                if (_isDisposed || (changesSelection &&
                    !_explorerService.IsTabPathCurrentlyAvailable(GetCloseSelectionPath(startIndex, count)))) return;
                if (count == 1) await CloseTabCoreAsync(_tabs[startIndex]);
                else await CloseTabRangeAsync(startIndex, count);
            }
            finally
            {
                try
                {
                    if (preparingHost && completePendingReveal != null) completePendingReveal();
                }
                finally
                {
                    _canApplyPreparedTabOperation = null;
                    _isClosingTabs = false;
                }
            }
        }

        private string GetCloseSelectionPath(int startIndex, int count)
        {
            string path = count == _tabs.Count ? null :
                (startIndex + count < _tabs.Count ? _tabs[startIndex + count].Path : _tabs[startIndex - 1].Path);
            return string.IsNullOrEmpty(path) ? _explorerService.GetResolvedHomeFolderPath() : path;
        }

        internal Task SelectTabAsync(TabItemViewModel tab, Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            string path = tab != null ? tab.Path : null;
            return RunPreparedTabOperationAsync(path, () => SelectTabCoreAsync(tab),
                () => tab != null && _tabs.Contains(tab) && PathEquals(tab.Path, path), preparePath, completePendingReveal);
        }

        internal Task InsertTabWithPathAsync(string path, int index, Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            return RunPreparedTabOperationAsync(path, async () => { await InsertTabCoreAsync(path, index, true); },
                () => true, preparePath, completePendingReveal);
        }

        internal Task DuplicateTabAsync(TabItemViewModel tab, Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            string path = tab != null ? tab.Path : null;
            return RunPreparedTabOperationAsync(path, () => DuplicateTabCoreAsync(tab),
                () => tab != null && _tabs.Contains(tab) && PathEquals(tab.Path, path), preparePath, completePendingReveal);
        }

        private async Task RunPreparedTabOperationAsync(string path, Func<Task> action, Func<bool> isCurrent,
            Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            if (_isDisposed || IsTabOperationPending || !isCurrent()) return;
            path = string.IsNullOrEmpty(path) ? _explorerService.GetResolvedHomeFolderPath() : path;
            if (!_explorerService.IsTabPathCurrentlyAvailable(path)) return;
            long selectionVersion = _selectionVersion;
            _canApplyPreparedTabOperation = () => !_isDisposed && _selectionVersion == selectionVersion && isCurrent();
            _isPreparingTabOperation = true;
            bool preparingHost = false;
            try
            {
                if (preparePath != null)
                {
                    preparingHost = true;
                    if (!await preparePath(path)) return;
                }
                if (_isDisposed || _selectionVersion != selectionVersion || !isCurrent()) return;
                await action();
            }
            finally
            {
                try
                {
                    if (preparingHost && completePendingReveal != null) completePendingReveal();
                }
                finally
                {
                    _canApplyPreparedTabOperation = null;
                    _isPreparingTabOperation = false;
                }
            }
        }

        internal bool CanCloseTab(TabItemViewModel tab)
        {
            int index = GetTabIndex(tab);
            return !_isDisposed && index >= 0 &&
                ((tab != _activeTab && _activeTab != null) ||
                 _explorerService.IsTabPathCurrentlyAvailable(GetCloseSelectionPath(index, 1)));
        }

        private async Task<bool> PrepareCloseSelectionAsync(int startIndex, int count)
        {
            int activeIndex = GetTabIndex(_activeTab);
            if (_activeTab != null && (activeIndex < startIndex || activeIndex >= startIndex + count)) return true;
            TabItemViewModel target;
            bool temporary = count == _tabs.Count;
            if (temporary)
            {
                string path = _explorerService.GetResolvedHomeFolderPath();
                target = new TabItemViewModel(path, _explorerService.GetFolderName(path), _explorerService);
                _tabs.Add(target);
            }
            else target = _tabs[startIndex + count < _tabs.Count ? startIndex + count : startIndex - 1];
            bool selected = false;
            try
            {
                await SelectTabCoreAsync(target);
                selected = !LastNavigationFailed && _activeTab == target && _tabs.Contains(target) && !_isDisposed;
                return selected;
            }
            finally
            {
                if (temporary && !selected) _tabs.Remove(target);
            }
        }

        public void CloseTab(TabItemViewModel tab)
        {
            ObserveSelection(CloseTabCoreAsync(tab));
        }

        private async Task CloseTabCoreAsync(TabItemViewModel tab)
        {
            if (!CanCloseTab(tab)) return;
            int index = -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] == tab) { index = i; break; }
            }
            if (index < 0) return;


            List<TabItemViewModel> beforeClose = new List<TabItemViewModel>(_tabs);
            List<ClosedTabInfo> beforeRecords = _closedTabHistory.GetRecordedItems();
            TabItemViewModel beforeActive = _activeTab;
            if (!await PrepareCloseSelectionAsync(index, 1) || !_tabs.Contains(tab)) return;
            index = GetTabIndex(tab);
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
                await SelectTabCoreAsync(newTab);
                UpdateTabTitles();
                return;
            }

            if (wasActiveTab || _activeTab == null)
            {
                if (index >= _tabs.Count) index = _tabs.Count - 1;
                await SelectTabCoreAsync(_tabs[index]);
            }
            else
            {
                if (index < _activeTabIndex)
                {
                    ActiveTabIndex = _activeTabIndex - 1;
                }
            }
            RegisterCloseRollback(beforeClose, beforeRecords, beforeActive);
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
            ReopenClosedTabAsync(null, null).GetAwaiter().GetResult();
        }

        internal async Task ReopenClosedTabAsync(Func<string, Task<bool>> preparePath, Action<Action> withPendingReveal)
        {
            if (_isDisposed || IsTabOperationPending) return;
            _isReopeningClosedTabs = true;
            try
            {
                List<ClosedTabInfo> batch = _closedTabHistory.PeekLastBatch();
                if (batch == null) return;

                for (int i = batch.Count - 1; i >= 0; i--)
                {
                    ClosedTabInfo info = batch[i];
                    long selectionVersion = _selectionVersion;
                    _canApplyPreparedTabOperation = () => !_isDisposed && _selectionVersion == selectionVersion;
                    bool preparing = false;
                    bool revealHandled = false;
                    try
                    {
                        if (preparePath != null)
                        {
                            preparing = true;
                            if (!await preparePath(info.Path)) return;
                        }
                        if (!IsPreparedTabOperationCurrent()) return;
                        bool restored = await InsertTabCoreAsync(info.Path, info.Position, true, true);
                        Action insert = delegate { };
                        if (withPendingReveal != null)
                        {
                            revealHandled = true;
                            withPendingReveal(insert);
                        }
                        else insert();
                        if (!restored) return;
                        _closedTabHistory.RemoveRestoredItem(info);
                        OnPropertyChanged("HasClosedTabs");
                    }
                    finally
                    {
                        try
                        {
                            if (preparing && !revealHandled && withPendingReveal != null)
                                withPendingReveal(delegate { });
                        }
                        finally { _canApplyPreparedTabOperation = null; }
                    }
                }
            }
            finally
            {
                _isReopeningClosedTabs = false;
            }
        }

        public void CloseTabsToRight(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index < 0) return;

            ObserveSelection(CloseTabRangeAsync(index + 1, _tabs.Count - index - 1));
        }

        public void CloseTabsToLeft(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index <= 0) return;

            ObserveSelection(CloseTabRangeAsync(0, index));
        }

        private async Task CloseTabRangeAsync(int startIndex, int count)
        {
            if (count <= 0) return;
            bool removesActiveTab = _activeTabIndex >= startIndex && _activeTabIndex < startIndex + count;
            if ((removesActiveTab || _activeTab == null) &&
                !_explorerService.IsTabPathCurrentlyAvailable(GetCloseSelectionPath(startIndex, count))) return;
            List<TabItemViewModel> beforeClose = new List<TabItemViewModel>(_tabs);
            List<ClosedTabInfo> beforeRecords = _closedTabHistory.GetRecordedItems();
            TabItemViewModel beforeActive = _activeTab;
            if (!await PrepareCloseSelectionAsync(startIndex, count)) return;
            bool unchanged = System.Linq.Enumerable.SequenceEqual(beforeClose, _tabs);
            bool addedHome = count == beforeClose.Count && _tabs.Count == beforeClose.Count + 1 &&
                System.Linq.Enumerable.SequenceEqual(beforeClose, System.Linq.Enumerable.Take(_tabs, beforeClose.Count));
            if (!unchanged && !addedHome) return;
            StartHistoryBatch();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    RecordClosedTab(_tabs[startIndex].Path, startIndex);
                    _tabs.RemoveAt(startIndex);
                }
                ActiveTabIndex = GetTabIndex(_activeTab);
                UpdateTabTitles();
            }
            finally
            {
                EndHistoryBatch();
                RegisterCloseRollback(beforeClose, beforeRecords, beforeActive);
            }
        }

        public void SelectTab(TabItemViewModel tab)
        {
            ObserveSelection(SelectTabCoreAsync(tab));
        }

        private async void ObserveSelection(Task operation)
        {
            try { await operation; }
            catch (Exception ex) { AppLogger.LogError("TabBarViewModel", "Tab operation failed.", ex); }
        }

        internal async Task SelectTabCoreAsync(TabItemViewModel tab)
        {
            _selectionsInFlight++;
            try { await SelectTabImplementationAsync(tab); }
            catch
            {
                LastNavigationFailed = true;
                NavigationFailureVersion++;
                throw;
            }
            finally { _selectionsInFlight--; }
        }

        private async Task SelectTabImplementationAsync(TabItemViewModel tab)
        {
            LastNavigationFailed = true;
            if (_isDisposed || tab == null || GetTabIndex(tab) < 0) return;
            InvalidateSelection();

            IsRestoringControlPanelHost = false;

            bool shouldUpdateTitles = false;
            Action restorePendingNavigation = _navigationTracker.CapturePendingNavigation();
            TabItemViewModel previousActiveTab = _activeTab;
            int previousActiveTabIndex = _activeTabIndex;

            string path = tab.Path;
            if (string.IsNullOrEmpty(path))
            {
                path = _explorerService.GetResolvedHomeFolderPath();
                tab.Path = path;
                tab.BaseTitle = _explorerService.GetFolderName(path);
                tab.Title = tab.BaseTitle;
                shouldUpdateTitles = true;
            }

            if (!_explorerService.IsTabPathCurrentlyAvailable(path))
            {
                return;
            }

            LastNavigationFailed = false;
            if (_navigationTracker.NavigatingToPath != null && _navigationTracker.NavigatingToPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                // The navigation can be shared by duplicate tabs, but their selection cannot.
                if (tab != _activeTab) SetActiveTabOnly(tab);
                if (shouldUpdateTitles)
                {
                    UpdateTabTitles();
                }
                return;
            }

            long readVersion = _selectionVersion;
            IntPtr selectionHost = _explorerHwnd;
            string currentPath;
            if (!_navigationTracker.TryGetRecentCachedExplorerPath(DateTime.UtcNow, out currentPath))
                currentPath = await _explorerService.ReadPathAsync(selectionHost);
            if (_isDisposed || _selectionVersion != readVersion || _explorerHwnd != selectionHost ||
                !_tabs.Contains(tab) || !PathEquals(tab.Path, path)) return;
            if (_navigationTracker.NavigatingToPath != null && PathEquals(_navigationTracker.NavigatingToPath, currentPath))
            {
                ClearPendingNavigationTracking();
                restorePendingNavigation = null;
            }
            // Keep late-arrival evidence until selection or synchronization consumes it.
            TabItemViewModel confirmedSource = FindTabByPath(currentPath);
            if (confirmedSource == null && _navigationTracker.NavigationSourceTab != null &&
                PathEquals(_navigationTracker.NavigationSourceTab.Path, currentPath))
                confirmedSource = _navigationTracker.NavigationSourceTab;
            AppLogger.LogDiagnostic(
                "TabBarViewModel",
                string.Format(
                    "SelectTab explorer={0} currentPath={1} targetPath={2} activeBefore={3}",
                    _explorerHwnd,
                    currentPath ?? string.Empty,
                    path ?? string.Empty,
                    previousActiveTab != null ? previousActiveTab.Path ?? string.Empty : string.Empty));
            TabItemViewModel failureSource = restorePendingNavigation == null
                ? confirmedSource ?? previousActiveTab : previousActiveTab;
            if (PathEquals(currentPath, path))
            {
                _navigationTracker.ForgetCancelled(currentPath, PathEquals);
                _navigationTracker.RememberPendingAsCancelled();
                if (tab != _activeTab)
                {
                    SetActiveTabOnly(tab);
                }
                if (_navigationTracker.PendingSelectedItems != null)
                {
                    long itemsVersion = _selectionVersion;
                    await _explorerService.RestoreItemsAsync(selectionHost, _navigationTracker.PendingSelectedItems);
                    if (_isDisposed || _selectionVersion != itemsVersion || _explorerHwnd != selectionHost || _activeTab != tab) return;
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

            long navigationVersion = _selectionVersion;
            bool navigationStarted;
            try
            {
                navigationStarted = await _explorerService.NavigatePathAsync(selectionHost, path);
                if (_isDisposed || _selectionVersion != navigationVersion || _explorerHwnd != selectionHost || !_tabs.Contains(tab))
                {
                    if (navigationStarted && !_isDisposed && _explorerHwnd == selectionHost)
                        _navigationTracker.RememberCancelled(path);
                    return;
                }
            }
            catch (Exception ex)
            {
                if (_isDisposed || _selectionVersion != navigationVersion || _explorerHwnd != selectionHost || !_tabs.Contains(tab))
                {
                    if (!_isDisposed && _explorerHwnd == selectionHost) _navigationTracker.RememberCancelled(path);
                    return;
                }
                // A timed-out write may still complete. Track it before rolling back so
                // a late arrival selects its own tab instead of overwriting the source.
                _navigationTracker.StartNavigation(NormalizeTabPath(path),
                    failureSource != tab ? failureSource : null,
                    failureSource != tab ? GetTabIndex(failureSource) : -1);
                CancelPendingNavigation();
                if (restorePendingNavigation != null) restorePendingNavigation();
                LastNavigationFailed = true;
                NavigationFailureVersion++;
                AppLogger.LogError("TabBarViewModel", "Explorer navigation failed; selection was restored.", ex);
                return;
            }

            if (navigationStarted)
            {
                if (_navigationTracker.NavigatingToPath == null)
                {
                    _pendingCloseRollback = null;
                    PendingHostRollback = null;
                }
                _navigationTracker.RememberPendingAsCancelled();
                AppLogger.LogDiagnostic(
                    "TabBarViewModel",
                    string.Format(
                        "SelectTab navigateStarted explorer={0} targetPath={1}",
                        _explorerHwnd,
                        path ?? string.Empty));
                TabItemViewModel source = confirmedSource ?? previousActiveTab;
                _navigationTracker.StartNavigation(
                    NormalizeTabPath(path),
                    source != tab ? source : null,
                    source != null && source != tab ? GetTabIndex(source) : -1
                );
            }
            else
            {
                AppLogger.LogDiagnostic(
                    "TabBarViewModel",
                    string.Format(
                        "SelectTab navigateRejected explorer={0} targetPath={1}",
                        _explorerHwnd,
                        path ?? string.Empty));
                _navigationTracker.ClearPending();
                if (restorePendingNavigation != null) restorePendingNavigation();
                LastNavigationFailed = true;
                NavigationFailureVersion++;

                if (failureSource != null && failureSource != tab)
                {
                    int previousIndex = GetTabIndex(failureSource);
                    if (previousIndex >= 0)
                    {
                        SetActiveTabOnly(failureSource);
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

        internal void RememberRemovedTab(TabItemViewModel tab)
        {
            if (tab != null) RecordClosedTab(tab.Path, GetTabIndex(tab));
        }

        internal bool RemoveUnavailableInactiveTabs(Func<string, bool> isTabPathCurrentlyAvailable, string currentPath)
        {
            if (isTabPathCurrentlyAvailable == null)
            {
                return false;
            }

            bool removed = false;
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                TabItemViewModel tab = _tabs[i];
                if (tab == null || tab == _activeTab || string.IsNullOrEmpty(tab.Path))
                {
                    continue;
                }

                if (PathEquals(tab.Path, currentPath))
                {
                    continue;
                }

                if (isTabPathCurrentlyAvailable(tab.Path))
                {
                    continue;
                }

                RememberRemovedTab(tab);
                _tabs.RemoveAt(i);
                removed = true;
            }

            if (removed)
            {
                ActiveTabIndex = GetTabIndex(_activeTab);
            }

            return removed;
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
