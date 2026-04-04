using System;
using System.Reflection;

namespace KjTabBar.Services
{
    public class TrayIconService : IDisposable
    {
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private System.Windows.Forms.ContextMenuStrip _trayMenu;

        public TrayIconService(Action onExitClicked)
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            
            string programName = "KjTabBar";
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                AssemblyTitleAttribute titleAttr = (AssemblyTitleAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyTitleAttribute));
                if (titleAttr != null && !string.IsNullOrEmpty(titleAttr.Title))
                {
                    programName = titleAttr.Title;
                }
            }
            catch { }

            _trayIcon.Text = programName;
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            _trayIcon.Visible = true;

            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            string exitText = System.Windows.Application.Current.TryFindResource("TrayMenuExit") as string ?? "終了";
            System.Windows.Forms.ToolStripMenuItem exitItem = new System.Windows.Forms.ToolStripMenuItem(exitText);
            exitItem.Click += (s, ev) =>
            {
                onExitClicked?.Invoke();
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

            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
        }
    }
}
