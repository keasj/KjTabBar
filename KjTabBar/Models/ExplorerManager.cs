using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    public class ExplorerManager : IExplorerService, IDisposable
    {
        public string AllControlPanelPath { get; } = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
        public string HomeFolderPath { get; } = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
        public string ProgramsAndFeaturesPath { get; } = "::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}";
        public string PowerOptionsPath { get; } = "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";
        private const string ControlPanelFolderShellPath = "shell:ControlPanelFolder";
        private const string ControlPanelItemNavigationPrefix = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\";

        private readonly ShellLocationNameResolver _shellLocationNameResolver;
        private readonly ShellKnownLocationCache _shellKnownLocationCache;
        private readonly ShellItemPathResolver _shellItemPathResolver;
        private readonly ShellPathAvailabilityEvaluator _shellPathAvailabilityEvaluator;
        private readonly ShellExplorerWindowMatcher _shellExplorerWindowMatcher;
        private readonly ShellFolderItemSelectionHelper _shellFolderItemSelectionHelper;
        private readonly ShellSelectedItemsReader _shellSelectedItemsReader;
        private readonly ShellFolderPathReader _shellFolderPathReader;
        private readonly ShellCurrentPathResolver _shellCurrentPathResolver;
        private readonly ShellWindowNavigator _shellWindowNavigator;
        private readonly ShellNamespaceTitleReader _shellNamespaceTitleReader;
        private readonly ShellParentFolderTitleReader _shellParentFolderTitleReader;
        private readonly ShellShortcutManager _shellShortcutManager;
        private readonly ShellFolderNameResolver _shellFolderNameResolver;
        private readonly ShellPathNormalizer _shellPathNormalizer;
        private readonly ShellWindowComInterop _comInterop;

        // ヘルパークラス
        private readonly ShellLinkCreator _linkCreator;
        private readonly ShellItemSelector _itemSelector;

        private readonly Services.ShellWorkerClient _shellWorker;
        private readonly Services.ShellMetadataCache _metadata = new Services.ShellMetadataCache();
        private readonly object _availabilitySync = new object();
        private readonly Dictionary<string, bool> _availability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        internal event EventHandler FolderTitlesChanged
        {
            add { _metadata.Updated += value; }
            remove { _metadata.Updated -= value; }
        }
        private static bool IsOnUiThread
        {
            get { return System.Windows.Application.Current != null && System.Windows.Application.Current.Dispatcher.CheckAccess(); }
        }

        internal bool UsesShellWorker { get { return _shellWorker != null; } }

        public ExplorerManager() : this(false)
        {
        }

        internal ExplorerManager(bool useShellWorker)
        {
            _shellWorker = useShellWorker ? new Services.ShellWorkerClient() : null;
            _shellNamespaceTitleReader = new ShellNamespaceTitleReader(
                delegate (object obj, string methodName, object[] args) { return ShellWindowComInterop.InvokeComMethod(obj, methodName, args); },
                ShellWindowComInterop.GetComProperty,
                ShellWindowComInterop.ReleaseComObjectSafe);
            _shellParentFolderTitleReader = new ShellParentFolderTitleReader(
                delegate (object obj, string methodName, object[] args) { return ShellWindowComInterop.InvokeComMethod(obj, methodName, args); },
                ShellWindowComInterop.GetComProperty,
                ShellWindowComInterop.ReleaseComObjectSafe);
            _shellLocationNameResolver = new ShellLocationNameResolver(
                AllControlPanelPath,
                HomeFolderPath,
                ProgramsAndFeaturesPath,
                PowerOptionsPath,
                delegate (string title) { return _shellPathNormalizer.FindControlPanelItemPathByTitle(title); });
            _shellPathNormalizer = new ShellPathNormalizer(
                AllControlPanelPath,
                HomeFolderPath,
                ProgramsAndFeaturesPath,
                PowerOptionsPath,
                GetLocalizedControlPanelTitle,
                GetLocalizedHomeTitle,
                GetLocalizedNetworkTitle,
                GetLocalizedRecycleBinTitle,
                GetLocalizedThisPCTitle,
                GetResolvedHomeFolderPath,
                _shellLocationNameResolver,
                delegate (string path) { return GetNamespaceTitleForWorker(path); });
            _shellFolderNameResolver = new ShellFolderNameResolver(
                GetLocalizedHomeTitle,
                GetLocalizedControlPanelTitle,
                IsControlPanelRootPath,
                NormalizeKnownPath,
                GetNavigableShellPath,
                delegate { object shell; ShellWindowComInterop.TryGetShellApplication(out shell); return shell; },
                _shellNamespaceTitleReader,
                _shellParentFolderTitleReader);
            _shellKnownLocationCache = new ShellKnownLocationCache(
                delegate (string shellPath, string fallback) { return GetNamespaceTitleForWorker(shellPath); },
                IsShellPathAvailable,
                delegate { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); });
            _shellShortcutManager = new ShellShortcutManager(
                GetFolderName,
                GetNavigableShellPath,
                NormalizeShellPath,
                delegate { object shell; ShellWindowComInterop.TryGetShellApplication(out shell); return shell; },
                ShellWindowComInterop.ReleaseComObjectSafe,
                ShellWindowComInterop.GetComProperty,
                delegate (object obj, string methodName, object[] args) { return ShellWindowComInterop.InvokeComMethod(obj, methodName, args); },
                AllControlPanelPath,
                ProgramsAndFeaturesPath,
                PowerOptionsPath);

            _comInterop = new ShellWindowComInterop(
                _shellExplorerWindowMatcher = new ShellExplorerWindowMatcher(
                    ShellWindowComInterop.GetComProperty,
                    NativeMethods.GetAncestor),
                _shellFolderPathReader = new ShellFolderPathReader(
                    ShellWindowComInterop.GetComProperty,
                    ShellWindowComInterop.ReleaseComObjectSafe),
                _shellCurrentPathResolver = new ShellCurrentPathResolver(
                    MapLocationNameToKnownShellPath,
                    IsControlPanelPath,
                    IsControlPanelRootPath,
                    NormalizeShellPath,
                    IsNavigablePath),
                _shellSelectedItemsReader = new ShellSelectedItemsReader(
                    ShellWindowComInterop.GetComProperty,
                    delegate (object obj, string methodName, object[] args) { return ShellWindowComInterop.InvokeComMethod(obj, methodName, args); },
                    ShellWindowComInterop.ReleaseComObjectSafe,
                    AppLogger.LogErrorThrottled),
                _shellFolderItemSelectionHelper = new ShellFolderItemSelectionHelper(
                    ShellWindowComInterop.GetComProperty,
                    delegate (object obj, string methodName, object[] args) { return ShellWindowComInterop.InvokeComMethod(obj, methodName, args); },
                    ShellWindowComInterop.ReleaseComObjectSafe,
                    _shellItemPathResolver = new ShellItemPathResolver(NormalizeKnownPath),
                    AppLogger.LogErrorThrottled),
                _shellWindowNavigator = new ShellWindowNavigator(
                    delegate (object obj, string methodName, object[] args)
                    {
                        return obj.GetType().InvokeMember(
                            methodName,
                            System.Reflection.BindingFlags.InvokeMethod,
                            null,
                            obj,
                            args);
                    },
                    delegate (object obj, string methodName, object[] args)
                    {
                        obj.GetType().InvokeMember(
                            methodName,
                            System.Reflection.BindingFlags.InvokeMethod,
                            null,
                            obj,
                            args);
                    }),
                GetNavigableShellPath,
                IsNavigablePath
            );
            _shellPathAvailabilityEvaluator = new ShellPathAvailabilityEvaluator(NormalizeKnownPath);

            // ヘルパーの生成
            _linkCreator = new ShellLinkCreator(_shellShortcutManager);
            _itemSelector = new ShellItemSelector(_comInterop);
        }

        internal string GetNamespaceTitleForWorker(string path)
        {
            if (_shellWorker != null) return _shellWorker.Invoke(Services.ShellOperation.NamespaceTitle, path)[0];
            return _shellFolderNameResolver.GetFolderNameInternal(path);
        }

        internal bool IsShellPathAvailableForWorker(string path)
        {
            return IsShellPathAvailable(path);
        }

        internal bool DesktopContains(string path)
        {
            return _shellWorker.Invoke(Services.ShellOperation.DesktopContains, path)[0] == "1";
        }

        internal bool IsDesktopShortcutTargetPath(string path)
        {
            return _shellWorker.Invoke(Services.ShellOperation.DesktopShortcutMatch, path)[0] == "1";
        }

        internal byte[] GetIconBytes(string path)
        {
            return Convert.FromBase64String(_shellWorker.Invoke(Services.ShellOperation.Icon, path)[0]);
        }

        public void Dispose()
        {
            if (_shellWorker != null) _shellWorker.Dispose();
        }

        public void ReleaseCachedComObjects()
        {
            if (_shellWorker != null)
            {
                if (_shellWorker.IsStarted) _shellWorker.Invoke(Services.ShellOperation.ReleaseCaches);
                return;
            }
            _comInterop.ReleaseCachedComObjects();
        }

        public List<IntPtr> FindExplorerWindows()
        {
            return _comInterop.FindExplorerWindows();
        }

        internal string ResolveDesktopInvokedShortcut(IntPtr source, int child)
        {
            if (_shellWorker != null) return _shellWorker.Invoke(Services.ShellOperation.DesktopInvokedShortcut,
                source.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                child.ToString(System.Globalization.CultureInfo.InvariantCulture))[0];
            return Services.DesktopRepeatedLaunchService.ResolveInvokedShortcut(source, child, this);
        }

        public string GetCurrentPath(IntPtr explorerHwnd)
        {
            if (_shellWorker != null) return _shellWorker.Invoke(Services.ShellOperation.CurrentPath, explorerHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))[0];
            return _comInterop.GetCurrentPath(explorerHwnd);
        }

        public List<string> GetSelectedItems(IntPtr explorerHwnd)
        {
            if (_shellWorker != null) return new List<string>(_shellWorker.Invoke(Services.ShellOperation.SelectedItems, explorerHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return _comInterop.GetSelectedItems(explorerHwnd);
        }

        public void SelectItems(IntPtr explorerHwnd, List<string> itemPaths)
        {
            if (_shellWorker != null)
            {
                List<string> arguments = new List<string> { explorerHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) };
                if (itemPaths != null) arguments.AddRange(itemPaths);
                _shellWorker.Invoke(Services.ShellOperation.SelectItems, arguments.ToArray());
                return;
            }
            _itemSelector.SelectItems(explorerHwnd, itemPaths);
        }

        public string GetLocalizedControlPanelTitle()
        {
            return _shellKnownLocationCache.GetLocalizedControlPanelTitle(AllControlPanelPath);
        }

        public string GetLocalizedNetworkTitle()
        {
            return _shellKnownLocationCache.GetLocalizedNetworkTitle();
        }

        public string GetLocalizedRecycleBinTitle()
        {
            return _shellKnownLocationCache.GetLocalizedRecycleBinTitle();
        }

        public string GetLocalizedThisPCTitle()
        {
            return _shellKnownLocationCache.GetLocalizedThisPCTitle();
        }

        public string GetLocalizedHomeTitle()
        {
            return _shellKnownLocationCache.GetLocalizedHomeTitle(HomeFolderPath);
        }

        public string GetResolvedHomeFolderPath()
        {
            return _shellKnownLocationCache.GetResolvedHomeFolderPath(HomeFolderPath);
        }

        private bool IsShellPathAvailable(string shellPath)
        {
            if (_shellWorker != null) return _shellWorker.Invoke(Services.ShellOperation.ShellPathAvailable, shellPath)[0] == "1";
            IntPtr pidl = IntPtr.Zero;
            uint dummyOut;
            try
            {
                int hr = NativeMethods.SHParseDisplayName(shellPath, IntPtr.Zero, out pidl, 0, out dummyOut);
                return hr == 0 && pidl != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    NativeMethods.ILFree(pidl);
                }
            }
        }

        public bool IsControlPanelRootPath(string path)
        {
            return _shellPathNormalizer.IsControlPanelRootPath(path);
        }

        public bool IsControlPanelItemPath(string path)
        {
            return _shellPathNormalizer.IsControlPanelItemPath(path);
        }

        public bool IsControlPanelPath(string path)
        {
            return _shellPathNormalizer.IsControlPanelPath(path);
        }

        public bool IsTransientShellPlaceholderPath(string path)
        {
            return _shellPathNormalizer.IsTransientShellPlaceholderPath(path);
        }

        public bool IsNavigablePath(string path)
        {
            return _shellPathAvailabilityEvaluator.IsNavigablePath(path);
        }

        public bool IsTabPathCurrentlyAvailable(string path)
        {
            if (_shellWorker != null)
            {
                if (string.IsNullOrEmpty(path)) return false;
                if (IsOnUiThread)
                {
                    lock (_availabilitySync)
                    {
                        bool available;
                        return !_availability.TryGetValue(path, out available) || available;
                    }
                }
                return _shellWorker.Invoke(Services.ShellOperation.PathAvailable, path)[0] == "1";
            }
            return _shellPathAvailabilityEvaluator.IsTabPathCurrentlyAvailable(path);
        }

        public bool Navigate(IntPtr explorerHwnd, string path)
        {
            if (_shellWorker != null)
                return _shellWorker.Invoke(Services.ShellOperation.Navigate,
                    explorerHwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture), path)[0] == "1";
            return _comInterop.Navigate(explorerHwnd, path);
        }

        public NativeMethods.RECT GetExplorerWindowRect(IntPtr explorerHwnd)
        {
            NativeMethods.RECT explorerRect;
            int hr = NativeMethods.DwmGetWindowAttribute(explorerHwnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out explorerRect,
                Marshal.SizeOf(typeof(NativeMethods.RECT)));
            if (hr != 0)
            {
                NativeMethods.GetWindowRect(explorerHwnd, out explorerRect);
            }

            NativeMethods.RECT result;
            result.Left = explorerRect.Left;
            result.Top = explorerRect.Top;
            result.Right = explorerRect.Right;
            result.Bottom = explorerRect.Bottom;
            return result;
        }

        private string GetDisplayMetadata(Services.ShellOperation operation, string path)
        {
            string fallback = path;
            try
            {
                string displayPath = operation == Services.ShellOperation.ParentFolderName
                    ? Path.GetDirectoryName((path ?? string.Empty).TrimEnd('\\')) : path;
                string name = Path.GetFileName((displayPath ?? string.Empty).TrimEnd('\\'));
                if (!string.IsNullOrEmpty(name)) fallback = name;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            return _metadata.Get(operation.ToString() + ":" + path,
                () => _shellWorker.Invoke(operation, path)[0], IsOnUiThread, fallback);
        }

        internal Dictionary<string, bool> GetPathAvailability(IList<string> paths)
        {
            string[] results = _shellWorker.Invoke(Services.ShellOperation.PathsAvailable, new List<string>(paths).ToArray());
            if (results.Length != paths.Count) throw new InvalidDataException("Incomplete availability response.");
            Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < paths.Count; i++) values[paths[i]] = results[i] == "1";
            lock (_availabilitySync)
            {
                _availability.Clear();
                foreach (KeyValuePair<string, bool> value in values) _availability[value.Key] = value.Value;
            }
            return values;
        }

        public string GetFolderName(string path)
        {
            if (_shellWorker != null) return GetDisplayMetadata(Services.ShellOperation.FolderName, path);
            return _shellFolderNameResolver.GetFolderName(path);
        }

        public string GetParentFolderName(string path)
        {
            if (_shellWorker != null) return GetDisplayMetadata(Services.ShellOperation.ParentFolderName, path);
            return _shellFolderNameResolver.GetParentFolderName(path);
        }

        public string ResolveShortcutTarget(string shortcutPath)
        {
            if (_shellWorker != null) return _shellWorker.Invoke(Services.ShellOperation.ResolveShortcut, shortcutPath)[0];
            return _linkCreator.ResolveShortcutTarget(shortcutPath);
        }

        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory)
        {
            _linkCreator.CreateShortcuts(sourcePaths, destinationDirectory, IntPtr.Zero);
        }

        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            _linkCreator.CreateShortcuts(sourcePaths, destinationDirectory, ownerHwnd);
        }

        public void CreateSymbolicLinks(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            _linkCreator.CreateSymbolicLinks(sourcePaths, destinationDirectory, ownerHwnd);
        }

        public string NormalizeKnownPath(string path)
        {
            return _shellPathNormalizer.NormalizeKnownPath(path);
        }

        public string NormalizeShellNamespacePath(string path)
        {
            return _shellPathNormalizer.NormalizeShellNamespacePath(path);
        }

        public string MapLocationNameToKnownShellPath(string locationName)
        {
            return _shellPathNormalizer.MapLocationNameToKnownShellPath(locationName);
        }

        public string CompactForComparison(string text)
        {
            return ShellLocationNameResolver.CompactForComparison(text);
        }

        internal string NormalizeShellPath(string path)
        {
            return _shellPathNormalizer.NormalizeShellPath(path);
        }

        internal string GetNavigableShellPath(string path)
        {
            return _shellPathNormalizer.GetNavigableShellPath(path);
        }

        internal string GetExternalExplorerLaunchPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return GetNavigableShellPath(path);
        }

        internal bool ShouldLaunchPlainExplorerForNewWindow(string path)
        {
            return IsControlPanelRootPath(path);
        }

        internal string GetNewWindowNavigationPath(string path)
        {
            if (ShouldLaunchPlainExplorerForNewWindow(path))
            {
                return ControlPanelFolderShellPath;
            }

            return path;
        }

        public bool OpenInNewWindow(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (ShouldLaunchPlainExplorerForNewWindow(path))
                {
                    string navigationPath = GetNewWindowNavigationPath(path);
                    System.Diagnostics.Process controlPanelExplorerProcess = System.Diagnostics.Process.Start("explorer.exe", "\"" + navigationPath + "\"");
                    if (controlPanelExplorerProcess != null)
                    {
                        controlPanelExplorerProcess.Dispose();
                        return true;
                    }

                    AppLogger.LogInfo("ExplorerManager", "Failed to open a new Explorer window for the requested Control Panel root path.");
                    System.Windows.MessageBox.Show(
                        "別ウィンドウでコントロール パネルを開く操作に失敗しました。",
                        "起動エラー",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return false;
                }

                string externalPath = GetExternalExplorerLaunchPath(path);
                System.Diagnostics.Process process = System.Diagnostics.Process.Start("explorer.exe", "\"" + externalPath + "\"");
                if (process != null)
                {
                    process.Dispose();
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ExplorerManager", "Failed to open explorer in new window.", ex);
                System.Windows.MessageBox.Show(
                    "別ウィンドウで開く操作に失敗しました。",
                    "起動エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }
    }
}
