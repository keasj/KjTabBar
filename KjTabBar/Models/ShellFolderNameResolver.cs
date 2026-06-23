using System;
using System.IO;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellFolderNameResolver
    {
        private readonly Func<string> _getLocalizedHomeTitle;
        private readonly Func<string> _getLocalizedControlPanelTitle;
        private readonly Func<string, bool> _isControlPanelRootPath;
        private readonly Func<string, string> _normalizeKnownPath;
        private readonly Func<string, string> _getNavigableShellPath;
        private readonly Func<object> _getShellApplication;
        private readonly ShellNamespaceTitleReader _shellNamespaceTitleReader;
        private readonly ShellParentFolderTitleReader _shellParentFolderTitleReader;

        public ShellFolderNameResolver(
            Func<string> getLocalizedHomeTitle,
            Func<string> getLocalizedControlPanelTitle,
            Func<string, bool> isControlPanelRootPath,
            Func<string, string> normalizeKnownPath,
            Func<string, string> getNavigableShellPath,
            Func<object> getShellApplication,
            ShellNamespaceTitleReader shellNamespaceTitleReader,
            ShellParentFolderTitleReader shellParentFolderTitleReader)
        {
            _getLocalizedHomeTitle = getLocalizedHomeTitle;
            _getLocalizedControlPanelTitle = getLocalizedControlPanelTitle;
            _isControlPanelRootPath = isControlPanelRootPath;
            _normalizeKnownPath = normalizeKnownPath;
            _getNavigableShellPath = getNavigableShellPath;
            _getShellApplication = getShellApplication;
            _shellNamespaceTitleReader = shellNamespaceTitleReader;
            _shellParentFolderTitleReader = shellParentFolderTitleReader;
        }

        public string GetFolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return _getLocalizedHomeTitle();
            if (_isControlPanelRootPath(path)) return _getLocalizedControlPanelTitle();
            if (_normalizeKnownPath(path).Equals("::{679F85CB-0220-4080-B29B-5540CC05AAB6}", StringComparison.OrdinalIgnoreCase)) return _getLocalizedHomeTitle();
            return GetFolderNameInternal(path);
        }

        public string GetParentFolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string displayPath = _getNavigableShellPath(path);
            if (string.IsNullOrEmpty(displayPath))
            {
                displayPath = path;
            }

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
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("ShellFolderNameResolver", "GetParentFolderNameShellItemFailed", "Failed to get parent folder name by IShellItem.", ex, TimeSpan.FromMinutes(5));
            }
            finally
            {
                ShellWindowComInterop.ReleaseComObjectSafe(parent);
                ShellWindowComInterop.ReleaseComObjectSafe(item);
                if (pidl != IntPtr.Zero) NativeMethods.ILFree(pidl);
            }

            object shellObject = _getShellApplication();
            try
            {
                if (shellObject == null) return null;
                return _shellParentFolderTitleReader.ReadTitle(shellObject, displayPath);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellFolderNameResolver", "Failed to get parent folder name.", ex);
                return null;
            }
        }

        public string GetFolderNameInternal(string path)
        {
            if (string.IsNullOrEmpty(path)) return _getLocalizedHomeTitle();

            string displayPath = _getNavigableShellPath(path);
            if (string.IsNullOrEmpty(displayPath))
            {
                displayPath = path;
            }

            object shellObject = _getShellApplication();
            try
            {
                if (shellObject != null)
                {
                    string title = _shellNamespaceTitleReader.ReadTitle(shellObject, displayPath);
                    if (!string.IsNullOrEmpty(title))
                    {
                        return title;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ShellFolderNameResolver", "Shell.Application title lookup failed.", ex);
            }

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
                            string title = Marshal.PtrToStringUni(pName);
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
            catch (Exception ex)
            {
                AppLogger.LogError("ShellFolderNameResolver", "Failed to derive folder name from path.", ex);
                return path;
            }
        }
    }
}
