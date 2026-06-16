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
        private readonly Action<IntPtr, NativeMethods.RECT> _moveExplorerWindow;
        private readonly Action<IntPtr> _postCloseWindow;
        private readonly Func<TabBarViewModel, IntPtr, bool> _rebindExplorerWindow;
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
                  MoveExplorerWindowCore,
                  DefaultRebindExplorerWindow,
                  delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); },
                  delegate { return new TabBarWindow(); },
                  delegate (TabBarWindow window) { window.Show(); })
        {
        }

        internal ExplorerWindowInteractionService(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            TabPersistenceService tabPersistence,
            Func<TabBarViewModel, IntPtr, bool> rebindExplorerWindow)
            : this(
                  explorerService,
                  windowTracking,
                  tabPersistence,
                  GetWindowTitleCore,
                  delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); },
                  NativeMethods.ForceSetForegroundWindow,
                  MoveExplorerWindowCore,
                  rebindExplorerWindow ?? DefaultRebindExplorerWindow,
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
            Action<IntPtr, NativeMethods.RECT> moveExplorerWindow,
            Func<TabBarViewModel, IntPtr, bool> rebindExplorerWindow,
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
            _moveExplorerWindow = moveExplorerWindow;
            _rebindExplorerWindow = rebindExplorerWindow;
            _postCloseWindow = postCloseWindow;
            _createTabBarWindow = createTabBarWindow;
            _showTabBarWindow = showTabBarWindow;
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
            : this(
                  explorerService,
                  windowTracking,
                  tabPersistence,
                  getWindowTitle,
                  showExplorerWindow,
                  forceSetForegroundWindow,
                  MoveExplorerWindowCore,
                  DefaultRebindExplorerWindow,
                  postCloseWindow,
                  createTabBarWindow,
                  showTabBarWindow)
        {
        }

        public void CreateNewTabBar(
            IntPtr hwnd,
            IUserSettings userSettings,
            Action<IntPtr, TabBarWindow> registerTabBar,
            string initialPath,
            bool useInitialPathOnly)
        {
            if (_windowTracking.HiddenPendingAbsorb.Remove(hwnd))
            {
                _showExplorerWindow(hwnd);
            }

            TabBarViewModel viewModel = new TabBarViewModel(hwnd, userSettings, _explorerService);
            InitializeTabsForNewWindow(viewModel, initialPath, useInitialPathOnly);

            TabBarWindow tabBarWindow = _createTabBarWindow();
            tabBarWindow.ExplorerService = _explorerService;
            tabBarWindow.WindowTrackingState = _windowTracking;
            tabBarWindow.DataContext = viewModel;
            _showTabBarWindow(tabBarWindow);

            if (registerTabBar != null)
            {
                registerTabBar(hwnd, tabBarWindow);
            }
        }

        internal void InitializeTabsForNewWindow(TabBarViewModel viewModel, string initialPath, bool useInitialPathOnly)
        {
            if (viewModel == null)
            {
                return;
            }

            bool loadedSavedTabs = _tabPersistence.LoadTabsTo(viewModel);

            if (string.IsNullOrEmpty(initialPath))
            {
                return;
            }

            bool allowSpecialPath = _explorerService.IsControlPanelPath(initialPath);

            TabItemViewModel targetTab = viewModel.FindTabByPath(initialPath);
            if (targetTab != null)
            {
                viewModel.SelectTab(targetTab);
                return;
            }

            if (loadedSavedTabs || useInitialPathOnly)
            {
                viewModel.InsertTabWithPath(initialPath, viewModel.Tabs.Count, allowSpecialPath);
                return;
            }

            viewModel.RestoreTabs(new string[] { initialPath });
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
            bool isControlPanelPath,
            Action<IntPtr> ignoreExplorerWindow)
        {
            string normalizedPath = _explorerService.NormalizeKnownPath(path);
            string targetPath = string.IsNullOrEmpty(normalizedPath) ? path : normalizedPath;
            bool effectiveControlPanelPath = isControlPanelPath || _explorerService.IsControlPanelPath(targetPath);
            bool hasReusableControlPanelTab = effectiveControlPanelPath && FindAnyControlPanelTab(targetViewModel) != null;
            bool effectiveAllowSpecialPath = allowSpecialPath || hasReusableControlPanelTab;

            if (!effectiveAllowSpecialPath && !IsPathTabCompatible(targetPath))
            {
                if (ignoreExplorerWindow != null)
                {
                    ignoreExplorerWindow(newExplorerHwnd);
                }
                return false;
            }

            if (effectiveAllowSpecialPath &&
                effectiveControlPanelPath &&
                TryRebindControlPanelTab(newExplorerHwnd, targetViewModel, targetPath))
            {
                return true;
            }

            List<string> selectedItems = _explorerService.GetSelectedItems(newExplorerHwnd);
            int insertIndex = targetViewModel.Tabs.Count;
            targetViewModel.InsertTabWithPathAndSelect(targetPath, insertIndex, selectedItems, effectiveAllowSpecialPath);

            FinalizeAbsorbedWindow(newExplorerHwnd, targetViewModel.ExplorerHwnd);

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

        private bool TryRebindControlPanelTab(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel, string path)
        {
            if (targetViewModel == null || string.IsNullOrEmpty(path) || newExplorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            string title = _explorerService.GetFolderName(path);
            string baseTitle = string.IsNullOrEmpty(title) ? _explorerService.GetLocalizedHomeTitle() : title;
            TabItemViewModel reusableTab = new TabItemViewModel(path, baseTitle, _explorerService);
            targetViewModel.Tabs.Add(reusableTab);

            IntPtr previousExplorerHwnd = targetViewModel.ExplorerHwnd;
            NativeMethods.RECT previousExplorerRect = _explorerService.GetExplorerWindowRect(previousExplorerHwnd);
            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(newExplorerHwnd))
            {
                _windowTracking.RestoreHiddenWindow(newExplorerHwnd);
                _showExplorerWindow(newExplorerHwnd);
            }

            AlignExplorerWindowToPreviousRect(newExplorerHwnd, previousExplorerRect);

            if (_rebindExplorerWindow == null || !_rebindExplorerWindow(targetViewModel, newExplorerHwnd))
            {
                targetViewModel.Tabs.Remove(reusableTab);
                return false;
            }

            targetViewModel.SelectTab(reusableTab);
            targetViewModel.UpdateTabTitles();

            _forceSetForegroundWindow(newExplorerHwnd);

            if (previousExplorerHwnd != IntPtr.Zero && previousExplorerHwnd != newExplorerHwnd)
            {
                _windowTracking.IgnoreWindow(previousExplorerHwnd);
                _postCloseWindow(previousExplorerHwnd);
            }

            return true;
        }

        private TabItemViewModel FindAnyControlPanelTab(TabBarViewModel targetViewModel)
        {
            if (targetViewModel == null)
            {
                return null;
            }

            for (int i = 0; i < targetViewModel.Tabs.Count; i++)
            {
                TabItemViewModel tab = targetViewModel.Tabs[i];
                if (tab == null || string.IsNullOrEmpty(tab.Path))
                {
                    continue;
                }

                if (_explorerService.IsControlPanelPath(tab.Path))
                {
                    return tab;
                }
            }

            return null;
        }

        private void FinalizeAbsorbedWindow(IntPtr newExplorerHwnd, IntPtr targetExplorerHwnd)
        {
            _forceSetForegroundWindow(targetExplorerHwnd);
            _windowTracking.MarkAbsorbedWindow(newExplorerHwnd);
            _postCloseWindow(newExplorerHwnd);
        }

        private static bool DefaultRebindExplorerWindow(TabBarViewModel viewModel, IntPtr newExplorerHwnd)
        {
            if (viewModel == null || newExplorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            viewModel.SetExplorerHwnd(newExplorerHwnd);
            return true;
        }

        private void AlignExplorerWindowToPreviousRect(IntPtr explorerHwnd, NativeMethods.RECT previousExplorerRect)
        {
            if (_moveExplorerWindow == null || explorerHwnd == IntPtr.Zero)
            {
                return;
            }

            if (previousExplorerRect.Width <= 0 || previousExplorerRect.Height <= 0)
            {
                return;
            }

            _moveExplorerWindow(explorerHwnd, previousExplorerRect);
        }

        private static void MoveExplorerWindowCore(IntPtr explorerHwnd, NativeMethods.RECT rect)
        {
            if (explorerHwnd == IntPtr.Zero || rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            NativeMethods.MoveWindow(explorerHwnd, rect.Left, rect.Top, rect.Width, rect.Height, false);
        }

        private static string GetWindowTitleCore(IntPtr hwnd)
        {
            StringBuilder titleBuilder = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            return titleBuilder.ToString();
        }
    }
}
