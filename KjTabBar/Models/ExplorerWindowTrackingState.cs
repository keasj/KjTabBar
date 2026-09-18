using System;
using System.Collections.Generic;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ExplorerWindowTrackingState
    {
        private static readonly TimeSpan ExplicitIndependentLaunchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan InternalHostSwitchLaunchTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RecentClosedManagedExplorerRectRetention = TimeSpan.FromSeconds(10);
        private readonly Func<IntPtr, bool> _isWindow;
        private readonly Func<IntPtr, NativeMethods.WINDOWPLACEMENT?> _getWindowPlacement;
        private readonly Func<IntPtr, NativeMethods.WINDOWPLACEMENT, bool> _setWindowPlacement;
        private readonly Dictionary<IntPtr, NativeMethods.RECT> _hiddenNormalPositions = new Dictionary<IntPtr, NativeMethods.RECT>();
        private readonly Action<IntPtr> _showWindow;
        private readonly Action<IntPtr> _closeWindow;
        private readonly List<DateTime> _explicitIndependentLaunchRequests = new List<DateTime>();
        private readonly List<DateTime> _internalHostSwitchLaunchRequests = new List<DateTime>();
        private NativeMethods.RECT _recentClosedManagedExplorerRect;
        private DateTime _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
        private bool _hasRecentClosedManagedExplorerRect;
        private NativeMethods.WINDOWPLACEMENT? _recentClosedPlacement;
        private readonly Dictionary<IntPtr, NativeMethods.WINDOWPLACEMENT> _pendingRestorePlacements = new Dictionary<IntPtr, NativeMethods.WINDOWPLACEMENT>();

        public HashSet<IntPtr> IgnoredWindows { get; private set; }
        public HashSet<IntPtr> InternalHostSwitchLaunchWindows { get; private set; }
        public HashSet<IntPtr> ProcessingExplorerWindows { get; private set; }
        public Dictionary<IntPtr, int> AbsorbPathRetryCounts { get; private set; }
        public HashSet<IntPtr> DesktopLaunchCandidates { get; private set; }
        public HashSet<IntPtr> DesktopInteractiveLaunchCandidates { get; private set; }
        public HashSet<IntPtr> ControlPanelTabLaunchCandidates { get; private set; }
        public HashSet<IntPtr> ManagedControlPanelLaunchWindows { get; private set; }
        public HashSet<IntPtr> ExplicitIndependentLaunchWindows { get; private set; }
        public Dictionary<IntPtr, DateTime> HiddenPendingAbsorb { get; private set; }
        public Dictionary<IntPtr, NativeMethods.RECT> HiddenOriginalRects { get; private set; }
        public Dictionary<IntPtr, IntPtr> ParkedExplorerOrigins { get; private set; }
        internal readonly Dictionary<IntPtr, NativeMethods.RECT> DeferredOriginRestoreRects = new Dictionary<IntPtr, NativeMethods.RECT>();

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

        internal ExplorerWindowTrackingState(Func<IntPtr, bool> isWindow, Action<IntPtr> showWindow, Action<IntPtr> closeWindow,
            Func<IntPtr, NativeMethods.WINDOWPLACEMENT?> getWindowPlacement = null,
            Func<IntPtr, NativeMethods.WINDOWPLACEMENT, bool> setWindowPlacement = null)
        {
            _isWindow = isWindow ?? NativeMethods.IsWindow;
            _getWindowPlacement = getWindowPlacement ?? GetWindowPlacementCore;
            _setWindowPlacement = setWindowPlacement ?? SetWindowPlacementCore;
            _showWindow = showWindow ?? delegate (IntPtr hwnd) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW); };
            _closeWindow = closeWindow ?? delegate (IntPtr hwnd) { NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); };
            IgnoredWindows = new HashSet<IntPtr>();
            InternalHostSwitchLaunchWindows = new HashSet<IntPtr>();
            ProcessingExplorerWindows = new HashSet<IntPtr>();
            AbsorbPathRetryCounts = new Dictionary<IntPtr, int>();
            DesktopLaunchCandidates = new HashSet<IntPtr>();
            DesktopInteractiveLaunchCandidates = new HashSet<IntPtr>();
            ControlPanelTabLaunchCandidates = new HashSet<IntPtr>();
            ManagedControlPanelLaunchWindows = new HashSet<IntPtr>();
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
            if (!_hiddenNormalPositions.ContainsKey(hwnd))
            {
                NativeMethods.WINDOWPLACEMENT? placement = _getWindowPlacement(hwnd);
                if (placement.HasValue && NativeMethods.IsUsableWindowRestoreRect(placement.Value.rcNormalPosition))
                    _hiddenNormalPositions[hwnd] = placement.Value.rcNormalPosition;
            }
            AppLogger.LogDiagnostic("WindowRecovery", string.Format("Capture hwnd={0} original={1},{2},{3},{4} capturedNormal={5}", hwnd, originalRect.Left, originalRect.Top, originalRect.Right, originalRect.Bottom, _hiddenNormalPositions.ContainsKey(hwnd)));
            HiddenOriginalRects[hwnd] = originalRect;
            HiddenPendingAbsorb[hwnd] = hiddenUtc;
        }

        // Call only when the host is ready to be shown: SetWindowPlacement can show it.
        internal void RestoreNormalPositionBeforeShow(IntPtr hwnd)
        {
            AppLogger.LogDiagnostic("WindowRecovery", string.Format("Restore hwnd={0} pendingPlacement={1} capturedNormal={2}", hwnd, _pendingRestorePlacements.ContainsKey(hwnd), _hiddenNormalPositions.ContainsKey(hwnd)));
            AppLogger.LogDiagnosticPlacement("RestoreNormal.Before", hwnd);
            NativeMethods.WINDOWPLACEMENT savedPlacement;
            if (_pendingRestorePlacements.TryGetValue(hwnd, out savedPlacement))
            {
                savedPlacement.length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
                if (!_setWindowPlacement(hwnd, savedPlacement)) return;
                _pendingRestorePlacements.Remove(hwnd);
                _hiddenNormalPositions.Remove(hwnd);
                return;
            }
            NativeMethods.RECT originalNormalPosition;
            if (!_hiddenNormalPositions.TryGetValue(hwnd, out originalNormalPosition)) return;
            NativeMethods.WINDOWPLACEMENT? current = _getWindowPlacement(hwnd);
            if (!current.HasValue) return;
            NativeMethods.WINDOWPLACEMENT placement = current.Value;
            if (!NativeMethods.IsUsableWindowRestoreRect(placement.rcNormalPosition))
            {
                // Repair temporary offscreen bounds without changing the current show state.
                placement.rcNormalPosition = originalNormalPosition;
                if (!_setWindowPlacement(hwnd, placement)) return;
            }
            _hiddenNormalPositions.Remove(hwnd);
        }

        private static NativeMethods.WINDOWPLACEMENT? GetWindowPlacementCore(IntPtr hwnd)
        {
            NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            return NativeMethods.GetWindowPlacement(hwnd, ref placement) ? placement : (NativeMethods.WINDOWPLACEMENT?)null;
        }

        private static bool SetWindowPlacementCore(IntPtr hwnd, NativeMethods.WINDOWPLACEMENT placement)
        {
            return NativeMethods.SetWindowPlacement(hwnd, ref placement);
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
            RemoveClosedWindows(ManagedControlPanelLaunchWindows, explorerWindows);
            RemoveClosedWindows(ExplicitIndependentLaunchWindows, explorerWindows);
            RemoveClosedWindows(ProcessingExplorerWindows, explorerWindows);
            RemoveClosedWindowKeys(HiddenPendingAbsorb, explorerWindows);
            RemoveClosedWindowKeys(HiddenOriginalRects, explorerWindows);
            foreach (IntPtr capturedHwnd in _hiddenNormalPositions.Keys)
            {
                if (!ContainsWindow(explorerWindows, capturedHwnd))
                    AppLogger.LogDiagnostic("WindowRecovery", string.Format("DropCapture hwnd={0} alive={1}", capturedHwnd, _isWindow(capturedHwnd)));
            }
            RemoveClosedWindowKeys(_hiddenNormalPositions, explorerWindows);
            // Visible-window enumeration omits the hidden Home host during CP startup.
            List<IntPtr> pendingPlacementWindows = new List<IntPtr>(_pendingRestorePlacements.Keys);
            foreach (IntPtr pendingHwnd in pendingPlacementWindows)
            {
                if (!_isWindow(pendingHwnd)) _pendingRestorePlacements.Remove(pendingHwnd);
            }
            foreach (IntPtr pendingHwnd in new List<IntPtr>(DeferredOriginRestoreRects.Keys))
            {
                if (!_isWindow(pendingHwnd)) DeferredOriginRestoreRects.Remove(pendingHwnd);
            }
            RemoveClosedParkedExplorerOrigins(explorerWindows);
        }

        public void ClearAbsorptionState(IntPtr hwnd)
        {
            AbsorbPathRetryCounts.Remove(hwnd);
            DesktopLaunchCandidates.Remove(hwnd);
            DesktopInteractiveLaunchCandidates.Remove(hwnd);
            ControlPanelTabLaunchCandidates.Remove(hwnd);
            ManagedControlPanelLaunchWindows.Remove(hwnd);
        }

        public void RememberRecentClosedManagedExplorerRect(NativeMethods.RECT rect, DateTime closedUtc, NativeMethods.WINDOWPLACEMENT? placement = null)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            _recentClosedManagedExplorerRect = rect;
            _recentClosedManagedExplorerRectUtc = closedUtc;
            _hasRecentClosedManagedExplorerRect = true;
            _recentClosedPlacement = IsRestorablePlacement(placement) ? placement : null;
        }

        public bool TryTakeRecentClosedManagedExplorerRect(DateTime utcNow, out NativeMethods.RECT rect)
        {
            NativeMethods.WINDOWPLACEMENT? placement;
            return TryTakeRecentClosedManagedExplorerRect(utcNow, out rect, out placement);
        }

        internal bool TryTakeRecentClosedManagedExplorerRect(DateTime utcNow, out NativeMethods.RECT rect, out NativeMethods.WINDOWPLACEMENT? placement, bool restoringSavedControlPanel = false)
        {
            placement = null;
            rect = default(NativeMethods.RECT);
            if (!_hasRecentClosedManagedExplorerRect)
            {
                return false;
            }

            // A saved Control Panel tab must retain its host position beyond a quick reopen.
            if (!restoringSavedControlPanel && (utcNow - _recentClosedManagedExplorerRectUtc) > RecentClosedManagedExplorerRectRetention)
            {
                _hasRecentClosedManagedExplorerRect = false;
                _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
                _recentClosedManagedExplorerRect = default(NativeMethods.RECT);
                _recentClosedPlacement = null;
                return false;
            }

            rect = _recentClosedManagedExplorerRect;
            placement = _recentClosedPlacement;
            _recentClosedPlacement = null;
            _hasRecentClosedManagedExplorerRect = false;
            _recentClosedManagedExplorerRectUtc = DateTime.MinValue;
            _recentClosedManagedExplorerRect = default(NativeMethods.RECT);
            return rect.Width > 0 && rect.Height > 0;
        }

        internal static bool IsRestorablePlacement(NativeMethods.WINDOWPLACEMENT? placement)
        {
            return placement.HasValue && (placement.Value.showCmd == 1 || placement.Value.showCmd == 3) &&
                NativeMethods.IsUsableWindowRestoreRect(placement.Value.rcNormalPosition);
        }

        internal NativeMethods.WINDOWPLACEMENT? GetHostSwitchRestorePlacement(IntPtr hwnd)
        {
            NativeMethods.WINDOWPLACEMENT? current = _getWindowPlacement(hwnd);
            if (!current.HasValue) return null;
            NativeMethods.WINDOWPLACEMENT placement = current.Value;
            if (placement.showCmd == 2) // SW_SHOWMINIMIZED
            {
                // WPF_RESTORETOMAXIMIZED describes the state before minimization.
                placement.showCmd = (placement.flags & 2) != 0 ? 3u : 1u;
                placement.flags &= ~2u;
            }
            return IsRestorablePlacement(placement) ? placement : (NativeMethods.WINDOWPLACEMENT?)null;
        }

        internal void StageRestorePlacement(IntPtr hwnd, NativeMethods.WINDOWPLACEMENT? placement)
        {
            if (hwnd != IntPtr.Zero && IsRestorablePlacement(placement))
                _pendingRestorePlacements[hwnd] = placement.Value;
        }

        internal void TransferRestorePlacement(IntPtr originalHwnd, IntPtr replacementHwnd)
        {
            NativeMethods.WINDOWPLACEMENT placement;
            if (replacementHwnd == IntPtr.Zero || originalHwnd == replacementHwnd ||
                !_pendingRestorePlacements.TryGetValue(originalHwnd, out placement)) return;
            _pendingRestorePlacements.Remove(originalHwnd);
            _pendingRestorePlacements[replacementHwnd] = placement;
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

        public bool HasPendingInternalHostSwitchLaunchRequest()
        {
            RemoveExpiredInternalHostSwitchLaunchRequests(DateTime.UtcNow);
            return _internalHostSwitchLaunchRequests.Count > 0;
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
                    // The ordinary absorption timeout must not reveal a replacement
                    // host while the longer internal launch is still being prepared.
                    if (HiddenPendingAbsorb.TryGetValue(hiddenKeys[h], out hiddenTime) &&
                        (nowUtc - hiddenTime) > InternalHostSwitchLaunchTimeout)
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
                if (DeferredOriginRestoreRects.TryGetValue(hwnd, out originalRect) ||
                    HiddenOriginalRects.TryGetValue(hwnd, out originalRect))
                {
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, originalRect.Left, originalRect.Top, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
                    HiddenOriginalRects.Remove(hwnd);
                    DeferredOriginRestoreRects.Remove(hwnd);
                }
            }
        }

        public void RestoreAllHiddenWindows()
        {
            RestoreAllHiddenWindows(true);
        }

        public void RestoreAllHiddenWindows(bool restoreParkedExplorerWindows)
        {
            HashSet<IntPtr> restoreKeys = new HashSet<IntPtr>(HiddenPendingAbsorb.Keys);
            restoreKeys.UnionWith(DeferredOriginRestoreRects.Keys);
            IntPtr[] hiddenKeys = new IntPtr[restoreKeys.Count];
            restoreKeys.CopyTo(hiddenKeys);
            for (int i = 0; i < hiddenKeys.Length; i++)
            {
                try
                {
                    bool deferredOrigin = DeferredOriginRestoreRects.ContainsKey(hiddenKeys[i]);
                    RestoreHiddenWindow(hiddenKeys[i]);
                    if (deferredOrigin && restoreParkedExplorerWindows && _isWindow(hiddenKeys[i]))
                    {
                        RestoreNormalPositionBeforeShow(hiddenKeys[i]);
                        _showWindow(hiddenKeys[i]);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ExplorerWindowTrackingState", "Failed to restore a hidden explorer window.", ex);
                }
            }

            HiddenPendingAbsorb.Clear();
            HiddenOriginalRects.Clear();
            _hiddenNormalPositions.Clear();
            _pendingRestorePlacements.Clear();
            DeferredOriginRestoreRects.Clear();
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
                if ((nowUtc - _internalHostSwitchLaunchRequests[0]) <= InternalHostSwitchLaunchTimeout)
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
