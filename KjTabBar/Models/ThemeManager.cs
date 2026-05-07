using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KjTabBar.Models
{
    /// <summary>
    /// Windows のアプリダークモード設定を監視し、テーマカラーを提供する。
    /// レジストリ HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize の
    /// AppsUseLightTheme 値（0=ダーク、1=ライト）を定期ポーリングで検出する。
    /// </summary>
    public sealed class ThemeManager
    {
        private static ThemeManager _instance;
        private static readonly object _lock = new object();

        private bool _isDarkMode;
        private DispatcherTimer _pollTimer;

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
        /// 定期ポーリングを開始する。UIスレッドから呼び出すこと。
        /// </summary>
        public void StartMonitoring()
        {
            if (_pollTimer != null) return;
            _pollTimer = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(2);
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();
        }

        /// <summary>
        /// 定期ポーリングを停止する。
        /// </summary>
        public void StopMonitoring()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Tick -= PollTimer_Tick;
                _pollTimer.Stop();
                _pollTimer = null;
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
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
            catch
            {
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
                // ダークモード用カラー（Fluent 風）
                resources["ThemeWindowBg"] = new SolidColorBrush(Color.FromArgb(0xE8, 0x20, 0x20, 0x20));
                resources["ThemeTabHover"] = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
                resources["ThemeFgNormal"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
                resources["ThemeFgSubtle"] = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
                resources["ThemeAccent"] = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                resources["ThemeActiveTabBorder"] = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
                resources["ThemeCloseHoverBg"] = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                resources["ThemeBorderLine"] = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                // ライトモード用カラー（Fluent 風）
                resources["ThemeWindowBg"] = new SolidColorBrush(Color.FromArgb(0xED, 0xF9, 0xF9, 0xF9));
                resources["ThemeTabHover"] = new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xEF));
                resources["ThemeFgNormal"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
                resources["ThemeFgSubtle"] = new SolidColorBrush(Color.FromRgb(0x5E, 0x5E, 0x5E));
                resources["ThemeAccent"] = new SolidColorBrush(Color.FromRgb(0xDF, 0xDF, 0xDF));
                resources["ThemeActiveTabBorder"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                resources["ThemeCloseHoverBg"] = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                resources["ThemeBorderLine"] = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
            }
        }
    }
}
