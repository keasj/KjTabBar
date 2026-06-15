using System;
using System.Collections.Generic;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar.Services
{
    internal sealed class TabBarRegistry
    {
        private readonly Dictionary<IntPtr, TabBarWindow> _tabBars = new Dictionary<IntPtr, TabBarWindow>();

        public bool Contains(IntPtr hwnd)
        {
            return _tabBars.ContainsKey(hwnd);
        }

        public bool TryGetTabBarWindow(IntPtr explorerHwnd, out TabBarWindow window)
        {
            if (explorerHwnd == IntPtr.Zero)
            {
                window = null;
                return false;
            }
            return _tabBars.TryGetValue(explorerHwnd, out window);
        }

        public void Add(IntPtr hwnd, TabBarWindow window)
        {
            _tabBars[hwnd] = window;
        }

        public bool RebindExplorerWindow(TabBarViewModel viewModel, IntPtr newExplorerHwnd)
        {
            if (viewModel == null || newExplorerHwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr previousHwnd = IntPtr.Zero;
            TabBarWindow window = null;

            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                if (ReferenceEquals(kvp.Value != null ? kvp.Value.DataContext : null, viewModel))
                {
                    previousHwnd = kvp.Key;
                    window = kvp.Value;
                    break;
                }
            }

            if (window == null)
            {
                return false;
            }

            if (previousHwnd != newExplorerHwnd)
            {
                _tabBars.Remove(previousHwnd);
                _tabBars[newExplorerHwnd] = window;
            }

            window.RebindExplorer(newExplorerHwnd);
            return true;
        }

        public void ClearAndCloseAll()
        {
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                try { kvp.Value.Close(); } catch (Exception ex) { AppLogger.LogError("TabBarRegistry", "Failed to close a tab bar window during exit.", ex); }
            }
            _tabBars.Clear();
        }

        public void RemoveInvalidWindows(List<IntPtr> explorerWindows)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                bool shouldRemove = false;
                try
                {
                    if (!ContainsWindow(explorerWindows, kvp.Key))
                    {
                        shouldRemove = true;
                    }
                    else if (!kvp.Value.IsExplorerAlive())
                    {
                        shouldRemove = true;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("TabBarRegistry", "Detected invalid tab bar window during cleanup.", ex);
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                TabBarWindow window;
                if (_tabBars.TryGetValue(toRemove[i], out window))
                {
                    try { window.Close(); } catch (Exception ex) { AppLogger.LogError("TabBarRegistry", "Failed to close invalid tab bar window during cleanup.", ex); }
                    _tabBars.Remove(toRemove[i]);
                }
            }
        }

        public TabBarViewModel FindValidTarget(Func<IntPtr, bool> isForegroundRelatedWindow)
        {
            TabBarViewModel firstValidTarget = null;
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel viewModel;
                if (!TryGetAliveTabBarViewModel(kvp, out viewModel))
                {
                    AppLogger.LogInfo(
                        "TabBarRegistry",
                        string.Format("FindValidTarget skipped entry key={0}", kvp.Key));
                    continue;
                }

                if (firstValidTarget == null)
                {
                    firstValidTarget = viewModel;
                }

                if (isForegroundRelatedWindow != null && isForegroundRelatedWindow(viewModel.ExplorerHwnd))
                {
                    return viewModel;
                }
            }

            AppLogger.LogInfo(
                "TabBarRegistry",
                string.Format(
                    "FindValidTarget returning={0} count={1}",
                    firstValidTarget != null ? firstValidTarget.ExplorerHwnd.ToString() : string.Empty,
                    _tabBars.Count));

            return firstValidTarget;
        }

        public List<TabBarViewModel> GetAliveViewModels()
        {
            List<TabBarViewModel> viewModels = new List<TabBarViewModel>();
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel viewModel;
                if (TryGetAliveTabBarViewModel(kvp, out viewModel))
                {
                    viewModels.Add(viewModel);
                }
            }
            return viewModels;
        }

        public bool TryFindAliveViewModel(IntPtr explorerHwnd, out TabBarViewModel viewModel)
        {
            viewModel = null;
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel current;
                if (!TryGetAliveTabBarViewModel(kvp, out current))
                {
                    continue;
                }

                if (current.ExplorerHwnd == explorerHwnd)
                {
                    viewModel = current;
                    return true;
                }
            }

            AppLogger.LogInfo(
                "TabBarRegistry",
                string.Format("TryFindAliveViewModel missed explorerHwnd={0} count={1}", explorerHwnd, _tabBars.Count));

            return false;
        }

        private static bool TryGetAliveTabBarViewModel(KeyValuePair<IntPtr, TabBarWindow> entry, out TabBarViewModel viewModel)
        {
            viewModel = null;
            try
            {
                if (!entry.Value.IsExplorerAlive())
                {
                    AppLogger.LogInfo(
                        "TabBarRegistry",
                        string.Format("TryGetAliveTabBarViewModel explorer not alive key={0}", entry.Key));
                    return false;
                }

                viewModel = entry.Value.DataContext as TabBarViewModel;
                if (viewModel == null)
                {
                    AppLogger.LogInfo(
                        "TabBarRegistry",
                        string.Format("TryGetAliveTabBarViewModel no DataContext key={0}", entry.Key));
                    return false;
                }

                bool isWindow = NativeMethods.IsWindow(viewModel.ExplorerHwnd);
                if (!isWindow)
                {
                    AppLogger.LogInfo(
                        "TabBarRegistry",
                        string.Format(
                            "TryGetAliveTabBarViewModel invalid ExplorerHwnd key={0} vm={1}",
                            entry.Key,
                            viewModel.ExplorerHwnd));
                }

                return isWindow;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarRegistry", "TryGetAliveTabBarViewModel failed.", ex);
                viewModel = null;
                return false;
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
