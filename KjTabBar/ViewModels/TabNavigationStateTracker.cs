using System;
using System.Collections.Generic;

namespace KjTabBar.ViewModels
{
    internal sealed class TabNavigationStateTracker
    {
        private static readonly TimeSpan ExplorerPathPollInterval = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan ExplorerHostSwitchGracePeriod = TimeSpan.FromMilliseconds(750);

        private string _navigatingToPath;
        private DateTime _navigateStartTime;
        private List<string> _pendingSelectedItems;
        private TabItemViewModel _navigationSourceTab;
        private int _navigationSourceTabIndex = -1;
        private readonly Dictionary<string, DateTime> _cancelledNavigations = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastExplorerPathPollUtc = DateTime.MinValue;
        private string _cachedExplorerPath;
        private DateTime _lastExplorerHostSwitchUtc = DateTime.MinValue;

        public string NavigatingToPath => _navigatingToPath;
        internal TabItemViewModel NavigationSourceTab => _navigationSourceTab;
        public DateTime NavigateStartTime => _navigateStartTime;
        public List<string> PendingSelectedItems
        {
            get => _pendingSelectedItems;
            set => _pendingSelectedItems = value;
        }

        internal Action CapturePendingNavigation()
        {
            if (_navigatingToPath == null) return null;
            string path = _navigatingToPath;
            DateTime started = _navigateStartTime;
            TabItemViewModel source = _navigationSourceTab;
            int index = _navigationSourceTabIndex;
            List<string> items = _pendingSelectedItems;
            return delegate
            {
                _navigatingToPath = path;
                _navigateStartTime = started;
                _navigationSourceTab = source;
                _navigationSourceTabIndex = index;
                _pendingSelectedItems = items;
                InvalidateCache();
            };
        }
        public void StartNavigation(string targetPath, TabItemViewModel sourceTab, int sourceIndex)
        {
            _navigatingToPath = targetPath;
            _navigateStartTime = DateTime.UtcNow;
            _navigationSourceTab = sourceTab;
            _navigationSourceTabIndex = sourceIndex;
            _lastExplorerPathPollUtc = DateTime.MinValue;
        }

        public void CancelNavigation(TabItemViewModel activeTab, out TabItemViewModel rollbackTab, out int rollbackIndex)
        {
            if (!string.IsNullOrEmpty(_navigatingToPath) && activeTab != null && activeTab != _navigationSourceTab)
            {
                RememberPendingAsCancelled();
            }

            rollbackTab = _navigationSourceTab;
            rollbackIndex = _navigationSourceTabIndex;

            ClearPending();
        }

        internal void RememberPendingAsCancelled()
        {
            if (!string.IsNullOrEmpty(_navigatingToPath)) _cancelledNavigations[_navigatingToPath] = DateTime.UtcNow;
        }

        public bool IsCancelledNavigationMatch(string currentPath, Func<string, string, bool> pathEquals)
        {
            foreach (string path in _cancelledNavigations.Keys)
                if (pathEquals(path, currentPath)) return true;
            return false;
        }

        internal void ForgetCancelled(string currentPath, Func<string, string, bool> pathEquals)
        {
            List<string> matches = new List<string>();
            foreach (string path in _cancelledNavigations.Keys)
                if (pathEquals(path, currentPath)) matches.Add(path);
            foreach (string path in matches) _cancelledNavigations.Remove(path);
        }
        public void ClearPending()
        {
            _navigatingToPath = null;
            _navigationSourceTab = null;
            _navigationSourceTabIndex = -1;
            _pendingSelectedItems = null;
        }

        public void ClearCancelled()
        {
            _cancelledNavigations.Clear();
        }

        public bool ShouldPoll(DateTime nowUtc, bool force)
        {
            if (force) return true;
            if (_lastExplorerPathPollUtc == DateTime.MinValue) return true;
            return (nowUtc - _lastExplorerPathPollUtc) >= ExplorerPathPollInterval;
        }

        internal void InvalidateCache()
        {
            _lastExplorerPathPollUtc = DateTime.MinValue;
            _cachedExplorerPath = null;
        }

        public void UpdateCache(string path, DateTime nowUtc)
        {
            _cachedExplorerPath = path;
            _lastExplorerPathPollUtc = nowUtc;
        }

        public string CachedExplorerPath => _cachedExplorerPath;

        public bool TryGetRecentCachedExplorerPath(DateTime nowUtc, out string path)
        {
            path = _cachedExplorerPath;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return !ShouldPoll(nowUtc, false);
        }

        public void NotifyExplorerHostChanged()
        {
            // Requests belong to the old host. Time alone does not prove completion.
            ClearCancelled();
            _lastExplorerHostSwitchUtc = DateTime.UtcNow;
            _lastExplorerPathPollUtc = DateTime.MinValue;
            _cachedExplorerPath = null;
        }

        public bool IsExplorerHostSwitchGraceActive(DateTime nowUtc)
        {
            if (_lastExplorerHostSwitchUtc == DateTime.MinValue)
            {
                return false;
            }

            return (nowUtc - _lastExplorerHostSwitchUtc) < ExplorerHostSwitchGracePeriod;
        }
    }
}
