using System;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellLinkCreator
    {
        private readonly ShellShortcutManager _shellShortcutManager;

        public ShellLinkCreator(ShellShortcutManager shellShortcutManager)
        {
            _shellShortcutManager = shellShortcutManager ?? throw new ArgumentNullException(nameof(shellShortcutManager));
        }

        public string ResolveShortcutTarget(string shortcutPath)
        {
            return _shellShortcutManager.ResolveShortcutTarget(shortcutPath);
        }

        public void CreateShortcuts(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            _shellShortcutManager.CreateShortcuts(sourcePaths, destinationDirectory, ownerHwnd);
        }

        public void CreateSymbolicLinks(string[] sourcePaths, string destinationDirectory, IntPtr ownerHwnd)
        {
            _shellShortcutManager.CreateSymbolicLinks(sourcePaths, destinationDirectory, ownerHwnd);
        }
    }
}
