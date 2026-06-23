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
        private readonly Func<IExplorerService, ExplorerWindowTrackingState, Func<TabBarViewModel, IntPtr, bool>, Action<IntPtr>, Action<IntPtr, NativeMethods.RECT>, Action<IntPtr>, ExplorerHostSwitchCoordinator> _createHostSwitchCoordinator;

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
                  delegate (TabBarWindow window) { window.Show(); },
                  CreateHostSwitchCoordinator)
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
                  delegate (TabBarWindow window) { window.Show(); },
                  CreateHostSwitchCoordinator)
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
            Action<TabBarWindow> showTabBarWindow,
            Func<IExplorerService, ExplorerWindowTrackingState, Func<TabBarViewModel, IntPtr, bool>, Action<IntPtr>, Action<IntPtr, NativeMethods.RECT>, Action<IntPtr>, ExplorerHostSwitchCoordinator> createHostSwitchCoordinator)
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
            _createHostSwitchCoordinator = createHostSwitchCoordinator ?? CreateHostSwitchCoordinator;
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
                  showTabBarWindow,
                  CreateHostSwitchCoordinator)
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
            : this(
                  explorerService,
                  windowTracking,
                  tabPersistence,
                  getWindowTitle,
                  showExplorerWindow,
                  forceSetForegroundWindow,
                  moveExplorerWindow,
                  rebindExplorerWindow,
                  postCloseWindow,
                  createTabBarWindow,
                  showTabBarWindow,
                  CreateHostSwitchCoordinator)
        {
        }

        public void CreateNewTabBar(
            IntPtr hwnd,
            IUserSettings userSettings,
            Action<IntPtr, TabBarWindow> registerTabBar,
            string initialPath,
            bool useInitialPathOnly)
        {
            RestorePreparedExplorerWindowForCreate(hwnd);

            TabBarViewModel viewModel = new TabBarViewModel(hwnd, userSettings, _explorerService);
            InitializeTabsForNewWindow(viewModel, initialPath, useInitialPathOnly);

            TabBarWindow tabBarWindow = _createTabBarWindow();
            tabBarWindow.ExplorerService = _explorerService;
            tabBarWindow.PersistTabState = delegate (TabBarViewModel currentViewModel)
            {
                if (_tabPersistence != null && currentViewModel != null)
                {
                    _tabPersistence.SaveTabsIfChanged(currentViewModel, true);
                }
            };
            tabBarWindow.WindowTrackingState = _windowTracking;
            tabBarWindow.ExplorerHostSwitchCoordinator = _createHostSwitchCoordinator(
                _explorerService,
                _windowTracking,
                _rebindExplorerWindow,
                _showExplorerWindow,
                _moveExplorerWindow,
                _postCloseWindow);
            tabBarWindow.DataContext = viewModel;
            _showTabBarWindow(tabBarWindow);
            RestorePersistedSpecialActiveTabHost(tabBarWindow, viewModel);

            if (registerTabBar != null)
            {
                registerTabBar(viewModel.ExplorerHwnd, tabBarWindow);
            }
        }

        private void RestorePersistedSpecialActiveTabHost(TabBarWindow tabBarWindow, TabBarViewModel viewModel)
        {
            if (tabBarWindow == null || viewModel == null || _explorerService == null)
            {
                return;
            }

            TabItemViewModel activeTab = viewModel.ActiveTab;
            if (activeTab == null || string.IsNullOrEmpty(activeTab.Path))
            {
                return;
            }

            if (!_explorerService.IsControlPanelPath(activeTab.Path))
            {
                return;
            }

            ExplorerHostSwitchCoordinator coordinator = tabBarWindow.ExplorerHostSwitchCoordinator;
            if (coordinator == null)
            {
                return;
            }

            if (System.Threading.SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext)
            {
                RestorePersistedSpecialActiveTabHostAsync(coordinator, viewModel, activeTab);
            }
            else
            {
                if (!coordinator.PrepareForPath(viewModel, activeTab.Path))
                {
                    return;
                }

                TabBarWindow.ExecuteTabSelectionWithPendingReveal(
                    delegate { viewModel.SelectTab(activeTab); },
                    coordinator.CompletePendingReveal);
            }
        }

        private async void RestorePersistedSpecialActiveTabHostAsync(
            ExplorerHostSwitchCoordinator coordinator,
            TabBarViewModel viewModel,
            TabItemViewModel activeTab)
        {
            try
            {
                if (!await coordinator.PrepareForPathAsync(viewModel, activeTab.Path))
                {
                    return;
                }

                TabBarWindow.ExecuteTabSelectionWithPendingReveal(
                    delegate { viewModel.SelectTab(activeTab); },
                    coordinator.CompletePendingReveal);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExplorerWindowInteractionService", "Failed to restore persisted special active tab host asynchronously.", ex);
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

            if (loadedSavedTabs && IsHomeInitialPath(initialPath))
            {
                return;
            }

            bool allowSpecialPath = _explorerService.IsControlPanelPath(initialPath);

            TabItemViewModel targetTab = viewModel.FindTabByPath(initialPath);
            if (targetTab != null)
            {
                if (!loadedSavedTabs)
                {
                    viewModel.SelectTab(targetTab);
                }
                return;
            }

            if (loadedSavedTabs)
            {
                viewModel.InsertTabWithPath(initialPath, viewModel.Tabs.Count, allowSpecialPath);
                return;
            }

            if (useInitialPathOnly)
            {
                viewModel.InsertTabWithPath(initialPath, viewModel.Tabs.Count, allowSpecialPath);
                return;
            }

            viewModel.RestoreTabs(new string[] { initialPath });
        }

        private bool IsHomeInitialPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = _explorerService.NormalizeKnownPath(path);
            string normalizedHomePath = _explorerService.NormalizeKnownPath(_explorerService.HomeFolderPath);
            if (string.Equals(normalizedPath, normalizedHomePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string resolvedHomePath = _explorerService.GetResolvedHomeFolderPath();
            if (string.IsNullOrEmpty(resolvedHomePath))
            {
                return false;
            }

            return string.Equals(path.TrimEnd('\\'), resolvedHomePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedPath, resolvedHomePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private void RestorePreparedExplorerWindowForCreate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd))
            {
                _windowTracking.HiddenPendingAbsorb.Remove(hwnd);
                _windowTracking.HiddenOriginalRects.Remove(hwnd);
            }

            NativeMethods.RECT recentClosedRect;
            if (_windowTracking.TryTakeRecentClosedManagedExplorerRect(DateTime.UtcNow, out recentClosedRect))
            {
                _moveExplorerWindow(hwnd, recentClosedRect);
            }

            _showExplorerWindow(hwnd);
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
            Action<IntPtr> ignoreExplorerWindow,
            bool wasManagedControlPanelLaunchSource = false)
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
                TryRebindControlPanelTab(newExplorerHwnd, targetViewModel, targetPath, wasManagedControlPanelLaunchSource))
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

        private bool TryRebindControlPanelTab(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel, string path, bool wasManagedControlPanelLaunchSource = false)
        {
            if (targetViewModel == null || string.IsNullOrEmpty(path) || newExplorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            TabItemViewModel reusableTab = null;
            if (wasManagedControlPanelLaunchSource)
            {
                reusableTab = FindEquivalentControlPanelTab(targetViewModel, path);
                if (reusableTab == null)
                {
                    if (targetViewModel.ActiveTab != null && _explorerService.IsControlPanelPath(targetViewModel.ActiveTab.Path))
                    {
                        reusableTab = targetViewModel.ActiveTab;
                    }
                }
            }

            bool createdReusableTab = false;
            if (reusableTab == null)
            {
                string title = _explorerService.GetFolderName(path);
                string baseTitle = string.IsNullOrEmpty(title) ? _explorerService.GetLocalizedHomeTitle() : title;
                reusableTab = new TabItemViewModel(path, baseTitle, _explorerService);
                targetViewModel.Tabs.Add(reusableTab);
                createdReusableTab = true;
            }

            IntPtr previousExplorerHwnd = targetViewModel.ExplorerHwnd;
            NativeMethods.RECT previousExplorerRect = GetWindowBoundsForMove(previousExplorerHwnd);
            bool hadHiddenPending = _windowTracking.HiddenPendingAbsorb.ContainsKey(newExplorerHwnd);
            NativeMethods.RECT hiddenOriginalRect = default(NativeMethods.RECT);
            bool hadHiddenOriginalRect = _windowTracking.HiddenOriginalRects.TryGetValue(newExplorerHwnd, out hiddenOriginalRect);
            AppLogger.LogInfo(
                "ExplorerWindowInteractionService",
                string.Format(
                    "TryRebindControlPanelTab start newExplorer={0} previousExplorer={1} path={2} hadHiddenPending={3} hadHiddenOriginalRect={4} activeBefore={5}",
                    newExplorerHwnd,
                    previousExplorerHwnd,
                    path ?? string.Empty,
                    hadHiddenPending,
                    hadHiddenOriginalRect,
                    targetViewModel.ActiveTab != null ? targetViewModel.ActiveTab.Path ?? string.Empty : string.Empty));
            if (hadHiddenPending)
            {
                NativeMethods.ShowWindow(newExplorerHwnd, NativeMethods.SW_HIDE);
                _windowTracking.HiddenPendingAbsorb.Remove(newExplorerHwnd);
                _windowTracking.HiddenOriginalRects.Remove(newExplorerHwnd);
            }

            AlignExplorerWindowToPreviousRect(newExplorerHwnd, previousExplorerRect);

            if (_rebindExplorerWindow == null || !_rebindExplorerWindow(targetViewModel, newExplorerHwnd))
            {
                RestorePreparedExplorerWindow(newExplorerHwnd, hadHiddenPending, hadHiddenOriginalRect, hiddenOriginalRect);
                if (createdReusableTab)
                {
                    targetViewModel.Tabs.Remove(reusableTab);
                }
                return false;
            }

            targetViewModel.SelectTab(reusableTab);
            targetViewModel.UpdateTabTitles();

            _forceSetForegroundWindow(newExplorerHwnd);

            if (previousExplorerHwnd != IntPtr.Zero && previousExplorerHwnd != newExplorerHwnd)
            {
                _windowTracking.RememberParkedExplorerOrigin(newExplorerHwnd, previousExplorerHwnd);
                NativeMethods.ShowWindow(previousExplorerHwnd, NativeMethods.SW_HIDE);
                AppLogger.LogInfo(
                    "ExplorerWindowInteractionService",
                    string.Format(
                        "TryRebindControlPanelTab parkedPreviousHost previous={0} controlPanel={1} path={2}",
                        previousExplorerHwnd,
                        newExplorerHwnd,
                        path ?? string.Empty));
            }

            if (hadHiddenPending)
            {
                _showExplorerWindow(newExplorerHwnd);
            }

            AppLogger.LogInfo(
                "ExplorerWindowInteractionService",
                string.Format(
                    "TryRebindControlPanelTab complete newExplorer={0} previousExplorer={1} currentExplorer={2} activeAfter={3} showedNew={4}",
                    newExplorerHwnd,
                    previousExplorerHwnd,
                    targetViewModel.ExplorerHwnd,
                    targetViewModel.ActiveTab != null ? targetViewModel.ActiveTab.Path ?? string.Empty : string.Empty,
                    hadHiddenPending));

            return true;
        }

        private TabItemViewModel FindEquivalentControlPanelTab(TabBarViewModel targetViewModel, string path)
        {
            if (targetViewModel == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            string trimmedPath = path.TrimEnd('\\');
            for (int i = 0; i < targetViewModel.Tabs.Count; i++)
            {
                TabItemViewModel tab = targetViewModel.Tabs[i];
                if (tab == null || string.IsNullOrEmpty(tab.Path))
                {
                    continue;
                }

                if (!_explorerService.IsControlPanelPath(tab.Path))
                {
                    continue;
                }

                if (string.Equals(tab.Path.TrimEnd('\\'), trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return tab;
                }

                string normalizedTabPath = _explorerService.NormalizeShellNamespacePath(tab.Path);
                if (!string.IsNullOrEmpty(normalizedPath) &&
                    !string.IsNullOrEmpty(normalizedTabPath) &&
                    string.Equals(normalizedTabPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return tab;
                }
            }

            return null;
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

        private NativeMethods.RECT GetWindowBoundsForMove(IntPtr explorerHwnd)
        {
            NativeMethods.RECT rect;
            if (explorerHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(explorerHwnd, out rect))
            {
                return rect;
            }

            return _explorerService.GetExplorerWindowRect(explorerHwnd);
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

        private static void RestorePreparedExplorerWindow(
            IntPtr explorerHwnd,
            bool hadHiddenPending,
            bool hadHiddenOriginalRect,
            NativeMethods.RECT hiddenOriginalRect)
        {
            if (explorerHwnd == IntPtr.Zero || !hadHiddenPending || !NativeMethods.IsWindow(explorerHwnd))
            {
                return;
            }

            if (hadHiddenOriginalRect && hiddenOriginalRect.Width > 0 && hiddenOriginalRect.Height > 0)
            {
                NativeMethods.SetWindowPos(
                    explorerHwnd,
                    IntPtr.Zero,
                    hiddenOriginalRect.Left,
                    hiddenOriginalRect.Top,
                    0,
                    0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
            }

            NativeMethods.ShowWindow(explorerHwnd, NativeMethods.SW_SHOW);
        }

        private static ExplorerHostSwitchCoordinator CreateHostSwitchCoordinator(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            Func<TabBarViewModel, IntPtr, bool> rebindExplorerWindow,
            Action<IntPtr> showExplorerWindow,
            Action<IntPtr, NativeMethods.RECT> moveExplorerWindow,
            Action<IntPtr> postCloseWindow)
        {
            return new ExplorerHostSwitchCoordinator(
                explorerService,
                windowTracking,
                rebindExplorerWindow,
                showExplorerWindow,
                moveExplorerWindow,
                postCloseWindow);
        }
}

}


