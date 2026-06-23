using System;
using System.Windows;
using System.Windows.Media;
using KjTabBar.Helpers;
using Microsoft.Win32;

namespace KjTabBar.Models
{
    /// <summary>
    /// Windows のアプリダークモード設定を監視し、テーマカラーを提供する。
    /// レジストリ HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize の
    /// AppsUseLightTheme 値（0=ダーク、1=ライト）をイベント通知時に再検出する。
    /// </summary>
    public sealed class ThemeManager
    {
        private static ThemeManager _instance;
        private static readonly object _lock = new object();

        private bool _isDarkMode;
        private bool _isMonitoring;

        /// <summary>テーマが変更されたときに発火する。</summary>
        public event EventHandler ThemeChanged;

        /// <summary>現在ダークモードかどうか。</summary>
        public bool IsDarkMode
        {
            get { return _isDarkMode; }
        }

        private ThemeManager()
        {
            _isDarkMode = DetectDarkMode();
        }

        public static ThemeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ThemeManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Windows のユーザー設定変更監視を開始する。UIスレッドから呼び出すこと。
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _isMonitoring = true;
        }

        /// <summary>
        /// Windows のユーザー設定変更監視を停止する。
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _isMonitoring = false;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(RefreshTheme));
                return;
            }

            RefreshTheme();
        }

        private void RefreshTheme()
        {
            bool newDark = DetectDarkMode();
            if (newDark != _isDarkMode)
            {
                _isDarkMode = newDark;
                EventHandler handler = ThemeChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// レジストリから Windows のアプリダークモード設定を読み取る。
        /// </summary>
        private static bool DetectDarkMode()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
                if (key != null)
                {
                    try
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val != null && val is int)
                        {
                            return (int)val == 0;
                        }
                    }
                    finally
                    {
                        key.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ThemeManager", "Failed to read Windows theme setting. Falling back to light mode.", ex);
                // レジストリ読み取り失敗時はライトモードとみなす
            }
            return false;
        }

        // ─────── テーマカラー定義 ───────

        /// <summary>
        /// 現在のテーマに応じたリソースディクショナリの色を Application.Resources に適用する。
        /// </summary>
        public void ApplyThemeToResources(ResourceDictionary resources)
        {
            if (resources == null) return;

            if (_isDarkMode)
            {
                // ダークモード用カラー（Fluent 2寄り）
                resources["ThemeWindowBg"] = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x20, 0x22));
                resources["ThemeTabHover"] = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                resources["ThemeFgNormal"] = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
                resources["ThemeFgSubtle"] = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
                resources["ThemeAccent"] = new SolidColorBrush(Color.FromArgb(0x66, 0x60, 0xCD, 0xFF));
                resources["ThemeActiveTabBorder"] = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
                resources["ThemeCloseHoverBg"] = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                resources["ThemeBorderLine"] = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                // ライトモード用カラー（Fluent 2寄り）
                resources["ThemeWindowBg"] = new SolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFA));
                resources["ThemeTabHover"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF9));
                resources["ThemeFgNormal"] = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                resources["ThemeFgSubtle"] = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
                resources["ThemeAccent"] = new SolidColorBrush(Color.FromRgb(0xD6, 0xE9, 0xF8));
                resources["ThemeActiveTabBorder"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                resources["ThemeCloseHoverBg"] = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                resources["ThemeBorderLine"] = new SolidColorBrush(Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
            }
        }
    }
}
