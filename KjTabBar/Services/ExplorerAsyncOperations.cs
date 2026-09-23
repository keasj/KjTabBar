using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    internal interface IAsyncExplorerService
    {
        Task<string> GetCurrentPathAsync(IntPtr hwnd);
        Task<bool> NavigateAsync(IntPtr hwnd, string path);
        Task SelectItemsAsync(IntPtr hwnd, List<string> items);
    }

    internal static class ExplorerAsyncOperations
    {
        internal static Task<string> ReadPathAsync(this IExplorerService explorer, IntPtr hwnd)
        {
            IAsyncExplorerService asyncExplorer = explorer as IAsyncExplorerService;
            return asyncExplorer != null ? asyncExplorer.GetCurrentPathAsync(hwnd) : Task.FromResult(explorer.GetCurrentPath(hwnd));
        }

        internal static Task<bool> NavigatePathAsync(this IExplorerService explorer, IntPtr hwnd, string path)
        {
            IAsyncExplorerService asyncExplorer = explorer as IAsyncExplorerService;
            return asyncExplorer != null ? asyncExplorer.NavigateAsync(hwnd, path) : Task.FromResult(explorer.Navigate(hwnd, path));
        }

        internal static Task RestoreItemsAsync(this IExplorerService explorer, IntPtr hwnd, List<string> items)
        {
            IAsyncExplorerService asyncExplorer = explorer as IAsyncExplorerService;
            if (asyncExplorer != null) return asyncExplorer.SelectItemsAsync(hwnd, items);
            explorer.SelectItems(hwnd, items);
            return Task.CompletedTask;
        }
    }
}
