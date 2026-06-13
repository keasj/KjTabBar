using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellLocationNameResolverTests
    {
        [TestMethod]
        public void MapLocationNameToKnownShellPath_ReturnsProgramsAndFeatures()
        {
            ShellLocationNameResolver resolver = new ShellLocationNameResolver(
                "cp-root",
                "home",
                "programs",
                "power",
                delegate (string title) { return null; });

            string mapped = resolver.MapLocationNameToKnownShellPath(
                "Programs and Features",
                "Control Panel",
                "Home",
                "Network",
                "Recycle Bin",
                "This PC");

            Assert.AreEqual("programs", mapped);
        }

        [TestMethod]
        public void IsControlPanelRootName_MatchesLocalizedTitle()
        {
            ShellLocationNameResolver resolver = new ShellLocationNameResolver(
                "cp-root",
                "home",
                "programs",
                "power",
                delegate (string title) { return null; });

            Assert.IsTrue(resolver.IsControlPanelRootName("コントロール パネル", "コントロール パネル"));
        }

        [TestMethod]
        public void MapLocationNameToKnownShellPath_ReturnsPowerOptions_From_WindowTitleSuffix()
        {
            ShellLocationNameResolver resolver = new ShellLocationNameResolver(
                "cp-root",
                "home",
                "programs",
                "power",
                delegate (string title) { return title == "電源オプション" ? "power" : null; });

            string mapped = resolver.MapLocationNameToKnownShellPath(
                "電源オプション - コントロール パネル",
                "コントロール パネル",
                "ホーム",
                "ネットワーク",
                "ごみ箱",
                "PC");

            Assert.AreEqual("power", mapped);
        }

    }
}
