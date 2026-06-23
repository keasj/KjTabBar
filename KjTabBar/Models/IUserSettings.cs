using System;

namespace KjTabBar.Models
{
    public interface IUserSettings
    {
        string FontFamily { get; set; }
        double FontSize { get; set; }
        bool IsBold { get; set; }
        bool IsItalic { get; set; }

        event EventHandler SettingsChanged;
        void Save();
        bool TrySave(out string errorMessage);
    }
}
