using System;
using System.Collections.Generic;

namespace KjTabBar.Models
{
    internal sealed class ShellItemSelector
    {
        private readonly ShellWindowComInterop _comInterop;

        public ShellItemSelector(ShellWindowComInterop comInterop)
        {
            _comInterop = comInterop ?? throw new ArgumentNullException(nameof(comInterop));
        }

        public void SelectItems(IntPtr explorerHwnd, List<string> itemPaths)
        {
            _comInterop.SelectItems(explorerHwnd, itemPaths);
        }
    }
}
