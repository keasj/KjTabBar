using System;
using System.Collections.Generic;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    public interface IExplorerService
    {
        List<IntPtr> FindExplorerWindows();
        string GetCurrentPath(IntPtr explorerHwnd);
        List<string> GetSelectedItems(IntPtr explorerHwnd);
        void SelectItems(IntPtr explorerHwnd, List<string> itemPaths);
        string GetResolvedHomeFolderPath();
        string GetFolderName(string path);
        bool IsControlPanelPath(string path);
        bool IsControlPanelRootPath(string path);
        string NormalizeShellNamespacePath(string path);
        string NormalizeKnownPath(string path);
        bool IsTabPathCurrentlyAvailable(string path);
        string MapLocationNameToKnownShellPath(string locationName);
        string ResolveShortcutTarget(string path);
        bool IsTransientShellPlaceholderPath(string path);
        bool Navigate(IntPtr explorerHwnd, string path);
        bool OpenInNewWindow(string path);
        void CreateShortcuts(string[] sourceFiles, string destinationFolder, IntPtr targetWindowHandle);
        void CreateSymbolicLinks(string[] sourceFiles, string destinationFolder, IntPtr targetWindowHandle);
        string GetParentFolderName(string path);

        string GetLocalizedControlPanelTitle();
        string GetLocalizedNetworkTitle();
        string GetLocalizedRecycleBinTitle();
        string GetLocalizedThisPCTitle();
        string GetLocalizedHomeTitle();

        void ReleaseCachedComObjects();

        NativeMethods.RECT GetExplorerWindowRect(IntPtr hwnd);

        string AllControlPanelPath { get; }
        string HomeFolderPath { get; }
        string ProgramsAndFeaturesPath { get; }
        string PowerOptionsPath { get; }
    }
}
