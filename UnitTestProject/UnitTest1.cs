using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using KjTabBar;
using KjTabBar.Helpers;
using KjTabBar.Models;

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