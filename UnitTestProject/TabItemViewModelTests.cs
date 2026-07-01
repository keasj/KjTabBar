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
    }
}