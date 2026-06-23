using System;
using KjTabBar.Helpers;

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
        public bool WasManagedControlPanelLaunchSource { get; set; }
        public bool HasActiveControlPanelTabOnValidTarget { get; set; }
        public bool HasValidTarget { get; set; }
    }

    internal sealed class ExplorerWindowEvaluationResult
    {
        public AbsorptionAction Action { get; set; }
        public string ResolvedPath { get; set; }
        public bool AllowSpecialPath { get; set; }
        public bool IsControlPanelPath { get; set; }
        public bool UseResolvedPathOnCreate { get; set; }
        public bool WasManagedControlPanelLaunchSource { get; set; }
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
            Func<string, bool> hasActiveControlPanelTab)
        {
            string path = _explorerService.GetCurrentPath(input.ExplorerHwnd);
            string titlePath = getTitleVirtualPath != null ? getTitleVirtualPath(input.ExplorerHwnd) : null;

            bool titleIndicatesControlPanel =
                !string.IsNullOrEmpty(titlePath) &&
                (_explorerService.IsControlPanelRootPath(titlePath) || _explorerService.IsControlPanelPath(titlePath));

            bool titleIndicatesControlPanelItem =
                !string.IsNullOrEmpty(titlePath) &&
                _explorerService.IsControlPanelPath(titlePath) &&
                !_explorerService.IsControlPanelRootPath(titlePath);

            if (titleIndicatesControlPanelItem &&
                (string.IsNullOrEmpty(path) ||
                 _explorerService.IsTransientShellPlaceholderPath(path) ||
                 !_explorerService.IsControlPanelPath(path) ||
                 _explorerService.IsControlPanelRootPath(path)))
            {
                path = titlePath;
            }

            bool isControlPanelPath = _explorerService.IsControlPanelPath(path) || titleIndicatesControlPanel;

            string controlPanelSearchPath = null;
            bool hasControlPanelTargetLocal = false;
            if (isControlPanelPath && hasControlPanelTarget != null)
            {
                string searchPath = path;
                if (_explorerService.IsControlPanelRootPath(titlePath))
                {
                    searchPath = _explorerService.AllControlPanelPath;
                }
                else if ((string.IsNullOrEmpty(searchPath) || !_explorerService.IsControlPanelPath(searchPath)) &&
                         _explorerService.IsControlPanelPath(titlePath))
                {
                    searchPath = titlePath;
                }

                controlPanelSearchPath = searchPath;
                hasControlPanelTargetLocal = hasControlPanelTarget(searchPath);
                if (!hasControlPanelTargetLocal &&
                    input.WasManagedControlPanelLaunchSource &&
                    input.HasValidTarget &&
                    !_explorerService.IsControlPanelRootPath(searchPath))
                {
                    hasControlPanelTargetLocal = true;
                }

                if (!hasControlPanelTargetLocal &&
                    input.HasActiveControlPanelTabOnValidTarget &&
                    !_explorerService.IsControlPanelRootPath(searchPath))
                {
                    hasControlPanelTargetLocal = true;
                }
            }

            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentRetryCount = input.RetryCount,
                IsDesktopCandidate = input.IsDesktopCandidate,
                IsDesktopInteractiveCandidate = input.IsDesktopInteractiveCandidate,
                IsHiddenPending = input.IsHiddenPending,
                IsControlPanelTabLaunchCandidate = input.IsControlPanelTabLaunchCandidate,
                WasManagedControlPanelLaunchSource = input.WasManagedControlPanelLaunchSource,
                HasActiveControlPanelTabOnValidTarget = input.HasActiveControlPanelTabOnValidTarget,
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
                    if (hasActiveControlPanelTab != null && hasActiveControlPanelTab(controlPanelSearchPath))
                    {
                        return true;
                    }

                    return input.HasActiveControlPanelTabOnValidTarget;
                }
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            bool resolvedIsControlPanelPath =
                _explorerService.IsControlPanelPath(resolvedPath) ||
                (!string.IsNullOrEmpty(titlePath) &&
                 (_explorerService.IsControlPanelRootPath(titlePath) || _explorerService.IsControlPanelPath(titlePath)));
            bool shouldUseResolvedPathOnCreate =
                resolvedIsControlPanelPath ||
                (input.IsDesktopCandidate &&
                 ExplorerAbsorptionLogic.ShouldAbsorbDesktopOriginPath(
                     context.IsDesktopFolderPathFunc != null && context.IsDesktopFolderPathFunc(resolvedPath),
                     context.IsDesktopShortcutTargetFunc != null && context.IsDesktopShortcutTargetFunc(resolvedPath),
                     context.IsDesktopShellItemPathFunc != null &&
                         (context.IsDesktopShellItemPathFunc(resolvedPath) ||
                          (!string.IsNullOrEmpty(titlePath) && context.IsDesktopShellItemPathFunc(titlePath))),
                     context.IsDesktopSpecialShellPathFunc != null &&
                         (context.IsDesktopSpecialShellPathFunc(resolvedPath) ||
                          (!string.IsNullOrEmpty(titlePath) && context.IsDesktopSpecialShellPathFunc(titlePath)))));

            if (resolvedIsControlPanelPath)
            {
                AppLogger.LogInfo(
                    "ExplorerWindowEvaluationService",
                    string.Format(
                        "CP evaluate hwnd={0} action={1} path={2} titlePath={3} searchPath={4} hasValidTarget={5} hasControlPanelTarget={6} hasActiveControlPanelTabOnValidTarget={7} isControlPanelTabLaunchCandidate={8} wasManagedControlPanelLaunchSource={9} allowSpecialPath={10}",
                        input.ExplorerHwnd,
                        action,
                        path ?? string.Empty,
                        titlePath ?? string.Empty,
                        controlPanelSearchPath ?? string.Empty,
                        input.HasValidTarget,
                        hasControlPanelTargetLocal,
                        input.HasActiveControlPanelTabOnValidTarget,
                        input.IsControlPanelTabLaunchCandidate,
                        input.WasManagedControlPanelLaunchSource,
                        allowSpecialPath));
            }

            return new ExplorerWindowEvaluationResult
            {
                Action = action,
                ResolvedPath = resolvedPath,
                AllowSpecialPath = allowSpecialPath,
                IsControlPanelPath = resolvedIsControlPanelPath,
                UseResolvedPathOnCreate =
                    action == AbsorptionAction.CreateNewTabBar &&
                    shouldUseResolvedPathOnCreate,
                WasManagedControlPanelLaunchSource = input.WasManagedControlPanelLaunchSource
            };
        }
    }
}
