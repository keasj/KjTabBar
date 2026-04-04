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
                Resources["SettingsBg"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
                Resources["SettingsFg"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                Resources["SettingsControlBg"] = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
                Resources["SettingsControlFg"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                Resources["SettingsBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            }
            else
            {
                Resources["SettingsBg"] = new SolidColorBrush(SystemColors.WindowColor);
                Resources["SettingsFg"] = new SolidColorBrush(Colors.Black);
                Resources["SettingsControlBg"] = new SolidColorBrush(Colors.White);
                Resources["SettingsControlFg"] = new SolidColorBrush(Colors.Black);
                Resources["SettingsBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SettingsViewModel vm = DataContext as SettingsViewModel;
            if (vm != null)
            {
                vm.SaveSettings();
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

