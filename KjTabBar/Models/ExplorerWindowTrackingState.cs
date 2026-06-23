using System;
using System.Collections.Generic;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ExplorerWindowTrackingState
    {
        private static readonly TimeSpan ExplicitIndependentLaunchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RecentClosedManagedExplorerRectRetention = TimeSpan.FromSeconds(10);
        private readonly Func<IntPtr, bool> _isWindow;
        private readonly Action<IntPtr> _showWindow;
        private readonly Action<IntPtr> _closeWindow;
        private readonly List<DateTime> _explicitIndependentLaunchRequests = new List<DateTime>();
        private readonly List<DateTime> _internalHostSwitchLaunchRequests = new List<DateTime>();
        private NativeMethods.RECT _recentClosedManagedExplorerRect;
        private DateTime _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
        private bool _hasRecentClosedManagedExplorerRect;

        public HashSet<IntPtr> IgnoredWindows { get; private set; }
        public HashSet<IntPtr> InternalHostSwitchLaunchWindows { get; private set; }
        public HashSet<IntPtr> ProcessingExplorerWindows { get; private set; }
        public Dictionary<IntPtr, int> AbsorbPathRetryCounts { get; private set; }
        public HashSet<IntPtr> DesktopLaunchCandidates { get; private set; }
        public HashSet<IntPtr> DesktopInteractiveLaunchCandidates { get; private set; }
        public HashSet<IntPtr> ControlPanelTabLaunchCandidates { get; private set; }
        public HashSet<IntPtr> ExplicitIndependentLaunchWindows { get; private set; }
        public Dictionary<IntPtr, DateTime> HiddenPendingAbsorb { get; private set; }
        public Dictionary<IntPtr, NativeMethods.RECT> HiddenOriginalRects { get; private set; }
        public Dictionary<IntPtr, IntPtr> ParkedExplorerOrigins { get; private set; }

        public ExplorerWindowTrackingState()
            : this(
                  NativeMethods.IsWindow,
                  delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); },
                  delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); })
        {
        }

        internal ExplorerWindowTrackingState(Func<IntPtr, bool> isWindow)
            : this(
                  isWindow,
                  delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); },
                  delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); })
        {
        }

        internal ExplorerWindowTrackingState(Func<IntPtr, bool> isWindow, Action<IntPtr> showWindow, Action<IntPtr> closeWindow)
        {
            _isWindow = isWindow ?? NativeMethods.IsWindow;
            _showWindow = showWindow ?? delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); };
            _closeWindow = closeWindow ?? delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); };
            IgnoredWindows = new HashSet<IntPtr>();
            InternalHostSwitchLaunchWindows = new HashSet<IntPtr>();
            ProcessingExplorerWindows = new HashSet<IntPtr>();
            AbsorbPathRetryCounts = new Dictionary<IntPtr, int>();
            DesktopLaunchCandidates = new HashSet<IntPtr>();
            DesktopInteractiveLaunchCandidates = new HashSet<IntPtr>();
            ControlPanelTabLaunchCandidates = new HashSet<IntPtr>();
            ExplicitIndependentLaunchWindows = new HashSet<IntPtr>();
            HiddenPendingAbsorb = new Dictionary<IntPtr, DateTime>();
            HiddenOriginalRects = new Dictionary<IntPtr, NativeMethods.RECT>();
            ParkedExplorerOrigins = new Dictionary<IntPtr, IntPtr>();
        }

        public bool IsAbsorbDecisionPending(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            return AbsorbPathRetryCounts.ContainsKey(hwnd);
        }

        public void AddHiddenPendingWindow(IntPtr hwnd, NativeMethods.RECT originalRect, DateTime hiddenUtc)
        {
            HiddenOriginalRects[hwnd] = originalRect;
            HiddenPendingAbsorb[hwnd] = hiddenUtc;
        }

        public void AddHiddenPendingWindows(List<IntPtr> explorerWindows)
        {
            IntPtr[] hiddenKeys = new IntPtr[HiddenPendingAbsorb.Count];
            HiddenPendingAbsorb.Keys.CopyTo(hiddenKeys, 0);
            for (int h = 0; h < hiddenKeys.Length; h++)
            {
                if (!ContainsWindow(explorerWindows, hiddenKeys[h]))
                {
                    explorerWindows.Add(hiddenKeys[h]);
                }
            }
        }

        public void CleanupClosedWindows(List<IntPtr> explorerWindows)
        {
            RemoveClosedWindows(IgnoredWindows, explorerWindows);
            RemoveClosedWindows(InternalHostSwitchLaunchWindows, explorerWindows);
            RemoveClosedWindowKeys(AbsorbPathRetryCounts, explorerWindows);
            RemoveClosedWindows(DesktopLaunchCandidates, explorerWindows);
            RemoveClosedWindows(DesktopInteractiveLaunchCandidates, explorerWindows);
            RemoveClosedWindows(ControlPanelTabLaunchCandidates, explorerWindows);
            RemoveClosedWindows(ExplicitIndependentLaunchWindows, explorerWindows);
            RemoveClosedWindows(ProcessingExplorerWindows, explorerWindows);
            RemoveClosedWindowKeys(HiddenPendingAbsorb, explorerWindows);
            RemoveClosedWindowKeys(HiddenOriginalRects, explorerWindows);
            RemoveClosedParkedExplorerOrigins(explorerWindows);
        }

        public void ClearAbsorptionState(IntPtr hwnd)
        {
            AbsorbPathRetryCounts.Remove(hwnd);
            DesktopLaunchCandidates.Remove(hwnd);
            DesktopInteractiveLaunchCandidates.Remove(hwnd);
            ControlPanelTabLaunchCandidates.Remove(hwnd);
        }

        public void RememberRecentClosedManagedExplorerRect(NativeMethods.RECT rect, DateTime closedUtc)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            _recentClosedManagedExplorerRect = rect;
            _recentClosedManagedExplorerRectUtc = closedUtc;
            _hasRecentClosedManagedExplorerRect = true;
        }

        public bool TryTakeRecentClosedManagedExplorerRect(DateTime utcNow, out NativeMethods.RECT rect)
        {
            rect = default(NativeMethods.RECT);
            if (!_hasRecentClosedManagedExplorerRect)
            {
                return false;
            }

            if ((utcNow - _recentClosedManagedExplorerRectUtc) > RecentClosedManagedExplorerRectRetention)
            {
                _hasRecentClosedManagedExplorerRect = false;
                _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
                _recentClosedManagedExplorerRect = default(NativeMethods.RECT);
                return false;
            }

            rect = _recentClosedManagedExplorerRect;
            _hasRecentClosedManagedExplorerRect = false;
            _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
            _recentClosedManagedExplorerRect = default(NativeMethods.RECT);
            return rect.Width > 0 && rect.Height > 0;
        }

        public void IgnoreWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            ClearAbsorptionState(hwnd);
            InternalHostSwitchLaunchWindows.Remove(hwnd);
            ClearParkedExplorerOrigin(hwnd);
            RemoveParkedExplorerOriginValue(hwnd);
            IgnoredWindows.Add(hwnd);
        }

        public void IgnoreExplicitIndependentLaunchWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            IgnoreWindow(hwnd);
            ExplicitIndependentLaunchWindows.Add(hwnd);
        }

        public void RegisterExplicitIndependentLaunchRequest()
        {
            _explicitIndependentLaunchRequests.Add(DateTime.UtcNow);
        }

        public void RegisterInternalHostSwitchLaunchRequest()
        {
            _internalHostSwitchLaunchRequests.Add(DateTime.UtcNow);
        }

        public void CancelExplicitIndependentLaunchRequest()
        {
            RemoveExpiredExplicitIndependentLaunchRequests(DateTime.UtcNow);
            if (_explicitIndependentLaunchRequests.Count <= 0)
            {
                return;
            }

            _explicitIndependentLaunchRequests.RemoveAt(_explicitIndependentLaunchRequests.Count - 1);
        }

        public bool TryConsumeExplicitIndependentLaunchRequest()
        {
            RemoveExpiredExplicitIndependentLaunchRequests(DateTime.UtcNow);
            if (_explicitIndependentLaunchRequests.Count <= 0)
            {
                return false;
            }

            _explicitIndependentLaunchRequests.RemoveAt(0);
            return true;
        }

        public void CancelInternalHostSwitchLaunchRequest()
        {
            RemoveExpiredInternalHostSwitchLaunchRequests(DateTime.UtcNow);
            if (_internalHostSwitchLaunchRequests.Count <= 0)
            {
                return;
            }

            _internalHostSwitchLaunchRequests.RemoveAt(_internalHostSwitchLaunchRequests.Count - 1);
        }

        public bool TryConsumeInternalHostSwitchLaunchRequest()
        {
            RemoveExpiredInternalHostSwitchLaunchRequests(DateTime.UtcNow);
            if (_internalHostSwitchLaunchRequests.Count <= 0)
            {
                return false;
            }

            _internalHostSwitchLaunchRequests.RemoveAt(0);
            return true;
        }

        public void MarkInternalHostSwitchLaunchWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            InternalHostSwitchLaunchWindows.Add(hwnd);
        }

        public void ClearInternalHostSwitchLaunchWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            InternalHostSwitchLaunchWindows.Remove(hwnd);
        }

        public void MarkAbsorbedWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            ClearAbsorptionState(hwnd);
            HiddenPendingAbsorb.Remove(hwnd);
            ExplicitIndependentLaunchWindows.Remove(hwnd);
            InternalHostSwitchLaunchWindows.Remove(hwnd);
            ClearParkedExplorerOrigin(hwnd);
            RemoveParkedExplorerOriginValue(hwnd);
            IgnoredWindows.Add(hwnd);
        }

        public void RememberParkedExplorerOrigin(IntPtr controlPanelExplorerHwnd, IntPtr originalExplorerHwnd)
        {
            if (controlPanelExplorerHwnd == IntPtr.Zero || originalExplorerHwnd == IntPtr.Zero)
            {
                return;
            }

            ParkedExplorerOrigins[controlPanelExplorerHwnd] = originalExplorerHwnd;
            AppLogger.LogInfo(
                "ExplorerWindowTrackingState",
                string.Format(
                    "RememberParkedExplorerOrigin controlPanel={0} original={1} map={2}",
                    controlPanelExplorerHwnd,
                    originalExplorerHwnd,
                    GetParkedExplorerOriginsSnapshot()));
        }

        public bool TryGetParkedExplorerOrigin(IntPtr controlPanelExplorerHwnd, out IntPtr originalExplorerHwnd)
        {
            if (controlPanelExplorerHwnd != IntPtr.Zero &&
                ParkedExplorerOrigins.TryGetValue(controlPanelExplorerHwnd, out originalExplorerHwnd))
            {
                return true;
            }

            originalExplorerHwnd = IntPtr.Zero;
            return false;
        }

        public void ClearParkedExplorerOrigin(IntPtr controlPanelExplorerHwnd)
        {
            if (controlPanelExplorerHwnd == IntPtr.Zero)
            {
                return;
            }

            ParkedExplorerOrigins.Remove(controlPanelExplorerHwnd);
            AppLogger.LogInfo(
                "ExplorerWindowTrackingState",
                string.Format(
                    "ClearParkedExplorerOrigin controlPanel={0} map={1}",
                    controlPanelExplorerHwnd,
                    GetParkedExplorerOriginsSnapshot()));
        }

        public void CloseParkedExplorerOrigin(IntPtr controlPanelExplorerHwnd)
        {
            IntPtr currentHwnd = controlPanelExplorerHwnd;
            while (currentHwnd != IntPtr.Zero)
            {
                IntPtr originalExplorerHwnd;
                if (TryGetParkedExplorerOrigin(currentHwnd, out originalExplorerHwnd))
                {
                    if (originalExplorerHwnd != IntPtr.Zero && _isWindow(originalExplorerHwnd))
                    {
                        try
                        {
                            _closeWindow(originalExplorerHwnd);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogError("ExplorerWindowTrackingState", "Failed to close a parked explorer window on origin close.", ex);
                        }
                    }
                    ClearParkedExplorerOrigin(currentHwnd);
                    currentHwnd = originalExplorerHwnd;
                }
                else
                {
                    break;
                }
            }
        }

        public bool IsParkedExplorerOriginValue(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            foreach (KeyValuePair<IntPtr, IntPtr> kvp in ParkedExplorerOrigins)
            {
                if (kvp.Value == explorerHwnd)
                {
                    return true;
                }
            }

            return false;
        }

        public List<IntPtr> GetHiddenWindowsToRestore(TimeSpan maxHiddenDuration, DateTime nowUtc)
        {
            List<IntPtr> hiddenToRestore = new List<IntPtr>();
            IntPtr[] hiddenKeys = new IntPtr[HiddenPendingAbsorb.Count];
            HiddenPendingAbsorb.Keys.CopyTo(hiddenKeys, 0);
            for (int h = 0; h < hiddenKeys.Length; h++)
            {
                bool shouldRestore = false;

                if (IgnoredWindows.Contains(hiddenKeys[h]))
                {
                    shouldRestore = true;
                }
                else if (InternalHostSwitchLaunchWindows.Contains(hiddenKeys[h]))
                {
                    DateTime hiddenTime;
                    if (HiddenPendingAbsorb.TryGetValue(hiddenKeys[h], out hiddenTime) &&
                        (nowUtc - hiddenTime) > maxHiddenDuration)
                    {
                        shouldRestore = true;
                    }
                }
                else
                {
                    DateTime hiddenTime;
                    if (HiddenPendingAbsorb.TryGetValue(hiddenKeys[h], out hiddenTime))
                    {
                        if (!IsAbsorbDecisionPending(hiddenKeys[h]) && (nowUtc - hiddenTime) > maxHiddenDuration)
                        {
                            shouldRestore = true;
                        }
                    }
                }

                if (shouldRestore)
                {
                    hiddenToRestore.Add(hiddenKeys[h]);
                }
            }

            return hiddenToRestore;
        }

        public void RestoreHiddenWindow(IntPtr hwnd)
        {
            HiddenPendingAbsorb.Remove(hwnd);
            InternalHostSwitchLaunchWindows.Remove(hwnd);
            if (NativeMethods.IsWindow(hwnd))
            {
                NativeMethods.RECT originalRect;
                if (HiddenOriginalRects.TryGetValue(hwnd, out originalRect))
                {
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, originalRect.Left, originalRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                    HiddenOriginalRects.Remove(hwnd);
                }
            }
        }

        public void RestoreAllHiddenWindows()
        {
            RestoreAllHiddenWindows(true);
        }

        public void RestoreAllHiddenWindows(bool restoreParkedExplorerWindows)
        {
            IntPtr[] hiddenKeys = new IntPtr[HiddenPendingAbsorb.Count];
            HiddenPendingAbsorb.Keys.CopyTo(hiddenKeys, 0);
            for (int i = 0; i < hiddenKeys.Length; i++)
            {
                try
                {
                    RestoreHiddenWindow(hiddenKeys[i]);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ExplorerWindowTrackingState", "Failed to restore a hidden explorer window.", ex);
                }
            }

            HiddenPendingAbsorb.Clear();
            HiddenOriginalRects.Clear();
            if (restoreParkedExplorerWindows)
            {
                RestoreAllParkedExplorerWindows();
            }
            else
            {
                CloseAllParkedExplorerWindows();
            }
        }

        public void RestoreAllParkedExplorerWindows()
        {
            HashSet<IntPtr> parkedOrigins = new HashSet<IntPtr>(ParkedExplorerOrigins.Values);
            foreach (IntPtr hwnd in parkedOrigins)
            {
                try
                {
                    if (_isWindow(hwnd))
                    {
                        _showWindow(hwnd);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ExplorerWindowTrackingState", "Failed to restore a parked explorer window.", ex);
                }
            }

            ParkedExplorerOrigins.Clear();
        }

        public void CloseAllParkedExplorerWindows()
        {
            HashSet<IntPtr> parkedOrigins = new HashSet<IntPtr>(ParkedExplorerOrigins.Values);
            foreach (IntPtr hwnd in parkedOrigins)
            {
                try
                {
                    if (_isWindow(hwnd))
                    {
                        _closeWindow(hwnd);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ExplorerWindowTrackingState", "Failed to close a parked explorer window.", ex);
                }
            }

            ParkedExplorerOrigins.Clear();
        }

        private void RemoveExpiredExplicitIndependentLaunchRequests(DateTime nowUtc)
        {
            while (_explicitIndependentLaunchRequests.Count > 0)
            {
                if ((nowUtc - _explicitIndependentLaunchRequests[0]) <= ExplicitIndependentLaunchTimeout)
                {
                    break;
                }

                _explicitIndependentLaunchRequests.RemoveAt(0);
            }
        }

        private void RemoveExpiredInternalHostSwitchLaunchRequests(DateTime nowUtc)
        {
            while (_internalHostSwitchLaunchRequests.Count > 0)
            {
                if ((nowUtc - _internalHostSwitchLaunchRequests[0]) <= ExplicitIndependentLaunchTimeout)
                {
                    break;
                }

                _internalHostSwitchLaunchRequests.RemoveAt(0);
            }
        }

        private static void RemoveClosedWindows(HashSet<IntPtr> collection, List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (IntPtr item in collection)
            {
                if (!ContainsWindow(explorerWindows, item))
                {
                    toRemove.Add(item);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                collection.Remove(toRemove[i]);
            }
        }

        private static void RemoveClosedWindowKeys<TValue>(Dictionary<IntPtr, TValue> collection, List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TValue> kvp in collection)
            {
                if (!ContainsWindow(explorerWindows, kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                collection.Remove(toRemove[i]);
            }
        }

        private void RemoveClosedParkedExplorerOrigins(List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, IntPtr> kvp in ParkedExplorerOrigins)
            {
                if (!IsTrackedWindowOpen(explorerWindows, kvp.Key) || !IsTrackedWindowOpen(explorerWindows, kvp.Value))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                ParkedExplorerOrigins.Remove(toRemove[i]);
            }

            if (toRemove.Count > 0)
            {
                AppLogger.LogInfo(
                    "ExplorerWindowTrackingState",
                    string.Format(
                        "RemoveClosedParkedExplorerOrigins removed={0} map={1}",
                        string.Join(",", toRemove),
                        GetParkedExplorerOriginsSnapshot()));
            }
        }

        private void RemoveParkedExplorerOriginValue(IntPtr originalExplorerHwnd)
        {
            if (originalExplorerHwnd == IntPtr.Zero)
            {
                return;
            }

            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, IntPtr> kvp in ParkedExplorerOrigins)
            {
                if (kvp.Value == originalExplorerHwnd)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                ParkedExplorerOrigins.Remove(toRemove[i]);
            }

            if (toRemove.Count > 0)
            {
                AppLogger.LogInfo(
                    "ExplorerWindowTrackingState",
                    string.Format(
                        "RemoveParkedExplorerOriginValue original={0} removed={1} map={2}",
                        originalExplorerHwnd,
                        string.Join(",", toRemove),
                        GetParkedExplorerOriginsSnapshot()));
            }
        }

        private bool IsTrackedWindowOpen(List<IntPtr> explorerWindows, IntPtr hwnd)
        {
            if (ContainsWindow(explorerWindows, hwnd))
            {
                return true;
            }

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            return _isWindow(hwnd);
        }

        private static bool ContainsWindow(List<IntPtr> explorerWindows, IntPtr hwnd)
        {
            for (int i = 0; i < explorerWindows.Count; i++)
            {
                if (explorerWindows[i] == hwnd)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetParkedExplorerOriginsSnapshot()
        {
            if (ParkedExplorerOrigins.Count <= 0)
            {
                return "<empty>";
            }

            List<string> entries = new List<string>();
            foreach (KeyValuePair<IntPtr, IntPtr> kvp in ParkedExplorerOrigins)
            {
                entries.Add(kvp.Key.ToString() + "->" + kvp.Value.ToString());
            }

            return string.Join(";", entries);
        }
    }
}
