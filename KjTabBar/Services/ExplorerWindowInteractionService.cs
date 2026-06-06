using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar.Services
{
    internal sealed class ExplorerWindowInteractionService
    {
        private readonly IExplorerService _explorerService;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly TabPersistenceService _tabPersistence;
        private readonly Func<IntPtr, string> _getWindowTitle;
        private readonly Action<IntPtr> _showExplorerWindow;
        private readonly Action<IntPtr> _forceSetForegroundWindow;
        private readonly Action<IntPtr> _postCloseWindow;
        private readonly Func<TabBarWindow> _createTabBarWindow;
        private readonly Action<TabBarWindow> _showTabBarWindow;

        public ExplorerWindowInteractionService(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            TabPersistenceService tabPersistence)
            : this(
                  explorerService,
                  windowTracking,
                  tabPersistence,
                  GetWindowTitleCore,
                  delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); },
                  NativeMethods.ForceSetForegroundWindow,
                  delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); },
                  delegate { return new TabBarWindow(); },
                  delegate (TabBarWindow window) { window.Show(); })
        {
        }

        internal ExplorerWindowInteractionService(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            TabPersistenceService tabPersistence,
            Func<IntPtr, string> getWindowTitle,
            Action<IntPtr> showExplorerWindow,
            Action<IntPtr> forceSetForegroundWindow,
            Action<IntPtr> postCloseWindow,
            Func<TabBarWindow> createTabBarWindow,
            Action<TabBarWindow> showTabBarWindow)
        {
            _explorerService = explorerService;
            _windowTracking = windowTracking;
            _tabPersistence = tabPersistence;
            _getWindowTitle = getWindowTitle;
            _showExplorerWindow = showExplorerWindow;
            _forceSetForegroundWindow = forceSetForegroundWindow;
            _postCloseWindow = postCloseWindow;
            _createTabBarWindow = createTabBarWindow;
            _showTabBarWindow = showTabBarWindow;
        }

        public void CreateNewTabBar(IntPtr hwnd, IUserSettings userSettings, Action<IntPtr, TabBarWindow> registerTabBar)
        {
            if (_windowTracking.HiddenPendingAbsorb.Remove(hwnd))
            {
                _showExplorerWindow(hwnd);
            }

            TabBarViewModel viewModel = new TabBarViewModel(hwnd, userSettings, _explorerService);
            _tabPersistence.LoadTabsTo(viewModel);

            TabBarWindow tabBarWindow = _createTabBarWindow();
            tabBarWindow.ExplorerService = _explorerService;
            tabBarWindow.DataContext = viewModel;
            _showTabBarWindow(tabBarWindow);

            if (registerTabBar != null)
            {
                registerTabBar(hwnd, tabBarWindow);
            }
        }

        public string GetDesktopVirtualPathFromWindowTitle(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return null;
            }

            string title = _getWindowTitle(explorerHwnd);
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            return _explorerService.MapLocationNameToKnownShellPath(title);
        }

        public bool AbsorbExplorerWindow(
            IntPtr newExplorerHwnd,
            TabBarViewModel targetViewModel,
            string path,
            bool allowSpecialPath,
            Action<IntPtr> ignoreExplorerWindow)
        {
            if (!allowSpecialPath && !IsPathTabCompatible(path))
            {
                if (ignoreExplorerWindow != null)
                {
                    ignoreExplorerWindow(newExplorerHwnd);
                }
                return false;
            }

            List<string> selectedItems = _explorerService.GetSelectedItems(newExplorerHwnd);
            int insertIndex = targetViewModel.Tabs.Count;
            targetViewModel.InsertTabWithPathAndSelect(path, insertIndex, selectedItems, allowSpecialPath);

            _forceSetForegroundWindow(targetViewModel.ExplorerHwnd);
            _windowTracking.MarkAbsorbedWindow(newExplorerHwnd);
            _postCloseWindow(newExplorerHwnd);

            return true;
        }

        public void RestoreHiddenWindow(IntPtr hwnd)
        {
            _windowTracking.RestoreHiddenWindow(hwnd);
        }

        private bool IsPathTabCompatible(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (_explorerService.IsControlPanelPath(path))
            {
                return false;
            }

            return true;
        }

        private static string GetWindowTitleCore(IntPtr hwnd)
        {
            StringBuilder titleBuilder = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            return titleBuilder.ToString();
        }
    }
}
