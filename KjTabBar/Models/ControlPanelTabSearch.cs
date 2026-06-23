using System;
using System.Collections.Generic;
using System.Text;
using KjTabBar.Helpers;
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
            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            bool isPowerOptionsPath =
                !string.IsNullOrEmpty(normalizedPath) &&
                normalizedPath.Equals(_explorerService.PowerOptionsPath, StringComparison.OrdinalIgnoreCase);
            StringBuilder debugBuilder = null;
            if (_explorerService.IsControlPanelPath(path))
            {
                debugBuilder = new StringBuilder();
                debugBuilder.AppendFormat(
                    "CP search path={0} normalized={1} candidates={2} isRoot={3} isPowerOptions={4}",
                    path ?? string.Empty,
                    normalizedPath ?? string.Empty,
                    candidates.Count,
                    isControlPanelRootPath,
                    isPowerOptionsPath);
            }

            TabBarViewModel soleControlPanelHost = null;
            TabBarViewModel foregroundControlPanelHost = null;
            TabBarViewModel previousForegroundControlPanelHost = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                TabBarViewModel viewModel = candidates[i];
                bool hasAnyControlPanelTab = HasAnyControlPanelTab(viewModel);
                bool hasEquivalentControlPanelTab = HasEquivalentControlPanelTab(viewModel, path);
                bool isForegroundRelated =
                    isForegroundRelatedWindow != null &&
                    isForegroundRelatedWindow(viewModel.ExplorerHwnd);
                bool wasForegroundRelated =
                    wasForegroundRelatedWindow != null &&
                    wasForegroundRelatedWindow(viewModel.ExplorerHwnd);
                if (debugBuilder != null)
                {
                    debugBuilder.AppendFormat(
                        " | hwnd={0} hasAnyCp={1} hasEquivalent={2} isFg={3} wasFg={4} active={5}",
                        viewModel.ExplorerHwnd,
                        hasAnyControlPanelTab,
                        hasEquivalentControlPanelTab,
                        isForegroundRelated,
                        wasForegroundRelated,
                        HasActiveControlPanelTab(viewModel));
                }

                if (!isControlPanelRootPath &&
                    isPowerOptionsPath &&
                    hasAnyControlPanelTab &&
                    soleControlPanelHost == null &&
                    candidates.Count == 1)
                {
                    soleControlPanelHost = viewModel;
                }

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

                if (!hasEquivalentControlPanelTab)
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
            }

            if (isControlPanelRootPath)
            {
                if (debugBuilder != null)
                {
                    AppLogger.LogInfo("ControlPanelTabSearch", debugBuilder.ToString() + " => result=<null root>");
                }
                return null;
            }

            if (foregroundControlPanelHost != null)
            {
                if (debugBuilder != null)
                {
                    AppLogger.LogInfo("ControlPanelTabSearch", debugBuilder.ToString() + string.Format(" => result={0} reason=foregroundHost", foregroundControlPanelHost.ExplorerHwnd));
                }
                return foregroundControlPanelHost;
            }

            if (previousForegroundControlPanelHost != null)
            {
                if (debugBuilder != null)
                {
                    AppLogger.LogInfo("ControlPanelTabSearch", debugBuilder.ToString() + string.Format(" => result={0} reason=previousForegroundHost", previousForegroundControlPanelHost.ExplorerHwnd));
                }
                return previousForegroundControlPanelHost;
            }

            if (soleControlPanelHost != null)
            {
                if (debugBuilder != null)
                {
                    AppLogger.LogInfo("ControlPanelTabSearch", debugBuilder.ToString() + string.Format(" => result={0} reason=soleControlPanelHost", soleControlPanelHost.ExplorerHwnd));
                }
                return soleControlPanelHost;
            }

            if (debugBuilder != null)
            {
                AppLogger.LogInfo("ControlPanelTabSearch", debugBuilder.ToString() + " => result=<null>");
            }

            return null;
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
