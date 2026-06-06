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
        private readonly Func<IntPtr, NativeMethods.RECT?> _getWindowRect;
        private readonly Action<IntPtr> _moveWindowOffscreen;
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
                  GetWindowRectCore,
                  MoveWindowOffscreenCore,
                  delegate { return DateTime.UtcNow; })
        {
        }

        internal ExplorerWindowMonitorCoordinator(
            TabBarRegistry tabBars,
            ExplorerWindowTrackingState windowTracking,
            DesktopForegroundTracker desktopForegroundTracker,
            ExplorerLaunchTracker explorerLaunchTracker,
            Func<IntPtr, string> getClassName,
            Func<IntPtr, NativeMethods.RECT?> getWindowRect,
            Action<IntPtr> moveWindowOffscreen,
            Func<DateTime> getUtcNow)
        {
            _tabBars = tabBars;
            _windowTracking = windowTracking;
            _desktopForegroundTracker = desktopForegroundTracker;
            _explorerLaunchTracker = explorerLaunchTracker;
            _getClassName = getClassName;
            _getWindowRect = getWindowRect;
            _moveWindowOffscreen = moveWindowOffscreen;
            _getUtcNow = getUtcNow;
        }

        public void HandleShowEvent(IntPtr hwnd, Func<TabBarViewModel> findValidTarget, Func<TabBarViewModel, bool> hasActiveControlPanelTab)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            if (_tabBars.Contains(hwnd)) return;
            if (_windowTracking.IgnoredWindows.Contains(hwnd)) return;
            if (_windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd)) return;
            if (_windowTracking.AbsorbPathRetryCounts.ContainsKey(hwnd)) return;

            if (_getClassName(hwnd) != "CabinetWClass") return;

            TabBarViewModel validTarget = findValidTarget != null ? findValidTarget() : null;
            if (validTarget == null) return;

            if (_explorerLaunchTracker.IsForegroundRelatedWindow(validTarget.ExplorerHwnd) &&
                hasActiveControlPanelTab != null &&
                hasActiveControlPanelTab(validTarget))
            {
                _windowTracking.ControlPanelTabLaunchCandidates.Add(hwnd);
            }

            if (!_desktopForegroundTracker.WasDesktopForegroundRecently()) return;

            _windowTracking.DesktopLaunchCandidates.Add(hwnd);
            if (_desktopForegroundTracker.WasDesktopInteractiveForegroundRecently())
            {
                _windowTracking.DesktopInteractiveLaunchCandidates.Add(hwnd);
            }

            NativeMethods.RECT? rect = _getWindowRect(hwnd);
            DateTime hiddenUtc = _getUtcNow();
            if (rect.HasValue)
            {
                _windowTracking.AddHiddenPendingWindow(hwnd, rect.Value, hiddenUtc);
                _moveWindowOffscreen(hwnd);
            }
            else
            {
                _windowTracking.HiddenPendingAbsorb[hwnd] = hiddenUtc;
            }
        }

        public List<ExplorerWindowProcessRequest> PrepareProcessRequests(
            List<IntPtr> explorerWindows,
            Func<TabBarViewModel> findValidTarget)
        {
            List<ExplorerWindowProcessRequest> requests = new List<ExplorerWindowProcessRequest>();

            _windowTracking.AddHiddenPendingWindows(explorerWindows);
            _tabBars.RemoveInvalidWindows(explorerWindows);
            _windowTracking.CleanupClosedWindows(explorerWindows);

            for (int i = 0; i < explorerWindows.Count; i++)
            {
                IntPtr hwnd = explorerWindows[i];
                if (_tabBars.Contains(hwnd)) continue;
                if (_windowTracking.ProcessingExplorerWindows.Contains(hwnd)) continue;

                TabBarViewModel validTarget = findValidTarget != null ? findValidTarget() : null;
                if (_windowTracking.IgnoredWindows.Contains(hwnd))
                {
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
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }
    }
}
