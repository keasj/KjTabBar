using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellShortcutManagerTests
    {
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
