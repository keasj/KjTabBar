using System;
using System.Windows;
using KjTabBar.Helpers;
using KjTabBar.ViewModels;

namespace KjTabBar.Views
{
    internal static class TabExternalDragOpenDecider
    {
        public static bool TryOpenInNewWindowAndCloseSourceTab(
            DragDropEffects dragEffect,
            TabItemViewModel tab,
            TabBarViewModel viewModel,
            Func<string, bool> openInNewWindow,
            NativeMethods.POINT cursorScreenPoint,
            NativeMethods.RECT windowScreenRect)
        {
            if (tab == null || viewModel == null || openInNewWindow == null)
            {
                return false;
            }

            if (!ShouldOpenInNewWindow(dragEffect, tab.Path, cursorScreenPoint, windowScreenRect))
            {
                return false;
            }

            if (!openInNewWindow(tab.Path))
            {
                return false;
            }

            viewModel.CloseTab(tab);
            return true;
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
