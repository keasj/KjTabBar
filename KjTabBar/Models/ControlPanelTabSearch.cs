using System;
using System.Collections.Generic;
using KjTabBar.ViewModels;

namespace KjTabBar.Models
{
    internal sealed class ControlPanelTabSearch
    {
        private readonly IExplorerService _explorerService;

        public ControlPanelTabSearch(IExplorerService explorerService)
        {
            _explorerService = explorerService;
        }

        public TabBarViewModel FindTarget(
            List<TabBarViewModel> candidates,
            string path,
            Func<IntPtr, bool> isForegroundRelatedWindow,
            Func<IntPtr, bool> wasForegroundRelatedWindow)
        {
            if (string.IsNullOrEmpty(path) || candidates == null)
            {
                return null;
            }

            bool isControlPanelRootPath = _explorerService.IsControlPanelRootPath(path);

            TabBarViewModel activeControlPanelTarget = null;
            TabBarViewModel firstControlPanelTarget = null;
            TabBarViewModel foregroundControlPanelHost = null;
            TabBarViewModel previousForegroundControlPanelHost = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                TabBarViewModel viewModel = candidates[i];
                bool hasAnyControlPanelTab = HasAnyControlPanelTab(viewModel);
                bool isForegroundRelated =
                    isForegroundRelatedWindow != null &&
                    isForegroundRelatedWindow(viewModel.ExplorerHwnd);
                bool wasForegroundRelated =
                    wasForegroundRelatedWindow != null &&
                    wasForegroundRelatedWindow(viewModel.ExplorerHwnd);

                if (!isControlPanelRootPath &&
                    foregroundControlPanelHost == null &&
                    hasAnyControlPanelTab &&
                    isForegroundRelated)
                {
                    foregroundControlPanelHost = viewModel;
                }

                if (!isControlPanelRootPath &&
                    previousForegroundControlPanelHost == null &&
                    hasAnyControlPanelTab &&
                    wasForegroundRelated)
                {
                    previousForegroundControlPanelHost = viewModel;
                }

                if (!HasEquivalentControlPanelTab(viewModel, path))
                {
                    continue;
                }

                if (isControlPanelRootPath)
                {
                    continue;
                }

                if (isForegroundRelated)
                {
                    return viewModel;
                }

                if (activeControlPanelTarget == null)
                {
                    TabItemViewModel activeTab = viewModel.ActiveTab;
                    if (activeTab != null && _explorerService.IsControlPanelPath(activeTab.Path))
                    {
                        activeControlPanelTarget = viewModel;
                    }
                }

                if (firstControlPanelTarget == null)
                {
                    firstControlPanelTarget = viewModel;
                }
            }

            if (isControlPanelRootPath)
            {
                return null;
            }

            if (activeControlPanelTarget != null)
            {
                return activeControlPanelTarget;
            }

            if (foregroundControlPanelHost != null)
            {
                return foregroundControlPanelHost;
            }

            if (previousForegroundControlPanelHost != null)
            {
                return previousForegroundControlPanelHost;
            }

            return firstControlPanelTarget;
        }

        public bool HasAnyControlPanelTab(TabBarViewModel viewModel)
        {
            if (viewModel == null)
            {
                return false;
            }

            for (int i = 0; i < viewModel.Tabs.Count; i++)
            {
                string tabPath = viewModel.Tabs[i].Path;
                if (string.IsNullOrEmpty(tabPath))
                {
                    continue;
                }

                if (_explorerService.IsControlPanelPath(tabPath))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasEquivalentControlPanelTab(TabBarViewModel viewModel, string path)
        {
            if (viewModel == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            string trimmedPath = path.TrimEnd('\\');
            for (int i = 0; i < viewModel.Tabs.Count; i++)
            {
                string tabPath = viewModel.Tabs[i].Path;
                if (string.IsNullOrEmpty(tabPath))
                {
                    continue;
                }
                if (!_explorerService.IsControlPanelPath(tabPath))
                {
                    continue;
                }

                if (tabPath.TrimEnd('\\').Equals(trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string normalizedTabPath = _explorerService.NormalizeShellNamespacePath(tabPath);
                if (!string.IsNullOrEmpty(normalizedPath) &&
                    !string.IsNullOrEmpty(normalizedTabPath) &&
                    normalizedTabPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasActiveControlPanelTab(TabBarViewModel viewModel)
        {
            if (viewModel == null)
            {
                return false;
            }

            TabItemViewModel activeTab = viewModel.ActiveTab;
            if (activeTab == null || string.IsNullOrEmpty(activeTab.Path))
            {
                return false;
            }

            return _explorerService.IsControlPanelPath(activeTab.Path);
        }
    }
}
