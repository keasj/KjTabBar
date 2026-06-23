using System;
using System.IO;
using KjTabBar.Models;

namespace KjTabBar.ViewModels
{
    internal static class TabRestorationHelper
    {
        public static bool IsPersistedTabPathRestorable(string path, IExplorerService explorerService, Func<string, string> normalizeTabPath)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalizedPath = normalizeTabPath(path);
            if (string.IsNullOrEmpty(normalizedPath)) return false;
            if (normalizedPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
            if (explorerService.IsControlPanelPath(normalizedPath)) return true;
            if (IsPotentialFileSystemTabPath(normalizedPath)) return true;
            return false;
        }

        public static bool IsPotentialFileSystemTabPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase)) return path.Length > 2;
            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) return true;
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }
}
