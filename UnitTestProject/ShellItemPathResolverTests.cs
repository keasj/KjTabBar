using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellItemPathResolverTests
    {
        [TestMethod]
        public void GetItemParseName_ReturnsFileName_ForFileSystemPath()
        {
            ShellItemPathResolver resolver = new ShellItemPathResolver(delegate (string path) { return path; });

            string parseName = resolver.GetItemParseName(@"C:\Temp\file.txt");

            Assert.AreEqual("file.txt", parseName);
        }

        [TestMethod]
        public void AreEquivalentItemPaths_UsesNormalizedKnownPath()
        {
            ShellItemPathResolver resolver = new ShellItemPathResolver(
                delegate (string path)
                {
                    if (path == "shell:Home") return "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";
                    return path;
                });

            bool equivalent = resolver.AreEquivalentItemPaths("shell:Home", "::{679F85CB-0220-4080-B29B-5540CC05AAB6}");

            Assert.IsTrue(equivalent);
        }
    }
}
