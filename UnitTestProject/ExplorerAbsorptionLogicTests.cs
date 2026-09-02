using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;
using System;
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
            Assert.AreEqual(@"Users\username\Desktop\Folder", relative);
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
        public void AreEquivalentDesktopShortcutTargetPath_Does_Not_Match_Different_Users()
        {
            string path1 = @"C:\Users\user1\Documents\Test";
            string path2 = @"E:\Users\user2\Documents\Test";

            bool result = ExplorerAbsorptionLogic.AreEquivalentDesktopShortcutTargetPath(path1, path2);

            Assert.IsFalse(result);
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

        [TestMethod]
        public void HasShortcutToPathInDesktop_Reuses_Cached_Shortcut_Target()
        {
            string desktopPath = CreateTemporaryDesktopWithShortcut("cached.lnk");
            try
            {
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
                CountingExplorerService explorerService = new CountingExplorerService(@"C:\Target");

                Assert.IsTrue(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));
                Assert.IsTrue(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));

                Assert.AreEqual(1, explorerService.ResolveShortcutTargetCallCount);
            }
            finally
            {
                Directory.Delete(desktopPath, true);
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
            }
        }

        [TestMethod]
        public void HasShortcutToPathInDesktop_Invalidates_Cache_When_Shortcut_Changes()
        {
            string desktopPath = CreateTemporaryDesktopWithShortcut("changed.lnk");
            string shortcutPath = Path.Combine(desktopPath, "changed.lnk");
            try
            {
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
                CountingExplorerService explorerService = new CountingExplorerService(@"C:\Target");

                Assert.IsTrue(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));
                File.AppendAllText(shortcutPath, "updated");
                File.SetLastWriteTimeUtc(shortcutPath, DateTime.UtcNow.AddMinutes(1));
                Assert.IsTrue(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));

                Assert.AreEqual(2, explorerService.ResolveShortcutTargetCallCount);
            }
            finally
            {
                Directory.Delete(desktopPath, true);
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
            }
        }

        [TestMethod]
        public void HasShortcutToPathInDesktop_Prunes_Deleted_Shortcuts_From_Cache()
        {
            string desktopPath = CreateTemporaryDesktopWithShortcut("first.lnk");
            string deletedShortcutPath = Path.Combine(desktopPath, "first.lnk");
            File.WriteAllText(Path.Combine(desktopPath, "second.lnk"), "shortcut");
            try
            {
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
                CountingExplorerService explorerService = new CountingExplorerService(@"C:\Other");

                Assert.IsFalse(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));
                Assert.AreEqual(2, ExplorerAbsorptionLogic.GetShortcutTargetCacheCountForTests());

                File.Delete(deletedShortcutPath);
                Assert.IsFalse(ExplorerAbsorptionLogic.HasShortcutToPathInDesktop(explorerService, desktopPath, @"C:\Target"));

                Assert.AreEqual(1, ExplorerAbsorptionLogic.GetShortcutTargetCacheCountForTests());
            }
            finally
            {
                Directory.Delete(desktopPath, true);
                ExplorerAbsorptionLogic.ClearShortcutTargetCacheForTests();
            }
        }

        private static string CreateTemporaryDesktopWithShortcut(string shortcutFileName)
        {
            string desktopPath = Path.Combine(Path.GetTempPath(), "KjTabBarTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(desktopPath);
            File.WriteAllText(Path.Combine(desktopPath, shortcutFileName), "shortcut");
            return desktopPath;
        }

        private sealed class CountingExplorerService : MockExplorerService
        {
            private readonly string _resolvedPath;

            public CountingExplorerService(string resolvedPath)
            {
                _resolvedPath = resolvedPath;
            }

            public int ResolveShortcutTargetCallCount { get; private set; }

            public override string ResolveShortcutTarget(string path)
            {
                ResolveShortcutTargetCallCount++;
                return _resolvedPath;
            }
        }
    }
}
