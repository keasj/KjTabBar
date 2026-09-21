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
        public void FullReview_FailedSaveRestoresSharedSettingsAndAllowsRetry()
        {
            FailingSettings settings = new FailingSettings();
            SettingsViewModel vm = new SettingsViewModel(settings);
            vm.FontFamily = "Segoe UI"; vm.FontSize = 24; vm.IsBold = true; vm.IsItalic = true;
            string error;
            Assert.IsFalse(vm.SaveSettings(out error));
            Assert.AreEqual("Arial", settings.FontFamily);
            Assert.AreEqual(14.0, settings.FontSize);
            Assert.IsFalse(settings.IsBold); Assert.IsFalse(settings.IsItalic);
            Assert.AreEqual(14.0, new SettingsViewModel(settings).FontSize);
            Assert.AreEqual(24.0, vm.FontSize);
            settings.Succeeds = true;
            Assert.IsTrue(vm.SaveSettings(out error));
            Assert.AreEqual(24.0, settings.FontSize);
        }

        private sealed class FailingSettings : IUserSettings
        {
            internal bool Succeeds;
            public string FontFamily { get; set; } = "Arial";
            public double FontSize { get; set; } = 14;
            public bool IsBold { get; set; }
            public bool IsItalic { get; set; }
            public event EventHandler SettingsChanged { add { } remove { } }
            public void Save() { }
            public bool TrySave(out string error) { error = Succeeds ? null : "Simulated save failure"; return Succeeds; }
        }
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

            string errorMessage;
            bool saved = vm.SaveSettings(out errorMessage);

            Assert.IsTrue(saved);
            Assert.IsNull(errorMessage);
            Assert.AreEqual("Times New Roman", mockSettings.FontFamily);
            Assert.AreEqual(16.0, mockSettings.FontSize);
            Assert.IsTrue(mockSettings.IsBold);
            Assert.IsTrue(mockSettings.IsItalic);
            Assert.IsTrue(eventFired);
        }
    }
}
