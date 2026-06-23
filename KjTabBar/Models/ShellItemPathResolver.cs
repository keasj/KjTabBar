using System;
using System.IO;

namespace KjTabBar.Models
{
    internal sealed class ShellItemPathResolver
    {
        private readonly Func<string, string> _normalizeKnownPath;

        public ShellItemPathResolver(Func<string, string> normalizeKnownPath)
        {
            _normalizeKnownPath = normalizeKnownPath;
        }

        public string GetItemParseName(string itemPath)
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

        public bool AreEquivalentItemPaths(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) || string.IsNullOrEmpty(path2))
            {
                return false;
            }

            string normalizedPath1 = _normalizeKnownPath != null ? _normalizeKnownPath(path1) : path1;
            string normalizedPath2 = _normalizeKnownPath != null ? _normalizeKnownPath(path2) : path2;
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

        public bool TryNormalizeFileSystemPath(string path, out string normalizedPath)
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
    }
}
