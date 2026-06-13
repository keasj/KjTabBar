using System;
using System.Collections.Generic;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class ExplorerTabTargetResolver
    {
        private readonly TabBarRegistry _tabBars;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly ControlPanelTabSearch _controlPanelTabSearch;

        public ExplorerTabTargetResolver(
            TabBarRegistry tabBars,
            ExplorerWindowTrackingState windowTracking,
            ControlPanelTabSearch controlPanelTabSearch)
        {
            _tabBars = tabBars;
            _windowTracking = windowTracking;
            _controlPanelTabSearch = controlPanelTabSearch;
        }

        public TabBarViewModel FindValidTabBarTarget(Func<IntPtr, bool> isForegroundRelatedWindow)
        {
            return _tabBars.FindValidTarget(isForegroundRelatedWindow);
        }

        public bool IsManagedControlPanelLaunchSource(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            TabBarViewModel viewModel;
            if (!_tabBars.TryFindAliveViewModel(explorerHwnd, out viewModel))
            {
                return false;
            }

            return HasActiveControlPanelTab(viewModel);
        }

        public TabBarViewModel FindControlPanelTabBarTarget(
            List<TabBarViewModel> candidates,
            string path,
            Func<IntPtr, bool> isForegroundRelatedWindow,
            Func<IntPtr, bool> wasForegroundRelatedWindow)
        {
            return _controlPanelTabSearch.FindTarget(candidates, path, isForegroundRelatedWindow, wasForegroundRelatedWindow);
        }

        public List<TabBarViewModel> GetAliveTabBarViewModels()
        {
            return _tabBars.GetAliveViewModels();
        }

        public bool HasEquivalentControlPanelTab(TabBarViewModel targetViewModel, string path)
        {
            return _controlPanelTabSearch.HasEquivalentControlPanelTab(targetViewModel, path);
        }

        public bool HasActiveControlPanelTab(TabBarViewModel targetViewModel)
        {
            return _controlPanelTabSearch.HasActiveControlPanelTab(targetViewModel);
        }

        public void IgnoreExplorerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _windowTracking.IgnoreWindow(hwnd);
            _windowTracking.RestoreHiddenWindow(hwnd);
        }
    }
}
