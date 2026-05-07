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

                // [DEB_BUG_FIX] 縲後・繝ｭ繧ｰ繝ｩ繝縺ｨ讖溯・縲咲ｭ峨・繧ｳ繝ｳ繝医Ο繝ｼ繝ｫ繝代ロ繝ｫ髢｢騾｣縺ｮ迚ｹ谿翫す繝ｧ繝ｼ繝医き繝・ヨ縺ｯ縲・
                // 繝励Ο繧ｻ繧ｹ襍ｷ蜍輔ｄCOM隗｣豎ｺ縺ｫ譎る俣縺後°縺九ｋ縺溘ａ縲！sDesktopCandidate(蜑肴勹繧ｦ繧｣繝ｳ繝峨え蛻､螳・縺・
                // 譎る俣蛻・ｌ縺ｧ False 縺ｫ縺ｪ縺｣縺ｦ縺励∪縺・こ繝ｼ繧ｹ縺悟､壹＞縲・
                // 遒ｺ螳溘↓繧ｿ繝門喧・亥精蜿主虚菴懊∈縺ｮ繝・げ繝ｬ髦ｲ豁｢・峨☆繧九◆繧√！sDesktopCandidate 縺ｫ髢｢菫ゅ↑縺・
                // 縲檎黄逅・噪縺ｪ繧ｷ繝ｧ繝ｼ繝医き繝・ヨ縺ｨ縺励※繝・せ繧ｯ繝医ャ繝励↓蟄伜惠縺吶ｋ縺九咲ｭ峨ｒ萓句､也噪縺ｫ辟｡譚｡莉ｶ縺ｧ隧穂ｾ｡縺吶ｋ縲・
                //
                // 窶ｻ豕ｨ諢・ 蟇ｾ雎｡繝代せ縺ｫ縺ｯ譛ｫ蟆ｾ縺ｫ \0 縺ｪ縺ｩ縺ｮ繝薙Η繝ｼ繧ｹ繝・・繝医′莉倡捩縺励※縺・ｋ縺薙→縺後≠繧翫・
                // ExplorerManager.NormalizeBasicShellNamespacePath 蛛ｴ縺ｧ髯､蜴ｻ縺励※豁｣隕丞喧縺吶ｋ繧医≧蟇ｾ蠢懈ｸ医∩縲・
                // 縺ｾ縺溘ゝitleVirtualPath縺九ｉ謗ｨ貂ｬ縺輔ｌ縺滓ｱ守畑繝代せ・・:{21EC...}・峨〒
                // 譛ｬ譚･縺ｮ CurrentPath・・:{26EE...}・峨ｒ荳頑嶌縺阪＠縺ｦ縺励∪繧上↑縺・ｈ縺・ｸ贋ｽ阪〒髦ｲ蠕｡貂医∩縲・
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

                // 縲栗sDesktopCandidate 縺・False 縺ｧ繧ょ精蜿弱☆繧九咲音萓九・縲・
                // 繝・せ繧ｯ繝医ャ繝苓ｵｷ轤ｹ蛟呵｣懊→縺励※譌｢縺ｫ髱櫁｡ｨ遉ｺ蛹匁ｸ医∩縺ｮ繧ｦ繧｣繝ｳ繝峨え縺ｫ髯仙ｮ壹☆繧九・
                // 縺薙ｌ繧剃ｻ倥￠縺ｪ縺・→縲∝挨縺ｮ Explorer 縺九ｉ髢九＞縺溷酔荳霍ｯ邱壹・ Control Panel 縺ｾ縺ｧ
                // 繝・せ繧ｯ繝医ャ繝嶺ｸ翫・ .lnk 縺悟ｭ伜惠縺吶ｋ縺縺代〒隱､蜷ｸ蜿弱＠縺ｦ縺励∪縺・・
                if (!context.IsDesktopCandidate && context.IsHiddenPending && isDesktopShortcutTarget)
                {
                    allowSpecialPath = true;
                    return AbsorptionAction.Absorb;
                }
            }
            else
            {
                // 騾壼ｸｸ縺ｮ繝代せ・・:\ 繧・E:\ 遲会ｼ峨・縲√お繧ｯ繧ｹ繝励Ο繝ｼ繝ｩ縺ｮ縲悟挨繧ｦ繧｣繝ｳ繝峨え縺ｧ髢九￥縲肴桃菴懊→譏守｢ｺ縺ｫ蛹ｺ蛻･縺吶ｋ縺溘ａ
                // 蠢・★ IsDesktopCandidate・育峩蜑阪∪縺ｧ繝・せ繧ｯ繝医ャ繝励′繧｢繧ｯ繝・ぅ繝悶□縺｣縺溘°縺ｨ縺・≧蛻､螳夲ｼ峨ｒ蠢・医→縺吶ｋ縲・
                // 縺薙ｌ繧貞､悶☆縺ｨ縲∵里蟄倥お繧ｯ繧ｹ繝励Ο繝ｼ繝ｩ繝ｼ縺九ｉ縲悟挨繧ｦ繧｣繝ｳ繝峨え縺ｧ髢九￥縲阪ｒ菴ｿ縺｣縺ｦ髢九°繧後◆蝣ｴ蜷医↓繧ゅ・
                // 縺溘∪縺溘∪繝・せ繧ｯ繝医ャ繝励↓縺昴・繝輔か繝ｫ繝縺ｮ繧ｷ繝ｧ繝ｼ繝医き繝・ヨ縺後≠縺｣縺溘□縺代〒蜷ｸ蜿弱＆繧後※縺励∪縺・ョ繧ｰ繝ｬ縺瑚ｵｷ縺阪ｋ縲・
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

