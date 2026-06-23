using System.Globalization;
using System.Windows;
using KjTabBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class LanguageResourceServiceTests
    {
        [TestMethod]
        public void GetDictionaryName_UsesJapaneseDictionary_ForJaCulture()
        {
            LanguageResourceService service = new LanguageResourceService();

            string result = service.GetDictionaryName(new CultureInfo("ja-JP"));

            Assert.AreEqual("StringResources.ja.xaml", result);
        }
    }
}
