using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using KjTabBar.ViewModels;
using KjTabBar.Models;

namespace UnitTestProject
{
    [TestClass]
    public class SettingsViewModelTests
    {
        [TestMethod]
        public void Load_SettingsViewModel_Uses_UserSettings()
        {
            MockUserSettings mockSettings = new MockUserSettings
            {
                FontFamily = "Comic Sans MS",
                FontSize = 18.0,
                IsBold = true,
                IsItalic = false
            };

            SettingsViewModel vm = new SettingsViewModel(mockSettings);

            Assert.AreEqual("Comic Sans MS", vm.FontFamily);
            Assert.AreEqual(18.0, vm.FontSize);
            Assert.IsTrue(vm.IsBold);
            Assert.IsFalse(vm.IsItalic);
        }

        [TestMethod]
        public void SaveSettings_Updates_UserSettings()
        {
            MockUserSettings mockSettings = new MockUserSettings
            {
                FontFamily = "Arial",
                FontSize = 14.0,
                IsBold = false,
                IsItalic = false
            };

            bool eventFired = false;
            mockSettings.SettingsChanged += (s, e) => { eventFired = true; };

            SettingsViewModel vm = new SettingsViewModel(mockSettings);

            vm.FontFamily = "Times New Roman";
            vm.FontSize = 16.0;
            vm.IsBold = true;
            vm.IsItalic = true;

            vm.SaveSettings();

            Assert.AreEqual("Times New Roman", mockSettings.FontFamily);
            Assert.AreEqual(16.0, mockSettings.FontSize);
            Assert.IsTrue(mockSettings.IsBold);
            Assert.IsTrue(mockSettings.IsItalic);
            Assert.IsTrue(eventFired);
        }
    }
}
