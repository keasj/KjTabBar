﻿using System;
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
            if (_isSyncing) return;
            _isSyncing = true;
            bool shouldUpdateTitles = false;
            try
            {
                bool forcePathPoll = (_viewModel.NavigationTracker.NavigatingToPath != null || _viewModel.NavigationTracker.PendingSelectedItems != null);

                // UIスレッドをブロックしないよう、COMアクセスをバックグラウンドスレッドで行う
                string currentPath = await ComThreadService.Instance.InvokeAsync(() => GetCurrentPathForSync(forcePathPoll));

                if (_viewModel.ActiveTab == null) return;

                if (_explorerService.IsControlPanelRootPath(currentPath))
                {
                    string normalizedCPPath = _explorerService.AllControlPanelPath;
                    if (!_viewModel.PathEquals(_viewModel.ActiveTab.Path, normalizedCPPath))
                    {
                        _viewModel.ActiveTab.Path = normalizedCPPath;
                        _viewModel.ActiveTab.BaseTitle = _explorerService.GetLocalizedControlPanelTitle();
                        _viewModel.ActiveTab.Title = _viewModel.ActiveTab.BaseTitle;
                        shouldUpdateTitles = true;
                    }
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
                            _viewModel.CancelPendingNavigation();
                        }
                        return;
                    }
                    return;
                }

                // 現在のアクティブタブとパスが一致 → 何もしない
                if (_viewModel.PathEquals(_viewModel.ActiveTab.Path, currentPath))
                {
                    _viewModel.ClearPendingNavigationTracking();
                    if (_viewModel.NavigationTracker.PendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_viewModel.ExplorerHwnd, _viewModel.NavigationTracker.PendingSelectedItems);
                        _viewModel.NavigationTracker.PendingSelectedItems = null;
                    }
                    return;
                }

                // ナビゲート先パスと一致 → タブ切り替え中のナビゲーション完了
                if (_viewModel.NavigationTracker.NavigatingToPath != null && _viewModel.PathEquals(_viewModel.NavigationTracker.NavigatingToPath, currentPath))
                {
                    _viewModel.ActiveTab.Path = currentPath;
                    _viewModel.ActiveTab.BaseTitle = _explorerService.GetFolderName(currentPath);
                    _viewModel.ActiveTab.Title = _viewModel.ActiveTab.BaseTitle;
                    _viewModel.ClearPendingNavigationTracking();
                    shouldUpdateTitles = true;
                    if (_viewModel.NavigationTracker.PendingSelectedItems != null)
                    {
                        _explorerService.SelectItems(_viewModel.ExplorerHwnd, _viewModel.NavigationTracker.PendingSelectedItems);
                        _viewModel.NavigationTracker.PendingSelectedItems = null;
                    }
                    return;
                }

                // ナビゲート先がまだ反映されていない → 待機（タイムアウト付き）
                if (_viewModel.NavigationTracker.NavigatingToPath != null)
                {
                    if ((DateTime.UtcNow - _viewModel.NavigationTracker.NavigateStartTime).TotalSeconds > 5)
                    {
                        // 5秒以上経過してもナビゲーションが完了しない場合は
                        // ナビゲーション失敗とみなし、状態を元のタブへ戻す
                        _viewModel.CancelPendingNavigation();
                        return;
                    }
                    else
                    {
                        return;
                    }
                }

                if (_viewModel.IsCancelledNavigationMatch(currentPath))
                {
                    ViewModels.TabItemViewModel cancelledTab = _viewModel.FindTabByPath(currentPath);
                    if (cancelledTab != null)
                    {
                        if (cancelledTab != _viewModel.ActiveTab)
                        {
                            _viewModel.SetActiveTabOnly(cancelledTab);
                        }
                        cancelledTab.Path = currentPath;
                        cancelledTab.Title = _explorerService.GetFolderName(currentPath);
                        shouldUpdateTitles = true;
                        _viewModel.ClearCancelledNavigationTracking();
                        return;
                    }

                    _viewModel.ClearCancelledNavigationTracking();
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

        private string GetCurrentPathForSync(bool forcePoll)
        {
            DateTime nowUtc = DateTime.UtcNow;

            if (!forcePoll)
            {
                if (!_viewModel.NavigationTracker.ShouldPoll(nowUtc, false))
                {
                    return _viewModel.NavigationTracker.CachedExplorerPath;
                }
            }

            string currentPath = _explorerService.GetCurrentPath(_viewModel.ExplorerHwnd);
            _viewModel.NavigationTracker.UpdateCache(currentPath, nowUtc);
            return currentPath;
        }
    }
}
