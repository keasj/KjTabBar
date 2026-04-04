using System;

namespace KjTabBar.Models
{
    public enum AbsorptionAction
    {
        WaitAndRetryIncrement,
        AbsorbWithFallback,
        CreateNewTabBar,
        Ignore,
        Absorb
    }

    public class ExplorerWindowContext
    {
        public int CurrentRetryCount { get; set; }
        public bool IsDesktopCandidate { get; set; }
        public bool IsDesktopInteractiveCandidate { get; set; }
        public bool IsHiddenPending { get; set; }
        public bool IsControlPanelTabLaunchCandidate { get; set; }
        public bool HasValidTarget { get; set; }
        public bool HasControlPanelTarget { get; set; }

        public string CurrentPath { get; set; }
        public string TitleVirtualPath { get; set; }
        
        // Let Evaluate handle the checking to avoid computing heavy COM operations beforehand
        public Func<string, bool> IsDesktopShortcutTargetFunc { get; set; }
        public Func<string, bool> IsDesktopFolderPathFunc { get; set; }
        public Func<string, bool> IsDesktopShellItemPathFunc { get; set; }
        public Func<string, bool> IsDesktopSpecialShellPathFunc { get; set; }
        
        public Func<string, bool> HasEquivalentControlPanelTabFunc { get; set; }
        public Func<bool> HasActiveControlPanelTabFunc { get; set; }
    }

    public static class ExplorerAbsorptionDecisionMaker
    {
        public const int MaxAbsorbPathRetryCount = 16;
        public const int MaxTransientControlPanelRetryCount = 8;
        
