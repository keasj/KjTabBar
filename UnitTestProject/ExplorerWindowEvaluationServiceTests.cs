using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowEvaluationServiceTests
    {
        [TestMethod]
        public void Evaluate_ReuseIsLimitedToDesktopLaunches()
        {
            MockExplorerService explorer = new MockExplorerService();
            string pc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            explorer.GetCurrentPathFunc = h => pc;
            ExplorerWindowEvaluationService service = new ExplorerWindowEvaluationService(explorer, new DesktopPathClassifier(explorer));
            ExplorerWindowEvaluationInput input = new ExplorerWindowEvaluationInput { ExplorerHwnd = (IntPtr)1, HasValidTarget = true };
            Assert.IsFalse(service.Evaluate(input, null, null, null, null).ReuseExistingTab);
            input.IsHiddenPending = true;
            Assert.IsFalse(service.Evaluate(input, null, null, null, null).ReuseExistingTab, "Hidden alone is not desktop launch evidence.");
            input.IsDesktopCandidate = true;
            ExplorerWindowEvaluationResult result = service.Evaluate(input, null, null, null, null);
            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.IsTrue(result.ReuseExistingTab);
            input.WasManagedControlPanelLaunchSource = true;
            Assert.IsFalse(service.Evaluate(input, null, null, null, null).ReuseExistingTab, "Internal host operations must not reuse tabs.");
            input.WasManagedControlPanelLaunchSource = false;
            input.HasValidTarget = false;
            Assert.IsTrue(service.Evaluate(input, null, null, null, null).ReuseExistingTab, "Desktop startup must reuse restored tabs.");
            input.IsDesktopCandidate = false;
            Assert.IsFalse(service.Evaluate(input, null, null, null, null).ReuseExistingTab);
        }

        [TestMethod]
        public void Evaluate_DesktopControlPanelLaunchReuses_ButInternalTransitionDoesNot()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = h => explorer.PowerOptionsPath;
            explorer.IsControlPanelPathFunc = p => p == explorer.PowerOptionsPath;
            ExplorerWindowEvaluationService service = new ExplorerWindowEvaluationService(explorer, new DesktopPathClassifier(explorer));
            ExplorerWindowEvaluationInput input = new ExplorerWindowEvaluationInput
            {
                ExplorerHwnd = (IntPtr)1, HasValidTarget = true, IsControlPanelTabLaunchCandidate = true
            };
            ExplorerWindowEvaluationResult result = service.Evaluate(input, null, p => true, p => true, p => true);
            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.IsTrue(result.ReuseExistingTab);
            input.WasManagedControlPanelLaunchSource = true;
            Assert.IsFalse(service.Evaluate(input, null, p => true, p => true, p => true).ReuseExistingTab);
        }
        [TestMethod]
        public void Evaluate_ControlPanelRootTitle_UsesAllControlPanelPathForTargetLookup()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "Control Panel"; };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            string requestedSearchPath = null;
            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)1,
                    HasValidTarget = true,
                    IsHiddenPending = true,
                    IsControlPanelTabLaunchCandidate = true
                },
                delegate (IntPtr hwnd) { return "Control Panel"; },
                delegate (string path)
                {
                    requestedSearchPath = path;
                    return true;
                },
                delegate (string path) { return true; },
                delegate (string path) { return true; });

            Assert.AreEqual(explorerService.AllControlPanelPath, requestedSearchPath);
            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.AllowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanelItemTitle_UsesItemPathForTargetLookup()
        {
            MockExplorerService explorerService = new NullCurrentPathExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            string requestedSearchPath = null;
            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)10,
                    HasValidTarget = true,
                    IsHiddenPending = true,
                    IsControlPanelTabLaunchCandidate = true
                },
                delegate (IntPtr hwnd) { return explorerService.PowerOptionsPath; },
                delegate (string path)
                {
                    requestedSearchPath = path;
                    return path == explorerService.PowerOptionsPath;
                },
                delegate (string path) { return true; },
                delegate (string path) { return true; });

            Assert.AreEqual(explorerService.PowerOptionsPath, requestedSearchPath);
            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.AreEqual(explorerService.PowerOptionsPath, result.ResolvedPath);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.AllowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanelItemTitle_PrefersItemPath_WhenCurrentPathIsControlPanelRoot()
        {
            MockExplorerService explorerService = new ControlPanelRootCurrentPathExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };
            explorerService.IsControlPanelRootPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath;
            };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)13,
                    HasValidTarget = true,
                    IsControlPanelTabLaunchCandidate = true
                },
                delegate (IntPtr hwnd) { return explorerService.PowerOptionsPath; },
                delegate (string path) { return path == explorerService.PowerOptionsPath; },
                delegate (string path) { return false; },
                delegate (string path) { return true; });

            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.AreEqual(explorerService.PowerOptionsPath, result.ResolvedPath);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.AllowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_DesktopCandidateWithoutTarget_CreatesNewTabBar()
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)2,
                    HasValidTarget = false,
                    IsDesktopCandidate = true
                },
                delegate (IntPtr hwnd) { return null; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            Assert.AreEqual(AbsorptionAction.CreateNewTabBar, result.Action);
            Assert.AreEqual(@"C:\MockPath", result.ResolvedPath);
            Assert.IsFalse(result.AllowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_DesktopFolderLaunchWithoutTarget_PrefersResolvedPathOnCreate()
        {
            MockExplorerService explorerService = new DesktopFolderExplorerService();
            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)3,
                    HasValidTarget = false,
                    IsDesktopCandidate = true
                },
                delegate (IntPtr hwnd) { return null; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            Assert.AreEqual(AbsorptionAction.CreateNewTabBar, result.Action);
            Assert.IsTrue(result.UseResolvedPathOnCreate);
        }

        [TestMethod]
        public void Evaluate_ControlPanelItemWithoutTarget_PrefersResolvedPathOnCreate()
        {
            MockExplorerService explorerService = new NullCurrentPathExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)11,
                    HasValidTarget = false
                },
                delegate (IntPtr hwnd) { return explorerService.PowerOptionsPath; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            Assert.AreEqual(AbsorptionAction.CreateNewTabBar, result.Action);
            Assert.AreEqual(explorerService.PowerOptionsPath, result.ResolvedPath);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.UseResolvedPathOnCreate);
        }

        [TestMethod]
        public void Evaluate_ControlPanelItem_UsesActiveValidTargetFallback_WhenSpecificTargetLookupFails()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)12,
                    HasValidTarget = true,
                    HasActiveControlPanelTabOnValidTarget = true
                },
                delegate (IntPtr hwnd) { return explorerService.PowerOptionsPath; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.AreEqual(explorerService.PowerOptionsPath, result.ResolvedPath);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.AllowSpecialPath);
        }

        [TestMethod]
        public void Evaluate_ControlPanelItem_UsesManagedControlPanelLaunchSourceFallback_WhenSpecificTargetLookupFails()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path)
            {
                return path == explorerService.AllControlPanelPath || path == explorerService.PowerOptionsPath;
            };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)14,
                    HasValidTarget = true,
                    WasManagedControlPanelLaunchSource = true
                },
                delegate (IntPtr hwnd) { return explorerService.PowerOptionsPath; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
            Assert.AreEqual(explorerService.PowerOptionsPath, result.ResolvedPath);
            Assert.IsTrue(result.IsControlPanelPath);
            Assert.IsTrue(result.AllowSpecialPath);
        }

        private sealed class DesktopFolderExplorerService : MockExplorerService
        {
            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
        }

        private sealed class NullCurrentPathExplorerService : MockExplorerService
        {
            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return null;
            }
        }

        private sealed class ControlPanelRootCurrentPathExplorerService : MockExplorerService
        {
            public override string GetCurrentPath(IntPtr explorerHwnd)
            {
                return AllControlPanelPath;
            }
        }
    }
}
