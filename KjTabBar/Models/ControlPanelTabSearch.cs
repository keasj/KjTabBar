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

        public TabBarViewModel FindTarget(List<TabBarViewModel> candidates, string path, Func<IntPtr, bool> isForegroundRelatedWindow)
        {
            if (string.IsNullOrEmpty(path) || candidates == null)
            {
                return null;
            }

            TabBarViewModel activeControlPanelTarget = null;
            TabBarViewModel firstControlPanelTarget = null;
            TabBarViewModel fallbackControlPanelHost = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                TabBarViewModel viewModel = candidates[i];
                if (fallbackControlPanelHost == null && HasAnyControlPanelTab(viewModel))
                {
                    fallbackControlPanelHost = viewModel;
                }

                if (!HasEquivalentControlPanelTab(viewModel, path))
                {
                    continue;
                }

                if (isForegroundRelatedWindow != null && isForegroundRelatedWindow(viewModel.ExplorerHwnd))
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

            if (activeControlPanelTarget != null)
            {
                return activeControlPanelTarget;
            }

            if (fallbackControlPanelHost != null)
            {
                return fallbackControlPanelHost;
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