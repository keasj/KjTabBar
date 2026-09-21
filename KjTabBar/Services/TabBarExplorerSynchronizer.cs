using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    public sealed class TabBarExplorerSynchronizer
    {
        private readonly ViewModels.TabBarViewModel _viewModel;
        private readonly IExplorerService _explorerService;
        private bool _isSyncing = false;

        public TabBarExplorerSynchronizer(ViewModels.TabBarViewModel viewModel, IExplorerService explorerService)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
        }

        public async Task SyncWithExplorerAsync()
        {
            if (_isSyncing || _viewModel.IsRestoringControlPanelHost || _viewModel.IsTabOperationPending) return;
            _isSyncing = true;
            bool shouldUpdateTitles = false;
            long version = _viewModel.SynchronizationVersion;
            try
            {
                bool forcePathPoll = (_viewModel.NavigationTracker.NavigatingToPath != null || _viewModel.NavigationTracker.PendingSelectedItems != null);
                DateTime pathPollNowUtc = DateTime.UtcNow;
                string currentPath;

                if (!forcePathPoll && !_viewModel.NavigationTracker.ShouldPoll(pathPollNowUtc, false))
                {
                    currentPath = _viewModel.NavigationTracker.CachedExplorerPath;
                }
                else
                {
                    IntPtr explorerHwnd = _viewModel.ExplorerHwnd;

                    // COM ワーカーでは Explorer のパス取得だけを行い、UI 管理状態には触れない。
                    currentPath = await ComThreadService.Instance.InvokeAsync(() => _explorerService.GetCurrentPath(explorerHwnd));
                    if (!_viewModel.IsSynchronizationCurrent(version) || _viewModel.IsRestoringControlPanelHost || _viewModel.ExplorerHwnd != explorerHwnd)
                    {
                        return;
                    }

                    _viewModel.NavigationTracker.UpdateCache(currentPath, DateTime.UtcNow);
                }

                DateTime syncNowUtc = DateTime.UtcNow;
                if (_viewModel.ActiveTab == null) return;

                IntPtr availabilityHost = _viewModel.ExplorerHwnd;
                List<string> pathsToCheck = new List<string>();
                HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ViewModels.TabItemViewModel tab in _viewModel.Tabs)
                {
                    if (!string.IsNullOrEmpty(tab.Path) && uniquePaths.Add(tab.Path)) pathsToCheck.Add(tab.Path);
                }
                Dictionary<string, bool> availability = await ComThreadService.Instance.InvokeAsync(delegate
                {
                    ExplorerManager manager = _explorerService as ExplorerManager;
                    if (manager != null && manager.UsesShellWorker) return manager.GetPathAvailability(pathsToCheck);
                    Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (string path in pathsToCheck)
                    {
                        if (!values.ContainsKey(path)) values[path] = _explorerService.IsTabPathCurrentlyAvailable(path);
                    }
                    return values;
                });
                if (!_viewModel.IsSynchronizationCurrent(version) || _viewModel.IsRestoringControlPanelHost || _viewModel.ExplorerHwnd != availabilityHost || _viewModel.ActiveTab == null)
                {
                    _viewModel.NavigationTracker.InvalidateCache();
                    return;
                }
                Func<string, bool> isAvailable = path =>
                {
                    bool value;
                    return path == null || !availability.TryGetValue(path, out value) || value;
                };

                if (_viewModel.RemoveUnavailableInactiveTabs(isAvailable, currentPath))
                {
                    shouldUpdateTitles = true;
                }

                if (_viewModel.NavigationTracker.NavigatingToPath == null &&
                    _viewModel.NavigationTracker.IsExplorerHostSwitchGraceActive(syncNowUtc) &&
                    !string.IsNullOrEmpty(currentPath) &&
                    !_viewModel.PathEquals(_viewModel.ActiveTab.Path, currentPath))
                {
                    return;
                }

                if (_viewModel.NavigationTracker.NavigatingToPath == null && _viewModel.IsCancelledNavigationMatch(currentPath))
                {
                    ViewModels.TabItemViewModel cancelledTab = _viewModel.FindTabByPath(currentPath);
                    if (cancelledTab == null)
                    {
                        // A rolled-back last-tab close may have removed its temporary Home tab.
                        cancelledTab = new ViewModels.TabItemViewModel(currentPath, _explorerService.GetFolderName(currentPath), _explorerService);
                        _viewModel.Tabs.Add(cancelledTab);
                    }
                    if (cancelledTab != null)
                    {
                        if (cancelledTab != _viewModel.ActiveTab)
                        {
                            _viewModel.SetActiveTabOnly(cancelledTab);
                        }
                        cancelledTab.Path = currentPath;
                        cancelledTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                        cancelledTab.Title = cancelledTab.BaseTitle;
                        shouldUpdateTitles = true;
                        _viewModel.NavigationTracker.ForgetCancelled(currentPath, _viewModel.PathEquals);
                        return;
                    }

                    _viewModel.NavigationTracker.ForgetCancelled(currentPath, _viewModel.PathEquals);
                }

                if (_explorerService.IsControlPanelRootPath(currentPath) &&
                    (_viewModel.NavigationTracker.NavigatingToPath == null ||
                     _viewModel.PathEquals(_viewModel.NavigationTracker.NavigatingToPath, currentPath)))
                {
                    string normalizedCPPath = _explorerService.AllControlPanelPath;
                    string localizedCPTitle = _explorerService.GetLocalizedControlPanelTitle();

                    if (!_viewModel.PathEquals(_viewModel.ActiveTab.Path, normalizedCPPath) ||
                         !string.Equals(_viewModel.ActiveTab.BaseTitle, localizedCPTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        _viewModel.ActiveTab.Path = normalizedCPPath;
                        _viewModel.ActiveTab.BaseTitle = localizedCPTitle;
                        _viewModel.ActiveTab.Title = _viewModel.ActiveTab.BaseTitle;
                        shouldUpdateTitles = true;
                    }
                    if (_viewModel.NavigationTracker.PendingSelectedItems != null)
                        _explorerService.SelectItems(_viewModel.ExplorerHwnd, _viewModel.NavigationTracker.PendingSelectedItems);
                    _viewModel.ClearPendingNavigationTracking();
                    return;
                }

                // パス取得が一時的に不安定な場合は現在のタブ状態を保持する
                if (string.IsNullOrEmpty(currentPath))
                {
                    if (_viewModel.NavigationTracker.NavigatingToPath != null)
                    {
                        if ((DateTime.UtcNow - _viewModel.NavigationTracker.NavigateStartTime).TotalSeconds > 5)
                        {
                            _viewModel.TimeoutPendingNavigation();
                        }
                        return;
                    }
                    return;
                }

                // 現在のアクティブタブとパスが一致 → 何もしない
                // コントロールパネルのパスの場合、カテゴリIDなどの違いを無視して同一とみなすのを防ぐため、
                // 文字列全体が完全に等しいかどうかもチェックする。
                if (_viewModel.PathEquals(_viewModel.ActiveTab.Path, currentPath) &&
                    (!_explorerService.IsControlPanelPath(currentPath) ||
                     string.Equals(_viewModel.ActiveTab.Path, currentPath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_viewModel.NavigationTracker.PendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_viewModel.ExplorerHwnd, _viewModel.NavigationTracker.PendingSelectedItems);
                    }
                    _viewModel.ClearPendingNavigationTracking();
                    return;
                }

                if (!isAvailable(_viewModel.ActiveTab.Path))
                {
                    ViewModels.TabItemViewModel matchingTab = _viewModel.FindTabByPath(currentPath);
                    if (matchingTab != null && matchingTab != _viewModel.ActiveTab)
                    {
                        ViewModels.TabItemViewModel unavailableActiveTab = _viewModel.ActiveTab;
                        _viewModel.RememberRemovedTab(unavailableActiveTab);
                        _viewModel.Tabs.Remove(unavailableActiveTab);
                        _viewModel.SetActiveTabOnly(matchingTab);
                        _viewModel.ClearPendingNavigationTracking();
                        shouldUpdateTitles = true;
                        return;
                    }
                }

                // ナビゲート先パスと一致 → タブ切り替え中のナビゲーション完了
                if (_viewModel.NavigationTracker.NavigatingToPath != null && _viewModel.PathEquals(_viewModel.NavigationTracker.NavigatingToPath, currentPath))
                {
                    _viewModel.ActiveTab.Path = currentPath;
                    _viewModel.ActiveTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                    _viewModel.ActiveTab.Title = _viewModel.ActiveTab.BaseTitle;
                    if (_viewModel.NavigationTracker.PendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_viewModel.ExplorerHwnd, _viewModel.NavigationTracker.PendingSelectedItems);
                    }
                    _viewModel.ClearPendingNavigationTracking();
                    shouldUpdateTitles = true;
                    return;
                }

                // ナビゲート先がまだ反映されていない → 待機（タイムアウト付き）
                if (_viewModel.NavigationTracker.NavigatingToPath != null)
                {
                    if ((DateTime.UtcNow - _viewModel.NavigationTracker.NavigateStartTime).TotalSeconds > 5)
                    {
                        // 5秒以上経過してもナビゲーションが完了しない場合は
                        // ナビゲーション失敗とみなし、状態を元のタブへ戻す
                        _viewModel.TimeoutPendingNavigation();
                        return;
                    }
                    else
                    {
                        return;
                    }
                }

                _viewModel.ActiveTab.Path = currentPath;
                _viewModel.ActiveTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                _viewModel.ActiveTab.Title = _viewModel.ActiveTab.BaseTitle;
                shouldUpdateTitles = true;
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarExplorerSynchronizer", "SyncWithExplorerAsync failed.", ex);
            }
            finally
            {
                _isSyncing = false;
                if (shouldUpdateTitles)
                {
                    _viewModel.UpdateTabTitles();
                }
            }
        }


    }
}
