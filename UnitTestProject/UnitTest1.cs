using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using KjTabBar;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace UnitTestProject
{
    [TestClass]
    public class SecurityRegressionTests
    {
        [TestMethod]
        public void LoadFromPath_Returns_Default_Settings_When_Dtd_Is_Present()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    tempFile,
                    "<?xml version=\"1.0\"?><!DOCTYPE settings [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]><UserSettings><FontFamily>&xxe;</FontFamily><FontSize>99</FontSize></UserSettings>");

                UserSettings settings = UserSettings.LoadFromPath(tempFile);

                Assert.AreEqual("Segoe UI", settings.FontFamily);
                Assert.AreEqual(UserSettings.DefaultFontSize, settings.FontSize);
                Assert.IsFalse(settings.IsBold);
                Assert.IsFalse(settings.IsItalic);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [TestMethod]
        public void ProtectedTextStorage_SaveLines_Does_Not_Write_Plaintext_Paths()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                string[] paths = new string[]
                {
                    @"C:\Users\alice\Documents\ClientA",
                    @"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
                };

                ProtectedTextStorage.SaveLines(tempFile, paths);
                string persistedText = File.ReadAllText(tempFile);
                string[] restoredPaths = ProtectedTextStorage.LoadLines(tempFile);

                CollectionAssert.AreEqual(paths, restoredPaths);
                Assert.IsTrue(persistedText.StartsWith("kjtb-dpapi-v1:", StringComparison.Ordinal));
                Assert.IsFalse(persistedText.Contains(paths[0]));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [TestMethod]
        public void ProtectedTextStorage_IsProtectedFile_Detects_Legacy_Plaintext_Format()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, @"C:\Work");

                Assert.IsFalse(ProtectedTextStorage.IsProtectedFile(tempFile));

                ProtectedTextStorage.SaveLines(tempFile, new string[] { @"C:\Work" });

                Assert.IsTrue(ProtectedTextStorage.IsProtectedFile(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [TestMethod]
        public void ProtectedTextStorage_LoadLines_Supports_Legacy_Plaintext_Format()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                string[] paths = new string[]
                {
                    @"C:\Work",
                    @"D:\Archive"
                };
                File.WriteAllText(tempFile, string.Join(Environment.NewLine, paths));

                string[] restoredPaths = ProtectedTextStorage.LoadLines(tempFile);

                CollectionAssert.AreEqual(paths, restoredPaths);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [TestMethod]
        public void TabPersistenceService_Uses_Custom_Path_Without_Touching_AppData_Default()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".tabs.txt");
            string activeTabFile = Path.Combine(Path.GetDirectoryName(tempFile), Path.GetFileNameWithoutExtension(tempFile) + ".active" + Path.GetExtension(tempFile));
            try
            {
                MockExplorerService explorer = new MockExplorerService();
                TabPersistenceService persistence = new TabPersistenceService(tempFile);
                TabBarViewModel vm = new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), explorer);
                vm.InsertTabWithPath(@"C:\SavedTab", 1);

                persistence.SaveTabsIfChanged(vm, true);

                Assert.IsTrue(File.Exists(tempFile));
                Assert.IsTrue(File.Exists(activeTabFile));
                string[] restoredPaths = ProtectedTextStorage.LoadLines(tempFile);
                string[] restoredActivePath = ProtectedTextStorage.LoadLines(activeTabFile);
                CollectionAssert.AreEqual(new string[] { @"C:\MockPath", @"C:\SavedTab" }, restoredPaths);
                CollectionAssert.AreEqual(new string[] { "index=1", @"C:\SavedTab" }, restoredActivePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                if (File.Exists(activeTabFile))
                {
                    File.Delete(activeTabFile);
                }
            }
        }

        [TestMethod]
        public void SanitizeForLog_Masks_File_Paths_And_Sids()
        {
            string sanitized = AppLogger.SanitizeForLog(@"Failed for C:\Users\alice\Secret\foo.txt and \\server\share\bar.txt (S-1-5-21-100-200-300-400).");

            Assert.IsFalse(sanitized.Contains(@"C:\Users\alice"));
            Assert.IsFalse(sanitized.Contains(@"\\server\share"));
            Assert.IsFalse(sanitized.Contains("S-1-5-21-100-200-300-400"));
            StringAssert.Contains(sanitized, "<path>");
            StringAssert.Contains(sanitized, "<sid>");
        }

        [TestMethod]
        public void IsStartupRunCommandForExecutable_Matches_Quoted_Command_Line()
        {
            Assert.IsTrue(SetupCustomActions.IsStartupRunCommandForExecutable(
                "\"C:\\Program Files\\KjTabBar\\KjTabBar.exe\" /background",
                @"C:\Program Files\KjTabBar\KjTabBar.exe"));

            Assert.IsFalse(SetupCustomActions.IsStartupRunCommandForExecutable(
                "\"C:\\Other\\KjTabBar.exe\" /background",
                @"C:\Program Files\KjTabBar\KjTabBar.exe"));
        }
    }
}
