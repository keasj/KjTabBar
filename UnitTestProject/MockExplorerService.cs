using KjTabBar.Models;
using System;
using System.Collections.Generic;

namespace UnitTestProject
{
    public class MockExplorerService : IExplorerService
    {
        public string AllControlPanelPath { get; set; } = @"AllControlPanelPath";
        public string HomeFolderPath { get; set; } = @"HomeFolderPath";
        public string ProgramsAndFeaturesPath { get; set; } = @"ProgramsAndFeaturesPath";
        public string PowerOptionsPath { get; set; } = @"PowerOptionsPath";

        public virtual string GetFolderName(string path) => "MockFolder";
        public string GetLocalizedControlPanelTitle() => "Control Panel";
        public string GetLocalizedHomeTitle() => "Home";
        public string GetLocalizedNetworkTitle() => "Network";
        public string GetLocalizedRecycleBinTitle() => "Recycle Bin";
        public string GetLocalizedThisPCTitle() => "This PC";
        public string GetResolvedHomeFolderPath() => @"C:\MockHome";
        public List<string> GetSelectedItems(IntPtr explorerHwnd) => new List<string>();
        public Func<string, bool> IsControlPanelPathFunc { get; set; }
        public Func<string, bool> IsControlPanelRootPathFunc { get; set; }
        public Func<string, bool> IsTransientShellPlaceholderPathFunc { get; set; }
        public Func<string, string> NormalizeKnownPathFunc { get; set; }
        public Func<string, string> NormalizeShellNamespacePathFunc { get; set; }
        public Func<List<IntPtr>> FindExplorerWindowsFunc { get; set; }
        public Func<IntPtr, string> GetCurrentPathFunc { get; set; }
        public bool OpenInNewWindowResult { get; set; } = true;
        public string OpenedInNewWindowPath { get; private set; }

        public List<IntPtr> FindExplorerWindows() => FindExplorerWindowsFunc != null ? FindExplorerWindowsFunc() : new List<IntPtr>();
        public virtual string GetCurrentPath(IntPtr explorerHwnd) => GetCurrentPathFunc != null ? GetCurrentPathFunc(explorerHwnd) : @"C:\MockPath";
        public bool IsControlPanelPath(string path) => IsControlPanelPathFunc != null ? IsControlPanelPathFunc(path) : path == AllControlPanelPath;
        public bool IsControlPanelRootPath(string path) => IsControlPanelRootPathFunc != null ? IsControlPanelRootPathFunc(path) : false;
        public virtual bool IsTabPathCurrentlyAvailable(string path) => true;
        public bool IsTransientShellPlaceholderPath(string path) => IsTransientShellPlaceholderPathFunc != null ? IsTransientShellPlaceholderPathFunc(path) : false;
        public string MapLocationNameToKnownShellPath(string locationName) => locationName;
        public virtual bool Navigate(IntPtr explorerHwnd, string path) => true;
        public string NormalizeKnownPath(string path) => NormalizeKnownPathFunc != null ? NormalizeKnownPathFunc(path) : path;
        public string NormalizeShellNamespacePath(string path) => NormalizeShellNamespacePathFunc != null ? NormalizeShellNamespacePathFunc(path) : path;
        public bool OpenInNewWindow(string path)
        {
            OpenedInNewWindowPath = path;
            return OpenInNewWindowResult;
        }
        public void CreateShortcuts(string[] sourceFiles, string destinationFolder, IntPtr targetWindowHandle) { }
        public void CreateSymbolicLinks(string[] sourceFiles, string destinationFolder, IntPtr targetWindowHandle) { }
        public void ReleaseCachedComObjects() { }
        public virtual string ResolveShortcutTarget(string path) => path;
        public void SelectItems(IntPtr explorerHwnd, List<string> itemPaths) { }

        public KjTabBar.Helpers.NativeMethods.RECT GetExplorerWindowRect(IntPtr hwnd)
        {
            return new KjTabBar.Helpers.NativeMethods.RECT { Left = 0, Top = 0, Right = 800, Bottom = 600 };
        }

        public string GetParentFolderName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                string parent = System.IO.Path.GetDirectoryName(path.TrimEnd('\\'));
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.GetFileName(parent) ?? parent;
            }
            catch { return null; }
        }
    }
}
