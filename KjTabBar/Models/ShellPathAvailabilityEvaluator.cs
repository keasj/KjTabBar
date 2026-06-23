using System;

namespace KjTabBar.Models
{
    internal sealed class ShellPathAvailabilityEvaluator
    {
        private readonly Func<string, string> _normalizeKnownPath;
        private readonly Func<string, bool> _directoryExists;
        private readonly Func<string, bool> _fileExists;

        public ShellPathAvailabilityEvaluator(Func<string, string> normalizeKnownPath)
            : this(normalizeKnownPath, System.IO.Directory.Exists, System.IO.File.Exists)
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
