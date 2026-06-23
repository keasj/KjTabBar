using System;
using KjTabBar.Models;

namespace UnitTestProject
{
    public class MockUserSettings : IUserSettings
    {
        public string FontFamily { get; set; } = "Arial";
        public double FontSize { get; set; } = 14.0;
        public bool IsBold { get; set; } = true;
        public bool IsItalic { get; set; } = false;

        public event EventHandler SettingsChanged;

        public void Save()
        {
            // Instead of writing to file, just raise the event
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TrySave(out string errorMessage)
        {
            errorMessage = null;
            Save();
            return true;
        }

        // Helper to trigger from tests without calling Save if needed
        public void TriggerChange()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
