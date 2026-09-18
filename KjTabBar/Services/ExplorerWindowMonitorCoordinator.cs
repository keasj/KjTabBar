using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar.Services
{
    internal sealed class ExplorerWindowProcessRequest
    {
        public IntPtr ExplorerHwnd { get; set; }
        public TabBarViewModel ValidTarget { get; set; }
    }

    internal sealed class ExplorerWindowMonitorCoordinator
    {
        private readonly HashSet<IntPtr> _preparedBeforeShow = new HashSet<IntPtr>();
        private readonly TabBarRegistry _tabBars;
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly DesktopForegroundTracker _desktopForegroundTracker;
        private readonly ExplorerLaunchTracker _explorerLaunchTracker;
        private readonly Func<IntPtr, string> _getClassName;
        private readonly Func<IntPtr, uint, IntPtr> _getAncestor;
        private readonly Func<IntPtr, NativeMethods.RECT?> _getWindowRect;
        private readonly Action<IntPtr> _moveWindowOffscreen;
        private readonly Action<TabBarViewModel> _persistClosingTabBarState;
        private readonly Func<DateTime> _getUtcNow;
        private readonly Func<IntPtr, bool> _isWindowVisible;
        private readonly Action<IntPtr> _hideWindow;

        public ExplorerWindowMonitorCoordinator(
            TabBarRegistry tabBars,
            ExplorerWindowTrackingState windowTracking,
            DesktopForegroundTracker desktopForegroundTracker,
            ExplorerLaunchTracker explorerLaunchTracker)
            : this(
                  tabBars,
                  windowTracking,
                  desktopForegroundTracker,
                  explorerLaunchTracker,
                  GetClassNameCore,
                  NativeMethods.GetAncestor,
                  GetWindowRectCore,
                  MoveWindowOffscreenCore,
                  null,
                  delegate { return DateTime.UtcNow; })
        {
        }

        internal ExplorerWindowMonitorCoordinator(
            TabBarRegistry tabBars,
            ExplorerWindowTrackingState windowTracking,
            DesktopForegroundTracker desktopForegroundTracker,
            ExplorerLaunchTracker explorerLaunchTracker,
            Func<IntPtr, string> getClassName,
            Func<IntPtr, uint, IntPtr> getAncestor,
            Func<IntPtr, NativeMethods.RECT?> getWindowRect,
            Action<IntPtr> moveWindowOffscreen,
            Action<TabBarViewModel> persistClosingTabBarState,
            Func<DateTime> getUtcNow,
            Func<IntPtr, bool> isWindowVisible = null,
            Action<IntPtr> hideWindow = null)
        {
            _tabBars = tabBars;
            _windowTracking = windowTracking;
            _desktopForegroundTracker = desktopForegroundTracker;
            _explorerLaunchTracker = explorerLaunchTracker;
            _getClassName = getClassName ?? GetClassNameCore;
            _getAncestor = getAncestor ?? NativeMethods.GetAncestor;
            _getWindowRect = getWindowRect ?? GetWindowRectCore;
            _moveWindowOffscreen = moveWindowOffscreen ?? MoveWindowOffscreenCore;
            _persistClosingTabBarState = persistClosingTabBarState;
            _getUtcNow = getUtcNow ?? delegate { return DateTime.UtcNow; };
            _isWindowVisible = isWindowVisible ?? NativeMethods.IsWindowVisible;
            _hideWindow = hideWindow ?? delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE); };
        }

        public void HandleCreateEvent(IntPtr hwnd, Func<TabBarViewModel> findValidTarget)
        {
            if (hwnd == IntPtr.Zero || _getClassName(hwnd) != "CabinetWClass") return;
            if (findValidTarget != null && findValidTarget() != null &&
                !_windowTracking.HasPendingInternalHostSwitchLaunchRequest()) return;
            NativeMethods.RECT? rect = _getWindowRect(hwnd);
            if (!rect.HasValue || !NativeMethods.IsUsableWindowRestoreRect(rect.Value)) return;

            HandleShowEvent(hwnd, findValidTarget, null);
            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd)) _preparedBeforeShow.Add(hwnd);
        }

        public void HandleShowEvent(IntPtr hwnd, Func<TabBarViewModel> findValidTarget, Func<TabBarViewModel, bool> hasActiveControlPanelTab, Action requestImmediateCycle = null)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IntPtr rootHwnd = GetRootWindowOrSelf(hwnd);
            if (rootHwnd == IntPtr.Zero)
            {
                return;
            }

            TabBarWindow registeredWindow;
            if (_tabBars.TryGetTabBarWindow(rootHwnd, out registeredWindow))
            {
                TabBarViewModel registeredViewModel = registeredWindow.DataContext as TabBarViewModel;
                // Explorer can show Home after we have registered and hidden it.
                if (registeredViewModel != null && registeredViewModel.ExplorerHwnd == rootHwnd &&
                    registeredViewModel.IsRestoringControlPanelHost && _isWindowVisible(rootHwnd))
                {
                    AppLogger.LogDiagnosticPlacement("Restore.RehideRegisteredHome.Before", rootHwnd);
                    _hideWindow(rootHwnd);
                    AppLogger.LogDiagnosticPlacement("Restore.RehideRegisteredHome.After", rootHwnd);
                }
                return;
            }
            if (_windowTracking.IgnoredWindows.Contains(rootHwnd)) return;
            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(rootHwnd))
            {
                if (_preparedBeforeShow.Remove(rootHwnd))
                {
                    // Explorer may reposition itself between CREATE and SHOW.
                    _moveWindowOffscreen(rootHwnd);
                    if (!_windowTracking.InternalHostSwitchLaunchWindows.Contains(rootHwnd))
                        requestImmediateCycle?.Invoke();
                }
                return;
            }
            if (_windowTracking.AbsorbPathRetryCounts.ContainsKey(rootHwnd)) return;

            if (_getClassName(rootHwnd) != "CabinetWClass") return;

            if (_windowTracking.TryConsumeInternalHostSwitchLaunchRequest())
            {
                NativeMethods.RECT? hostSwitchRect = _getWindowRect(rootHwnd);
                DateTime hostSwitchHiddenUtc = _getUtcNow();
                if (hostSwitchRect.HasValue)
                {
                    _windowTracking.AddHiddenPendingWindow(rootHwnd, hostSwitchRect.Value, hostSwitchHiddenUtc);
                    _moveWindowOffscreen(rootHwnd);
                }
                else
                {
                    _windowTracking.HiddenPendingAbsorb[rootHwnd] = hostSwitchHiddenUtc;
                }

                _windowTracking.MarkInternalHostSwitchLaunchWindow(rootHwnd);
                return;
            }

            if (_windowTracking.TryConsumeExplicitIndependentLaunchRequest())
            {
                _windowTracking.IgnoreExplicitIndependentLaunchWindow(rootHwnd);
                return;
            }

            TabBarViewModel validTarget = findValidTarget != null ? findValidTarget() : null;
            if (validTarget == null)
            {
                // First/reopened host: avoid waiting for the next background timer tick.
                if (!_windowTracking.ProcessingExplorerWindows.Contains(rootHwnd))
                {
                    // Keep the initial Home page offscreen until the saved selection is known.
                    NativeMethods.RECT? initialRect = _getWindowRect(rootHwnd);
                    if (initialRect.HasValue && NativeMethods.IsUsableWindowRestoreRect(initialRect.Value))
                    {
                        _windowTracking.AddHiddenPendingWindow(rootHwnd, initialRect.Value, _getUtcNow());
                        _moveWindowOffscreen(rootHwnd);
                    }
                    requestImmediateCycle?.Invoke();
                }
                return;
            }

            // Capture the origin before later foreground updates and COM retries lose it.
            if (_explorerLaunchTracker.WasManagedControlPanelLaunchSource())
            {
                _windowTracking.ManagedControlPanelLaunchWindows.Add(rootHwnd);
            }

            bool wasDesktopForegroundRecently = _desktopForegroundTracker.WasDesktopForegroundRecently();
            if ((_explorerLaunchTracker.IsForegroundRelatedWindow(validTarget.ExplorerHwnd) ||
                 _explorerLaunchTracker.WasForegroundRelatedWindow(validTarget.ExplorerHwnd)) &&
                hasActiveControlPanelTab != null &&
                hasActiveControlPanelTab(validTarget) &&
                wasDesktopForegroundRecently)
            {
                _windowTracking.ControlPanelTabLaunchCandidates.Add(rootHwnd);
            }

            if (!wasDesktopForegroundRecently) return;

            _windowTracking.DesktopLaunchCandidates.Add(rootHwnd);
            if (_desktopForegroundTracker.WasDesktopInteractiveForegroundRecently())
            {
                _windowTracking.DesktopInteractiveLaunchCandidates.Add(rootHwnd);
            }

            NativeMethods.RECT? rect = _getWindowRect(rootHwnd);
            DateTime hiddenUtc = _getUtcNow();
            if (rect.HasValue)
            {
                _windowTracking.AddHiddenPendingWindow(rootHwnd, rect.Value, hiddenUtc);
                _moveWindowOffscreen(rootHwnd);
            }
            else
            {
                _windowTracking.HiddenPendingAbsorb[rootHwnd] = hiddenUtc;
            }
        }

        public List<ExplorerWindowProcessRequest> PrepareProcessRequests(
            List<IntPtr> explorerWindows,
            Func<TabBarViewModel> findValidTarget)
        {
            List<ExplorerWindowProcessRequest> requests = new List<ExplorerWindowProcessRequest>();

            _preparedBeforeShow.RemoveWhere(hwnd => !_windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd));
            _windowTracking.AddHiddenPendingWindows(explorerWindows);
            _tabBars.RemoveInvalidWindows(explorerWindows, _persistClosingTabBarState);
            _windowTracking.CleanupClosedWindows(explorerWindows);

            for (int i = 0; i < explorerWindows.Count; i++)
            {
                IntPtr hwnd = explorerWindows[i];
                if (_tabBars.Contains(hwnd)) continue;
                if (_preparedBeforeShow.Contains(hwnd))
                {
                    // SHOW delivery can lag even though Windows has already shown the host.
                    // Only recover first hosts here; the switch coordinator owns replacements.
                    if (_windowTracking.InternalHostSwitchLaunchWindows.Contains(hwnd) || !_isWindowVisible(hwnd))
                        continue;
                    _moveWindowOffscreen(hwnd);
                    _preparedBeforeShow.Remove(hwnd);
                    AppLogger.LogDiagnostic("ReopenDecision", "VisibleBeforeShowCallback hwnd=" + hwnd);
                }
                bool desktopReshow = _windowTracking.DesktopLaunchCandidates.Contains(hwnd) &&
                    _windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd);
                if (_windowTracking.IsParkedExplorerOriginValue(hwnd) && !desktopReshow)
                {
                    AppLogger.LogInfo(
                        "ExplorerWindowMonitorCoordinator",
                        string.Format("PrepareProcessRequests skipParkedOriginValue hwnd={0}", hwnd));
                    continue;
                }
                if (_windowTracking.InternalHostSwitchLaunchWindows.Contains(hwnd)) continue;
                if (_windowTracking.ProcessingExplorerWindows.Contains(hwnd)) continue;

                TabBarViewModel validTarget = findValidTarget != null ? findValidTarget() : null;
                if (_windowTracking.IgnoredWindows.Contains(hwnd))
                {
                    if (_windowTracking.ExplicitIndependentLaunchWindows.Contains(hwnd))
                    {
                        continue;
                    }

                    if (!ExplorerWindowDecisionLogic.ShouldReevaluateIgnoredWindow(validTarget != null))
                    {
                        continue;
                    }

                    _windowTracking.IgnoredWindows.Remove(hwnd);
                }

                _windowTracking.ProcessingExplorerWindows.Add(hwnd);
                requests.Add(new ExplorerWindowProcessRequest
                {
                    ExplorerHwnd = hwnd,
                    ValidTarget = validTarget
                });
            }

            return requests;
        }

        private static string GetClassNameCore(IntPtr hwnd)
        {
            System.Text.StringBuilder className = new System.Text.StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            return className.ToString();
        }

        private static NativeMethods.RECT? GetWindowRectCore(IntPtr hwnd)
        {
            NativeMethods.RECT rect;
            if (NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return rect;
            }

            return null;
        }

        private static void MoveWindowOffscreenCore(IntPtr hwnd)
        {
            System.Diagnostics.Stopwatch timer = AppLogger.StartDiagnosticTiming();
            AppLogger.LogDiagnosticTiming("Offscreen.Begin", hwnd, timer);
            AppLogger.LogDiagnosticPlacement("Offscreen.Before", hwnd);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                NativeMethods.HiddenWindowCoordinate,
                NativeMethods.HiddenWindowCoordinate,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
            AppLogger.LogDiagnosticPlacement("Offscreen.After", hwnd);
            AppLogger.LogDiagnosticTiming("Offscreen.Completed", hwnd, timer);
        }

        private IntPtr GetRootWindowOrSelf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (_getAncestor != null)
            {
                IntPtr rootHwnd = _getAncestor(hwnd, NativeMethods.GA_ROOT);
                if (rootHwnd != IntPtr.Zero)
                {
                    return rootHwnd;
                }
            }

            return hwnd;
        }
    }
}
