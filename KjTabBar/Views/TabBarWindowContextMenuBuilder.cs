using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    internal sealed class TabBarWindowContextMenuBuilder
    {
        private readonly TabBarWindow _window;
        private readonly IExplorerService _explorerService;

        public TabBarWindowContextMenuBuilder(TabBarWindow window, IExplorerService explorerService)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
        }

        public void ShowTabContextMenu(Border tabBd, TabItemViewModel tabVM, TabBarViewModel vm, Action onClosed)
        {
            if (tabBd == null || tabVM == null || vm == null) return;

            _window.Activate();
            ContextMenu menu = new ContextMenu();
            ApplyFluentMenuStyle(menu);

            MenuItem duplicateItem = new MenuItem() { Header = _window.TryFindResource("MenuDuplicateTab") as string ?? "タブの複製(&D)" };
            duplicateItem.Click += (s, ev) =>
            {
                vm.DuplicateTab(tabVM);
            };
            menu.Items.Add(duplicateItem);

            MenuItem openInNewWindowItem = new MenuItem() { Header = _window.TryFindResource("MenuOpenNewWindow") as string ?? "別ウィンドウで開く(&N)" };
            openInNewWindowItem.Click += (s, ev) =>
            {
                string path = tabVM.Path;
                if (string.IsNullOrEmpty(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _explorerService.OpenInNewWindow(path);
            };
            menu.Items.Add(openInNewWindowItem);

            if (!string.IsNullOrEmpty(tabVM.Path))
            {
                MenuItem copyPathItem = new MenuItem() { Header = _window.TryFindResource("MenuCopyPath") as string ?? "パスのコピー(&P)" };
                copyPathItem.Click += (s, ev) =>
                {
                    try { Clipboard.SetText(tabVM.Path); } catch (Exception ex) { AppLogger.LogError("TabBarWindow", "Failed to copy tab path to clipboard.", ex); }
                };
                menu.Items.Add(copyPathItem);
            }

            menu.Items.Add(new Separator());

            MenuItem closeItem = new MenuItem() { Header = _window.TryFindResource("MenuCloseTab") as string ?? "タブを閉じる(&C)" };
            closeItem.Click += (s, ev) =>
            {
                vm.CloseTab(tabVM);
            };
            menu.Items.Add(closeItem);

            int tabIndex = vm.Tabs.IndexOf(tabVM);

            MenuItem closeToRightItem = new MenuItem() { Header = _window.TryFindResource("MenuCloseTabsToRight") as string ?? "右側のタブを閉じる(&R)" };
            closeToRightItem.IsEnabled = (tabIndex >= 0 && tabIndex < vm.Tabs.Count - 1);
            closeToRightItem.Click += (s, ev) =>
            {
                vm.CloseTabsToRight(tabVM);
            };
            menu.Items.Add(closeToRightItem);

            MenuItem closeToLeftItem = new MenuItem() { Header = _window.TryFindResource("MenuCloseTabsToLeft") as string ?? "左側のタブを閉じる(&L)" };
            closeToLeftItem.IsEnabled = (tabIndex > 0);
            closeToLeftItem.Click += (s, ev) =>
            {
                vm.CloseTabsToLeft(tabVM);
            };
            menu.Items.Add(closeToLeftItem);

            menu.Items.Add(new Separator());

            MenuItem reopenItem = new MenuItem() { Header = _window.TryFindResource("MenuReopenClosedTab") as string ?? "閉じたタブを開く(&T)" };
            reopenItem.IsEnabled = vm.HasClosedTabs;
            reopenItem.Click += (s, ev) =>
            {
                vm.ReopenClosedTab();
            };
            menu.Items.Add(reopenItem);

            if (onClosed != null)
            {
                menu.Closed += (s, ev) => onClosed();
            }

            menu.PlacementTarget = tabBd;
            menu.IsOpen = true;
        }

        public void ShowBackgroundContextMenu(UIElement placementTarget, TabBarViewModel vm, Action onClosed)
        {
            _window.Activate();

            ContextMenu menu = new ContextMenu();
            ApplyFluentMenuStyle(menu);

            MenuItem reopenItem = new MenuItem() { Header = _window.TryFindResource("MenuReopenClosedTab") as string ?? "閉じたタブを開く(&T)" };
            reopenItem.IsEnabled = (vm != null && vm.HasClosedTabs);
            reopenItem.Click += (s, ev) =>
            {
                if (vm != null) vm.ReopenClosedTab();
            };
            menu.Items.Add(reopenItem);

            menu.Items.Add(new Separator());

            MenuItem settingsItem = new MenuItem() { Header = _window.TryFindResource("MenuSettings") as string ?? "設定..." };
            settingsItem.Click += (s, ev) =>
            {
                SettingsWindow w = new SettingsWindow();
                w.Owner = _window;
                w.ShowDialog();
            };
            menu.Items.Add(settingsItem);

            if (onClosed != null)
            {
                menu.Closed += (s, ev) => onClosed();
            }

            menu.PlacementTarget = placementTarget;
            menu.IsOpen = true;
        }

        public void ApplyFluentMenuStyle(ContextMenu menu)
        {
            if (menu == null) return;

            try
            {
                menu.Background = _window.TryFindResource("ThemeWindowBg") as Brush;
                menu.Foreground = _window.TryFindResource("ThemeFgNormal") as Brush;
                menu.BorderBrush = _window.TryFindResource("ThemeBorderLine") as Brush;
                menu.BorderThickness = new Thickness(1);
                menu.Padding = new Thickness(4);
                menu.MinWidth = 220;

                Style itemStyle = new Style(typeof(MenuItem));
                itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
                itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, _window.TryFindResource("ThemeFgNormal")));
                itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 6, 10, 6)));
                itemStyle.Setters.Add(new Setter(MenuItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

                Trigger itemHoverTrigger = new Trigger();
                itemHoverTrigger.Property = MenuItem.IsHighlightedProperty;
                itemHoverTrigger.Value = true;
                itemHoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, _window.TryFindResource("ThemeTabHover")));
                itemHoverTrigger.Setters.Add(new Setter(MenuItem.ForegroundProperty, _window.TryFindResource("ThemeFgNormal")));
                itemStyle.Triggers.Add(itemHoverTrigger);

                Trigger disabledTrigger = new Trigger();
                disabledTrigger.Property = MenuItem.IsEnabledProperty;
                disabledTrigger.Value = false;
                disabledTrigger.Setters.Add(new Setter(MenuItem.OpacityProperty, 0.55));
                itemStyle.Triggers.Add(disabledTrigger);

                menu.Resources[typeof(MenuItem)] = itemStyle;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("TabBarWindowContextMenuBuilder", "ApplyFluentMenuStyle failed. Falling back to default menu style.", ex);
            }
        }
    }
}
