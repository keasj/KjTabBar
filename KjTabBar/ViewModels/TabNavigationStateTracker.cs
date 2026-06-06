using System;
using System.Collections.Generic;

namespace KjTabBar.ViewModels
{
    internal sealed class TabNavigationStateTracker
    {
        private static readonly TimeSpan ExplorerPathPollInterval = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan CancelledNavigationGracePeriod = TimeSpan.FromSeconds(15);

        private string _navigatingToPath;
        private DateTime _navigateStartTime;
        private List<string> _pendingSelectedItems;
        private TabItemViewModel _navigationSourceTab;
        private int _navigationSourceTabIndex = -1;
        private string _cancelledNavigationPath;
        private DateTime _cancelledNavigationUtc = DateTime.MinValue;
        private DateTime _lastExplorerPathPollUtc = DateTime.MinValue;
        private string _cachedExplorerPath;

        public string NavigatingToPath => _navigatingToPath;
        public DateTime NavigateStartTime => _navigateStartTime;
        public List<string> PendingSelectedItems
        {
            get => _pendingSelectedItems;
            set => _pendingSelectedItems = value;
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
                _cancelledNavigationPath = _navigatingToPath;
                _cancelledNavigationUtc = DateTime.UtcNow;
            }
            else
            {
                ClearCancelled();
            }

            rollbackTab = _navigationSourceTab;
            rollbackIndex = _navigationSourceTabIndex;

            ClearPending();
        }

        public bool IsCancelledNavigationMatch(string currentPath, Func<string, string, bool> pathEquals)
        {
            if (string.IsNullOrEmpty(_cancelledNavigationPath))
            {
                return false;
            }

            if (_cancelledNavigationUtc == DateTime.MinValue ||
                (DateTime.UtcNow - _cancelledNavigationUtc) > CancelledNavigationGracePeriod)
            {
                ClearCancelled();
                return false;
            }

            return pathEquals(_cancelledNavigationPath, currentPath);
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
            _cancelledNavigationPath = null;
            _cancelledNavigationUtc = DateTime.MinValue;
        }

        public bool ShouldPoll(DateTime nowUtc, bool force)
        {
            if (force) return true;
            if (_lastExplorerPathPollUtc == DateTime.MinValue) return true;
            return (nowUtc - _lastExplorerPathPollUtc) >= ExplorerPathPollInterval;
        }

        public void UpdateCache(string path, DateTime nowUtc)
        {
            _cachedExplorerPath = path;
            _lastExplorerPathPollUtc = nowUtc;
        }

        public string CachedExplorerPath => _cachedExplorerPath;
    }
}
