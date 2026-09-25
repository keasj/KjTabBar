using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Threading.Tasks;
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
        private readonly Action<IntPtr> _hideExplorerWindow;
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
                  ShowExplorerWindowCore,
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
                  ShowExplorerWindowCore,
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
            Func<IExplorerService, ExplorerWindowTrackingState, Func<TabBarViewModel, IntPtr, bool>, Action<IntPtr>, Action<IntPtr, NativeMethods.RECT>, Action<IntPtr>, ExplorerHostSwitchCoordinator> createHostSwitchCoordinator, Action<IntPtr> hideExplorerWindow = null)
        {
            _explorerService = explorerService;
            _windowTracking = windowTracking;
            _tabPersistence = tabPersistence;
            _getWindowTitle = getWindowTitle;
            _showExplorerWindow = showExplorerWindow;
            _hideExplorerWindow = hideExplorerWindow ?? delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE); };
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
            bool useInitialPathOnly, bool reuseExistingTab = false)
        {
            try
            {
                System.Diagnostics.Stopwatch createTimer = AppLogger.StartDiagnosticTiming();
                TabBarViewModel viewModel = new TabBarViewModel(hwnd, userSettings, _explorerService, initialPath);
                AppLogger.LogDiagnosticTiming("Create.ViewModel", hwnd, createTimer);
                InitializeTabsForNewWindow(viewModel, initialPath, useInitialPathOnly, reuseExistingTab);
                AppLogger.LogDiagnosticTiming("Create.SavedTabsLoaded", hwnd, createTimer);
                if (createTimer != null) AppLogger.LogDiagnostic("RestoreDecision", string.Format(
                    "hwnd={0} restoringControlPanel={1} activeControlPanel={2} initialHomeLiteral={3} initialOnly={4}",
                    hwnd, viewModel.IsRestoringControlPanelHost,
                    viewModel.ActiveTab != null && _explorerService.IsControlPanelPath(viewModel.ActiveTab.Path),
                    string.Equals(initialPath, _explorerService.HomeFolderPath, StringComparison.OrdinalIgnoreCase), useInitialPathOnly));
                AppLogger.LogDiagnosticPlacement("Create.BeforePositionPrepared", hwnd);
                RestorePreparedExplorerWindowForCreate(hwnd, viewModel.IsRestoringControlPanelHost);
                AppLogger.LogDiagnosticPlacement("Create.AfterPositionPrepared", hwnd);
                AppLogger.LogDiagnosticTiming("Create.PositionPrepared", hwnd, createTimer);

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
                // Visibility is bound in XAML; assigning a visible DataContext can show
                // the window even without an explicit Show call.
                if (viewModel.IsRestoringControlPanelHost) viewModel.WindowVisibility = System.Windows.Visibility.Hidden;
                tabBarWindow.DataContext = viewModel;
                AppLogger.LogDiagnosticPlacement("Create.AfterDataContext", hwnd);
                // Register before asynchronous host preparation so rebinding can find
                // this window without showing the temporary Home tab bar first.
                if (viewModel.IsRestoringControlPanelHost)
                {
                    if (registerTabBar != null) registerTabBar(viewModel.ExplorerHwnd, tabBarWindow);
                    RestorePersistedSpecialActiveTabHost(tabBarWindow, viewModel);
                }
                else
                {
                    _showTabBarWindow(tabBarWindow);
                    AppLogger.LogDiagnosticTiming("Create.TabBarShown", hwnd, createTimer);
                    if (registerTabBar != null) registerTabBar(viewModel.ExplorerHwnd, tabBarWindow);
                }
            }
            catch
            {
                _windowTracking.RestoreHiddenWindow(hwnd);
                _showExplorerWindow(hwnd);
                throw;
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
                CompletePersistedHostRestoration(viewModel, viewModel.ExplorerHwnd);
                viewModel.WindowVisibility = System.Windows.Visibility.Visible;
                _showTabBarWindow(tabBarWindow);
                return;
            }

            Action showRestoredTabBar = delegate
            {
                if (NativeMethods.IsWindow(viewModel.ExplorerHwnd))
                {
                    viewModel.WindowVisibility = System.Windows.Visibility.Visible;
                    _showTabBarWindow(tabBarWindow);
                }
            };

            if (System.Threading.SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext)
            {
                _ = RestorePersistedSpecialActiveTabHostAsync(coordinator, viewModel, activeTab, showRestoredTabBar);
            }
            else
            {
                RestorePersistedSpecialActiveTabHostAsync(coordinator, viewModel, activeTab, showRestoredTabBar).GetAwaiter().GetResult();
            }
        }

        internal async System.Threading.Tasks.Task RestorePersistedSpecialActiveTabHostAsync(
            ExplorerHostSwitchCoordinator coordinator,
            TabBarViewModel viewModel,
            TabItemViewModel activeTab, Action showRestoredTabBar = null)
        {
            IntPtr originalHwnd = viewModel.ExplorerHwnd;
            bool hostReady = false;
            long restoreVersion = viewModel.SynchronizationVersion;
            string restorePath = activeTab.Path;
            Func<bool> isCurrent = () => !viewModel.IsDisposed && viewModel.IsRestoringControlPanelHost &&
                viewModel.SynchronizationVersion == restoreVersion && viewModel.ActiveTab == activeTab &&
                viewModel.Tabs.Contains(activeTab) && viewModel.PathEquals(activeTab.Path, restorePath);
            System.Diagnostics.Stopwatch restoreTimer = AppLogger.StartDiagnosticTiming();
            try
            {
                if (!await coordinator.PrepareForPathAsync(viewModel, restorePath, isCurrent))
                {
                    return;
                }

                if (viewModel.IsDisposed || !viewModel.Tabs.Contains(activeTab) || viewModel.ActiveTab != activeTab) return;
                hostReady = true;
                AppLogger.LogDiagnosticTiming("Restore.HostReady", originalHwnd, restoreTimer);
                try { await viewModel.SelectTabCoreAsync(activeTab); }
                finally { coordinator.CompletePendingReveal(); }
                AppLogger.LogDiagnosticTiming("Restore.Revealed", originalHwnd, restoreTimer);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExplorerWindowInteractionService", "Failed to restore persisted special active tab host asynchronously.", ex);
            }
            finally
            {
                if (!hostReady && isCurrent())
                {
                    string currentPath = null;
                    try
                    {
                        currentPath = await ComThreadService.Instance.InvokeAsync(
                            () => _explorerService.GetCurrentPath(viewModel.ExplorerHwnd));
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ExplorerWindowInteractionService", "Failed to read the restore source path.", ex);
                    }
                    viewModel.CancelPersistedHostRestoration(currentPath);
                }
                CompletePersistedHostRestoration(viewModel, originalHwnd);
                if (!viewModel.IsDisposed && showRestoredTabBar != null) showRestoredTabBar();
            }
        }

        private void CompletePersistedHostRestoration(TabBarViewModel viewModel, IntPtr originalHwnd)
        {
            viewModel.IsRestoringControlPanelHost = false;
            // A failed switch (or an already suitable host) must not leave Explorer hidden.
            if (viewModel.ExplorerHwnd == originalHwnd)
            {
                NativeMethods.RECT restoreRect;
                if (_windowTracking.DeferredOriginRestoreRects.TryGetValue(originalHwnd, out restoreRect))
                {
                    _moveExplorerWindow(originalHwnd, restoreRect);
                    _windowTracking.DeferredOriginRestoreRects.Remove(originalHwnd);
                }
                _windowTracking.RestoreNormalPositionBeforeShow(originalHwnd);
                _showExplorerWindow(originalHwnd);
            }
        }

        internal void InitializeTabsForNewWindow(TabBarViewModel viewModel, string initialPath, bool useInitialPathOnly, bool reuseExistingTab = false)
        {
            if (viewModel == null)
            {
                return;
            }

            // An explicit launch target must not race a saved-tab navigation.
            bool preserveInitialPath = !string.IsNullOrEmpty(initialPath) && (reuseExistingTab || !IsHomeInitialPath(initialPath));
            bool loadedSavedTabs = _tabPersistence.LoadTabsTo(viewModel,
                deferControlPanelNavigation: true, deferNavigation: preserveInitialPath);

            if (string.IsNullOrEmpty(initialPath))
            {
                return;
            }

            if (loadedSavedTabs && !preserveInitialPath)
            {
                return;
            }

            bool allowSpecialPath = _explorerService.IsControlPanelPath(initialPath);

            TabItemViewModel targetTab = reuseExistingTab ? viewModel.FindDesktopLaunchTab(initialPath) : viewModel.FindTabByPath(initialPath);
            if (targetTab != null)
            {
                if (loadedSavedTabs && useInitialPathOnly && !reuseExistingTab)
                {
                    viewModel.InsertTabWithPath(initialPath, viewModel.Tabs.Count, allowSpecialPath);
                }
                else
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

        internal void RestorePreparedExplorerWindowForCreate(IntPtr hwnd, bool deferShow)
        {
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.RECT originalRect;
            bool hasOriginalRect = _windowTracking.HiddenOriginalRects.TryGetValue(hwnd, out originalRect);
            AppLogger.LogDiagnostic("WindowRecovery", string.Format("Create hwnd={0} defer={1} hasOriginal={2} original={3},{4},{5},{6}", hwnd, deferShow, hasOriginalRect, originalRect.Left, originalRect.Top, originalRect.Right, originalRect.Bottom));
            if (deferShow)
            {
                // Explorer may show Home again during startup; keep its bounds offscreen.
                _hideExplorerWindow(hwnd);
            }

            NativeMethods.RECT recentClosedRect;
            NativeMethods.WINDOWPLACEMENT? recentPlacement;
            if (_windowTracking.TryTakeRecentClosedManagedExplorerRect(DateTime.UtcNow, out recentClosedRect, out recentPlacement, deferShow))
            {
                _windowTracking.StageRestorePlacement(hwnd, recentPlacement);
                if (deferShow) _windowTracking.DeferredOriginRestoreRects[hwnd] = recentClosedRect;
                else _moveExplorerWindow(hwnd, recentClosedRect);
            }
            else if (hasOriginalRect)
            {
                if (deferShow) _windowTracking.DeferredOriginRestoreRects[hwnd] = originalRect;
                else _moveExplorerWindow(hwnd, originalRect);
            }

            _windowTracking.HiddenPendingAbsorb.Remove(hwnd);
            _windowTracking.HiddenOriginalRects.Remove(hwnd);
            if (!deferShow)
            {
                _windowTracking.RestoreNormalPositionBeforeShow(hwnd);
                _showExplorerWindow(hwnd);
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

        internal TimeSpan AbsorptionTimeout { get; set; } = TimeSpan.FromSeconds(5);
        private readonly HashSet<IntPtr> _pendingAbsorptions = new HashSet<IntPtr>();

        public bool AbsorbExplorerWindow(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel,
            string path, bool allowSpecialPath, bool isControlPanelPath, Action<IntPtr> ignoreExplorerWindow,
            bool wasManagedControlPanelLaunchSource = false, bool reuseExistingTab = false)
        {
            Task<bool> operation = AbsorbExplorerWindowAsync(newExplorerHwnd, targetViewModel, path,
                allowSpecialPath, isControlPanelPath, ignoreExplorerWindow, wasManagedControlPanelLaunchSource, reuseExistingTab: reuseExistingTab);
            if (operation.IsCompleted) return operation.GetAwaiter().GetResult();
            ObserveAbsorption(operation);
            return false;
        }

        private async void ObserveAbsorption(Task<bool> operation)
        {
            try { await operation; }
            catch (Exception ex) { AppLogger.LogError("ExplorerWindowInteractionService", "Absorption failed.", ex); }
        }

        internal async Task<bool> AbsorbExplorerWindowAsync(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel,
            string path, bool allowSpecialPath, bool isControlPanelPath, Action<IntPtr> ignoreExplorerWindow,
            bool wasManagedControlPanelLaunchSource = false, bool operationReserved = false, bool reuseExistingTab = false)
        {
            if (!_pendingAbsorptions.Add(newExplorerHwnd)) return false;
            bool completed = false;
            bool reservedHere = false;
            try
            {
                if (targetViewModel == null || targetViewModel.IsDisposed) return false;
                if (!operationReserved)
                {
                    reservedHere = targetViewModel.TryBeginExternalTabOperation();
                    if (!reservedHere) return false;
                }
                completed = await AbsorbExplorerWindowCoreAsync(newExplorerHwnd, targetViewModel, path,
                    allowSpecialPath, isControlPanelPath, ignoreExplorerWindow, wasManagedControlPanelLaunchSource, reuseExistingTab: reuseExistingTab);
                return completed;
            }
            finally
            {
                _pendingAbsorptions.Remove(newExplorerHwnd);
                if (reservedHere) targetViewModel.EndExternalTabOperation();
                if (!completed)
                {
                    RestoreUnabsorbedWindow(newExplorerHwnd);
                    if (ignoreExplorerWindow != null) ignoreExplorerWindow(newExplorerHwnd);
                }
            }
        }

        private async Task<bool> AbsorbExplorerWindowCoreAsync(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel,
            string path, bool allowSpecialPath, bool isControlPanelPath, Action<IntPtr> ignoreExplorerWindow,
            bool wasManagedControlPanelLaunchSource, bool reuseExistingTab)
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

            if (effectiveAllowSpecialPath && effectiveControlPanelPath)
                return TryRebindControlPanelTab(newExplorerHwnd, targetViewModel, targetPath, wasManagedControlPanelLaunchSource, reuseExistingTab);

            long version = targetViewModel.SynchronizationVersion;
            IntPtr host = targetViewModel.ExplorerHwnd;
            List<string> selectedItems = _explorerService is ExplorerManager
                ? await ComThreadService.Instance.InvokeAsync(() => _explorerService.GetSelectedItems(newExplorerHwnd))
                : _explorerService.GetSelectedItems(newExplorerHwnd);
            if (!targetViewModel.IsExternalOperationCurrent(version) || targetViewModel.ExplorerHwnd != host) return false;
            TabItemViewModel inserted = reuseExistingTab ? targetViewModel.FindDesktopLaunchTab(targetPath) : null;
            if (inserted != null)
            {
                await targetViewModel.SelectTabCoreAsync(inserted);
                if (targetViewModel.LastNavigationFailed || targetViewModel.ActiveTab != inserted) return false;
            }
            else
            {
                if (!await targetViewModel.InsertTabCoreAsync(targetPath, targetViewModel.Tabs.Count, effectiveAllowSpecialPath)) return false;
                inserted = targetViewModel.ActiveTab;
            }
            version = targetViewModel.SynchronizationVersion;
            System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                if (!targetViewModel.IsExternalOperationCurrent(version) || targetViewModel.ExplorerHwnd != host ||
                    targetViewModel.ActiveTab != inserted || !targetViewModel.Tabs.Contains(inserted)) return false;
                if (_explorerService is ExplorerManager && (!NativeMethods.IsWindow(host) || !NativeMethods.IsWindow(newExplorerHwnd))) return false;
                string current = await _explorerService.ReadPathAsync(host);
                if (!targetViewModel.IsExternalOperationCurrent(version) || targetViewModel.ExplorerHwnd != host) return false;
                if (targetViewModel.PathEquals(current, targetPath))
                {
                    if (selectedItems != null && selectedItems.Count > 0)
                        await _explorerService.RestoreItemsAsync(host, selectedItems);
                    if (!targetViewModel.IsExternalOperationCurrent(version) || targetViewModel.ExplorerHwnd != host) return false;
                    targetViewModel.ClearPendingNavigationTracking();
                    FinalizeAbsorbedWindow(newExplorerHwnd, host);
                    return true;
                }
                if (elapsed.Elapsed >= AbsorptionTimeout) break;
                await Task.Delay(100);
            } while (true);
            targetViewModel.TimeoutPendingNavigation();
            return false;
        }

        internal void RestoreUnabsorbedWindow(IntPtr hwnd)
        {
            _windowTracking.RestoreHiddenWindow(hwnd);
            _showExplorerWindow(hwnd);
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

        private bool TryRebindControlPanelTab(IntPtr newExplorerHwnd, TabBarViewModel targetViewModel, string path, bool wasManagedControlPanelLaunchSource = false, bool reuseExistingTab = false)
        {
            if (targetViewModel == null || string.IsNullOrEmpty(path) || newExplorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            TabItemViewModel reusableTab = reuseExistingTab ? targetViewModel.FindDesktopLaunchTab(path) : null;
            if (!reuseExistingTab && wasManagedControlPanelLaunchSource)
            {
                if (targetViewModel.ActiveTab != null && _explorerService.IsControlPanelPath(targetViewModel.ActiveTab.Path))
                {
                    reusableTab = targetViewModel.ActiveTab;
                }
                else
                {
                    reusableTab = FindEquivalentControlPanelTab(targetViewModel, path);
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
            bool previousHostIsControlPanel = targetViewModel.ActiveTab != null &&
                _explorerService.IsControlPanelPath(targetViewModel.ActiveTab.Path);
            IntPtr originalFolderHwnd = IntPtr.Zero;
            if (previousHostIsControlPanel)
            {
                _windowTracking.TryGetParkedExplorerOrigin(previousExplorerHwnd, out originalFolderHwnd);
            }
            NativeMethods.RECT previousExplorerRect = GetWindowBoundsForMove(previousExplorerHwnd);
            NativeMethods.WINDOWPLACEMENT? previousPlacement = _windowTracking.GetHostSwitchRestorePlacement(previousExplorerHwnd);
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

            NativeMethods.RECT targetExplorerRect = previousExplorerRect;
            if (!NativeMethods.IsUsableWindowRestoreRect(targetExplorerRect) && hadHiddenOriginalRect)
            {
                targetExplorerRect = hiddenOriginalRect;
            }

            AlignExplorerWindowToPreviousRect(newExplorerHwnd, targetExplorerRect);

            if (_rebindExplorerWindow == null || !_rebindExplorerWindow(targetViewModel, newExplorerHwnd))
            {
                RestorePreparedExplorerWindow(newExplorerHwnd, hadHiddenPending, hadHiddenOriginalRect, hiddenOriginalRect);
                if (createdReusableTab)
                {
                    targetViewModel.Tabs.Remove(reusableTab);
                }
                return false;
            }

            // The new host already displays this page. Adopt it without navigating to the old tab path.
            reusableTab.Path = path;
            reusableTab.BaseTitle = _explorerService.GetFolderName(path);
            targetViewModel.ClearPendingNavigationTracking();
            targetViewModel.ClearCancelledNavigationTracking();
            targetViewModel.NavigationTracker.UpdateCache(path, DateTime.UtcNow);
            targetViewModel.IsRestoringControlPanelHost = false;
            targetViewModel.SetActiveTabOnly(reusableTab);
            targetViewModel.UpdateTabTitles();

            _windowTracking.StageRestorePlacement(newExplorerHwnd, previousPlacement);
            _windowTracking.RestoreNormalPositionBeforeShow(newExplorerHwnd);
            _forceSetForegroundWindow(newExplorerHwnd);

            if (previousExplorerHwnd != IntPtr.Zero && previousExplorerHwnd != newExplorerHwnd)
            {
                if (previousHostIsControlPanel)
                {
                    // A second Control Panel shortcut replaces the same kind of host.
                    // Its parked partner must remain the original folder Explorer.
                    _windowTracking.ClearParkedExplorerOrigin(previousExplorerHwnd);
                    if (originalFolderHwnd != IntPtr.Zero && originalFolderHwnd != newExplorerHwnd)
                    {
                        _windowTracking.RememberParkedExplorerOrigin(newExplorerHwnd, originalFolderHwnd);
                    }
                    _windowTracking.MarkAbsorbedWindow(previousExplorerHwnd);
                    _postCloseWindow(previousExplorerHwnd);
                }
                else
                {
                    _windowTracking.RememberParkedExplorerOrigin(newExplorerHwnd, previousExplorerHwnd);
                }
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
            _windowTracking.RestoreNormalPositionBeforeShow(targetExplorerHwnd);
            _forceSetForegroundWindow(targetExplorerHwnd);
            if (newExplorerHwnd == targetExplorerHwnd)
            {
                // Windows reused the parked host; it is now the managed window, not disposable.
                _windowTracking.ClearAbsorptionState(newExplorerHwnd);
                _windowTracking.HiddenPendingAbsorb.Remove(newExplorerHwnd);
                _windowTracking.HiddenOriginalRects.Remove(newExplorerHwnd);
                return;
            }
            _windowTracking.MarkAbsorbedWindow(newExplorerHwnd);
            _postCloseWindow(newExplorerHwnd);
        }

        private void AlignExplorerWindowToPreviousRect(IntPtr explorerHwnd, NativeMethods.RECT previousExplorerRect)
        {
            if (_moveExplorerWindow == null || explorerHwnd == IntPtr.Zero)
            {
                return;
            }

            if (!NativeMethods.IsUsableWindowRestoreRect(previousExplorerRect))
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

        private static void ShowExplorerWindowCore(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero || !NativeMethods.IsWindow(explorerHwnd))
            {
                return;
            }

            NativeMethods.ShowWindow(explorerHwnd, NativeMethods.SW_SHOW);
            // A restored Explorer can retain missing caption buttons until its frame is recalculated.
            // Post the same size-preserving refresh verified on the affected Explorer window.
            NativeMethods.SetWindowPos(
                explorerHwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER |
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_ASYNCWINDOWPOS);
        }

        private static void MoveExplorerWindowCore(IntPtr explorerHwnd, NativeMethods.RECT rect)
        {
            if (explorerHwnd == IntPtr.Zero || rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            // Restoring an offscreen window must invalidate both its frame and folder view.
            NativeMethods.MoveWindow(explorerHwnd, rect.Left, rect.Top, rect.Width, rect.Height, true);
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


