using System;

namespace KjTabBar.Models
{
    internal sealed class ShellExplorerWindowMatcher
    {
        private readonly Func<object, string, object> _getComProperty;
        private readonly Func<IntPtr, uint, IntPtr> _getAncestor;

        public ShellExplorerWindowMatcher(
            Func<object, string, object> getComProperty,
            Func<IntPtr, uint, IntPtr> getAncestor)
        {
            _getComProperty = getComProperty;
            _getAncestor = getAncestor;
        }

        public bool IsExplorerWindow(object window, string fullName)
        {
            return !string.IsNullOrEmpty(fullName) &&
                   fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                   _getComProperty(window, "HWND") != null;
        }

        public bool TryGetWindowHwnd(object window, out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            object hwndObject = _getComProperty(window, "HWND");
            if (hwndObject == null)
            {
                return false;
            }

            try
            {
                hwnd = (IntPtr)Convert.ToInt64(hwndObject);
                return true;
            }
            catch
            {
                hwnd = IntPtr.Zero;
                return false;
            }
        }

        public bool MatchesTargetWindow(IntPtr shellWindowHwnd, IntPtr explorerHwnd)
        {
            if (shellWindowHwnd == explorerHwnd)
            {
                return true;
            }

            IntPtr rootHwnd = _getAncestor(shellWindowHwnd, Helpers.NativeMethods.GA_ROOT);
            return rootHwnd == explorerHwnd;
        }
    }
}
