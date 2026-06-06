using System;

namespace KjTabBar.Models
{
    internal sealed class ExplorerWindowEvaluationInput
    {
        public IntPtr ExplorerHwnd { get; set; }
        public int RetryCount { get; set; }
        public bool IsDesktopCandidate { get; set; }
        public bool IsDesktopInteractiveCandidate { get; set; }
        public bool IsHiddenPending { get; set; }
        public bool IsControlPanelTabLaunchCandidate { get; set; }
        public bool HasValidTarget { get; set; }
    }

    internal sealed class ExplorerWindowEvaluationResult
    {
        public AbsorptionAction Action { get; set; }
        public string ResolvedPath { get; set; }
        public bool AllowSpecialPath { get; set; }
        public bool IsControlPanelPath { get; set; }
    }

    internal sealed class ExplorerWindowEvaluationService
    {
        private readonly IExplorerService _explorerService;
        private readonly DesktopPathClassifier _desktopPathClassifier;

        public ExplorerWindowEvaluationService(IExplorerService explorerService, DesktopPathClassifier desktopPathClassifier)
        {
            _explorerService = explorerService;
            _desktopPathClassifier = desktopPathClassifier;
        }

        public ExplorerWindowEvaluationResult Evaluate(
            ExplorerWindowEvaluationInput input,
            Func<IntPtr, string> getTitleVirtualPath,
            Func<string, bool> hasControlPanelTarget,
            Func<string, bool> hasEquivalentControlPanelTab,
            Func<bool> hasActiveControlPanelTab)
        {
            string path = _explorerService.GetCurrentPath(input.ExplorerHwnd);
            string titlePath = getTitleVirtualPath != null ? getTitleVirtualPath(input.ExplorerHwnd) : null;

            bool isControlPanelPath = _explorerService.IsControlPanelPath(path) ||
                                      (!string.IsNullOrEmpty(titlePath) && _explorerService.IsControlPanelRootPath(titlePath));

            bool hasControlPanelTargetLocal = false;
            if (isControlPanelPath && hasControlPanelTarget != null)
            {
                string searchPath = _explorerService.IsControlPanelRootPath(titlePath) ? _explorerService.AllControlPanelPath : path;
                hasControlPanelTargetLocal = hasControlPanelTarget(searchPath);
            }

            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentRetryCount = input.RetryCount,
                IsDesktopCandidate = input.IsDesktopCandidate,
                IsDesktopInteractiveCandidate = input.IsDesktopInteractiveCandidate,
                IsHiddenPending = input.IsHiddenPending,
                IsControlPanelTabLaunchCandidate = input.IsControlPanelTabLaunchCandidate,
                HasValidTarget = input.HasValidTarget,
                HasControlPanelTarget = hasControlPanelTargetLocal,

                CurrentPath = path,
                TitleVirtualPath = titlePath,

                IsDesktopShortcutTargetFunc = delegate (string p) { return _desktopPathClassifier.IsDesktopShortcutTargetPath(p); },
                IsDesktopFolderPathFunc = delegate (string p) { return _desktopPathClassifier.IsDesktopFolderPath(p); },
                IsDesktopShellItemPathFunc = delegate (string p) { return _desktopPathClassifier.IsDesktopShellItemPath(p); },
                IsDesktopSpecialShellPathFunc = delegate (string p) { return _desktopPathClassifier.IsDesktopSpecialShellPath(p); },

                HasEquivalentControlPanelTabFunc = delegate (string p)
                {
                    return hasEquivalentControlPanelTab != null && hasEquivalentControlPanelTab(p);
                },
                HasActiveControlPanelTabFunc = delegate
                {
                    return hasActiveControlPanelTab != null && hasActiveControlPanelTab();
                }
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            return new ExplorerWindowEvaluationResult
            {
                Action = action,
                ResolvedPath = resolvedPath,
                AllowSpecialPath = allowSpecialPath,
                IsControlPanelPath = isControlPanelPath
            };
        }
    }
}
