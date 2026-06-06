using System;
using System.Windows.Threading;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class AppUiDispatcherAdapter
    {
        private readonly Dispatcher _dispatcher;
        private readonly ExplorerTabTargetResolver _tabTargetResolver;
        private readonly Func<IntPtr, bool> _isForegroundRelatedWindow;

        public AppUiDispatcherAdapter(
            Dispatcher dispatcher,
            ExplorerTabTargetResolver tabTargetResolver,
            Func<IntPtr, bool> isForegroundRelatedWindow)
        {
            _dispatcher = dispatcher;
            _tabTargetResolver = tabTargetResolver;
            _isForegroundRelatedWindow = isForegroundRelatedWindow;
        }

        public TabBarViewModel FindValidTabBarTarget()
        {
            return _dispatcher.Invoke(new Func<TabBarViewModel>(() =>
            {
                return _tabTargetResolver.FindValidTabBarTarget(_isForegroundRelatedWindow);
            }));
        }

        public TabBarViewModel FindControlPanelTabBarTarget(string path)
        {
            return _dispatcher.Invoke(new Func<TabBarViewModel>(() =>
            {
                return _tabTargetResolver.FindControlPanelTabBarTarget(
                    _tabTargetResolver.GetAliveTabBarViewModels(),
                    path,
                    _isForegroundRelatedWindow);
            }));
        }

        public bool HasEquivalentControlPanelTab(TabBarViewModel targetViewModel, string path)
        {
            return _dispatcher.Invoke(new Func<bool>(() =>
            {
                return _tabTargetResolver.HasEquivalentControlPanelTab(targetViewModel, path);
            }));
        }

        public bool HasActiveControlPanelTab(TabBarViewModel targetViewModel)
        {
            return _dispatcher.Invoke(new Func<bool>(() =>
            {
                return _tabTargetResolver.HasActiveControlPanelTab(targetViewModel);
            }));
        }
    }
}
