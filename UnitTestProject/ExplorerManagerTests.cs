using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerManagerTests
    {
        [TestMethod]
        public void NormalizeKnownPath_Preserves_Home_As_Special_Path()
        {
            ExplorerManager manager = new ExplorerManager();

            string normalized = manager.NormalizeKnownPath("shell:Home");

            Assert.AreEqual(manager.HomeFolderPath, normalized);
        }

        [TestMethod]
        public void MapLocationNameToKnownShellPath_Preserves_Home_As_Special_Path()
        {
            ExplorerManager manager = new ExplorerManager();

            string mapped = manager.MapLocationNameToKnownShellPath("Home");

            Assert.AreEqual(manager.HomeFolderPath, mapped);
        }

        [TestMethod]
        public void GetExternalExplorerLaunchPath_Normalizes_ControlPanel_Item()
        {
            ExplorerManager manager = new ExplorerManager();

            string launchPath = manager.GetExternalExplorerLaunchPath(manager.ProgramsAndFeaturesPath);

            Assert.AreEqual("::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\" + manager.ProgramsAndFeaturesPath, launchPath);
        }
    }
}
