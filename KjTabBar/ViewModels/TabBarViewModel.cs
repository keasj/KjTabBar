using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.ViewModels
{
    public class TabBarViewModel : ViewModelBase, IDisposable
    {
        private IntPtr _explorerHwnd;
        private ObservableCollection<TabItemViewModel> _tabs;
        private TabItemViewModel _activeTab;
        private int _activeTabIndex;
        private string _navigatingToPath;
        private DateTime _navigateStartTime;
        private List<string> _pendingSelectedItems;
        private TabItemViewModel _navigationSourceTab;
        private int _navigationSourceTabIndex = -1;
        private string _cancelledNavigationPath;
        private DateTime _cancelledNavigationUtc = DateTime.MinValue;
        private DateTime _lastExplorerPathPollUtc = DateTime.MinValue;
        private string _cachedExplorerPath = null;
        private static readonly TimeSpan ExplorerPathPollInterval = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan CancelledNavigationGracePeriod = TimeSpan.FromSeconds(15);


        private System.Windows.Media.FontFamily _fontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        private double _fontSize = 11.5;
        private System.Windows.FontWeight _fontWeight = System.Windows.FontWeights.Normal;
        private System.Windows.FontStyle _fontStyle = System.Windows.FontStyles.Normal;
        private System.Windows.Visibility _windowVisibility = System.Windows.Visibility.Visible;
        private IUserSettings _userSettings;
        private IExplorerService _explorerService;
        private bool _isDisposed;

        public IntPtr ExplorerHwnd
        {
            get { return _explorerHwnd; }
        }

        private class ClosedTabInfo
        {
            public string Path;
            public int Position;
        }

        private class ClosedTabBatch
        {
            public List<ClosedTabInfo> Tabs = new List<ClosedTabInfo>();
        }

        private List<ClosedTabBatch> _closedTabHistory = new List<ClosedTabBatch>();
        private ClosedTabBatch _currentRecordingBatch = null;

        public bool HasClosedTabs
        {
            get { return _closedTabHistory.Count > 0; }
        }

        private void StartHistoryBatch()
        {
            _currentRecordingBatch = new ClosedTabBatch();
        }

        private void EndHistoryBatch()
        {
            if (_currentRecordingBatch != null && _currentRecordingBatch.Tabs.Count > 0)
            {
                _closedTabHistory.Add(_currentRecordingBatch);
                if (_closedTabHistory.Count > 50) _closedTabHistory.RemoveAt(0);
                OnPropertyChanged("HasClosedTabs");
            }
            _currentRecordingBatch = null;
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
            _navigatingToPath = null;

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

        /// <summary>
        /// 指定したパスと同じパスのタブを探す。見つかればそのタブを返す。
        /// </summary>
        private TabItemViewModel FindTabByPath(string path)
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

        /// <summary>
        /// 指定したパスで新しいタブを追加し、エクスプローラーをそのパスにナビゲートする。
        /// 同じパスのタブが既にあればそちらを選択する。
        /// </summary>
        public void AddTabWithPath(string path)
        {
            AddTabWithPath(path, false);
        }

        public void AddTabWithPath(string path, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 現在のアクティブタブの後ろに挿入
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
            _pendingSelectedItems = selectedItems;
            AddTabWithPath(path, allowSpecialPath);
        }

        public void InsertTabWithPathAndSelect(string path, int index, System.Collections.Generic.List<string> selectedItems, bool allowSpecialPath)
        {
            if (string.IsNullOrEmpty(path)) return;
            _pendingSelectedItems = selectedItems;
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

            // 右隣に追加
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

            if (newIndex == _tabs.Count) newIndex--; // 末尾の場合は調整
            if (oldIndex == newIndex) return;

            _tabs.Move(oldIndex, newIndex);

            // ActiveTabIndexを再計算
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

        private void SetActiveTabOnly(TabItemViewModel tab)
        {
            if (tab == null)
            {
                return;
            }

            ActiveTab = tab;
            ActiveTabIndex = GetTabIndex(tab);
        }

        private void ClearPendingNavigationTracking()
        {
            _navigatingToPath = null;
            _navigationSourceTab = null;
            _navigationSourceTabIndex = -1;
        }

        private void ClearCancelledNavigationTracking()
        {
            _cancelledNavigationPath = null;
            _cancelledNavigationUtc = DateTime.MinValue;
        }

        private bool IsCancelledNavigationMatch(string currentPath)
        {
            if (string.IsNullOrEmpty(_cancelledNavigationPath))
            {
                return false;
            }

            if (_cancelledNavigationUtc == DateTime.MinValue ||
                (DateTime.UtcNow - _cancelledNavigationUtc) > CancelledNavigationGracePeriod)
            {
                ClearCancelledNavigationTracking();
                return false;
            }

            return PathEquals(_cancelledNavigationPath, currentPath);
        }

        private void CancelPendingNavigation()
        {
            TabItemViewModel navigationSourceTab = _navigationSourceTab;
            int navigationSourceTabIndex = _navigationSourceTabIndex;
            string cancelledNavigationPath = _navigatingToPath;
            TabItemViewModel cancelledNavigationTab = _activeTab;

            if (!string.IsNullOrEmpty(cancelledNavigationPath) &&
                cancelledNavigationTab != null &&
                cancelledNavigationTab != navigationSourceTab)
            {
                _cancelledNavigationPath = cancelledNavigationPath;
                _cancelledNavigationUtc = DateTime.UtcNow;
            }
            else
            {
                ClearCancelledNavigationTracking();
            }

            ClearPendingNavigationTracking();
            _pendingSelectedItems = null;

            int existingIndex = GetTabIndex(navigationSourceTab);
            if (existingIndex >= 0)
            {
                SetActiveTabOnly(navigationSourceTab);
                return;
            }

            if (navigationSourceTabIndex >= 0 && navigationSourceTabIndex < _tabs.Count)
            {
                SetActiveTabOnly(_tabs[navigationSourceTabIndex]);
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
                    // 最初の有効なタブが見つかった際に初期タブをクリア
                    _tabs.Clear();
                    _activeTab = null; // アクティブタブ参照も切る
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
                        // 復元したタブの中にエクスプローラーが最初に開こうとしたパスがあった場合は、そのタブを選択
                        SelectTab(targetTab);
                    }
                    else
                    {
                        if (IsPersistedTabPathRestorable(initialPath))
                        {
                            // 復元したタブの中に同名パスがなければ新規追加してアクティブにする
                            string title = _explorerService.GetFolderName(initialPath);
                            TabItemViewModel newTab = new TabItemViewModel(initialPath, title, _explorerService);
                            _tabs.Add(newTab);
                            SelectTab(newTab);
                        }
                        else if (_tabs.Count > 0)
                        {
                            SelectTab(_tabs[0]);
                        }
                    }
                }
                else if (_tabs.Count > 0)
                {
                    // 初期パスがない場合は1番目のタブを選択
                    SelectTab(_tabs[0]);
                }
            }
            UpdateTabTitles();
        }

        private bool IsPersistedTabPathRestorable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalizedPath = NormalizeTabPath(path);
            if (string.IsNullOrEmpty(normalizedPath)) return false;
            if (normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
            if (_explorerService.IsControlPanelPath(normalizedPath)) return true;
            if (IsPotentialFileSystemTabPath(normalizedPath)) return true;
            return false;
        }

        private bool IsPotentialFileSystemTabPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) return true;
            return System.IO.Directory.Exists(path);
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
                // 非アクティブタブを閉じた場合、アクティブタブのインデックスを補正
                if (index < _activeTabIndex)
                {
                    ActiveTabIndex = _activeTabIndex - 1;
                }
            }
            UpdateTabTitles();
        }

        private void RecordClosedTab(string path, int position)
        {
            if (string.IsNullOrEmpty(path)) return;

            ClosedTabInfo info = new ClosedTabInfo();
            info.Path = path;
            info.Position = position;

            if (_currentRecordingBatch != null)
            {
                _currentRecordingBatch.Tabs.Add(info);
            }
            else
            {
                ClosedTabBatch batch = new ClosedTabBatch();
                batch.Tabs.Add(info);
                _closedTabHistory.Add(batch);
                if (_closedTabHistory.Count > 50) _closedTabHistory.RemoveAt(0);
                OnPropertyChanged("HasClosedTabs");
            }
        }

        public void ReopenClosedTab()
        {
            if (_closedTabHistory.Count == 0) return;

            int lastIdx = _closedTabHistory.Count - 1;
            ClosedTabBatch batch = _closedTabHistory[lastIdx];
            _closedTabHistory.RemoveAt(lastIdx);
            OnPropertyChanged("HasClosedTabs");

            // 履歴に保存された順序の逆順で復元することで、元のインデックス位置を正しく再現する
            for (int i = batch.Tabs.Count - 1; i >= 0; i--)
            {
                ClosedTabInfo info = batch.Tabs[i];
                InsertTabWithPath(info.Path, info.Position);
            }
        }

        /// <summary>
        /// 指定したタブより右側にあるすべてのタブを閉じる。
        /// </summary>
        public void CloseTabsToRight(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index < 0) return;

            StartHistoryBatch();
            // インデックスが変わらないよう、常に index + 1 の位置を消し続ける
            while (_tabs.Count > index + 1)
            {
                CloseTab(_tabs[index + 1]);
            }
            EndHistoryBatch();
        }

        /// <summary>
        /// 指定したタブより左側にあるすべてのタブを閉じる。
        /// </summary>
        public void CloseTabsToLeft(TabItemViewModel tab)
        {
            if (tab == null) return;
            int index = GetTabIndex(tab);
            if (index <= 0) return;

            StartHistoryBatch();
            // 先頭から index 個分消す。
            // CloseTab を呼ぶたびに _tabs の内容が変わるが、常に 0 番目を消せば左側が消える。
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
                // 旧データの null ホームタブは OS 互換パスに補正する
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

            // 万が一のOS側の仕様（特定のフォルダへNavigateすると別窓が強制的に開く等の仕様）によって、
            // 別窓が開いてはそれが再び「タブ」として吸収(Absorb)される...という無限ループ(増殖)を防ぐため、
            // 既に同じパスへナビゲーションを試みている最中なら、二重にNavigateを呼ばないようにする
            if (_navigatingToPath != null && _navigatingToPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                if (shouldUpdateTitles)
                {
                    UpdateTabTitles();
                }
                return;
            }

            string currentPath = _explorerService.GetCurrentPath(_explorerHwnd);
            if (PathEquals(currentPath, path))
            {
                // 既に現在のエクスプローラーが目的のパスに居るなら再ナビゲーションしない
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
                _navigatingToPath = NormalizeTabPath(path);
                _navigateStartTime = DateTime.UtcNow;
                if (previousActiveTab != null && previousActiveTab != tab)
                {
                    _navigationSourceTab = previousActiveTab;
                    _navigationSourceTabIndex = previousActiveTabIndex;
                }
                else
                {
                    _navigationSourceTab = null;
                    _navigationSourceTabIndex = -1;
                }
                _lastExplorerPathPollUtc = DateTime.MinValue;
            }
            else
            {
                ClearPendingNavigationTracking();
                _pendingSelectedItems = null;

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

        private bool _isSyncing = false;

        /// <summary>
        /// エクスプローラーのパス変化を検出する。
        /// SelectTabによるナビゲーション中はタブを追加しない。
        /// 同じパスのタブが既にあればそちらを選択する。
        /// </summary>
        public async void SyncWithExplorer()
        {
            if (_isSyncing) return;
            _isSyncing = true;
            bool shouldUpdateTitles = false;
            try
            {
                bool forcePathPoll = (_navigatingToPath != null || _pendingSelectedItems != null);

                // UIスレッドをブロックしないよう、COMアクセスをバックグラウンドスレッドで行う
                string currentPath = await Services.ComThreadService.Instance.InvokeAsync(() => GetCurrentPathForSync(forcePathPoll));

                if (_activeTab == null) return;

                if (_explorerService.IsControlPanelRootPath(currentPath))
                {
                    string normalizedCPPath = _explorerService.AllControlPanelPath;
                    if (!PathEquals(_activeTab.Path, normalizedCPPath))
                    {
                        _activeTab.Path = normalizedCPPath;
                        _activeTab.BaseTitle = _explorerService.GetLocalizedControlPanelTitle();
                        _activeTab.Title = _activeTab.BaseTitle;
                        shouldUpdateTitles = true;
                    }
                    ClearPendingNavigationTracking();
                    _pendingSelectedItems = null;
                    return;
                }

                // パス取得が一時的に不安定な場合は現在のタブ状態を保持する
                if (string.IsNullOrEmpty(currentPath))
                {
                    if (_navigatingToPath != null)
                    {
                        if ((DateTime.UtcNow - _navigateStartTime).TotalSeconds > 5)
                        {
                            CancelPendingNavigation();
                        }
                        return;
                    }
                    return;
                }

                // 現在のアクティブタブとパスが一致 → 何もしない
                if (PathEquals(_activeTab.Path, currentPath))
                {
                    ClearPendingNavigationTracking();
                    if (_pendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_explorerHwnd, _pendingSelectedItems);
                        _pendingSelectedItems = null;
                    }
                    return;
                }

                // ナビゲート先パスと一致 → タブ切り替え中のナビゲーション完了
                if (_navigatingToPath != null && PathEquals(_navigatingToPath, currentPath))
                {
                    _activeTab.Path = currentPath;
                    _activeTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                    _activeTab.Title = _activeTab.BaseTitle;
                    ClearPendingNavigationTracking();
                    shouldUpdateTitles = true;
                    if (_pendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_explorerHwnd, _pendingSelectedItems);
                        _pendingSelectedItems = null;
                    }
                    return;
                }

                // ナビゲート先がまだ反映されていない → 待機（タイムアウト付き）
                if (_navigatingToPath != null)
                {
                    if ((DateTime.UtcNow - _navigateStartTime).TotalSeconds > 5)
                    {
                        // 5秒以上経過してもナビゲーションが完了しない場合は
                        // ナビゲーション失敗とみなし、状態を元のタブへ戻す
                        CancelPendingNavigation();
                        return;
                    }
                    else
                    {
                        return;
                    }
                }

                if (IsCancelledNavigationMatch(currentPath))
                {
                    TabItemViewModel cancelledTab = FindTabByPath(currentPath);
                    if (cancelledTab != null)
                    {
                        if (cancelledTab != _activeTab)
                        {
                            SetActiveTabOnly(cancelledTab);
                        }
                        cancelledTab.Path = currentPath;
                        cancelledTab.Title = _explorerService.GetFolderName(currentPath);
                        shouldUpdateTitles = true;
                        ClearCancelledNavigationTracking();
                        return;
                    }

                    ClearCancelledNavigationTracking();
                }

                _activeTab.Path = currentPath;
                _activeTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                _activeTab.Title = _activeTab.BaseTitle;
                shouldUpdateTitles = true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarViewModel", "SyncWithExplorer failed.", ex);
            }
            finally
            {
                _isSyncing = false;
                if (shouldUpdateTitles)
                {
                    UpdateTabTitles();
                }
            }
        }

        private string GetCurrentPathForSync(bool forcePoll)
        {
            DateTime nowUtc = DateTime.UtcNow;

            if (!forcePoll)
            {
                if (_lastExplorerPathPollUtc != DateTime.MinValue &&
                    (nowUtc - _lastExplorerPathPollUtc) < ExplorerPathPollInterval)
                {
                    return _cachedExplorerPath;
                }
            }

            string currentPath = _explorerService.GetCurrentPath(_explorerHwnd);
            _cachedExplorerPath = currentPath;
            _lastExplorerPathPollUtc = nowUtc;
            return currentPath;
        }

        private bool PathEquals(string path1, string path2)
        {
            string normalizedPath1 = NormalizeTabPath(path1);
            string normalizedPath2 = NormalizeTabPath(path2);
            if (normalizedPath1 == null && normalizedPath2 == null) return true;
            if (normalizedPath1 == null || normalizedPath2 == null) return false;
            return string.Equals(normalizedPath1.TrimEnd('\\'), normalizedPath2.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateTabTitles()
        {
            if (_tabs == null || _tabs.Count == 0) return;

            // 1. 各タブの基本情報（フォルダ名）を取得し、タイトルを一旦リセット (キャッシュを活用して高速化)
            for (int i = 0; i < _tabs.Count; i++)
            {
                TabItemViewModel tab = _tabs[i];
                if (string.IsNullOrEmpty(tab.BaseTitle))
                {
                    tab.BaseTitle = _explorerService.GetFolderName(tab.Path);
                }
                tab.Title = tab.BaseTitle;
            }

            // 2. 「フォルダ名」が同じだが「フルパス」が異なるタブ（名前重複）を特定 (O(N) に最適化)
            Dictionary<string, List<int>> baseNameGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _tabs.Count; i++)
            {
                string title = _tabs[i].Title;
                if (string.IsNullOrEmpty(title)) title = "Home";
                if (!baseNameGroups.ContainsKey(title)) baseNameGroups[title] = new List<int>();
                baseNameGroups[title].Add(i);
            }

            HashSet<int> collisionIndices = new HashSet<int>();
            foreach (KeyValuePair<string, List<int>> kvp in baseNameGroups)
            {
                if (kvp.Value.Count > 1)
                {
                    // パスが異なるものが1つでもあれば、そのグループ全体を深堀り対象とする
                    string firstPath = _tabs[kvp.Value[0]].Path;
                    bool hasDifferentPath = false;
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        if (!string.Equals(firstPath, _tabs[kvp.Value[i]].Path, StringComparison.OrdinalIgnoreCase))
                        {
                            hasDifferentPath = true;
                            break;
                        }
                    }

                    if (hasDifferentPath)
                    {
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            collisionIndices.Add(kvp.Value[i]);
                        }
                    }
                }
            }

            // 3. 名前重複があるタブに対し、階層を遡る
            if (collisionIndices.Count > 0)
            {
                bool changed = true;
                int maxIterations = 10;
                while (changed && maxIterations-- > 0)
                {
                    changed = false;
                    Dictionary<string, List<int>> currentTitleGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (int idx in collisionIndices)
                    {
                        string t = _tabs[idx].Title;
                        if (string.IsNullOrEmpty(t)) continue;
                        if (!currentTitleGroups.ContainsKey(t)) currentTitleGroups[t] = new List<int>();
                        currentTitleGroups[t].Add(idx);
                    }

                    foreach (KeyValuePair<string, List<int>> entry in currentTitleGroups)
                    {
                        if (entry.Value.Count > 1)
                        {
                            foreach (int idx in entry.Value)
                            {
                                string nextTitle = GetDeeperTitle(_tabs[idx].Path, _tabs[idx].Title);
                                if (!string.Equals(nextTitle, _tabs[idx].Title, StringComparison.OrdinalIgnoreCase))
                                {
                                    _tabs[idx].Title = nextTitle;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }

            // 5. 長すぎるタイトルを「先頭...末尾」形式に短縮
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Title = ShortenTitle(_tabs[i].Title, 30);
            }

            // 6. 最終的なTitleに対して、重複があれば元のタブも含め (1)〇〇, (2)〇〇 を付加する
            Dictionary<string, List<int>> finalTitleGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _tabs.Count; i++)
            {
                string baseTitle = _tabs[i].Title;
                if (!finalTitleGroups.ContainsKey(baseTitle))
                {
                    finalTitleGroups[baseTitle] = new List<int>();
                }
                finalTitleGroups[baseTitle].Add(i);
            }

            foreach (KeyValuePair<string, List<int>> kvp in finalTitleGroups)
            {
                if (kvp.Value.Count > 1)
                {
                    int count = 1;
                    foreach (int idx in kvp.Value)
                    {
                        _tabs[idx].Title = $"({count}){kvp.Key}";
                        count++;
                    }
                }
            }
        }

        private string GetDeeperTitle(string path, string currentTitle)
        {
            if (string.IsNullOrEmpty(path))
            {
                return currentTitle;
            }

            // 特殊なシェルパスは拡張できない
            if (path.StartsWith("::{") || path.StartsWith("shell:"))
            {
                return currentTitle;
            }

            if (currentTitle.Contains("..."))
            {
                return path;
            }

            string normalizedPath = path.TrimEnd('\\');
            // セグメントに分割（空要素を除去することで UNC の先頭バックスラッシュ等も一時的に消える）
            string[] segments = normalizedPath.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0) return path;

            // 現在表示されているセグメント数をカウント
            // currentTitle内の「\」の数から推定（例: "Folder" -> 1, "Parent\Folder" -> 2）
            int currentSegmentCount = currentTitle.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
            int nextSegmentCount = currentSegmentCount + 1;

            if (segments.Length <= 1 || nextSegmentCount > segments.Length)
            {
                // すでに絶対パス形式（ドライブ文字等を含む）の場合は、さらに親を付加すると冗長になるため避ける
                if (currentTitle.Length >= 2 && currentTitle[1] == ':')
                {
                    return currentTitle;
                }

                // 物理的な階層がこれ以上ない場合、シェルオブジェクト経由で親の名前を取得することを試みる（仮想フォルダ対応）
                string parentName = _explorerService.GetParentFolderName(normalizedPath);
                if (!string.IsNullOrEmpty(parentName) && !string.Equals(parentName, currentTitle, StringComparison.OrdinalIgnoreCase))
                {
                    string joined = parentName + @"\" + currentTitle;
                    // ドライブ名（"Windows (C:)" 等）に対して joined が "C:\Windows (C:)" になるのを防ぐ
                    if (path.Length >= 2 && path[1] == ':' && string.Equals(parentName, path.Substring(0, 2), StringComparison.OrdinalIgnoreCase))
                    {
                         return path;
                    }
                    return joined;
                }
                return path;
            }

            // 1つ上のセグメントを取得
            string parentSegment = segments[segments.Length - nextSegmentCount];
            string result = parentSegment + @"\" + currentTitle;

            // UNC パスまたはドライブレター、ルートに関する補正
            if (path.StartsWith(@"\\") && !result.StartsWith(@"\\") && segments.Length == nextSegmentCount)
            {
                result = @"\\" + result;
            }
            else if (result.Length >= 2 && result[1] == ':' && result.Length > 2 && result[2] != '\\')
            {
                result = result.Insert(2, @"\");
            }

            return result;
        }

        private string ShortenTitle(string title, int maxLen)
        {
            if (string.IsNullOrEmpty(title) || title.Length <= maxLen) return title;

            // 特殊なシェルパス (::{...}) は、下手に短縮すると意味不明になるので、
            // そのまま表示を優先する (WPF側で末尾省略される)
            if (title.StartsWith("::{") || title.StartsWith("shell:")) return title;

            // パス (バックスラッシュを含む) の場合は、「先頭...末尾」の短縮を試みる
            if (title.Contains(@"\"))
            {
                // ドライブレターやルート (\) を特定
                int rootLen = 0;
                if (title.Length >= 3 && title[1] == ':' && title[2] == '\\') rootLen = 3; // C:\
                else if (title.StartsWith(@"\\")) // UNC
                {
                    int nextSlash = title.IndexOf(@"\", 2);
                    if (nextSlash > 0) rootLen = nextSlash + 1;
                }
                else if (title.StartsWith(@"\")) rootLen = 1;

                string leafName = title.Substring(title.LastIndexOf(@"\") + 1);
                if (string.IsNullOrEmpty(leafName)) leafName = title; // 末尾が \ の場合など

                // ルート部分 + "...\" + 末尾部分 で収まるかチェック
                // ルートが特定できている場合のみ構造を維持
                if ((rootLen > 0 || title.StartsWith(@"\\")) && rootLen + 3 + leafName.Length <= maxLen)
                {
                    string rootPart = title.Substring(0, rootLen);
                    // 必要なら "\" を補う
                    if (!rootPart.EndsWith(@"\") && !leafName.Contains(@"\")) rootPart += @"\";
                    return rootPart + @"...\" + leafName;
                }
            }

            // 一般的な文字列としての短縮 (Start...End)
            int startCount = maxLen / 3;
            if (startCount < 1) startCount = 1;
            int endCount = maxLen - startCount - 3;
            if (endCount < 5) endCount = 5; // 末尾を多めに残す

            if (startCount + endCount + 3 > title.Length) return title;

            return title.Substring(0, startCount) + "..." + title.Substring(title.Length - endCount);
        }
    }
}