        public static AbsorptionAction Evaluate(ExplorerWindowContext context, IExplorerService explorerService, out string resolvedPath, out bool allowSpecialPath)
        {
            resolvedPath = context.CurrentPath;
            allowSpecialPath = false;

            if (explorerService.IsControlPanelRootPath(context.TitleVirtualPath))
            {
                if (string.IsNullOrEmpty(resolvedPath) || 
                    explorerService.IsTransientShellPlaceholderPath(resolvedPath) ||
                    !explorerService.IsControlPanelPath(resolvedPath))
                {
                    resolvedPath = explorerService.AllControlPanelPath;
                }
            }
            else if (!string.IsNullOrEmpty(context.TitleVirtualPath))
            {
                if (string.IsNullOrEmpty(resolvedPath) || explorerService.IsTransientShellPlaceholderPath(resolvedPath))
                {
                    resolvedPath = context.TitleVirtualPath;
                }
            }

            string decisionPath = resolvedPath;
            string normalizedDecisionPath = explorerService.NormalizeShellNamespacePath(decisionPath);

            bool shouldRetryTransientControlPanel = string.IsNullOrEmpty(context.TitleVirtualPath) &&
                                                    !string.IsNullOrEmpty(normalizedDecisionPath) &&
                                                    normalizedDecisionPath.Equals(explorerService.AllControlPanelPath, StringComparison.OrdinalIgnoreCase);
                                                    
            bool shouldRetryTransientDesktopPlaceholder =
                ExplorerWindowDecisionLogic.ShouldRetryTransientDesktopPlaceholder(
                    context.HasValidTarget,
                    context.IsDesktopInteractiveCandidate,
                    context.TitleVirtualPath,
                    explorerService.IsTransientShellPlaceholderPath(decisionPath),
                    context.CurrentRetryCount,
                    MaxTransientControlPanelRetryCount);
                    
            if ((shouldRetryTransientControlPanel || shouldRetryTransientDesktopPlaceholder) &&
                context.CurrentRetryCount < MaxTransientControlPanelRetryCount)
            {
                return AbsorptionAction.WaitAndRetryIncrement;
            }

            if (string.IsNullOrEmpty(decisionPath))
            {
                if (context.CurrentRetryCount >= MaxAbsorbPathRetryCount - 1)
                {
                    if (context.HasValidTarget && (context.IsDesktopCandidate || context.IsHiddenPending))
                    {
                        resolvedPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
                        allowSpecialPath = true;
                        return AbsorptionAction.AbsorbWithFallback;
                    }
                    else if (!context.HasValidTarget)
                    {
                        return AbsorptionAction.CreateNewTabBar;
                    }
                    else
                    {
                        return AbsorptionAction.Ignore;
                    }
                }
                else
                {
                    return AbsorptionAction.WaitAndRetryIncrement;
                }
            }

            bool isControlPanelPath = explorerService.IsControlPanelPath(decisionPath);

            bool effectiveHasTarget = context.HasValidTarget;
            if (isControlPanelPath && context.HasControlPanelTarget)
            {
                effectiveHasTarget = true;
            }

            if (!effectiveHasTarget)
            {
                return AbsorptionAction.CreateNewTabBar;
            }

            bool isSpecialShellPath = false;
            
            if (context.IsDesktopSpecialShellPathFunc != null && context.IsDesktopSpecialShellPathFunc(decisionPath))
            {
                isSpecialShellPath = true;
            }
            else if (!string.IsNullOrEmpty(context.TitleVirtualPath) && context.IsDesktopSpecialShellPathFunc != null)
            {
                isSpecialShellPath = context.IsDesktopSpecialShellPathFunc(context.TitleVirtualPath);
            }

            bool isDesktopShortcutTarget = context.IsDesktopShortcutTargetFunc != null && context.IsDesktopShortcutTargetFunc(decisionPath);
            bool isDesktopFolderPath = context.IsDesktopFolderPathFunc != null && context.IsDesktopFolderPathFunc(decisionPath);
            bool isDesktopShellItemPath = context.IsDesktopShellItemPathFunc != null && context.IsDesktopShellItemPathFunc(decisionPath);
            if (!isDesktopShellItemPath && !string.IsNullOrEmpty(context.TitleVirtualPath) && context.IsDesktopShellItemPathFunc != null)
            {
                isDesktopShellItemPath = context.IsDesktopShellItemPathFunc(context.TitleVirtualPath);
            }

            if (isControlPanelPath)
            {
                if (context.IsControlPanelTabLaunchCandidate)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }

                // [DEB_BUG_FIX] 「プログラムと機能」等のコントロールパネル関連の特殊ショートカットは、
                // プロセス起動やCOM解決に時間がかかるため、IsDesktopCandidate(前景ウィンドウ判定)が
                // 時間切れで False になってしまうケースが多い。
                // 確実にタブ化（吸収動作へのデグレ防止）するため、IsDesktopCandidate に関係なく
                // 「物理的なショートカットとしてデスクトップに存在するか」等を例外的に無条件で評価する。
                //
                // ※注意: 対象パスには末尾に \0 などのビューステートが付着していることがあり、
                // ExplorerManager.NormalizeBasicShellNamespacePath 側で除去して正規化するよう対応済み。
                // また、TitleVirtualPathから推測された汎用パス（::{21EC...}）で
                // 本来の CurrentPath（::{26EE...}）を上書きしてしまわないよう上位で防御済み。
                if (context.IsDesktopCandidate &&
                    ExplorerAbsorptionLogic.ShouldAbsorbDesktopOriginPath(
                        isDesktopFolderPath,
                        isDesktopShortcutTarget,
                        isDesktopShellItemPath,
                        false))
                {
                    allowSpecialPath = true; 
                    return AbsorptionAction.Absorb;
                }

                // 「IsDesktopCandidate が False でも吸収する」特例は、
                // デスクトップ起点候補として既に非表示化済みのウィンドウに限定する。
                // これを付けないと、別の Explorer から開いた同一路線の Control Panel まで
                // デスクトップ上の .lnk が存在するだけで誤吸収してしまう。
                if (!context.IsDesktopCandidate && context.IsHiddenPending && isDesktopShortcutTarget)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }
            }
            else
            {
                // 通常のパス（C:\ や E:\ 等）は、エクスプローラの「別ウィンドウで開く」操作と明確に区別するため
                // 必ず IsDesktopCandidate（直前までデスクトップがアクティブだったかという判定）を必須とする。
                // これを外すと、既存エクスプローラーから「別ウィンドウで開く」を使って開かれた場合にも、
                // たまたまデスクトップにそのフォルダのショートカットがあっただけで吸収されてしまうデグレが起きる。
                if (context.IsDesktopCandidate)
                {
                    if (ExplorerAbsorptionLogic.ShouldAbsorbDesktopOriginPath(
                        isDesktopFolderPath,
                        isDesktopShortcutTarget,
                        isDesktopShellItemPath,
                        isSpecialShellPath))
                    {
                        allowSpecialPath = isSpecialShellPath;
                        return AbsorptionAction.Absorb;
                    }
                }
            }

            return AbsorptionAction.Ignore;
        }
    }
}
