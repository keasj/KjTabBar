using System;

namespace KjTabBar.Models
{
    internal sealed class ShellCurrentPathResolver
    {
        private readonly Func<string, string> _mapLocationNameToKnownShellPath;
        private readonly Func<string, bool> _isControlPanelRootPath;
        private readonly Func<string, string> _normalizeShellPath;
        private readonly Func<string, bool> _isNavigablePath;

        public ShellCurrentPathResolver(
            Func<string, string> mapLocationNameToKnownShellPath,
            Func<string, bool> isControlPanelRootPath,
            Func<string, string> normalizeShellPath,
            Func<string, bool> isNavigablePath)
        {
            _mapLocationNameToKnownShellPath = mapLocationNameToKnownShellPath;
            _isControlPanelRootPath = isControlPanelRootPath;
            _normalizeShellPath = normalizeShellPath;
            _isNavigablePath = isNavigablePath;
        }

        public string Resolve(string locationUrl, string locationName, string folderPath)
        {
            string mappedControlPanelPath = _mapLocationNameToKnownShellPath(locationName);
            if (!string.IsNullOrEmpty(mappedControlPanelPath) && _isControlPanelRootPath(mappedControlPanelPath))
            {
                return mappedControlPanelPath;
            }

            string normalizedFolderPath = _normalizeShellPath(folderPath);
            if (!string.IsNullOrEmpty(normalizedFolderPath))
            {
                return normalizedFolderPath;
            }

            if (!string.IsNullOrEmpty(folderPath) && _isNavigablePath(folderPath))
            {
                return folderPath;
            }

            if (!string.IsNullOrEmpty(locationUrl))
            {
                Uri uri;
                if (Uri.TryCreate(locationUrl, UriKind.Absolute, out uri))
                {
                    string localPath = uri.LocalPath;
                    if (_isNavigablePath(localPath))
                    {
                        return localPath;
                    }
                }

                string normalizedLocationPath = _normalizeShellPath(locationUrl);
                if (!string.IsNullOrEmpty(normalizedLocationPath))
                {
                    return normalizedLocationPath;
                }
            }

            string mappedVirtualPath = _mapLocationNameToKnownShellPath(locationName);
            if (!string.IsNullOrEmpty(mappedVirtualPath))
            {
                return mappedVirtualPath;
            }

            if (!string.IsNullOrEmpty(folderPath))
            {
                return folderPath;
            }

            return null;
        }
    }
}
