using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;
using System.IO;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerAbsorptionLogicTests
    {
        private MockExplorerService _mockExplorer;

        [TestInitialize]
        public void Setup()
        {
            _mockExplorer = new MockExplorerService();
            _mockExplorer.AllControlPanelPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
        }

        [TestMethod]
        public void TryGetUsersRelativePath_Returns_Relative_Path()
        {
            string path = @"C:\Users\username\Desktop\Folder";
            bool result = ExplorerAbsorptionLogic.TryGetUsersRelativePath(path, out string relative);
            Assert.IsTrue(result);
            Assert.AreEqual(@"Desktop\Folder", relative);
        }

        [TestMethod]
        public void TryGetUsersRelativePath_Fails_On_Invalid_Path()
        {
            string path = @"C:\Windows\System32";
            bool result = ExplorerAbsorptionLogic.TryGetUsersRelativePath(path, out string relative);
            
            Assert.IsFalse(result);
            Assert.IsNull(relative);
        }

        [TestMethod]
        public void IsSameOrChildPath_Returns_True_For_Child()
        {
            string parent = @"C:\MockFolder";
            string child = @"C:\MockFolder\Subfolder";

            bool result = ExplorerAbsorptionLogic.IsSameOrChildPath(child, parent);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsSameOrChildPath_Returns_False_For_Diff_Folder()
        {
            string parent = @"C:\MockFolder";
            string child = @"D:\MockFolder\Subfolder";

            bool result = ExplorerAbsorptionLogic.IsSameOrChildPath(child, parent);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AreEquivalentDesktopShortcutTargetPath_Matches_UsersRelativePath()
        {
            // If path1 is C:\Users\user1\Desktop\Link and path2 is D:\Users\user1\Desktop\Link 
            // the logic ignores drive letters for shortcut matching if they share the \Users\... part.
            string path1 = @"C:\Users\tester\Documents\Test";
            string path2 = @"E:\Users\tester\Documents\Test";

            bool result = ExplorerAbsorptionLogic.AreEquivalentDesktopShortcutTargetPath(path1, path2);
            Assert.IsTrue(result);
        }
        
        [TestMethod]
        public void AreEquivalentDesktopShortcutTargetPath_Matches_ExactPath()
        {
            string path1 = @"C:\Program Files\App";
            string path2 = @"C:\Program Files\APP\"; // case insensitive and trailing slash ignored

            bool result = ExplorerAbsorptionLogic.AreEquivalentDesktopShortcutTargetPath(path1, path2);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldAbsorbDesktopOriginPath_Absorbs_Special_Shell_Path()
        {
            bool result = ExplorerAbsorptionLogic.ShouldAbsorbDesktopOriginPath(false, false, false, true);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldAbsorbDesktopOriginPath_Absorbs_Desktop_Shell_Item()
        {
            bool result = ExplorerAbsorptionLogic.ShouldAbsorbDesktopOriginPath(false, false, true, true);
            Assert.IsTrue(result);
        }
    }
}
