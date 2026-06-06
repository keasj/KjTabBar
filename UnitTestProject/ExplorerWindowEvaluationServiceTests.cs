using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowEvaluationServiceTests
    {
        [TestMethod]
        public void Evaluate_ControlPanelRootTitle_UsesAllControlPanelPathForTargetLookup()
        {
            MockExplorerService explorerService = new MockExplorerService();
            explorerService.IsControlPanelPathFunc = delegate (string path) { return path == explorerService.AllControlPanelPath; };
            explorerService.IsControlPanelRootPathFunc = delegate (string path) { return path == "Control Panel"; };

            ExplorerWindowEvaluationService service =
                new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));

            string requestedSearchPath = null;
            ExplorerWindowEvaluationResult result = service.Evaluate(
                new ExplorerWindowEvaluationInput
                {
                    ExplorerHwnd = (IntPtr)1,
                    HasValidTarget = true,
                    IsControlPanelTabLaunchCandidate = true
                },
                delegate (IntPtr hwnd) { return "Control Panel"; },
                delegate (string path)
                {
                    requestedSearchPath = path;
                    return true;
                },
                delegate (string path) { return true; },
                delegate { return true; });

            Assert.AreEqual(explorerService.AllControlPanelPath, requestedSearchPath);
            Assert.AreEqual(AbsorptionAction.Absorb, result.Action);
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
                delegate { return false; });

            Assert.AreEqual(AbsorptionAction.CreateNewTabBar, result.Action);
            Assert.AreEqual(@"C:\MockPath", result.ResolvedPath);
            Assert.IsFalse(result.AllowSpecialPath);
        }
    }
}
