using System;
using System.Windows;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    internal sealed class TrayIconService : IDisposable
    {
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private System.Windows.Forms.ContextMenuStrip _trayMenu;
        private System.Drawing.Icon _trayIconObj;
        private IntPtr _trayIconHandle = IntPtr.Zero;

        public void Initialize(Func<string, object> findResource, Action exitAction)
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Text = GetProgramName();
            _trayIcon.Icon = LoadTrayIcon();
            _trayIcon.Visible = true;

            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            string exitText = null;
            if (findResource != null)
            {
                exitText = findResource("TrayMenuExit") as string;
            }
            if (string.IsNullOrEmpty(exitText))
            {
                exitText = "Exit";
            }

            System.Windows.Forms.ToolStripMenuItem exitItem = new System.Windows.Forms.ToolStripMenuItem(exitText);
            exitItem.Click += (s, ev) =>
            {
                if (exitAction != null)
                {
                    exitAction();
                }
            };
            _trayMenu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = _trayMenu;
        }

        public void Dispose()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_trayIconObj != null)
            {
                _trayIconObj.Dispose();
                _trayIconObj = null;
            }

            if (_trayIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_trayIconHandle);
                _trayIconHandle = IntPtr.Zero;
            }

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
        }

        private static string GetProgramName()
        {
            string programName = "KjTabBar";
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                System.Reflection.AssemblyTitleAttribute titleAttr = (System.Reflection.AssemblyTitleAttribute)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyTitleAttribute));
                if (titleAttr != null && !string.IsNullOrEmpty(titleAttr.Title))
                {
                    programName = titleAttr.Title;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TrayIconService", "Failed to load assembly title for tray icon.", ex);
            }

            return programName;
        }

        private System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                Uri iconUri = new Uri("pack://application:,,,/KjTabBar;component/Assets/Icons/app_icon.png");
                System.Windows.Resources.StreamResourceInfo streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    System.IO.Stream stream = streamInfo.Stream;
                    System.Drawing.Bitmap bitmap = null;
                    try
                    {
                        bitmap = new System.Drawing.Bitmap(stream);
                        _trayIconHandle = bitmap.GetHicon();
                        _trayIconObj = System.Drawing.Icon.FromHandle(_trayIconHandle);
                        return _trayIconObj;
                    }
                    finally
                    {
                        if (bitmap != null) bitmap.Dispose();
                        if (stream != null) stream.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TrayIconService", "Failed to create tray icon from PNG resource. Falling back to default icon.", ex);
            }

            return System.Drawing.SystemIcons.Application;
        }
    }
}
