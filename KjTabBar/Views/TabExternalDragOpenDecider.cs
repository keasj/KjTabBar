using System;
using System.Windows;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    internal enum TabDetachResult
    {
        NotOpened,
        WindowOpenedSourceRetained,
        ClosePending,
        Completed
    }

    internal static class TabExternalDragOpenDecider
    {
        public static bool TryOpenInNewWindowAndCloseSourceTab(
            DragDropEffects dragEffect,
            TabItemViewModel tab,
            string draggedPath,
            TabBarViewModel viewModel,
            Func<string, bool> openInNewWindow,
            NativeMethods.POINT cursorScreenPoint,
            NativeMethods.RECT windowScreenRect)
        {
            if (tab == null || viewModel == null || openInNewWindow == null)
            {
                return false;
            }

            if (!ShouldOpenInNewWindow(dragEffect, draggedPath, cursorScreenPoint, windowScreenRect))
            {
                return false;
            }

            if (viewModel.IsTabOperationPending || !viewModel.CanCloseTab(tab)) return false;

            if (!openInNewWindow(draggedPath))
            {
                return false;
            }

            viewModel.CloseTab(tab);
            return !viewModel.Tabs.Contains(tab);
        }

        internal static async Task<TabDetachResult> TryOpenInNewWindowAndCloseSourceTabAsync(
            DragDropEffects dragEffect, TabItemViewModel tab, string draggedPath,
            TabBarViewModel viewModel, Func<string, bool> openInNewWindow,
            NativeMethods.POINT cursorScreenPoint, NativeMethods.RECT windowScreenRect,
            Func<string, Task<bool>> preparePath, Action completePendingReveal)
        {
            if (tab == null || viewModel == null || openInNewWindow == null ||
                viewModel.IsTabOperationPending || !viewModel.CanCloseTab(tab) ||
                !ShouldOpenInNewWindow(dragEffect, draggedPath, cursorScreenPoint, windowScreenRect)) return TabDetachResult.NotOpened;
            // Opening can fail without changing the managed host. Never prepare a
            // different host until the independent window has been launched.
            if (!openInNewWindow(draggedPath)) return TabDetachResult.NotOpened;
            await viewModel.CloseTabsAsync(viewModel.Tabs.IndexOf(tab), 1, preparePath, completePendingReveal);
            if (viewModel.Tabs.Contains(tab)) return TabDetachResult.WindowOpenedSourceRetained;
            // Navigation acceptance is not completion; a timeout may still restore the source.
            return viewModel.NavigationTracker.NavigatingToPath != null
                ? TabDetachResult.ClosePending : TabDetachResult.Completed;
        }

        internal static async Task<TabDetachResult> CompletePendingCloseAsync(TabBarViewModel viewModel, TabItemViewModel source)
        {
            while (!viewModel.IsDisposed && viewModel.NavigationTracker.NavigatingToPath != null)
            {
                await viewModel.SyncWithExplorerAsync();
                if (viewModel.NavigationTracker.NavigatingToPath != null) await Task.Delay(100);
            }
            return viewModel.IsDisposed || viewModel.Tabs.Contains(source)
                ? TabDetachResult.WindowOpenedSourceRetained : TabDetachResult.Completed;
        }

        public static bool ShouldOpenInNewWindow(
            DragDropEffects dragEffect,
            string path,
            NativeMethods.POINT cursorScreenPoint,
            NativeMethods.RECT windowScreenRect)
        {
            if (dragEffect != DragDropEffects.None || string.IsNullOrEmpty(path))
            {
                return false;
            }

            return cursorScreenPoint.X < windowScreenRect.Left ||
                   cursorScreenPoint.X >= windowScreenRect.Right ||
                   cursorScreenPoint.Y < windowScreenRect.Top ||
                   cursorScreenPoint.Y >= windowScreenRect.Bottom;
        }
    }
}
