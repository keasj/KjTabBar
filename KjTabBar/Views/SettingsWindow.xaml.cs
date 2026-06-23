using System;
using System.Windows;
using System.Windows.Media;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = new SettingsViewModel(UserSettings.Current);
            ApplyTheme();
            ThemeManager.Instance.ThemeChanged += ThemeManager_ThemeChanged;
        }

        private void ThemeManager_ThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.Instance.ThemeChanged -= ThemeManager_ThemeChanged;
            DataContext = null;
            base.OnClosed(e);
        }

        private void ApplyTheme()
        {
            bool isDark = ThemeManager.Instance.IsDarkMode;

            if (isDark)
            {
                Resources["SettingsBg"] = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x26));
                Resources["SettingsFg"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                Resources["SettingsControlBg"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
                Resources["SettingsControlFg"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                Resources["SettingsBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x5F, 0x5F, 0x5F));
            }
            else
            {
                Resources["SettingsBg"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9));
                Resources["SettingsFg"] = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
                Resources["SettingsControlBg"] = new SolidColorBrush(Colors.White);
                Resources["SettingsControlFg"] = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                Resources["SettingsBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm != null)
            {
                string errorMessage;
                if (!vm.SaveSettings(out errorMessage))
                {
                    string errorTitle = TryFindResource("SaveSettingsErrorTitle") as string ?? "Save Error";
                    string errorPrefix = TryFindResource("SaveSettingsErrorMessage") as string ?? "Failed to save settings.";
                    MessageBox.Show(
                        string.IsNullOrEmpty(errorMessage) ? errorPrefix : errorPrefix + "\n\n" + errorMessage,
                        errorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnIncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm != null)
            {
                double newSize = vm.FontSize + 1.0;
                if (newSize > 32.0) newSize = 32.0;
                vm.FontSize = newSize;
            }
        }

        private void BtnDecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm != null)
            {
                double newSize = vm.FontSize - 1.0;
                if (newSize < 8.0) newSize = 8.0;
                vm.FontSize = newSize;
            }
        }
    }
}

