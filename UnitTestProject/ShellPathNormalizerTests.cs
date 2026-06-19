using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellPathNormalizerTests
    {
        private const string AllControlPanelPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
        private const string HomeFolderPath = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
        private const string ProgramsAndFeaturesPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}";
        private const string PowerOptionsPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";

        private ShellPathNormalizer CreateNormalizer(Func<string, string> getFolderNameInternal = null)
        {
            ShellLocationNameResolver locationResolver = new ShellLocationNameResolver(
                AllControlPanelPath,
                HomeFolderPath,
                ProgramsAndFeaturesPath,
                PowerOptionsPath,
                title => null
            );

            return new ShellPathNormalizer(
                AllControlPanelPath,
                HomeFolderPath,
                ProgramsAndFeaturesPath,
                PowerOptionsPath,
                () => "コントロール パネル",
                () => "ホーム",
                () => "ネットワーク",
                () => "ごみ箱",
                () => "PC",
                () => @"C:\Users\TestUser",
                locationResolver,
                getFolderNameInternal ?? (path => null)
            );
        }

        [TestMethod]
        public void IsControlPanelRootPath_Returns_True_For_ControlPanel_GUID_Path()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.IsTrue(normalizer.IsControlPanelRootPath("::{26EE0668-A00A-44D7-9371-BEB064C98683}"));
            Assert.IsTrue(normalizer.IsControlPanelRootPath("shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}"));
            Assert.IsTrue(normalizer.IsControlPanelRootPath("shell:controlpanel"));
            Assert.IsTrue(normalizer.IsControlPanelRootPath("コントロール パネル"));
        }

        [TestMethod]
        public void IsControlPanelRootPath_Returns_False_For_Non_ControlPanel_Path()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.IsFalse(normalizer.IsControlPanelRootPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
            Assert.IsFalse(normalizer.IsControlPanelRootPath(@"C:\Windows"));
            Assert.IsFalse(normalizer.IsControlPanelRootPath(PowerOptionsPath));
            Assert.IsFalse(normalizer.IsControlPanelRootPath(""));
            Assert.IsFalse(normalizer.IsControlPanelRootPath(null));
        }

        [TestMethod]
        public void IsTransientShellPlaceholderPath_Returns_True_For_Special_Paths()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.IsTrue(normalizer.IsTransientShellPlaceholderPath("::{26EE0668-A00A-44D7-9371-BEB064C98683}"));
            Assert.IsTrue(normalizer.IsTransientShellPlaceholderPath("::{679F85CB-0220-4080-B29B-5540CC05AAB6}"));
            Assert.IsTrue(normalizer.IsTransientShellPlaceholderPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        }

        [TestMethod]
        public void IsTransientShellPlaceholderPath_Returns_False_For_Normal_Paths()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.IsFalse(normalizer.IsTransientShellPlaceholderPath(@"C:\Users"));
            Assert.IsFalse(normalizer.IsTransientShellPlaceholderPath("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"));
        }

        [TestMethod]
        public void NormalizeShellPath_Resolves_Shell_Prefixes()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual(HomeFolderPath, normalizer.NormalizeShellPath("shell:home"));
            Assert.AreEqual(HomeFolderPath, normalizer.NormalizeShellPath("shell:quickaccess"));
            Assert.AreEqual(AllControlPanelPath, normalizer.NormalizeShellPath("shell:controlpanelfolder"));
            Assert.AreEqual("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", normalizer.NormalizeShellPath("shell:thispcfolder"));
            Assert.AreEqual("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", normalizer.NormalizeShellPath("shell:networkplacesfolder"));
            Assert.AreEqual("::{645FF040-5081-101B-9F08-00AA002F954E}", normalizer.NormalizeShellPath("shell:recyclebinfolder"));
        }

        [TestMethod]
        public void NormalizeShellPath_Removes_Trailing_Slash_And_Null()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", normalizer.NormalizeShellPath("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\"));
        }

        [TestMethod]
        public void NormalizeShellPath_Resolves_ControlPanel_Cpl_Text()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual(PowerOptionsPath, normalizer.NormalizeShellPath(@"C:\Windows\System32\powercfg.cpl"));
            Assert.AreEqual(ProgramsAndFeaturesPath, normalizer.NormalizeShellPath(@"C:\Windows\System32\appwiz.cpl"));
        }

        [TestMethod]
        public void NormalizeShellPath_Resolves_ControlPanel_Canonical_Name_Text()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual(PowerOptionsPath, normalizer.NormalizeShellPath("control.exe /name Microsoft.PowerOptions"));
            Assert.AreEqual(ProgramsAndFeaturesPath, normalizer.NormalizeShellPath("control.exe /name Microsoft.ProgramsAndFeatures"));
        }

        [TestMethod]
        public void NormalizeKnownPath_Preserves_Unrecognized_Path()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual(@"C:\Windows", normalizer.NormalizeKnownPath(@"C:\Windows"));
            Assert.AreEqual(HomeFolderPath, normalizer.NormalizeKnownPath("shell:home"));
        }

        [TestMethod]
        public void NormalizeShellNamespacePath_Strips_Embedded_Null_Suffix_From_ControlPanel_Item_Path()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();

            string normalized = normalizer.NormalizeShellNamespacePath(
                "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}\0\\::{00000000-0000-0000-0000-000000000000}");

            Assert.AreEqual("::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}", normalized);
        }

        [TestMethod]
        public void GetNavigableShellPath_Resolves_Home_And_ControlPanel()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.AreEqual(@"C:\Users\TestUser", normalizer.GetNavigableShellPath(HomeFolderPath));
            Assert.AreEqual("shell:controlpanel", normalizer.GetNavigableShellPath("shell:controlpanel"));
            Assert.AreEqual(AllControlPanelPath, normalizer.GetNavigableShellPath("shell:controlpanelfolder"));
        }

        [TestMethod]
        public void GetNavigableShellPath_Prefixes_ControlPanel_Item()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            // ProgramsAndFeaturesPath contains GUID 7B81BE6A-CE2B-4676-A29E-EB907A5126C5
            string expected = "::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}";
            Assert.AreEqual(expected, normalizer.GetNavigableShellPath("::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}"));
        }

        [TestMethod]
        public void MapLocationNameToKnownShellPath_Delegates_Correctly()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            string mapped = normalizer.MapLocationNameToKnownShellPath("Programs and Features");
            Assert.AreEqual(ProgramsAndFeaturesPath, mapped);
        }

        [TestMethod]
        public void FindControlPanelItemPathByTitle_Finds_ProgramsAndFeatures()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            string path = normalizer.FindControlPanelItemPathByTitle("Programs and Features");
            Assert.IsNotNull(path);
            string normalizedPath = normalizer.NormalizeShellPath(path);
            Assert.AreEqual("::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}", normalizedPath.ToUpperInvariant());
        }

        [TestMethod]
        public void FindControlPanelItemPathByTitle_Finds_PowerOptions()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            string path = normalizer.FindControlPanelItemPathByTitle("Power Options");
            Assert.IsNotNull(path);
            string normalizedPath = normalizer.NormalizeShellPath(path);
            Assert.AreEqual("::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}", normalizedPath.ToUpperInvariant());
        }

        [TestMethod]
        public void IsControlPanelItemPath_Returns_True_For_Known_Item()
        {
            ShellPathNormalizer normalizer = CreateNormalizer();
            Assert.IsTrue(normalizer.IsControlPanelItemPath(ProgramsAndFeaturesPath));
            Assert.IsFalse(normalizer.IsControlPanelItemPath(AllControlPanelPath));
        }
    }
}
