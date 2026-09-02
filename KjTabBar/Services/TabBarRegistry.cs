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
            window.Closed += (s, e) =>
            {
                if (window.ExplorerHwnd != IntPtr.Zero)
                {
                    _tabBars.Remove(window.ExplorerHwnd);
                }
                else
                {
                    _tabBars.Remove(hwnd);
                }
            };
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
            List<TabBarWindow> windows = new List<TabBarWindow>(_tabBars.Values);
            _tabBars.Clear();

            for (int i = 0; i < windows.Count; i++)
            {
                try { windows[i].Close(); } catch (Exception ex) { AppLogger.LogError("TabBarRegistry", "Failed to close a tab bar window during exit.", ex); }
            }
        }

        public void RemoveInvalidWindows(List<IntPtr> explorerWindows, Action<TabBarViewModel> beforeClose = null)
        {
            List<IntPtr> toRemove = new List<IntPtr>();
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                bool shouldRemove = false;
                try
                {
                    if (!ContainsWindow(explorerWindows, kvp.Key))
                    {
                        TabBarViewModel currentViewModel = kvp.Value.DataContext as TabBarViewModel;
                        if (currentViewModel != null && ContainsWindow(explorerWindows, currentViewModel.ExplorerHwnd))
                        {
                            shouldRemove = false;
                        }
                        else
                        {
                            shouldRemove = !kvp.Value.IsExplorerAlive();
                        }
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
                    if (beforeClose != null)
                    {
                        try
                        {
                            TabBarViewModel viewModel = window.DataContext as TabBarViewModel;
                            if (viewModel != null)
                            {
                                beforeClose(viewModel);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogError("TabBarRegistry", "Failed to persist tab state before closing invalid tab bar window.", ex);
                        }
                    }

                    try { window.Close(); } catch (Exception ex) { AppLogger.LogError("TabBarRegistry", "Failed to close invalid tab bar window during cleanup.", ex); }
                    _tabBars.Remove(toRemove[i]);
                }
            }
        }

        public TabBarViewModel FindValidTarget(Func<IntPtr, bool> isForegroundRelatedWindow)
        {
            TabBarViewModel firstValidTarget = null;
            int aliveCount = 0;
            foreach (KeyValuePair<IntPtr, TabBarWindow> kvp in _tabBars)
            {
                TabBarViewModel viewModel;
                if (!TryGetAliveTabBarViewModel(kvp, out viewModel))
                {
                    continue;
                }

                aliveCount++;
                if (firstValidTarget == null)
                {
                    firstValidTarget = viewModel;
                }

                if (isForegroundRelatedWindow != null && isForegroundRelatedWindow(viewModel.ExplorerHwnd))
                {
                    return viewModel;
                }
            }

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

            return false;
        }

        private static bool TryGetAliveTabBarViewModel(KeyValuePair<IntPtr, TabBarWindow> entry, out TabBarViewModel viewModel)
        {
            viewModel = null;
            try
            {
                if (!entry.Value.IsExplorerAlive())
                {
                    return false;
                }

                viewModel = entry.Value.DataContext as TabBarViewModel;
                if (viewModel == null)
                {
                    return false;
                }

                return NativeMethods.IsWindow(viewModel.ExplorerHwnd);
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
