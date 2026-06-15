using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;
using System;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerAbsorptionDecisionMakerTests
    {
        private MockExplorerService _explorerService;

        [TestInitialize]
        public void Setup()
        {
            _explorerService = new MockExplorerService();
            _explorerService.AllControlPanelPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
            _explorerService.IsControlPanelRootPathFunc = delegate (string path)
            {
                return string.Equals(path, _explorerService.AllControlPanelPath, StringComparison.OrdinalIgnoreCase);
            };
        }

        [TestMethod]
        public void Evaluate_WaitAndRetry_For_UnresolvedControlPanel()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = null,
                CurrentRetryCount = 2,
                HasValidTarget = true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.WaitAndRetryIncrement, action);
        }

        [TestMethod]
        public void Evaluate_WaitAndRetry_For_UnresolvedControlPanelRoot_When_AllControlPanelPath_Uses_21EC()
        {
            _explorerService.AllControlPanelPath = "::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            _explorerService.IsControlPanelRootPathFunc = delegate (string path)
            {
                return string.Equals(path, "::{21EC2020-3AEA-1069-A2DD-08002B30309D}", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(path, "::{26EE0668-A00A-44D7-9371-BEB064C98683}", StringComparison.OrdinalIgnoreCase);
            };
            _explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return _explorerService.IsControlPanelRootPath(path);
            };
            _explorerService.NormalizeShellNamespacePathFunc = delegate (string path)
            {
                return path;
            };

            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = null,
                CurrentRetryCount = 0,
                HasValidTarget = false
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.WaitAndRetryIncrement, action);
        }

        [TestMethod]
        public void Evaluate_NullPath_Reaches_MaxRetry_AbsorbWithFallback()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = null,
                TitleVirtualPath = null,
                CurrentRetryCount = ExplorerAbsorptionDecisionMaker.MaxAbsorbPathRetryCount - 1,
                HasValidTarget = true,
                IsDesktopCandidate = true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.AbsorbWithFallback, action);
            Assert.AreEqual("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_HiddenDesktopShortcut_WithoutDesktopCandidate_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = "コントロール パネル",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                IsHiddenPending = true,
                IsDesktopShortcutTargetFunc = (p) => true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_CandidateWithoutHiddenPending_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.ProgramsAndFeaturesPath,
                TitleVirtualPath = "プログラムと機能",
                HasValidTarget = true,
                IsDesktopCandidate = false,
                IsControlPanelTabLaunchCandidate = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.ProgramsAndFeaturesPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_CandidateWithHiddenPending_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.ProgramsAndFeaturesPath,
                TitleVirtualPath = "プログラムと機能",
                HasValidTarget = true,
                IsDesktopCandidate = false,
                IsHiddenPending = true,
                IsControlPanelTabLaunchCandidate = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.ProgramsAndFeaturesPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_TargetedControlPanelHostWithoutCandidate_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.PowerOptionsPath,
                TitleVirtualPath = "電源オプション",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                HasActiveControlPanelTabOnValidTarget = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.PowerOptionsPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_ActiveControlPanelTabWithoutCandidate_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.ProgramsAndFeaturesPath,
                TitleVirtualPath = "プログラムと機能",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                CurrentRetryCount = 0,
                HasActiveControlPanelTabOnValidTarget = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.ProgramsAndFeaturesPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_ActiveControlPanelTabWithoutCandidate_Absorbs_AfterRetryWindow()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.PowerOptionsPath,
                TitleVirtualPath = "電源オプション",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                CurrentRetryCount = ExplorerAbsorptionDecisionMaker.MaxTransientControlPanelRetryCount,
                HasActiveControlPanelTabOnValidTarget = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.PowerOptionsPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_TargetedHostWithoutForegroundRelatedActiveValidTarget_Returns_Ignore()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.PowerOptionsPath,
                TitleVirtualPath = "電源オプション",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                HasActiveControlPanelTabOnValidTarget = false
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Ignore, action);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_TargetedHostWithMatchedControlPanelTarget_Absorbs_WithoutForegroundRelatedValidTarget()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.PowerOptionsPath,
                TitleVirtualPath = "電源オプション",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                HasActiveControlPanelTabOnValidTarget = false,
                HasActiveControlPanelTabFunc = () => true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.PowerOptionsPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_ManagedControlPanelLaunchSource_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = _explorerService.PowerOptionsPath,
                TitleVirtualPath = "電源オプション",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                HasActiveControlPanelTabOnValidTarget = false,
                WasManagedControlPanelLaunchSource = true
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.AreEqual(_explorerService.PowerOptionsPath, resolvedPath);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_DesktopCandidate_HiddenPending_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = "Control Panel",
                HasValidTarget = true,
                HasControlPanelTarget = false,
                IsDesktopCandidate = true,
                IsHiddenPending = true,
                IsDesktopShortcutTargetFunc = (p) => false,
                IsDesktopFolderPathFunc = (p) => false,
                IsDesktopShellItemPathFunc = (p) => false
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.IsTrue(allowSpecialPath);
        }
        [TestMethod]
        public void Evaluate_ControlPanel_WithoutDesktopEvidence_Returns_Ignore()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = "コントロール パネル",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                HasEquivalentControlPanelTabFunc = (p) => true,
                HasActiveControlPanelTabFunc = () => true,
                IsDesktopShortcutTargetFunc = (p) => false,
                IsDesktopFolderPathFunc = (p) => false,
                IsDesktopShellItemPathFunc = (p) => false
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Ignore, action);
        }

        [TestMethod]
        public void Evaluate_ControlPanel_DesktopShortcut_WithoutDesktopTrace_Returns_Ignore()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}",
                TitleVirtualPath = "コントロール パネル",
                HasValidTarget = true,
                HasControlPanelTarget = true,
                IsDesktopCandidate = false,
                IsHiddenPending = false,
                IsDesktopShortcutTargetFunc = (p) => true,
                IsDesktopFolderPathFunc = (p) => false,
                IsDesktopShellItemPathFunc = (p) => false
            };

            _explorerService.IsControlPanelPathFunc = (p) => true;

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Ignore, action);
        }

        [TestMethod]
        public void Evaluate_Normal_NonDesktopCandidate_Returns_Ignore()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = @"C:\Windows",
                TitleVirtualPath = "Windows",
                HasValidTarget = true,
                IsDesktopCandidate = false
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Ignore, action);
        }

        [TestMethod]
        public void Evaluate_Normal_DesktopShortcut_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = @"C:\Program Files",
                HasValidTarget = true,
                IsDesktopCandidate = true,
                IsDesktopShortcutTargetFunc = (p) => true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.IsFalse(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_SpecialShellPath_DesktopCandidate_Returns_Absorb()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                HasValidTarget = true,
                IsDesktopCandidate = true,
                IsDesktopSpecialShellPathFunc = (p) => true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Absorb, action);
            Assert.IsTrue(allowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_SpecialShellPath_NonDesktopCandidate_Returns_Ignore()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                HasValidTarget = true,
                IsDesktopCandidate = false,
                IsDesktopSpecialShellPathFunc = (p) => true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.Ignore, action);
        }

        [TestMethod]
        public void Evaluate_NoValidTarget_Returns_CreateNewTabBar()
        {
            ExplorerWindowContext context = new ExplorerWindowContext
            {
                CurrentPath = @"C:\Users",
                HasValidTarget = false,
                IsDesktopCandidate = true
            };

            string resolvedPath;
            bool allowSpecialPath;
            AbsorptionAction action = ExplorerAbsorptionDecisionMaker.Evaluate(context, _explorerService, out resolvedPath, out allowSpecialPath);

            Assert.AreEqual(AbsorptionAction.CreateNewTabBar, action);
        }
    }
}

