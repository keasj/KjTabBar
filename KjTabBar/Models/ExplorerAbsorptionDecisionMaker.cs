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
        public bool HasActiveControlPanelTabOnValidTarget { get; set; }
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
                                                    explorerService.IsControlPanelRootPath(normalizedDecisionPath);
                                                    
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

            bool isDesktopShortcutTarget = false;
            bool isDesktopFolderPath = false;
            bool isDesktopShellItemPath = false;

            if (context.IsDesktopShortcutTargetFunc != null && context.IsDesktopShortcutTargetFunc(decisionPath))
            {
                isDesktopShortcutTarget = true;
            }

            if (!isDesktopShortcutTarget && context.IsDesktopFolderPathFunc != null && context.IsDesktopFolderPathFunc(decisionPath))
            {
                isDesktopFolderPath = true;
            }

            if (!isDesktopShortcutTarget && !isDesktopFolderPath && context.IsDesktopShellItemPathFunc != null)
            {
                if (context.IsDesktopShellItemPathFunc(decisionPath))
                {
                    isDesktopShellItemPath = true;
                }
                else if (!string.IsNullOrEmpty(context.TitleVirtualPath))
                {
                    isDesktopShellItemPath = context.IsDesktopShellItemPathFunc(context.TitleVirtualPath);
                }
            }

            if (isControlPanelPath)
            {
                if (!context.IsControlPanelTabLaunchCandidate &&
                    context.HasActiveControlPanelTabOnValidTarget &&
                    context.HasControlPanelTarget &&
                    (context.HasEquivalentControlPanelTabFunc == null || !context.HasEquivalentControlPanelTabFunc(decisionPath)))
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }

                if (context.IsControlPanelTabLaunchCandidate)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }

                // Control Panel launch can lag in COM/path resolution.
                // If this window is already desktop-origin and hidden-pending,
                // absorb defensively to avoid false Ignore decisions.
                if (context.IsDesktopCandidate && context.IsHiddenPending)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }

                // [DEB_BUG_FIX] 「プログラムと機能」など Control Panel 関連の特殊ショートカットは、
                // プロセス起動や COM 解決に時間がかかり、IsDesktopCandidate(前景ウィンドウ判定) が
                // タイミング次第で false になるケースがある。
                // 誤って Ignore 判定にならないよう、IsDesktopCandidate に依存せず、
                // 「デスクトップ上のショートカット/フォルダとして解釈できるか」を補助判定に使う。
                //
                // ※注意: 対象パスに末尾 \0 などのノイズが付くことがあるため、
                // ExplorerManager.NormalizeBasicShellNamespacePath 側で除去・正規化している。
                // また、TitleVirtualPath 由来の汎用パス ({21EC...}) で
                // 本来の CurrentPath ({26EE...}) を上書きしないよう防御している。
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

                // 「IsDesktopCandidate が false でも吸収する」特例は、
                // 既に hidden-pending のウィンドウに限定する。
                // これを絞らないと、別 Explorer から開いた Control Panel まで
                // デスクトップ上の .lnk の存在だけで誤吸収する可能性がある。
                if (!context.IsDesktopCandidate && context.IsHiddenPending && isDesktopShortcutTarget)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }
            }
            else
            {
                // 通常パス (C:\, E:\ など) は、エクスプローラーの「別ウィンドウで開く」と
                // 区別するため IsDesktopCandidate を必須にする。
                // これを外すと、既存エクスプローラーから開いた場合でも、
                // デスクトップ上の同名ショートカットがあるだけで誤吸収するデグレが起きる。
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

