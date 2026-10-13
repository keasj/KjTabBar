using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellShortcutManagerTests
    {
        [TestMethod]
        public void ElevatedLinks_PreservePathsAndFileTypes_AndReportPartialFailure()
        {
            string[] values = { @"C:\出力\a & b.txt", @"C:\入力\a & b.txt", "0", @"C:\出力\folder", @"C:\入力\folder", "1" };
            int calls = 0;
            int result = KjTabBar.Services.SymbolicLinkElevationService.Execute(values, (link, target, flags) =>
            {
                Assert.AreEqual(values[calls * 3], link);
                Assert.AreEqual(values[calls * 3 + 1], target);
                Assert.AreEqual((uint)calls, flags);
                calls++;
                return calls != 1;
            });
            Assert.AreEqual(2, calls);
            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void ElevatedLinks_ValidateEntireBatchBeforeCreating()
        {
            string[] values = { @"C:\output\ok", @"C:\input\ok", "0", "relative", @"C:\input\other", "1" };
            int calls = 0;
            int result = KjTabBar.Services.SymbolicLinkElevationService.Execute(values, (link, target, flags) => { calls++; return true; });
            Assert.AreEqual(1, result);
            Assert.AreEqual(0, calls);
        }

        [TestMethod]
        public void ElevatedLinks_ReturnSuccessWhenAllCreated()
        {
            int result = KjTabBar.Services.SymbolicLinkElevationService.Execute(
                new[] { @"C:\output\file", @"C:\input\file", "0" }, (link, target, flags) => true);
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void ElevatedLinks_RejectRequestWhoseContentsChanged()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "modified request");
                Assert.AreEqual(1, KjTabBar.Services.SymbolicLinkElevationService.Run(
                    new[] { KjTabBar.Services.SymbolicLinkElevationService.StartupArgument, path, "wrong hash" }));
            }
            finally { System.IO.File.Delete(path); }
        }

        [TestMethod]
        public void ResolveShortcutTarget_ReturnsInputPath_WhenNotLnkFile()
        {
            ShellShortcutManager manager = new ShellShortcutManager(
                path => "FolderName",
                path => path,
                path => path,
                () => null,
                obj => {},
                (obj, prop) => null,
                (obj, method, args) => null,
                "::{21EC2020-3AEA-1069-A2DD-08002B30309D}",
                "::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}",
                "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}"
            );

            string result = manager.ResolveShortcutTarget(@"C:\Temp\not_shortcut.txt");

            Assert.AreEqual(@"C:\Temp\not_shortcut.txt", result);
        }

        [TestMethod]
        public void ResolveShortcutTarget_ReturnsInputPath_WhenEmptyPath()
        {
            ShellShortcutManager manager = new ShellShortcutManager(
                path => "FolderName",
                path => path,
                path => path,
                () => null,
                obj => {},
                (obj, prop) => null,
                (obj, method, args) => null,
                "::{21EC2020-3AEA-1069-A2DD-08002B30309D}",
                "::{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}",
                "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}"
            );

            string result = manager.ResolveShortcutTarget("");

            Assert.IsNull(result);
        }
    }
}
