using System;
using System.Collections.Generic;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ExplorerWindowTrackingState
    {
        public HashSet<IntPtr> IgnoredWindows { get; private set; }
        public HashSet<IntPtr> ProcessingExplorerWindows { get; private set; }
        public Dictionary<IntPtr, int> AbsorbPathRetryCounts { get; private set; }
        public HashSet<IntPtr> DesktopLaunchCandidates { get; private set; }
        public HashSet<IntPtr> DesktopInteractiveLaunchCandidates { get; private set; }
        public HashSet<IntPtr> ControlPanelTabLaunchCandidates { get; private set; }
        public Dictionary<IntPtr, DateTime> HiddenPendingAbsorb { get; private set; }
        public Dictionary<IntPtr, NativeMethods.RECT> HiddenOriginalRects { get; private set; }

        public ExplorerWindowTrackingState()
        {
            IgnoredWindows = new HashSet<IntPtr>();
            ProcessingExplorerWindows = new HashSet<IntPtr>();
            AbsorbPathRetryCounts = new Dictionary<IntPtr, int>();
            DesktopLaunchCandidates = new HashSet<IntPtr>();
            DesktopInteractiveLaunchCandidates = new HashSet<IntPtr>();
            ControlPanelTabLaunchCandidates = new HashSet<IntPtr>();
            HiddenPendingAbsorb = new Dictionary<IntPtr, DateTime>();
            HiddenOriginalRects = new Dictionary<IntPtr, NativeMethods.RECT>();
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
            RemoveClosedWindowKeys(AbsorbPathRetryCounts, explorerWindows);
            RemoveClosedWindows(DesktopLaunchCandidates, explorerWindows);
            RemoveClosedWindows(DesktopInteractiveLaunchCandidates, explorerWindows);
            RemoveClosedWindows(ControlPanelTabLaunchCandidates, explorerWindows);
            RemoveClosedWindows(ProcessingExplorerWindows, explorerWindows);
            RemoveClosedWindowKeys(HiddenPendingAbsorb, explorerWindows);
            RemoveClosedWindowKeys(HiddenOriginalRects, explorerWindows);
        }

        public void ClearAbsorptionState(IntPtr hwnd)
        {
            AbsorbPathRetryCounts.Remove(hwnd);
            DesktopLaunchCandidates.Remove(hwnd);
            DesktopInteractiveLaunchCandidates.Remove(hwnd);
            ControlPanelTabLaunchCandidates.Remove(hwnd);
        }

        public void IgnoreWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            ClearAbsorptionState(hwnd);
            IgnoredWindows.Add(hwnd);
        }
        public void MarkAbsorbedWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            ClearAbsorptionState(hwnd);
            HiddenPendingAbsorb.Remove(hwnd);
            IgnoredWindows.Add(hwnd);
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
    }
}