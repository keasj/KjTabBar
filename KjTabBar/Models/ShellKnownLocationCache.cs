using System;

namespace KjTabBar.Models
{
    internal sealed class ShellKnownLocationCache
    {
        private readonly Func<string, string, string> _resolveDisplayTitle;
        private readonly Func<string, bool> _isShellPathAvailable;
        private readonly Func<string> _getUserProfilePath;

        private string _localizedControlPanelTitle;
        private string _localizedNetworkTitle;
        private string _localizedRecycleBinTitle;
        private string _localizedThisPCTitle;
        private string _localizedHomeTitle;
        private string _resolvedHomeFolderPath;

        public ShellKnownLocationCache(
            Func<string, string, string> resolveDisplayTitle,
            Func<string, bool> isShellPathAvailable,
            Func<string> getUserProfilePath)
        {
            _resolveDisplayTitle = resolveDisplayTitle;
            _isShellPathAvailable = isShellPathAvailable;
            _getUserProfilePath = getUserProfilePath;
        }

        public string GetLocalizedControlPanelTitle(string controlPanelPath)
        {
            return GetOrCacheLocalizedTitle(ref _localizedControlPanelTitle, controlPanelPath, "Control Panel");
        }

        public string GetLocalizedNetworkTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedNetworkTitle, "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "Network");
        }

        public string GetLocalizedRecycleBinTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedRecycleBinTitle, "::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin");
        }

        public string GetLocalizedThisPCTitle()
        {
            return GetOrCacheLocalizedTitle(ref _localizedThisPCTitle, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "This PC");
        }

        public string GetLocalizedHomeTitle(string homeFolderPath)
        {
            return GetOrCacheLocalizedTitle(ref _localizedHomeTitle, homeFolderPath, "Home");
        }

        public string GetResolvedHomeFolderPath(string homeFolderPath)
        {
            if (_resolvedHomeFolderPath == null)
            {
                if (_isShellPathAvailable != null && _isShellPathAvailable(homeFolderPath))
                {
                    _resolvedHomeFolderPath = homeFolderPath;
                }
                else
                {
                    _resolvedHomeFolderPath = _getUserProfilePath != null
                        ? _getUserProfilePath()
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
            }

            return _resolvedHomeFolderPath;
        }

        private string GetOrCacheLocalizedTitle(ref string cache, string shellPath, string fallback)
        {
            if (cache == null)
            {
                if (_resolveDisplayTitle != null)
                {
                    cache = _resolveDisplayTitle(shellPath, fallback);
                }

                if (string.IsNullOrEmpty(cache))
                {
                    cache = fallback;
                }
            }

            return cache;
        }
    }
}
