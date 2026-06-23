using System;

namespace KjTabBar.Models
{
    internal sealed class DesktopPathClassifier
    {
        private readonly IExplorerService _explorerService;
        private readonly DesktopShellItemPathCache _desktopShellItemPathCache;

        private static readonly string[] DesktopSpecialShellPaths = new string[]
        {
            "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
            "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
            "::{645FF040-5081-101B-9F08-00AA002F954E}",
            "::{679F85CB-0220-4080-B29B-5540CC05AAB6}",
            "::{F874310E-B6B7-47DC-BC84-B9E6B38F5903}",
            "::{F87431B7-B615-448F-972C-469618B6A34D}"
        };

        public DesktopPathClassifier(IExplorerService explorerService)
        {
            _explorerService = explorerService;
            _desktopShellItemPathCache = new DesktopShellItemPathCache(explorerService);
        }

        public bool IsDesktopFolderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string userDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            if (ExplorerAbsorptionLogic.IsSameOrChildPath(path, userDesktopPath))
            {
                return true;
            }
            if (ExplorerAbsorptionLogic.IsSameOrChildPath(path, commonDesktopPath))
            {
                return true;
            }

            return false;
        }

        public bool IsDesktopShortcutTargetPath(string path)
        {
            return ExplorerAbsorptionLogic.IsDesktopShortcutTargetPath(_explorerService, path);
        }

        public bool IsDesktopShellItemPath(string path)
        {
            return _desktopShellItemPathCache.Contains(path);
        }

        public bool IsDesktopSpecialShellPath(string path)
        {
            string normalizedPath = _explorerService.NormalizeShellNamespacePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            for (int i = 0; i < DesktopSpecialShellPaths.Length; i++)
            {
                if (normalizedPath.Equals(DesktopSpecialShellPaths[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}