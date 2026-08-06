using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class ExplorerWindowProcessRequest
    {
        public IntPtr ExplorerHwnd { get; set; }
        public TabBarViewModel ValidTarget { get; set; }
    }

    internal sealed class ExplorerWindowMonitorCoordinator
    {
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
            Func<DateTime> getUtcNow)
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
        }

        public void HandleShowEvent(IntPtr hwnd, Func<TabBarViewModel> findValidTarget, Func<TabBarViewModel, bool> hasActiveControlPanelTab)
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

            if (_tabBars.Contains(rootHwnd)) return;
            if (_windowTracking.IgnoredWindows.Contains(rootHwnd)) return;
            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(rootHwnd)) return;
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
            if (validTarget == null) return;

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

            _windowTracking.AddHiddenPendingWindows(explorerWindows);
            _tabBars.RemoveInvalidWindows(explorerWindows, _persistClosingTabBarState);
            _windowTracking.CleanupClosedWindows(explorerWindows);

            for (int i = 0; i < explorerWindows.Count; i++)
            {
                IntPtr hwnd = explorerWindows[i];
                if (_tabBars.Contains(hwnd)) continue;
                if (_windowTracking.IsParkedExplorerOriginValue(hwnd))
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
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                NativeMethods.HiddenWindowCoordinate,
                NativeMethods.HiddenWindowCoordinate,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
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
