using System;

namespace KjTabBar.Models
{
    internal sealed class ShellPathAvailabilityEvaluator
    {
        private readonly Func<string, string> _normalizeKnownPath;
        private readonly Func<string, bool> _directoryExists;
        private readonly Func<string, bool> _fileExists;

        public ShellPathAvailabilityEvaluator(Func<string, string> normalizeKnownPath)
            : this(normalizeKnownPath, IsDirectoryAvailable, System.IO.File.Exists)
        {
        }

        internal ShellPathAvailabilityEvaluator(
            Func<string, string> normalizeKnownPath,
            Func<string, bool> directoryExists,
            Func<string, bool> fileExists)
        {
            _normalizeKnownPath = normalizeKnownPath;
            _directoryExists = directoryExists;
            _fileExists = fileExists;
        }

        private static bool IsDirectoryAvailable(string path)
        {
            return IsDirectoryAvailable(path, Helpers.NativeMethods.GetDriveType, System.IO.File.GetAttributes);
        }

        internal static bool IsDirectoryAvailable(string path, Func<string, uint> getDriveType,
            Func<string, System.IO.FileAttributes> getAttributes)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string root = System.IO.Path.GetPathRoot(path);
                // Only a fixed local drive can provide evidence for automatic removal.
                if (path.StartsWith(@"\\", StringComparison.Ordinal) || string.IsNullOrEmpty(root) ||
                    getDriveType(root) != 3) return true;
                try
                {
                    return (getAttributes(path) & System.IO.FileAttributes.Directory) != 0;
                }
                catch (System.IO.FileNotFoundException) { }
                catch (System.IO.DirectoryNotFoundException) { }
                string parent = System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(parent)) return true;
                // Confirm the containing directory is readable before treating a child as deleted.
                return (getAttributes(parent) & System.IO.FileAttributes.Directory) == 0;
            }
            catch (System.IO.IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
            catch (System.Security.SecurityException) { return true; }
            catch (ArgumentException) { return true; }
        }

        public bool IsNavigablePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                return _directoryExists(path) || _fileExists(path);
            }

            if (path.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
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

            string normalizedPath = _normalizeKnownPath != null ? _normalizeKnownPath(path) : null;
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
            if (normalizedPath.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _directoryExists(normalizedPath);
        }
    }
}
