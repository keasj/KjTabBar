using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ShellKnownLocationCacheTests
    {
        [TestMethod]
        public void GetLocalizedControlPanelTitle_UsesFallback_WhenResolverReturnsNull()
        {
            ShellKnownLocationCache cache = new ShellKnownLocationCache(
                delegate (string shellPath, string fallback) { return null; },
                delegate (string shellPath) { return false; },
                delegate { return @"C:\Users\Test"; });

            string title = cache.GetLocalizedControlPanelTitle("cp");

            Assert.AreEqual("Control Panel", title);
        }

        [TestMethod]
        public void GetResolvedHomeFolderPath_FallsBackToUserProfile_WhenShellPathUnavailable()
        {
            ShellKnownLocationCache cache = new ShellKnownLocationCache(
                delegate (string shellPath, string fallback) { return fallback; },
                delegate (string shellPath) { return false; },
                delegate { return @"C:\Users\Test"; });

            string path = cache.GetResolvedHomeFolderPath("shell-home");

            Assert.AreEqual(@"C:\Users\Test", path);
        }
    }
}
