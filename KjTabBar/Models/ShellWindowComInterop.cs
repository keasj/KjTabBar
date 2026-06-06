using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellWindowComInterop
    {
        private static NativeMethods.EnumWindowsProc _enumWindowsProc = EnumWindowsCallback;
        private readonly ShellWindowCacheManager _cacheManager = new ShellWindowCacheManager();

        private readonly ShellExplorerWindowMatcher _shellExplorerWindowMatcher;
        private readonly ShellFolderPathReader _shellFolderPathReader;
        private readonly ShellCurrentPathResolver _shellCurrentPathResolver;
        private readonly ShellSelectedItemsReader _shellSelectedItemsReader;
        private readonly ShellFolderItemSelectionHelper _shellFolderItemSelectionHelper;
        private readonly ShellWindowNavigator _shellWindowNavigator;
        private readonly Func<string, string> _getNavigableShellPath;
        private readonly Func<string, bool> _isNavigablePath;

        public ShellWindowComInterop(
            ShellExplorerWindowMatcher shellExplorerWindowMatcher,
            ShellFolderPathReader shellFolderPathReader,
            ShellCurrentPathResolver shellCurrentPathResolver,
            ShellSelectedItemsReader shellSelectedItemsReader,
            ShellFolderItemSelectionHelper shellFolderItemSelectionHelper,
            ShellWindowNavigator shellWindowNavigator,
            Func<string, string> getNavigableShellPath,
            Func<string, bool> isNavigablePath)
        {
            _shellExplorerWindowMatcher = shellExplorerWindowMatcher;
            _shellFolderPathReader = shellFolderPathReader;
            _shellCurrentPathResolver = shellCurrentPathResolver;
            _shellSelectedItemsReader = shellSelectedItemsReader;
            _shellFolderItemSelectionHelper = shellFolderItemSelectionHelper;
            _shellWindowNavigator = shellWindowNavigator;
            _getNavigableShellPath = getNavigableShellPath;
            _isNavigablePath = isNavigablePath;
        }

        internal static object GetComProperty(object obj, string propertyName)
        {
            if (obj == null) return null;
            try { return obj.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, obj, null); }
            catch { return null; }
        }

        internal static object InvokeComMethod(object obj, string methodName, params object[] args)
        {
            if (obj == null) return null;
            try { return obj.GetType().InvokeMember(methodName, System.Reflection.BindingFlags.InvokeMethod, null, obj, args); }
            catch { return null; }
        }

        internal static void ReleaseComObjectSafe(object comObject)
        {
            ShellWindowCacheManager.ReleaseComObjectSafe(comObject);
        }

        public static bool TryGetShellApplication(out object shellObject)
        {
            return ShellWindowCacheManager.TryGetShellApplication(out shellObject);
        }

        public void ReleaseCachedComObjects()
        {
            _cacheManager.ReleaseCachedComObjects();
        }

        private static bool EnumWindowsCallback(IntPtr hwnd, IntPtr lParam)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, 256);
            if (className.ToString() == "CabinetWClass")
            {
                GCHandle handle = GCHandle.FromIntPtr(lParam);
                List<IntPtr> result = (List<IntPtr>)handle.Target;
                result.Add(hwnd);
            }
            return true;
        }

        public List<IntPtr> FindExplorerWindows()
        {
            List<IntPtr> result = new List<IntPtr>();
            GCHandle handle = GCHandle.Alloc(result);
            try
            {
                NativeMethods.EnumWindows(_enumWindowsProc, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
            return result;
        }

        public string GetCurrentPath(IntPtr explorerHwnd)
        {
            string result = null;
            object windowsObject = null;
            if (!_cacheManager.TryCreateShellWindows(out windowsObject))
            {
                return null;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return null;
                int count = 0;
                try { count = Convert.ToInt32(countObj); }
                catch (Exception ex)
                {
                    AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetCurrentPathCountConvert", "Failed to convert Shell Windows count while getting current path.", ex, TimeSpan.FromMinutes(5));
                    return null;
                }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetCurrentPathFullName", "Failed to read FullName while getting current path.", ex, TimeSpan.FromMinutes(5)); }
                        if (!_shellExplorerWindowMatcher.IsExplorerWindow(window, fullName)) continue;

                        IntPtr hwnd;
                        if (!_shellExplorerWindowMatcher.TryGetWindowHwnd(window, out hwnd))
                        {
                            AppLogger.LogError("ShellWindowComInterop", "Failed to convert HWND while navigating explorer.", new InvalidOperationException("Failed to convert Shell window HWND."));
                            continue;
                        }

                        if (!_shellExplorerWindowMatcher.MatchesTargetWindow(hwnd, explorerHwnd)) continue;

                        string locationUrl = "";
                        try { locationUrl = (string)GetComProperty(window, "LocationURL"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetCurrentPathLocationUrl", "Failed to read LocationURL while getting current path.", ex, TimeSpan.FromMinutes(5)); }

                        string locationName = "";
                        try { locationName = (string)GetComProperty(window, "LocationName"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetCurrentPathLocationName", "Failed to read LocationName while getting current path.", ex, TimeSpan.FromMinutes(5)); }

                        string folderPath = null;
                        try
                        {
                            folderPath = _shellFolderPathReader.ReadFolderPath(window);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetCurrentPathDocumentPath", "Failed to read folder path from explorer document.", ex, TimeSpan.FromMinutes(5));
                        }
                        result = _shellCurrentPathResolver.Resolve(locationUrl, locationName, folderPath);
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogErrorThrottled("ShellWindowComInterop", "NavigateWindowEnumerate", "Failed while enumerating a Shell window during navigation.", ex, TimeSpan.FromMinutes(5));
                    }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowComInterop", "Navigate failed and Shell.Application cache will be reset.", ex);
                ShellWindowCacheManager.ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                _cacheManager.RunPeriodicComCleanup();
            }
            return result;
        }

        public List<string> GetSelectedItems(IntPtr explorerHwnd)
        {
            List<string> selectedItems = new List<string>();
            object windowsObject = null;
            if (!_cacheManager.TryCreateShellWindows(out windowsObject))
            {
                return selectedItems;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return selectedItems;
                int count = 0;
                try { count = Convert.ToInt32(countObj); }
                catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetSelectedItemsCountConvert", "Failed to convert Shell Windows count while getting selected items.", ex, TimeSpan.FromMinutes(5)); return selectedItems; }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetSelectedItemsFullName", "Failed to read FullName while getting selected items.", ex, TimeSpan.FromMinutes(5)); }
                        if (!_shellExplorerWindowMatcher.IsExplorerWindow(window, fullName)) continue;

                        IntPtr hwnd;
                        if (!_shellExplorerWindowMatcher.TryGetWindowHwnd(window, out hwnd))
                        {
                            AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetSelectedItemsHwndConvert", "Failed to convert HWND while getting selected items.", new InvalidOperationException("Failed to convert Shell window HWND."), TimeSpan.FromMinutes(5));
                            continue;
                        }

                        if (!_shellExplorerWindowMatcher.MatchesTargetWindow(hwnd, explorerHwnd)) continue;

                        object document = null;
                        try
                        {
                            document = GetComProperty(window, "Document");
                            List<string> currentSelectedItems = _shellSelectedItemsReader.ReadSelectedItemPaths(document);
                            for (int j = 0; j < currentSelectedItems.Count; j++)
                            {
                                selectedItems.Add(currentSelectedItems[j]);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetSelectedItemsDocument", "Failed while reading selected items from explorer document.", ex, TimeSpan.FromMinutes(5));
                        }
                        finally
                        {
                            ReleaseComObjectSafe(document);
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogErrorThrottled("ShellWindowComInterop", "GetSelectedItemsWindowEnumerate", "Failed while enumerating a Shell window for selected items.", ex, TimeSpan.FromMinutes(5));
                    }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowComInterop", "GetSelectedItems failed and Shell.Application cache will be reset.", ex);
                ShellWindowCacheManager.ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                _cacheManager.RunPeriodicComCleanup();
            }
            return selectedItems;
        }

        public void SelectItems(IntPtr explorerHwnd, List<string> itemPaths)
        {
            if (itemPaths == null || itemPaths.Count == 0) return;
            object windowsObject = null;
            if (!_cacheManager.TryCreateShellWindows(out windowsObject))
            {
                return;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null)
                {
                    return;
                }

                int count = 0;
                try
                {
                    count = Convert.ToInt32(countObj);
                }
                catch (Exception ex)
                {
                    AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsCountConvert", "Failed to convert Shell Windows count while selecting items.", ex, TimeSpan.FromMinutes(5));
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsFullName", "Failed to read FullName while selecting items.", ex, TimeSpan.FromMinutes(5)); }
                        if (!_shellExplorerWindowMatcher.IsExplorerWindow(window, fullName)) continue;

                        IntPtr hwnd;
                        if (!_shellExplorerWindowMatcher.TryGetWindowHwnd(window, out hwnd))
                        {
                            AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsHwndConvert", "Failed to convert HWND while selecting items.", new InvalidOperationException("Failed to convert Shell window HWND."), TimeSpan.FromMinutes(5));
                            continue;
                        }
                        if (!_shellExplorerWindowMatcher.MatchesTargetWindow(hwnd, explorerHwnd)) continue;

                        object document = null;
                        object folder = null;
                        object folderItems = null;
                        try
                        {
                            document = GetComProperty(window, "Document");
                            folder = GetComProperty(document, "Folder");
                            folderItems = InvokeComMethod(folder, "Items");
                            int itemCount = _shellFolderItemSelectionHelper.GetComCollectionCount(folderItems);
                            bool hasSelectedItem = false;
                            for (int j = 0; j < itemPaths.Count; j++)
                            {
                                object item = _shellFolderItemSelectionHelper.FindFolderItemByPath(folder, folderItems, itemCount, itemPaths[j]);
                                try
                                {
                                    if (item != null)
                                    {
                                        int flags = hasSelectedItem ? 1 : 29;
                                        InvokeComMethod(document, "SelectItem", item, flags);
                                        hasSelectedItem = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsInvoke", "Failed to select an individual explorer item.", ex, TimeSpan.FromMinutes(5));
                                }
                                finally
                                {
                                    ReleaseComObjectSafe(item);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsDocument", "Failed while preparing explorer item selection.", ex, TimeSpan.FromMinutes(5));
                        }
                        finally
                        {
                            ReleaseComObjectSafe(folderItems);
                            ReleaseComObjectSafe(folder);
                            ReleaseComObjectSafe(document);
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogErrorThrottled("ShellWindowComInterop", "SelectItemsWindowEnumerate", "Failed while enumerating a Shell window for selection.", ex, TimeSpan.FromMinutes(5));
                    }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellWindowComInterop", "SelectItems failed and Shell.Application cache will be reset.", ex);
                ShellWindowCacheManager.ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                _cacheManager.RunPeriodicComCleanup();
            }
        }

        public bool Navigate(IntPtr explorerHwnd, string path)
        {
            string navigatePath = _getNavigableShellPath(path);

            if (!_isNavigablePath(navigatePath)) return false;

            bool navigated = false;
            object windowsObject = null;
            if (!_cacheManager.TryCreateShellWindows(out windowsObject))
            {
                return false;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return false;
                int count = 0;
                try { count = Convert.ToInt32(countObj); }
                catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "NavigateCountConvert", "Failed to convert Shell Windows count while navigating.", ex, TimeSpan.FromMinutes(5)); return false; }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); }
                        catch (Exception ex) { AppLogger.LogErrorThrottled("ShellWindowComInterop", "NavigateFullName", "Failed to read FullName while navigating.", ex, TimeSpan.FromMinutes(5)); }
                        if (!_shellExplorerWindowMatcher.IsExplorerWindow(window, fullName)) continue;

                        IntPtr hwnd;
                        if (!_shellExplorerWindowMatcher.TryGetWindowHwnd(window, out hwnd)) continue;

                        if (!_shellExplorerWindowMatcher.MatchesTargetWindow(hwnd, explorerHwnd)) continue;

                        _shellWindowNavigator.Navigate(window, navigatePath);
                        navigated = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogErrorThrottled("ShellWindowComInterop", "NavigateWindowFailed", "Failed to navigate an Explorer window.", ex, TimeSpan.FromMinutes(5));
                    }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch
            {
                ShellWindowCacheManager.ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                _cacheManager.RunPeriodicComCleanup();
            }
            return navigated;
        }
    }
}
