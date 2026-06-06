using System;

namespace KjTabBar.Models
{
    internal sealed class DesktopForegroundTracker
    {
        private static readonly TimeSpan DesktopLaunchDetectWindow = TimeSpan.FromSeconds(2);
        private bool _isDesktopForeground;
        private DateTime _lastDesktopLaunchTokenUtc = DateTime.MinValue;
        private DateTime _lastDesktopInteractiveLaunchTokenUtc = DateTime.MinValue;
        private string _lastForegroundClassName = string.Empty;

        public IntPtr LastForegroundWindow { get; private set; }

        public bool WasDesktopForegroundRecently()
        {
            if (_isDesktopForeground)
            {
                return true;
            }
            if (_lastDesktopLaunchTokenUtc == DateTime.MinValue)
            {
                return false;
            }
            return (DateTime.UtcNow - _lastDesktopLaunchTokenUtc) <= DesktopLaunchDetectWindow;
        }

        public bool WasDesktopInteractiveForegroundRecently()
        {
            if (_lastDesktopInteractiveLaunchTokenUtc == DateTime.MinValue)
            {
                return false;
            }
            return (DateTime.UtcNow - _lastDesktopInteractiveLaunchTokenUtc) <= DesktopLaunchDetectWindow;
        }

        public void Update(IntPtr foregroundWindow, string className)
        {
            bool isDesktopWindowClass = IsDesktopShellWindowClass(className);
            if (!isDesktopWindowClass && _isDesktopForeground)
            {
                _lastDesktopLaunchTokenUtc = DateTime.UtcNow;
                if (IsDesktopItemViewWindowClass(_lastForegroundClassName))
                {
                    _lastDesktopInteractiveLaunchTokenUtc = DateTime.UtcNow;
                }
            }

            _isDesktopForeground = isDesktopWindowClass;
            LastForegroundWindow = foregroundWindow;
            _lastForegroundClassName = className;
        }

        private static readonly string[] DesktopShellWindowClasses = new string[]
        {
            "Progman", "WorkerW", "SHELLDLL_DefView", "SysListView32"
        };

        private static readonly string[] DesktopItemViewWindowClasses = new string[]
        {
            "SHELLDLL_DefView", "SysListView32"
        };

        private static bool IsDesktopShellWindowClass(string className)
        {
            return MatchesAnyClassName(className, DesktopShellWindowClasses);
        }

        private static bool IsDesktopItemViewWindowClass(string className)
        {
            return MatchesAnyClassName(className, DesktopItemViewWindowClasses);
        }

        private static bool MatchesAnyClassName(string className, string[] candidates)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                if (className.Equals(candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}