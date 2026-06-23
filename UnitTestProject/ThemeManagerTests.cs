using System.Windows;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ThemeManagerTests
    {
        [TestMethod]
        public void ApplyThemeToResources_PopulatesRequiredThemeKeys()
        {
            ResourceDictionary resources = new ResourceDictionary();

            ThemeManager.Instance.ApplyThemeToResources(resources);

            Assert.IsTrue(resources.Contains("ThemeWindowBg"));
            Assert.IsTrue(resources.Contains("ThemeTabHover"));
            Assert.IsTrue(resources.Contains("ThemeFgNormal"));
            Assert.IsTrue(resources.Contains("ThemeFgSubtle"));
            Assert.IsTrue(resources.Contains("ThemeAccent"));
            Assert.IsTrue(resources.Contains("ThemeActiveTabBorder"));
            Assert.IsTrue(resources.Contains("ThemeCloseHoverBg"));
            Assert.IsTrue(resources.Contains("ThemeBorderLine"));
        }
    }
}
