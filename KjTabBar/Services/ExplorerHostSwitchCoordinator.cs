using System;
using System.Collections.Generic;
using System.Threading;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class ExplorerHostSwitchCoordinator
    {
        private IntPtr _pendingRevealExplorerHwnd;
        private readonly IExplorerService _explorerService;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly Func<TabBarViewModel, IntPtr, bool> _rebindExplorerWindow;
        private readonly Action<IntPtr> _showExplorerWindow;
        private readonly Action<IntPtr, NativeMethods.RECT> _moveExplorerWindow;
        private readonly Action<IntPtr> _postCloseWindow;
        private readonly Func<IntPtr, bool> _isWindow;
        private readonly Func<List<IntPtr>> _findExplorerWindows;
        private readonly Func<IntPtr, string> _getCurrentPath;
        private readonly Func<string, bool> _openInNewWindow;
        private readonly Action<int> _sleep;

        public ExplorerHostSwitchCoordinator(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            Func<TabBarViewModel, IntPtr, bool> rebindExplorerWindow,
            Action<IntPtr> showExplorerWindow,
            Action<IntPtr, NativeMethods.RECT> moveExplorerWindow,
            Action<IntPtr> postCloseWindow)
            : this(
                  explorerService,
                  windowTracking,
                  rebindExplorerWindow,
                  showExplorerWindow,
                  moveExplorerWindow,
                  postCloseWindow,
                  NativeMethods.IsWindow,
                  delegate { return explorerService != null ? explorerService.FindExplorerWindows() : new List<IntPtr>(); },
                  delegate (IntPtr hwnd) { return explorerService != null ? explorerService.GetCurrentPath(hwnd) : null; },
                  delegate (string path) { return explorerService != null && explorerService.OpenInNewWindow(path); },
                  Thread.Sleep)
        {
        }

        internal ExplorerHostSwitchCoordinator(
            IExplorerService explorerService,
            ExplorerWindowTrackingState windowTracking,
            Func<TabBarViewModel, IntPtr, bool> rebindExplorerWindow,
            Action<IntPtr> showExplorerWindow,
            Action<IntPtr, NativeMethods.RECT> moveExplorerWindow,
            Action<IntPtr> postCloseWindow,
            Func<IntPtr, bool> isWindow,
            Func<List<IntPtr>> findExplorerWindows,
            Func<IntPtr, string> getCurrentPath,
            Func<string, bool> openInNewWindow,
            Action<int> sleep)
        {
            _explorerService = explorerService;
            _windowTracking = windowTracking;
            _rebindExplorerWindow = rebindExplorerWindow;
            _showExplorerWindow = showExplorerWindow;
            _moveExplorerWindow = moveExplorerWindow;
            _postCloseWindow = postCloseWindow;
            _isWindow = isWindow;
            _findExplorerWindows = findExplorerWindows;
            _getCurrentPath = getCurrentPath;
            _openInNewWindow = openInNewWindow;
            _sleep = sleep;
        }

        public bool PrepareForPath(TabBarViewModel viewModel, string targetPath)
        {
            _pendingRevealExplorerHwnd = IntPtr.Zero;

            if (viewModel == null || string.IsNullOrEmpty(targetPath))
            {
                return true;
            }

            if (_explorerService == null ||
                _windowTracking == null ||
                _rebindExplorerWindow == null)
            {
                return true;
            }

            bool targetIsControlPanelPath = _explorerService.IsControlPanelPath(targetPath);
            IntPtr currentExplorerHwnd = viewModel.ExplorerHwnd;
            bool currentIsControlPanelHost = IsControlPanelHost(currentExplorerHwnd, viewModel, targetIsControlPanelPath);
            IntPtr parkedExplorerHwnd;
            AppLogger.LogInfo(
                "ExplorerHostSwitchCoordinator",
                string.Format(
                    "PrepareForPath current={0} targetPath={1} targetIsControlPanel={2} currentIsControlPanel={3}",
                    currentExplorerHwnd,
                    targetPath ?? string.Empty,
                    targetIsControlPanelPath,
                    currentIsControlPanelHost));
            if (!_windowTracking.TryGetParkedExplorerOrigin(currentExplorerHwnd, out parkedExplorerHwnd))
            {
                AppLogger.LogInfo(
                    "ExplorerHostSwitchCoordinator",
                    string.Format("PrepareForPath noParkedOrigin current={0}", currentExplorerHwnd));
                if (currentIsControlPanelHost != targetIsControlPanelPath)
                {
                    return TrySwitchToFreshExplorerHost(viewModel, targetPath, currentExplorerHwnd);
                }

                return true;
            }

            if (parkedExplorerHwnd == IntPtr.Zero || (_isWindow != null && !_isWindow(parkedExplorerHwnd)))
            {
                AppLogger.LogInfo(
                    "ExplorerHostSwitchCoordinator",
                    string.Format("PrepareForPath invalidParkedOrigin current={0} parked={1}", currentExplorerHwnd, parkedExplorerHwnd));
                _windowTracking.ClearParkedExplorerOrigin(currentExplorerHwnd);
                if (currentIsControlPanelHost != targetIsControlPanelPath)
                {
                    return TrySwitchToFreshExplorerHost(viewModel, targetPath, currentExplorerHwnd);
                }

                return true;
            }

            bool parkedIsControlPanelHost = !currentIsControlPanelHost;
            bool shouldSwitchToParkedHost = targetIsControlPanelPath != currentIsControlPanelHost;

            if (!shouldSwitchToParkedHost)
            {
                AppLogger.LogInfo(
                    "ExplorerHostSwitchCoordinator",
                    string.Format(
                        "PrepareForPath keepCurrentHost current={0} parked={1} parkedIsControlPanel={2}",
                        currentExplorerHwnd,
                        parkedExplorerHwnd,
                        parkedIsControlPanelHost));
                return true;
            }

            NativeMethods.RECT currentExplorerRect = GetWindowBoundsForMove(currentExplorerHwnd);

            try
            {
                if (_moveExplorerWindow != null &&
                    currentExplorerRect.Width > 0 &&
                    currentExplorerRect.Height > 0)
                {
                    _moveExplorerWindow(parkedExplorerHwnd, currentExplorerRect);
                }

                if (!_rebindExplorerWindow(viewModel, parkedExplorerHwnd))
                {
                    return false;
                }

                _windowTracking.ClearParkedExplorerOrigin(currentExplorerHwnd);
                _windowTracking.RememberParkedExplorerOrigin(parkedExplorerHwnd, currentExplorerHwnd);
                NativeMethods.ShowWindow(currentExplorerHwnd, NativeMethods.SW_HIDE);
                _pendingRevealExplorerHwnd = parkedExplorerHwnd;
                AppLogger.LogInfo(
                    "ExplorerHostSwitchCoordinator",
                    string.Format("PrepareForPath switchedToParkedHost current={0} parked={1}", currentExplorerHwnd, parkedExplorerHwnd));

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExplorerHostSwitchCoordinator", "Failed to restore the original explorer host.", ex);
                return false;
            }
        }

        private bool IsControlPanelHost(IntPtr explorerHwnd, TabBarViewModel viewModel, bool targetIsControlPanelPath)
        {
            if (explorerHwnd == IntPtr.Zero || _explorerService == null)
            {
                return false;
            }

            TabItemViewModel activeTab = viewModel != null ? viewModel.ActiveTab : null;
            if (activeTab != null && !string.IsNullOrEmpty(activeTab.Path))
            {
                bool activeTabIsControlPanel = _explorerService.IsControlPanelPath(activeTab.Path);
                if (activeTabIsControlPanel == targetIsControlPanelPath)
                {
                    return activeTabIsControlPanel;
                }
            }

            string currentPath = _getCurrentPath != null ? _getCurrentPath(explorerHwnd) : null;
            if (!string.IsNullOrEmpty(currentPath))
            {
                return _explorerService.IsControlPanelPath(currentPath);
            }

            return activeTab != null && _explorerService.IsControlPanelPath(activeTab.Path);
        }

        private bool TrySwitchToFreshExplorerHost(TabBarViewModel viewModel, string targetPath, IntPtr currentExplorerHwnd)
        {
            if (_openInNewWindow == null || _findExplorerWindows == null)
            {
                return false;
            }

            NativeMethods.RECT currentExplorerRect = GetWindowBoundsForMove(currentExplorerHwnd);
            HashSet<IntPtr> previousExplorerWindows = new HashSet<IntPtr>(_findExplorerWindows());
            AppLogger.LogInfo(
                "ExplorerHostSwitchCoordinator",
                string.Format(
                    "TrySwitchToFreshExplorerHost current={0} targetPath={1} previousCount={2}",
                    currentExplorerHwnd,
                    targetPath ?? string.Empty,
                    previousExplorerWindows.Count));

            _windowTracking.RegisterInternalHostSwitchLaunchRequest();
            if (!_openInNewWindow(targetPath))
            {
                _windowTracking.CancelInternalHostSwitchLaunchRequest();
                AppLogger.LogInfo("ExplorerHostSwitchCoordinator", "TrySwitchToFreshExplorerHost openInNewWindowFailed");
                return false;
            }

            IntPtr newExplorerHwnd = WaitForNewExplorerWindow(previousExplorerWindows, currentExplorerHwnd, targetPath);
            if (newExplorerHwnd == IntPtr.Zero)
            {
                _windowTracking.CancelInternalHostSwitchLaunchRequest();
                AppLogger.LogInfo("ExplorerHostSwitchCoordinator", "TrySwitchToFreshExplorerHost noNewExplorerWindowFound");
                return false;
            }

            try
            {
                bool hadHiddenPending = _windowTracking.HiddenPendingAbsorb.ContainsKey(newExplorerHwnd);
                NativeMethods.RECT hiddenOriginalRect = default(NativeMethods.RECT);
                bool hadHiddenOriginalRect = _windowTracking.HiddenOriginalRects.TryGetValue(newExplorerHwnd, out hiddenOriginalRect);

                _windowTracking.ClearInternalHostSwitchLaunchWindow(newExplorerHwnd);
                if (hadHiddenPending)
                {
                    NativeMethods.ShowWindow(newExplorerHwnd, NativeMethods.SW_HIDE);
                }

                _windowTracking.HiddenPendingAbsorb.Remove(newExplorerHwnd);
                _windowTracking.HiddenOriginalRects.Remove(newExplorerHwnd);

                if (_moveExplorerWindow != null &&
                    currentExplorerRect.Width > 0 &&
                    currentExplorerRect.Height > 0)
                {
                    _moveExplorerWindow(newExplorerHwnd, currentExplorerRect);
                }

                if (!_rebindExplorerWindow(viewModel, newExplorerHwnd))
                {
                    RestorePreparedExplorerWindow(newExplorerHwnd, hadHiddenPending, hadHiddenOriginalRect, hiddenOriginalRect);
                    return false;
                }

                _windowTracking.RememberParkedExplorerOrigin(newExplorerHwnd, currentExplorerHwnd);
                NativeMethods.ShowWindow(currentExplorerHwnd, NativeMethods.SW_HIDE);
                _pendingRevealExplorerHwnd = newExplorerHwnd;
                AppLogger.LogInfo(
                    "ExplorerHostSwitchCoordinator",
                    string.Format(
                        "TrySwitchToFreshExplorerHost switchedToFreshHost current={0} new={1} hadHiddenPending={2}",
                        currentExplorerHwnd,
                        newExplorerHwnd,
                        hadHiddenPending));

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExplorerHostSwitchCoordinator", "Failed to switch to a fresh explorer host.", ex);
                return false;
            }
        }

        private IntPtr WaitForNewExplorerWindow(HashSet<IntPtr> previousExplorerWindows, IntPtr currentExplorerHwnd, string targetPath)
        {
            if (_findExplorerWindows == null)
            {
                return IntPtr.Zero;
            }

            for (int retry = 0; retry < 20; retry++)
            {
                List<IntPtr> explorerWindows = _findExplorerWindows();
                for (int i = 0; i < explorerWindows.Count; i++)
                {
                    IntPtr hwnd = explorerWindows[i];
                    if (hwnd == IntPtr.Zero || hwnd == currentExplorerHwnd || previousExplorerWindows.Contains(hwnd))
                    {
                        continue;
                    }

                    if (_isWindow != null && !_isWindow(hwnd))
                    {
                        continue;
                    }

                    string currentPath = _getCurrentPath != null ? _getCurrentPath(hwnd) : null;
                    if (_explorerService == null || string.IsNullOrEmpty(currentPath))
                    {
                        continue;
                    }

                    string normalizedCurrentPath = _explorerService.NormalizeKnownPath(currentPath);
                    string normalizedTargetPath = _explorerService.NormalizeKnownPath(targetPath);
                    if (string.IsNullOrEmpty(normalizedCurrentPath) ||
                        string.IsNullOrEmpty(normalizedTargetPath) ||
                        !string.Equals(normalizedCurrentPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppLogger.LogInfo(
                        "ExplorerHostSwitchCoordinator",
                        string.Format(
                            "WaitForNewExplorerWindow matched hwnd={0} retry={1} currentPath={2}",
                            hwnd,
                            retry,
                            currentPath ?? string.Empty));
                    return hwnd;
                }

                if (_sleep != null)
                {
                    _sleep(100);
                }
            }

            return IntPtr.Zero;
        }

        public void CompletePendingReveal()
        {
            if (_pendingRevealExplorerHwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr pendingRevealExplorerHwnd = _pendingRevealExplorerHwnd;
            _pendingRevealExplorerHwnd = IntPtr.Zero;
            AppLogger.LogInfo(
                "ExplorerHostSwitchCoordinator",
                string.Format("CompletePendingReveal hwnd={0}", pendingRevealExplorerHwnd));

            if (_showExplorerWindow != null)
            {
                _showExplorerWindow(pendingRevealExplorerHwnd);
            }
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

        private NativeMethods.RECT GetWindowBoundsForMove(IntPtr explorerHwnd)
        {
            NativeMethods.RECT rect;
            if (explorerHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(explorerHwnd, out rect))
            {
                return rect;
            }

            return _explorerService.GetExplorerWindowRect(explorerHwnd);
        }
    }
}
