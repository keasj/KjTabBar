using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    public class ExplorerManager : IExplorerService
    {
        public string AllControlPanelPath { get; } = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
        public string HomeFolderPath { get; } = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
        public string ProgramsAndFeaturesPath { get; } = "::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}";
        public string PowerOptionsPath { get; } = "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";
        private const string ControlPanelItemNavigationPrefix = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\";

        private static NativeMethods.EnumWindowsProc _enumWindowsProc = EnumWindowsCallback;
        private int _comCleanupCounter = 0;
        private const int ComCleanupInterval = 40;
        private static readonly object _shellApplicationSync = new object();
        [ThreadStatic]
        private static object _threadLocalShellApplication = null;
        private static readonly object _controlPanelItemTitleMapSync = new object();
        private static Dictionary<string, string> _controlPanelItemPathsByTitle = null;
        private static HashSet<string> _controlPanelItemPaths = null;

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
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
            catch
            {
            }
        }

        private void RunPeriodicComCleanup()
        {
            int counter = System.Threading.Interlocked.Increment(ref _comCleanupCounter);
            if (counter < ComCleanupInterval)
            {
                return;
            }

            System.Threading.Interlocked.Exchange(ref _comCleanupCounter, 0);
            try
            {
                Marshal.CleanupUnusedObjectsInCurrentContext();
            }
            catch
            {
            }
        }

        private void ResetShellApplication()
        {
            ReleaseComObjectSafe(_threadLocalShellApplication);
            _threadLocalShellApplication = null;
        }

        public void ReleaseCachedComObjects()
        {
            ResetShellApplication();
            RunPeriodicComCleanup();
        }

        internal static bool TryGetShellApplication(out object shellObject)
        {
            shellObject = null;

            if (_threadLocalShellApplication != null)
            {
                shellObject = _threadLocalShellApplication;
                return true;
            }

            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                return false;
            }

            try
            {
                _threadLocalShellApplication = Activator.CreateInstance(shellType);
                shellObject = _threadLocalShellApplication;
                return shellObject != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCreateShellWindows(out object windowsObject)
        {
            windowsObject = null;

            object shellObject = null;
            if (!TryGetShellApplication(out shellObject))
            {
                return false;
            }

            try
            {
                object shellDynamic = shellObject;
                windowsObject = InvokeComMethod(shellDynamic, "Windows");
                return windowsObject != null;
            }
            catch
            {
                ResetShellApplication();
                return false;
            }
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

        /// <summary>
        /// エクスプローラーの現在のパスを取得する。
        /// ファイルシステムパスおよび既知の特殊シェルパスを返す。
        /// 判定できない一時状態では null を返す。
        /// </summary>
        public string GetCurrentPath(IntPtr explorerHwnd)
        {
            string result = null;
            object windowsObject = null;
            if (!TryCreateShellWindows(out windowsObject))
            {
                return null;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return null;
                int count = 0;
                try { count = Convert.ToInt32(countObj); } catch { return null; }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); } catch { }
                        if (string.IsNullOrEmpty(fullName)) continue;
                        if (!fullName.ToLowerInvariant().EndsWith("explorer.exe")) continue;

                        object hwndObj = GetComProperty(window, "HWND");
                        if (hwndObj == null) continue;

                        IntPtr hwnd = IntPtr.Zero;
                        try
                        {
                            long hwndVal = Convert.ToInt64(hwndObj);
                            hwnd = (IntPtr)hwndVal;
                        }
                        catch
                        {
                            continue;
                        }

                        if (hwnd != explorerHwnd)
                        {
                            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                            if (rootHwnd != explorerHwnd)
                            {
                                continue;
                            }
                        }

                        string locationUrl = "";
                        try { locationUrl = (string)GetComProperty(window, "LocationURL"); } catch { }

                        string locationName = "";
                        try { locationName = (string)GetComProperty(window, "LocationName"); } catch { }

                        // [BUG_FIX] コントロールパネル配下の項目（電源オプション等）からコントロールパネル（ルート）に
                        // 戻った際、folderPath が古い項目のパスを返し続けることがあるため、
                        // locationName がコントロールパネルルートを示している場合は最優先でそのパスを返す。
                        string mappedCPPath = MapLocationNameToKnownShellPath(locationName);
                        if (!string.IsNullOrEmpty(mappedCPPath) && IsControlPanelRootPath(mappedCPPath))
                        {
                            result = mappedCPPath;
                            break;
                        }

                        string folderPath = null;
                        object document = null;
                        object folder = null;
                        object folderSelf = null;
                        try
                        {
                            document = GetComProperty(window, "Document");
                            if (document != null)
                            {
                                folder = GetComProperty(document, "Folder");
                                if (folder != null)
                                {
                                    folderSelf = GetComProperty(folder, "Self");
                                    if (folderSelf != null)
                                    {
                                        string rawPath = (string)GetComProperty(folderSelf, "Path");
                                        if (rawPath != null)
                                        {
                                            folderPath = rawPath.TrimEnd('\0');
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            ReleaseComObjectSafe(folderSelf);
                            ReleaseComObjectSafe(folder);
                            ReleaseComObjectSafe(document);
                        }

                        string normalizedFolderPath = NormalizeShellPath(folderPath);
                        if (!string.IsNullOrEmpty(normalizedFolderPath))
                        {
                            result = normalizedFolderPath;
                            break;
                        }

                        if (!string.IsNullOrEmpty(folderPath) && IsNavigablePath(folderPath))
                        {
                            result = folderPath;
                            break;
                        }

                        if (!string.IsNullOrEmpty(locationUrl))
                        {
                            Uri uri;
                            if (Uri.TryCreate(locationUrl, UriKind.Absolute, out uri))
                            {
                                string localPath = uri.LocalPath;
                                // ファイルシステムパスかチェック
                                if (IsNavigablePath(localPath))
                                {
                                    result = localPath;
                                    break;
                                }
                            }

                            string normalizedLocationPath = NormalizeShellPath(locationUrl);
                            if (!string.IsNullOrEmpty(normalizedLocationPath))
                            {
                                result = normalizedLocationPath;
                                break;
                            }
                        }

                        string mappedVirtualPath = MapLocationNameToKnownShellPath(locationName);
                        if (!string.IsNullOrEmpty(mappedVirtualPath))
                        {
                            result = mappedVirtualPath;
                            break;
                        }

                        // locationUrlがない、または解釈できない場合でも、内部ID（CLSID等）を含む
                        // folderPathがあるならそれを返す（ナビゲート可能なパスとして必須）
                        if (!string.IsNullOrEmpty(folderPath))
                        {
                            result = folderPath;
                        }
                        break;
                    }
                    catch { }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch
            {
                ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                RunPeriodicComCleanup();
            }
            return result;
        }

        public List<string> GetSelectedItems(IntPtr explorerHwnd)
        {
            List<string> selectedItems = new List<string>();
            object windowsObject = null;
            if (!TryCreateShellWindows(out windowsObject))
            {
                return selectedItems;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return selectedItems;
                int count = 0;
                try { count = Convert.ToInt32(countObj); } catch { return selectedItems; }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); } catch { }
                        if (string.IsNullOrEmpty(fullName)) continue;
                        if (!fullName.ToLowerInvariant().EndsWith("explorer.exe")) continue;

                        object hwndObj = GetComProperty(window, "HWND");
                        if (hwndObj == null) continue;

                        IntPtr hwnd = IntPtr.Zero;
                        try
                        {
                            long hwndVal = Convert.ToInt64(hwndObj);
                            hwnd = (IntPtr)hwndVal;
                        }
                        catch
                        {
                            continue;
                        }

                        if (hwnd != explorerHwnd)
                        {
                            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                            if (rootHwnd != explorerHwnd)
                            {
                                continue;
                            }
                        }

                        object document = null;
                        object selected = null;
                        try
                        {
                            document = GetComProperty(window, "Document");
                            selected = InvokeComMethod(document, "SelectedItems");
                            object selCountObj = GetComProperty(selected, "Count");
                            int selCount = 0;
                            if (selCountObj != null)
                            {
                                try { selCount = Convert.ToInt32(selCountObj); } catch { }
                            }
                            for (int j = 0; j < selCount; j++)
                            {
                                object item = null;
                                try
                                {
                                    item = InvokeComMethod(selected, "Item", j);
                                    string selectedItemPath = GetComProperty(item, "Path") as string;
                                    if (!string.IsNullOrEmpty(selectedItemPath))
                                    {
                                        selectedItems.Add(selectedItemPath);
                                    }
                                }
                                catch { }
                                finally
                                {
                                    ReleaseComObjectSafe(item);
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            ReleaseComObjectSafe(selected);
                            ReleaseComObjectSafe(document);
                        }
                        break;
                    }
                    catch { }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch
            {
                ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                RunPeriodicComCleanup();
            }
            return selectedItems;
        }

        public void SelectItems(IntPtr explorerHwnd, List<string> itemPaths)
        {
            if (itemPaths == null || itemPaths.Count == 0) return;
            object windowsObject = null;
            if (!TryCreateShellWindows(out windowsObject))
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
                catch
                {
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
                        try { fullName = (string)GetComProperty(window, "FullName"); } catch { }
                        if (string.IsNullOrEmpty(fullName)) continue;
                        if (!fullName.ToLowerInvariant().EndsWith("explorer.exe")) continue;

                        object hwndObj = GetComProperty(window, "HWND");
                        if (hwndObj == null) continue;
                        IntPtr hwnd = IntPtr.Zero;
                        try
                        {
                            long hwndValue = Convert.ToInt64(hwndObj);
                            hwnd = (IntPtr)hwndValue;
                        }
                        catch
                        {
                            continue;
                        }
                        if (hwnd != explorerHwnd)
                        {
                            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                            if (rootHwnd != explorerHwnd)
                            {
                                continue;
                            }
                        }

                        object document = null;
                        object folder = null;
                        object folderItems = null;
                        try
                        {
                            document = GetComProperty(window, "Document");
                            folder = GetComProperty(document, "Folder");
                            folderItems = InvokeComMethod(folder, "Items");
                            int itemCount = GetComCollectionCount(folderItems);
                            bool hasSelectedItem = false;
                            for (int j = 0; j < itemPaths.Count; j++)
                            {
                                object item = FindFolderItemByPath(folder, folderItems, itemCount, itemPaths[j]);
                                try
                                {
                                    if (item != null)
                                    {
                                        int flags = hasSelectedItem ? 1 : 29; // 29 = 1|4|8|16 (select, ensure visible, focus, deselect others)
                                        InvokeComMethod(document, "SelectItem", item, flags);
                                        hasSelectedItem = true;
                                    }
                                }
                                catch { }
                                finally
                                {
                                    ReleaseComObjectSafe(item);
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            ReleaseComObjectSafe(folderItems);
                            ReleaseComObjectSafe(folder);
                            ReleaseComObjectSafe(document);
                        }
                        break;
                    }
                    catch { }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch
            {
                ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                RunPeriodicComCleanup();
            }
        }

        private int GetComCollectionCount(object comCollection)
        {
            if (comCollection == null)
            {
                return 0;
            }

            object countObject = GetComProperty(comCollection, "Count");
            if (countObject == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(countObject);
            }
            catch
            {
                return 0;
            }
        }

        private object FindFolderItemByPath(object folder, object folderItems, int itemCount, string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath))
            {
                return null;
            }

            for (int i = 0; i < itemCount; i++)
            {
                object item = null;
                try
                {
                    item = InvokeComMethod(folderItems, "Item", i);
                    string itemPath = GetComProperty(item, "Path") as string;
                    if (AreEquivalentItemPaths(itemPath, targetPath))
                    {
                        return item;
                    }
                }
                catch
                {
                }

                ReleaseComObjectSafe(item);
            }

            string parseName = GetItemParseName(targetPath);
            if (string.IsNullOrEmpty(parseName))
            {
                return null;
            }

            return InvokeComMethod(folder, "ParseName", parseName);
        }

        private string GetItemParseName(string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return null;
            }

            string trimmedPath = itemPath.Trim();
            if (trimmedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                trimmedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedPath;
            }

            string fileName = Path.GetFileName(trimmedPath.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }

            return trimmedPath.TrimEnd('\\');
        }

        private bool AreEquivalentItemPaths(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
            {
                return false;
            }

            string normalizedPath1 = NormalizeKnownPath(path1);
            string normalizedPath2 = NormalizeKnownPath(path2);
            if (!string.IsNullOrEmpty(normalizedPath1) &&
                !string.IsNullOrEmpty(normalizedPath2) &&
                normalizedPath1.TrimEnd('\\').Equals(normalizedPath2.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedFileSystemPath1;
            string normalizedFileSystemPath2;
            if (TryNormalizeFileSystemPath(path1, out normalizedFileSystemPath1) &&
                TryNormalizeFileSystemPath(path2, out normalizedFileSystemPath2) &&
                normalizedFileSystemPath1.Equals(normalizedFileSystemPath2, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return path1.TrimEnd('\\').Equals(path2.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private bool TryNormalizeFileSystemPath(string path, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string trimmedPath = path.Trim();
            if (trimmedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                trimmedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                normalizedPath = Path.GetFullPath(trimmedPath).TrimEnd('\\');
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string _localizedControlPanelTitle = null;
        private string _localizedNetworkTitle = null;
        private string _localizedRecycleBinTitle = null;
        private string _localizedThisPCTitle = null;
        private string _localizedHomeTitle = null;

        private string GetOrCacheLocalizedTitle(ref string cache, string shellPath, string fallback)
        {
            if (cache == null)
            {
                cache = GetFolderNameInternal(shellPath);
                if (string.IsNullOrEmpty(cache))
                {
                    cache = fallback;
                }
            }
            return cache;
        }

        public string GetLocalizedControlPanelTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedControlPanelTitle, AllControlPanelPath, "Control Panel");
        }

        public string GetLocalizedNetworkTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedNetworkTitle, "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "Network");
        }

        public string GetLocalizedRecycleBinTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedRecycleBinTitle, "::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin");
        }

        public string GetLocalizedThisPCTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedThisPCTitle, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "This PC");
        }

        public string GetLocalizedHomeTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedHomeTitle, HomeFolderPath, "Home");
        }

        private string _resolvedHomeFolderPath = null;

        /// <summary>
        /// 現在の OS で利用可能なホームフォルダパスを返す。
        /// Windows 11 22H2以降では HomeFolderPath (GUID)、
        /// それ以前の OS ではユーザープロファイルパスにフォールバックする。
        /// </summary>
        public string GetResolvedHomeFolderPath()
        {
            if (_resolvedHomeFolderPath == null)
            {
                IntPtr pidl = IntPtr.Zero;
                uint dummyOut;
                try
                {
                    int hr = NativeMethods.SHParseDisplayName(HomeFolderPath, IntPtr.Zero, out pidl, 0, out dummyOut);
                    if (hr == 0 && pidl != IntPtr.Zero)
                    {
                        _resolvedHomeFolderPath = HomeFolderPath;
                    }
                    else
                    {
                        _resolvedHomeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }
                }
                catch
                {
                    _resolvedHomeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                finally
                {
                    if (pidl != IntPtr.Zero)
                    {
                        NativeMethods.ILFree(pidl);
                    }
                }
            }
            return _resolvedHomeFolderPath;
        }

        private static readonly string[] ControlPanelRootGuidTokens = new string[]
        {
            "26ee0668-a00a-44d7-9371-beb064c98683",
            "21ec2020-3aea-1069-a2dd-08002b30309d",
            "5399e694-6ce5-4d6c-8fce-1d8870fdcba0",
            "82a74aeb-aeb4-465c-a014-d097ee346d63"
        };

        /// <summary>
        /// コントロールパネル本体を表す GUID を含むか判定する。
        /// 配下項目の GUID まで含まれている場合は別メソッドで除外する。
        /// </summary>
        private bool ContainsControlPanelRootGuid(string lowerPath)
        {
            if (string.IsNullOrEmpty(lowerPath))
            {
                return false;
            }

            for (int i = 0; i < ControlPanelRootGuidTokens.Length; i++)
            {
                if (lowerPath.Contains(ControlPanelRootGuidTokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsControlPanelRootGuid(string guidToken)
        {
            if (string.IsNullOrEmpty(guidToken))
            {
                return false;
            }

            for (int i = 0; i < ControlPanelRootGuidTokens.Length; i++)
            {
                if (guidToken.Equals(ControlPanelRootGuidTokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsNonControlPanelRootGuid(string lowerPath)
        {
            if (string.IsNullOrEmpty(lowerPath))
            {
                return false;
            }

            int searchIndex = 0;
            while (searchIndex < lowerPath.Length)
            {
                int openBraceIndex = lowerPath.IndexOf('{', searchIndex);
                if (openBraceIndex < 0)
                {
                    break;
                }

                int closeBraceIndex = lowerPath.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    break;
                }

                string guidToken = lowerPath.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                if (!IsControlPanelRootGuid(guidToken))
                {
                    return true;
                }

                searchIndex = closeBraceIndex + 1;
            }

            return false;
        }

        public bool IsControlPanelRootPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string lowerPath = path.ToLowerInvariant();
            string compactPath = CompactForComparison(lowerPath);
            string compactLocalizedCP = CompactForComparison(GetLocalizedControlPanelTitle().ToLowerInvariant());

            if (compactPath.Equals("controlpanel") ||
                compactPath.Equals("controlpanelfolder") ||
                compactPath.Equals("allcontrolpanelitems") ||
                compactPath.Equals(compactLocalizedCP))
            {
                return true;
            }

            if (lowerPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (compactPath.Equals("shell:controlpanel") ||
                    compactPath.StartsWith("shell:controlpanelfolder"))
                {
                    return true;
                }
                if (!lowerPath.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!lowerPath.StartsWith("::{") && !lowerPath.StartsWith("shell:::{"))
            {
                return false;
            }

            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            if (ContainsNonControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            return true;
        }

        public bool IsControlPanelItemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                normalizedPath = NormalizeShellNamespacePath(path);
            }

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (IsControlPanelRootPath(normalizedPath))
            {
                return false;
            }

            EnsureControlPanelItemTitleMap();

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPaths == null)
                {
                    return false;
                }

                return _controlPanelItemPaths.Contains(normalizedPath);
            }
        }

        public bool IsControlPanelPath(string path)
        {
            if (IsControlPanelRootPath(path))
            {
                return true;
            }

            return IsControlPanelItemPath(path);
        }

        public bool IsTransientShellPlaceholderPath(string path)
        {
            string normalizedPath = NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.Equals(AllControlPanelPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (normalizedPath.Equals(HomeFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (normalizedPath.Equals("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// パスがナビゲーション可能かどうかを判定する。
        /// ファイルシステムパス（ドライブレター、UNCパス）および仮想パス（CLSID）を有効とする。
        /// </summary>
        public bool IsNavigablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // ドライブレター（C:\ 等）で始まるか
            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                return Directory.Exists(path) || File.Exists(path);
            }
            // UNCパス（\\server\）で始まるか
            if (path.StartsWith("\\\\"))
            {
                return true; // UNCパスは存在チェックが遅いため存在すると仮定
            }
            // 仮想パス（CLSID形式: ::{GUID}）
            if (path.StartsWith("::{"))
            {
                return true;
            }
            return false;
        }

        public bool IsTabPathCurrentlyAvailable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = NormalizeKnownPath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                normalizedPath = path;
            }

            if (normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (normalizedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Directory.Exists(normalizedPath);
        }

        public bool Navigate(IntPtr explorerHwnd, string path)
        {
            string navigatePath = GetNavigableShellPath(path);

            // ナビゲーション不可能なパスは無視
            if (!IsNavigablePath(navigatePath)) return false;

            bool navigated = false;
            object windowsObject = null;
            if (!TryCreateShellWindows(out windowsObject))
            {
                return false;
            }

            object windows = windowsObject;
            try
            {
                object countObj = GetComProperty(windows, "Count");
                if (countObj == null) return false;
                int count = 0;
                try { count = Convert.ToInt32(countObj); } catch { return false; }
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = InvokeComMethod(windows, "Item", i);
                        if (window == null) continue;
                        string fullName = "";
                        try { fullName = (string)GetComProperty(window, "FullName"); } catch { }
                        if (string.IsNullOrEmpty(fullName)) continue;
                        if (!fullName.ToLowerInvariant().EndsWith("explorer.exe")) continue;

                        object hwndObj = GetComProperty(window, "HWND");
                        if (hwndObj == null) continue;

                        IntPtr hwnd = IntPtr.Zero;
                        try
                        {
                            long hwndVal = Convert.ToInt64(hwndObj);
                            hwnd = (IntPtr)hwndVal;
                        }
                        catch
                        {
                            continue;
                        }

                        if (hwnd != explorerHwnd)
                        {
                            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                            if (rootHwnd != explorerHwnd)
                            {
                                continue;
                            }
                        }

                        if (navigatePath.StartsWith("::{"))
                        {
                            IntPtr pidl = IntPtr.Zero;
                            uint dummyOut;
                            int hr = NativeMethods.SHParseDisplayName(navigatePath, IntPtr.Zero, out pidl, 0, out dummyOut);
                            if (hr == 0 && pidl != IntPtr.Zero)
                            {
                                try
                                {
                                    uint size = NativeMethods.ILGetSize(pidl);
                                    byte[] pidlBytes = new byte[size];
                                    Marshal.Copy(pidl, pidlBytes, 0, (int)size);
                                    object url = pidlBytes;

                                    // ナビゲーションフラグ(navNoHistory = 2, navOpenInNewWindow = 1 等)
                                    // 0を指定して同一ウィンドウでの遷移を試みる
                                    object flags = 0;
                                    object targetFrame = null;
                                    window.GetType().InvokeMember("Navigate2",
                                        System.Reflection.BindingFlags.InvokeMethod,
                                        null,
                                        (object)window,
                                        new object[] { url, flags, targetFrame });
                                }
                                finally
                                {
                                    NativeMethods.ILFree(pidl);
                                }
                            }
                            else
                            {
                                InvokeComMethod(window, "Navigate", navigatePath);
                            }
                        }
                        else
                        {
                            InvokeComMethod(window, "Navigate", navigatePath);
                        }
                        navigated = true;
                        break;
                    }
                    catch { }
                    finally
                    {
                        ReleaseComObjectSafe(window);
                    }
                }
            }
            catch
            {
                ResetShellApplication();
            }
            finally
            {
                ReleaseComObjectSafe(windowsObject);
                RunPeriodicComCleanup();
            }
            return navigated;
        }

        /// <summary>
        /// DWMの実際の可視ウィンドウ境界を取得する（不可視ボーダーを除く）。
        /// メソッド名は実態に合わせてウィンドウ矩形としている。
        /// </summary>
        public NativeMethods.RECT GetExplorerWindowRect(IntPtr explorerHwnd)
        {
            NativeMethods.RECT explorerRect;
            int hr = NativeMethods.DwmGetWindowAttribute(explorerHwnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out explorerRect,
                Marshal.SizeOf(typeof(NativeMethods.RECT)));
            if (hr != 0)
            {
                // DWMが使えない場合のフォールバック
                NativeMethods.GetWindowRect(explorerHwnd, out explorerRect);
            }

            NativeMethods.RECT result;
            result.Left = explorerRect.Left;
            result.Top = explorerRect.Top;
            result.Right = explorerRect.Right;
            result.Bottom = explorerRect.Bottom;
            return result;
        }

        public string GetFolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return GetLocalizedHomeTitle();
            if (IsControlPanelRootPath(path)) return GetLocalizedControlPanelTitle();
            if (NormalizeKnownPath(path).Equals(HomeFolderPath, StringComparison.OrdinalIgnoreCase)) return GetLocalizedHomeTitle();
            return GetFolderNameInternal(path);
        }

        public string GetParentFolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string displayPath = GetNavigableShellPath(path);
            if (string.IsNullOrEmpty(displayPath))
            {
                displayPath = path;
            }

            // 方法1: IShellItem を使用 (最も堅牢)
            IntPtr pidl = IntPtr.Zero;
            NativeMethods.IShellItem item = null;
            NativeMethods.IShellItem parent = null;
            try
            {
                uint dummy;
                if (NativeMethods.SHParseDisplayName(displayPath, IntPtr.Zero, out pidl, 0, out dummy) == 0 && pidl != IntPtr.Zero)
                {
                    Guid iid = typeof(NativeMethods.IShellItem).GUID;
                    if (NativeMethods.SHCreateItemFromIDList(pidl, ref iid, out item) == 0)
                    {
                        if (item.GetParent(out parent) == 0 && parent != null)
                        {
                            IntPtr pName;
                            if (parent.GetDisplayName(NativeMethods.SIGDN.NORMALDISPLAY, out pName) == 0 && pName != IntPtr.Zero)
                            {
                                string name = Marshal.PtrToStringUni(pName);
                                Marshal.FreeCoTaskMem(pName);
                                if (!string.IsNullOrEmpty(name)) return name;
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                ReleaseComObjectSafe(parent);
                ReleaseComObjectSafe(item);
                if (pidl != IntPtr.Zero) NativeMethods.ILFree(pidl);
            }

            // 方法2: Shell.Application COM を使用 (フォールバック)
            object shellObject = null;
            object folder = null;
            object parentFolder = null;
            try
            {
                if (!TryGetShellApplication(out shellObject)) return null;
                folder = InvokeComMethod(shellObject, "NameSpace", displayPath);
                if (folder == null) return null;

                parentFolder = GetComProperty(folder, "ParentFolder");
                if (parentFolder == null)
                {
                    return null;
                }
                
                string title = GetComProperty(parentFolder, "Title") as string;
                if (string.IsNullOrEmpty(title))
                {
                    object parentItem = null;
                    try
                    {
                        parentItem = GetComProperty(parentFolder, "Self");
                        title = GetComProperty(parentItem, "Name") as string;
                    }
                    finally
                    {
                        ReleaseComObjectSafe(parentItem);
                    }
                }

                return title;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObjectSafe(parentFolder);
                ReleaseComObjectSafe(folder);
            }
        }

        private string GetFolderNameInternal(string path)
        {
            if (string.IsNullOrEmpty(path)) return GetLocalizedHomeTitle();

            string displayPath = GetNavigableShellPath(path);
            if (string.IsNullOrEmpty(displayPath))
            {
                displayPath = path;
            }

            // キャッシュ済み Shell.Application で表示名を取得
            object shellObject = null;
            object ns = null;
            try
            {
                if (TryGetShellApplication(out shellObject))
                {
                    object shell = shellObject;
                    ns = InvokeComMethod(shell, "NameSpace", displayPath);
                    if (ns != null)
                    {
                        string title = GetComProperty(ns, "Title") as string;
                        if (!string.IsNullOrEmpty(title))
                        {
                            return title;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                ReleaseComObjectSafe(ns);
                // shellObject はキャッシュ済みインスタンスのため解放しない
            }

            // Shell.Application が失敗した場合は COM API で直接パースして表示名を取得
            if (displayPath.StartsWith("::{") || displayPath.StartsWith("shell:"))
            {
                IntPtr pidl = IntPtr.Zero;
                uint dummyOut;
                int hr = NativeMethods.SHParseDisplayName(displayPath, IntPtr.Zero, out pidl, 0, out dummyOut);
                if (hr == 0 && pidl != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr pName;
                        if (NativeMethods.SHGetNameFromIDList(pidl, NativeMethods.SIGDN.NORMALDISPLAY, out pName) == 0 && pName != IntPtr.Zero)
                        {
                            string title = Marshal.PtrToStringUni(pName); // Unicode 指定
                            Marshal.FreeCoTaskMem(pName);
                            if (!string.IsNullOrEmpty(title)) return title.TrimEnd('\0');
                        }
                    }
                    finally
                    {
                        NativeMethods.ILFree(pidl);
                    }
                }
            }

            try
            {
                DirectoryInfo dirInfo = new DirectoryInfo(path);
                string name = dirInfo.Name;
                if (string.IsNullOrEmpty(name) || dirInfo.Parent == null)
                {
                    return path;
                }
                return name;
            }
            catch
            {
                return path;
            }
        }

        public string ResolveShortcutTarget(string shortcutPath)
        {
            if (string.IsNullOrEmpty(shortcutPath)) return null;
            if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return shortcutPath;

            object shell = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return shortcutPath;

                shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut != null)
                {
                    try
                    {
                        string targetPath = null;
                        string arguments = null;

                        try
                        {
                            targetPath = (string)shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                        }
                        catch { }

                        try
                        {
                            arguments = (string)shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                        }
                        catch { }

                        string shellPathFromArguments = ExtractShellPathFromShortcutArguments(arguments);
                        if (!string.IsNullOrEmpty(shellPathFromArguments))
                        {
                            return shellPathFromArguments;
                        }

                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            string normalizedShellTargetPath = NormalizeShellPath(targetPath);
                            if (!string.IsNullOrEmpty(normalizedShellTargetPath))
                            {
                                return normalizedShellTargetPath;
                            }

                            return targetPath;
                        }
                    }
                    finally
                    {
                        ReleaseComObjectSafe(shortcut);
                    }
                }
            }
            catch
            {
                // エラー時は入力パスを返す
            }
            finally
            {
                ReleaseComObjectSafe(shell);
            }

            // WScript.Shellで解決できなかった場合（特殊フォルダなど）、Shell.Applicationで試行
            string virtualTargetPath = ResolveVirtualShortcutTarget(shortcutPath);
            if (!string.IsNullOrEmpty(virtualTargetPath))
            {
                return virtualTargetPath;
            }

            return shortcutPath;
        }

        /// <summary>
        /// 指定されたパスのショートカットを仕向先ディレクトリに作成する。
        /// </summary>
        /// <param name="sourcePaths">作成元のファイル/フォルダパス一覧</param>
        /// <param name="destinationDirectory">作成先ディレクトリ</param>
        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory)
        {
            CreateShortcuts(sourcePaths, destinationDirectory, IntPtr.Zero);
        }

        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            if (sourcePaths == null || sourcePaths.Length == 0 || string.IsNullOrEmpty(destinationDirectory)) return;

            object shell = null;
            List<string> tempShortcutPaths = new List<string>();
            string tempDirectory = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                shell = Activator.CreateInstance(shellType);

                for (int i = 0; i < sourcePaths.Length; i++)
                {
                    string sourcePath = sourcePaths[i];
                    string fileName = Path.GetFileName(sourcePath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        // ドライブや特殊フォルダ（::{...}）の場合は表示名を取得
                        fileName = GetFolderName(sourcePath);
                    }

                    // ファイル名として不適切な文字を置換
                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    for (int j = 0; j < invalidChars.Length; j++)
                    {
                        fileName = fileName.Replace(invalidChars[j], '_');
                    }

                    string shortcutPath = BuildUniqueShortcutPath(destinationDirectory, fileName);
                    if (TryCreateShortcutFile(shellType, shell, sourcePath, shortcutPath))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(tempDirectory))
                    {
                        tempDirectory = Path.Combine(Path.GetTempPath(), "KjTabBar", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDirectory);
                    }

                    string tempShortcutPath = Path.Combine(tempDirectory, Path.GetFileName(shortcutPath));
                    if (TryCreateShortcutFile(shellType, shell, sourcePath, tempShortcutPath))
                    {
                        tempShortcutPaths.Add(tempShortcutPath);
                    }
                }

                if (tempShortcutPaths.Count > 0)
                {
                    NativeMethods.SHFILEOPSTRUCT shf = new NativeMethods.SHFILEOPSTRUCT();
                    shf.hwnd = ownerHwnd;
                    shf.wFunc = NativeMethods.FO_MOVE;
                    shf.pFrom = string.Join("\0", tempShortcutPaths.ToArray()) + "\0\0";
                    shf.pTo = destinationDirectory + "\0\0";
                    shf.fFlags = NativeMethods.FOF_ALLOWUNDO;
                    NativeMethods.SHFileOperation(ref shf);
                }
            }
            catch
            {
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch
                    {
                    }
                }

                ReleaseComObjectSafe(shell);
                RunPeriodicComCleanup();
            }
        }

        public void CreateSymbolicLinks(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            if (sourcePaths == null || sourcePaths.Length == 0 || string.IsNullOrEmpty(destinationDirectory)) return;

            var linksToCreate = new List<Tuple<string, string, bool>>();
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sourcePaths.Length; i++)
            {
                string sourcePath = sourcePaths[i];
                string fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = GetFolderName(sourcePath);
                }

                char[] invalidChars = Path.GetInvalidFileNameChars();
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    fileName = fileName.Replace(invalidChars[j], '_');
                }

                bool isDirectory = Directory.Exists(sourcePath);
                string ext = isDirectory ? "" : Path.GetExtension(sourcePath);
                string baseName = isDirectory || string.IsNullOrEmpty(ext) ? fileName : Path.GetFileNameWithoutExtension(fileName);

                string linkName = baseName + ext;
                string linkPath = Path.Combine(destinationDirectory, linkName);
                
                int count = 2;
                while (File.Exists(linkPath) || Directory.Exists(linkPath) || usedPaths.Contains(linkPath))
                {
                    linkName = baseName + " (" + count + ")" + ext;
                    linkPath = Path.Combine(destinationDirectory, linkName);
                    count++;
                }

                linksToCreate.Add(new Tuple<string, string, bool>(linkPath, sourcePath, isDirectory));
                usedPaths.Add(linkPath);
            }

            var failedLinks = new List<Tuple<string, string, bool>>();
            var otherErrorOccurred = false;

            foreach (var link in linksToCreate)
            {
                string linkPath = link.Item1;
                string sourcePath = link.Item2;
                bool isDirectory = link.Item3;

                uint flags = isDirectory ? NativeMethods.SYMBOLIC_LINK_FLAG_DIRECTORY : NativeMethods.SYMBOLIC_LINK_FLAG_FILE;
                flags |= NativeMethods.SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE;

                if (!NativeMethods.CreateSymbolicLink(linkPath, sourcePath, flags))
                {
                    int errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    // 1314 = ERROR_PRIVILEGE_NOT_HELD
                    if (errorCode == 1314)
                    {
                        failedLinks.Add(link);
                    }
                    else
                    {
                        otherErrorOccurred = true;
                    }
                }
            }

            if (failedLinks.Count > 0)
            {
                var result = System.Windows.MessageBox.Show(
                    "シンボリックリンクを作成する権限がありません。\n管理者権限を使用して作成しますか？\n\n（「いいえ」を選択した場合、Windowsの設定で「開発者モード」をオンにして権限を確保する必要があります）",
                    "権限の確認",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    string tempBatFile = Path.Combine(Path.GetTempPath(), "KjTabBar_CreateSymlinks_" + Guid.NewGuid().ToString("N") + ".bat");
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(tempBatFile, false, new UTF8Encoding(false)))
                        {
                            sw.WriteLine("@echo off");
                            sw.WriteLine("chcp 65001 >nul");
                            foreach (var link in failedLinks)
                            {
                                string opt = link.Item3 ? "/d " : "";
                                // バッチファイル内では%を%%にエスケープする必要がある
                                string escapedLinkPath = link.Item1.Replace("%", "%%");
                                string escapedSourcePath = link.Item2.Replace("%", "%%");
                                sw.WriteLine($"mklink {opt}\"{escapedLinkPath}\" \"{escapedSourcePath}\"");
                            }
                        }

                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempBatFile,
                            UseShellExecute = true,
                            Verb = "runas",
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                            CreateNoWindow = true
                        };
                        using (var process = System.Diagnostics.Process.Start(startInfo))
                        {
                            process.WaitForExit();
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // UACプロンプトでキャンセルされた場合などは何もしない
                    }
                    finally
                    {
                        if (File.Exists(tempBatFile))
                        {
                            try { File.Delete(tempBatFile); } catch { }
                        }
                    }
                }
            }
            
            if (otherErrorOccurred)
            {
                System.Windows.MessageBox.Show(
                    "一部のシンボリックリンクの作成に失敗しました。",
                    "エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

        }


        private string BuildUniqueShortcutPath(string destinationDirectory, string baseFileName)
        {
            string shortcutName = baseFileName + " - ショートカット.lnk";
            string shortcutPath = Path.Combine(destinationDirectory, shortcutName);

            int count = 2;
            while (File.Exists(shortcutPath))
            {
                shortcutName = baseFileName + " - ショートカット (" + count + ").lnk";
                shortcutPath = Path.Combine(destinationDirectory, shortcutName);
                count++;
            }

            return shortcutPath;
        }

        private bool TryCreateShortcutFile(Type shellType, object shell, string sourcePath, string shortcutPath)
        {
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut != null)
                {
                    string normalizedSourcePath = NormalizeShellPath(sourcePath);
                    if (!string.IsNullOrEmpty(normalizedSourcePath))
                    {
                        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        string explorerExePath = Path.Combine(windowsDirectory, "explorer.exe");
                        string shortcutArguments = "\"" + GetNavigableShellPath(normalizedSourcePath) + "\"";

                        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { explorerExePath });
                        shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { shortcutArguments });
                    }
                    else
                    {
                        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { sourcePath });
                    }

                    shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObjectSafe(shortcut);
            }

            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch
            {
            }

            return false;
        }


        private string ResolveVirtualShortcutTarget(string shortcutPath)
        {
            object shellObject = null;
            object folder = null;
            object folderItem = null;
            object link = null;
            object target = null;
            try
            {
                if (TryGetShellApplication(out shellObject))
                {
                    object shell = shellObject;
                    string dir = Path.GetDirectoryName(shortcutPath);
                    string name = Path.GetFileName(shortcutPath);
                    folder = InvokeComMethod(shell, "NameSpace", dir);
                    if (folder != null)
                    {
                        folderItem = InvokeComMethod(folder, "ParseName", name);
                        if (folderItem != null)
                        {
                            bool isLink = false;
                            object isLinkObj = GetComProperty(folderItem, "IsLink");
                            if (isLinkObj != null && isLinkObj is bool)
                            {
                                isLink = (bool)isLinkObj;
                            }
                            if (isLink)
                            {
                                link = GetComProperty(folderItem, "GetLink");
                                if (link != null)
                                {
                                    target = GetComProperty(link, "Target");
                                    if (target != null)
                                    {
                                        string virtualPath = (string)GetComProperty(target, "Path");
                                        if (!string.IsNullOrEmpty(virtualPath))
                                        {
                                            return NormalizeShellPath(virtualPath) ?? virtualPath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                ReleaseComObjectSafe(target);
                ReleaseComObjectSafe(link);
                ReleaseComObjectSafe(folderItem);
                ReleaseComObjectSafe(folder);
                // shellObject はキャッシュ済みインスタンスのため解放しない
            }
            return null;
        }

        private string NormalizeShellPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string trimmed = path.Trim().TrimEnd('\\');
            if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                string lowerTrimmed = trimmed.ToLowerInvariant();
                string compactTrimmed = CompactForComparison(lowerTrimmed);

                if (compactTrimmed.Equals("shell:home") ||
                    compactTrimmed.Equals("shell:homefolder") ||
                    compactTrimmed.StartsWith("shell:quickaccess"))
                {
                    return HomeFolderPath;
                }
                if (compactTrimmed.StartsWith("shell:controlpanelfolder"))
                {
                    return AllControlPanelPath;
                }
                if (compactTrimmed.StartsWith("shell:mycomputerfolder") ||
                    compactTrimmed.StartsWith("shell:thispcfolder"))
                {
                    return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
                }
                if (compactTrimmed.StartsWith("shell:networkplacesfolder"))
                {
                    return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
                }
                if (compactTrimmed.StartsWith("shell:recyclebinfolder"))
                {
                    return "::{645FF040-5081-101B-9F08-00AA002F954E}";
                }
            }

            return NormalizeShellNamespacePath(trimmed);
        }

        public string NormalizeKnownPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (!string.IsNullOrEmpty(normalizedPath))
            {
                return normalizedPath;
            }

            return path;
        }

        internal string GetNavigableShellPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalizedPath = NormalizeShellPath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return path;
            }

            if (normalizedPath.Equals(HomeFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return GetResolvedHomeFolderPath();
            }

            if (IsControlPanelRootPathForNavigation(normalizedPath))
            {
                return AllControlPanelPath;
            }

            string controlPanelItemPath = GetControlPanelItemPathForNavigation(normalizedPath);
            if (!string.IsNullOrEmpty(controlPanelItemPath))
            {
                return ControlPanelItemNavigationPrefix + controlPanelItemPath;
            }

            return normalizedPath;
        }

        private bool IsControlPanelRootPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            string lowerPath = normalizedPath.ToLowerInvariant();
            if (!lowerPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            if (ContainsNonControlPanelRootGuid(lowerPath))
            {
                return false;
            }

            return true;
        }

        private string GetControlPanelItemPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return null;
            }

            string normalizedCompositeItemPath = NormalizeControlPanelItemShellPath(normalizedPath);
            if (!string.IsNullOrEmpty(normalizedCompositeItemPath))
            {
                return normalizedCompositeItemPath;
            }

            if (!normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string guidToken = GetLastGuidToken(normalizedPath);
            if (string.IsNullOrEmpty(guidToken) || IsControlPanelRootGuid(guidToken))
            {
                return null;
            }

            string standaloneItemPath = "::{" + guidToken.ToUpperInvariant() + "}";
            if (IsKnownControlPanelItemPathForNavigation(standaloneItemPath))
            {
                return standaloneItemPath;
            }

            return null;
        }

        private bool IsKnownControlPanelItemPathForNavigation(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.Equals(ProgramsAndFeaturesPath, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals(PowerOptionsPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPaths != null && _controlPanelItemPaths.Contains(normalizedPath))
                {
                    return true;
                }
            }

            string guidToken = GetLastGuidToken(normalizedPath);
            if (string.IsNullOrEmpty(guidToken) || IsControlPanelRootGuid(guidToken))
            {
                return false;
            }

            RegistryKey clsidKey = null;
            try
            {
                clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + guidToken.ToUpperInvariant() + "}");
                if (clsidKey == null)
                {
                    return false;
                }

                if (clsidKey.GetValue("System.ControlPanel.Category") != null)
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                if (clsidKey != null)
                {
                    clsidKey.Dispose();
                }
            }

            return false;
        }

        private string NormalizeBasicShellNamespacePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string trimmed = path.TrimEnd('\0').Trim().TrimEnd('\\');
            if (trimmed.Length > 2 && trimmed[trimmed.Length - 2] == '\\' && char.IsDigit(trimmed[trimmed.Length - 1]))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
            }
            if (trimmed.StartsWith("shell:::{", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length > 9)
                {
                    return "::{" + trimmed.Substring(9);
                }

                return null;
            }
            if (trimmed.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return null;
        }

        private string GetLastGuidToken(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string lastGuidToken = null;
            int searchIndex = 0;
            while (searchIndex < path.Length)
            {
                int openBraceIndex = path.IndexOf('{', searchIndex);
                if (openBraceIndex < 0)
                {
                    break;
                }

                int closeBraceIndex = path.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    break;
                }

                lastGuidToken = path.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                searchIndex = closeBraceIndex + 1;
            }

            return lastGuidToken;
        }

        private string NormalizeControlPanelItemShellPath(string normalizedNamespacePath)
        {
            if (string.IsNullOrEmpty(normalizedNamespacePath))
            {
                return null;
            }

            string lowerPath = normalizedNamespacePath.ToLowerInvariant();
            if (!ContainsControlPanelRootGuid(lowerPath))
            {
                return null;
            }

            string lastGuidToken = GetLastGuidToken(lowerPath);
            if (string.IsNullOrEmpty(lastGuidToken) || IsControlPanelRootGuid(lastGuidToken))
            {
                return null;
            }

            return "::{" + lastGuidToken.ToUpperInvariant() + "}";
        }

        /// <summary>
        /// シェル名前空間パス (shell:::{...} / ::{...}) を ::{...} 形式に正規化する。
        /// シェル名前空間パスでない場合は null を返す。
        /// </summary>
        public string NormalizeShellNamespacePath(string path)
        {
            string normalizedNamespacePath = NormalizeBasicShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedNamespacePath))
            {
                return null;
            }

            string normalizedControlPanelItemPath = NormalizeControlPanelItemShellPath(normalizedNamespacePath);
            if (!string.IsNullOrEmpty(normalizedControlPanelItemPath))
            {
                return normalizedControlPanelItemPath;
            }

            return normalizedNamespacePath;
        }

        public string MapLocationNameToKnownShellPath(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return null;

            string lowerName = locationName.ToLowerInvariant();
            string compactName = CompactForComparison(lowerName);

            string compactLocalizedCP = CompactForComparison(GetLocalizedControlPanelTitle().ToLowerInvariant());
            if (compactName.Equals("controlpanel") || compactName.Equals("allcontrolpanelitems") || compactName.Equals(compactLocalizedCP))
            {
                return AllControlPanelPath;
            }
            string compactLocalizedHome = CompactForComparison(GetLocalizedHomeTitle().ToLowerInvariant());
            if (compactName.Equals("home") ||
                compactName.Equals("quickaccess") ||
                compactName.Equals(compactLocalizedHome))
            {
                return HomeFolderPath;
            }
            string compactLocalizedNetwork = CompactForComparison(GetLocalizedNetworkTitle().ToLowerInvariant());
            if (compactName.Equals("network") || compactName.Equals(compactLocalizedNetwork))
            {
                return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
            }
            string compactLocalizedBin = CompactForComparison(GetLocalizedRecycleBinTitle().ToLowerInvariant());
            if (compactName.Equals("recyclebin") || compactName.Equals(compactLocalizedBin))
            {
                return "::{645FF040-5081-101B-9F08-00AA002F954E}";
            }
            string compactLocalizedPC = CompactForComparison(GetLocalizedThisPCTitle().ToLowerInvariant());
            if (compactName.Equals("pc") || compactName.Equals("thispc") || compactName.Equals(compactLocalizedPC))
            {
                return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }

            if (compactName.Equals("programsandfeatures"))
            {
                return ProgramsAndFeaturesPath;
            }
            if (compactName.Equals("poweroptions"))
            {
                return PowerOptionsPath;
            }

            string controlPanelItemPath = FindControlPanelItemPathByTitle(locationName);
            if (!string.IsNullOrEmpty(controlPanelItemPath))
            {
                return controlPanelItemPath;
            }

            return null;
        }

        private string FindControlPanelItemPathByTitle(string locationName)
        {
            if (string.IsNullOrEmpty(locationName))
            {
                return null;
            }

            string compactLocationName = CompactForComparison(locationName.ToLowerInvariant());
            if (string.IsNullOrEmpty(compactLocationName))
            {
                return null;
            }

            EnsureControlPanelItemTitleMap();

            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPathsByTitle == null)
                {
                    return null;
                }

                string mappedPath;
                if (_controlPanelItemPathsByTitle.TryGetValue(compactLocationName, out mappedPath))
                {
                    return mappedPath;
                }
            }

            return null;
        }

        private void EnsureControlPanelItemTitleMap()
        {
            lock (_controlPanelItemTitleMapSync)
            {
                if (_controlPanelItemPathsByTitle != null)
                {
                    return;
                }

                Dictionary<string, string> titleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> itemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                RegistryKey namespaceRootKey = null;
                try
                {
                    namespaceRootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace");
                    if (namespaceRootKey != null)
                    {
                        string[] subKeyNames = namespaceRootKey.GetSubKeyNames();
                        for (int i = 0; i < subKeyNames.Length; i++)
                        {
                            string subKeyName = subKeyNames[i];
                            if (string.IsNullOrEmpty(subKeyName))
                            {
                                continue;
                            }

                            string trimmedSubKeyName = subKeyName.Trim();
                            if (!trimmedSubKeyName.StartsWith("{", StringComparison.OrdinalIgnoreCase) ||
                                !trimmedSubKeyName.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string shellPath = "::" + trimmedSubKeyName;
                            string title = GetFolderNameInternal(shellPath);
                            AddControlPanelItemTitleMapEntry(titleMap, title, shellPath);
                            AddControlPanelItemPathEntry(itemPaths, shellPath);

                            RegistryKey clsidKey = null;
                            try
                            {
                                clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + trimmedSubKeyName);
                                if (clsidKey != null)
                                {
                                    object defaultTitleObj = clsidKey.GetValue(null);
                                    string defaultTitle = defaultTitleObj as string;
                                    AddControlPanelItemTitleMapEntry(titleMap, defaultTitle, shellPath);
                                }
                            }
                            catch
                            {
                            }
                            finally
                            {
                                if (clsidKey != null)
                                {
                                    clsidKey.Dispose();
                                }
                            }
                        }
                    }

                    AddControlPanelItemTitleMapEntry(titleMap, "Programs and Features", ProgramsAndFeaturesPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "プログラムと機能", ProgramsAndFeaturesPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "Power Options", PowerOptionsPath);
                    AddControlPanelItemTitleMapEntry(titleMap, "電源オプション", PowerOptionsPath);
                    AddControlPanelItemPathEntry(itemPaths, ProgramsAndFeaturesPath);
                    AddControlPanelItemPathEntry(itemPaths, PowerOptionsPath);
                }
                catch
                {
                }
                finally
                {
                    if (namespaceRootKey != null)
                    {
                        namespaceRootKey.Dispose();
                    }
                }

                _controlPanelItemPathsByTitle = titleMap;
                _controlPanelItemPaths = itemPaths;
            }
        }

        private void AddControlPanelItemTitleMapEntry(Dictionary<string, string> titleMap, string title, string path)
        {
            if (titleMap == null || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(path))
            {
                return;
            }

            string compactTitle = CompactForComparison(title.ToLowerInvariant());
            if (string.IsNullOrEmpty(compactTitle))
            {
                return;
            }

            if (!titleMap.ContainsKey(compactTitle))
            {
                titleMap.Add(compactTitle, path);
            }
        }

        private void AddControlPanelItemPathEntry(HashSet<string> itemPaths, string path)
        {
            if (itemPaths == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            string normalizedPath = NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                normalizedPath = NormalizeShellPath(path);
            }

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return;
            }

            if (IsControlPanelRootPath(normalizedPath))
            {
                return;
            }

            itemPaths.Add(normalizedPath);
        }

        private string ExtractShellPathFromShortcutArguments(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return null;

            string trimmed = arguments.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;

            string normalizedDirect = NormalizeShellPath(trimmed);
            if (!string.IsNullOrEmpty(normalizedDirect))
            {
                return normalizedDirect;
            }

            string extractedFromShell = ExtractShellPathFromText(trimmed, "shell:::{");
            if (!string.IsNullOrEmpty(extractedFromShell))
            {
                return extractedFromShell;
            }

            string extractedFromClsid = ExtractShellPathFromText(trimmed, "::{");
            if (!string.IsNullOrEmpty(extractedFromClsid))
            {
                return extractedFromClsid;
            }

            string compact = CompactForComparison(trimmed.ToLowerInvariant());
            if (compact.Equals("controlpanelfolder"))
            {
                return AllControlPanelPath;
            }
            if (compact.Contains("microsoft.programsandfeatures") || compact.Contains("appwiz.cpl"))
            {
                return ProgramsAndFeaturesPath;
            }
            if (compact.Contains("microsoft.poweroptions") || compact.Contains("powercfg.cpl"))
            {
                return PowerOptionsPath;
            }

            return null;
        }

        private string ExtractShellPathFromText(string text, string marker)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (string.IsNullOrEmpty(marker)) return null;

            int startIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0) return null;

            string token = ExtractShellToken(text, startIndex);
            if (string.IsNullOrEmpty(token)) return null;

            return NormalizeShellPath(token);
        }

        private string ExtractShellToken(string text, int startIndex)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (startIndex < 0 || startIndex >= text.Length) return null;

            int endIndex = text.Length;
            for (int i = startIndex; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch) || ch == '"')
                {
                    endIndex = i;
                    break;
                }
            }

            string token = text.Substring(startIndex, endIndex - startIndex);
            token = token.Trim().TrimEnd(',');
            return token;
        }

        public string CompactForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == ' ' || ch == '\t' || ch == '\u3000')
                {
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        internal string GetExternalExplorerLaunchPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return GetNavigableShellPath(path);
        }

        public void OpenInNewWindow(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string externalPath = GetExternalExplorerLaunchPath(path);
                System.Diagnostics.Process process = System.Diagnostics.Process.Start("explorer.exe", "\"" + externalPath + "\"");
                if (process != null)
                {
                    process.Dispose();
                }
            }
            catch { }
        }
    }
}





