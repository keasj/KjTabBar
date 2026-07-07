using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private IUserSettings _settings;
        private string _fontFamily;
        private double _fontSize;
        private bool _isBold;
        private bool _isItalic;

        private ObservableCollection<string> _availableFonts;

        public string FontFamily
        {
            get { return _fontFamily; }
            set { _fontFamily = value; OnPropertyChanged("FontFamily"); }
        }

        public double FontSize
        {
            get { return _fontSize; }
            set { _fontSize = value; OnPropertyChanged("FontSize"); }
        }

        public bool IsBold
        {
            get { return _isBold; }
            set { _isBold = value; OnPropertyChanged("IsBold"); }
        }

        public bool IsItalic
        {
            get { return _isItalic; }
            set { _isItalic = value; OnPropertyChanged("IsItalic"); }
        }



        public string ProgramName { get; private set; }
        public string Version { get; private set; }

        public ObservableCollection<string> AvailableFonts
        {
            get { return _availableFonts; }
        }

        public SettingsViewModel(IUserSettings settings)
        {
            _settings = settings;
            _fontFamily = settings.FontFamily;
            _fontSize = UserSettings.NormalizeFontSize(settings.FontSize);
            _isBold = settings.IsBold;
            _isItalic = settings.IsItalic;


            _availableFonts = new ObservableCollection<string>();
            System.Collections.Generic.List<string> fontNames = new System.Collections.Generic.List<string>();
            foreach (FontFamily family in Fonts.SystemFontFamilies)
            {
                fontNames.Add(family.Source);
            }
            fontNames.Sort();
            foreach (string fontName in fontNames)
            {
                _availableFonts.Add(fontName);
            }

            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                System.Reflection.AssemblyTitleAttribute titleAttr = (System.Reflection.AssemblyTitleAttribute)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyTitleAttribute));
                ProgramName = titleAttr != null ? titleAttr.Title : "KjTabBar";
                Version = "v" + asm.GetName().Version.ToString();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SettingsViewModel", "Failed to read assembly metadata for settings window.", ex);
                ProgramName = "KjTabBar";
                Version = "v1.2.2.0";
            }
        }

        public bool SaveSettings(out string errorMessage)
        {
            errorMessage = null;
            _settings.FontFamily = _fontFamily;
            _settings.FontSize = UserSettings.NormalizeFontSize(_fontSize);
            _settings.IsBold = _isBold;
            _settings.IsItalic = _isItalic;
            _fontSize = _settings.FontSize;

            return _settings.TrySave(out errorMessage);
        }
    }
}
