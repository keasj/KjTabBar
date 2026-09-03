using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.ViewModels;

namespace UnitTestProject
{
    [TestClass]
    public class TabItemViewModelTests
    {
        [TestMethod]
        public void ShouldUseFileAttributeIconLookup_Returns_True_For_Unc_Path()
        {
            Assert.IsTrue(TabItemViewModel.ShouldUseFileAttributeIconLookup(@"\\server\share\folder"));
            Assert.IsFalse(TabItemViewModel.ShouldUseFileAttributeIconLookup(@"C:\Folder"));
            Assert.IsFalse(TabItemViewModel.ShouldUseFileAttributeIconLookup("shell:Desktop"));
            Assert.IsFalse(TabItemViewModel.ShouldUseFileAttributeIconLookup(null));
        }

        [TestMethod]
        public void ShouldUseFileAttributeIconLookup_Returns_True_For_Mapped_Network_Drive()
        {
            string requestedRootPath = null;

            bool result = TabItemViewModel.ShouldUseFileAttributeIconLookup(
                @"Z:\Folder",
                delegate (string rootPath)
                {
                    requestedRootPath = rootPath;
                    return KjTabBar.Helpers.NativeMethods.DRIVE_REMOTE;
                });

            Assert.IsTrue(result);
            Assert.AreEqual(@"Z:\", requestedRootPath);
        }

        [TestMethod]
        public void ShouldUseFileAttributeIconLookup_Returns_False_For_Fixed_Drive()
        {
            bool result = TabItemViewModel.ShouldUseFileAttributeIconLookup(
                @"C:\Folder",
                delegate { return 3; });

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Path_Does_Not_Raise_Change_For_Equivalent_Path()
        {
            TabItemViewModel viewModel = new TabItemViewModel(@"C:\Folder", "Folder", new MockExplorerService());
            int pathChangeCount = 0;
            viewModel.PropertyChanged += delegate (object sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "Path")
                {
                    pathChangeCount++;
                }
            };

            viewModel.Path = @"c:\folder";

            Assert.AreEqual(0, pathChangeCount);
        }
    }
}
