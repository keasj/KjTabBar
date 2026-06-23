using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellFolderNameResolverTests
    {
        [TestMethod]
        public void GetFolderName_ReturnsHomeTitle_WhenEmptyPath()
        {
            ShellFolderNameResolver resolver = new ShellFolderNameResolver(
                () => "Home",
                () => "ControlPanel",
                path => false,
                path => path,
                path => path,
                () => null,
                null,
                null
            );

            string title = resolver.GetFolderName("");

            Assert.AreEqual("Home", title);
        }

        [TestMethod]
        public void GetFolderName_ReturnsControlPanelTitle_WhenControlPanelRootPath()
        {
            ShellFolderNameResolver resolver = new ShellFolderNameResolver(
                () => "Home",
                () => "Control Panel Title",
                path => path == "cp",
                path => path,
                path => path,
                () => null,
                null,
                null
            );

            string title = resolver.GetFolderName("cp");

            Assert.AreEqual("Control Panel Title", title);
        }

        [TestMethod]
        public void GetFolderName_FallsBackToDirectoryInfoName_ForNormalPath()
        {
            ShellFolderNameResolver resolver = new ShellFolderNameResolver(
                () => "Home",
                () => "ControlPanel",
                path => false,
                path => path,
                path => path,
                () => null,
                null,
                null
            );

            string title = resolver.GetFolderName(@"C:\Windows\System32");

            Assert.AreEqual("System32", title);
        }
    }
}
