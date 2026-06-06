using System;
using System.Runtime.InteropServices;
using KjTabBar.Helpers;

namespace KjTabBar.Models
{
    internal sealed class ShellWindowNavigator
    {
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly Action<object, string, object[]> _invokeNavigate2;
        private readonly Func<string, Tuple<int, IntPtr>> _parseDisplayName;
        private readonly Func<IntPtr, uint> _getPidlSize;
        private readonly Action<IntPtr> _freePidl;

        public ShellWindowNavigator(
            Func<object, string, object[], object> invokeComMethod,
            Action<object, string, object[]> invokeNavigate2)
            : this(
                  invokeComMethod,
                  invokeNavigate2,
                  ParseDisplayName,
                  NativeMethods.ILGetSize,
                  NativeMethods.ILFree)
        {
        }

        internal ShellWindowNavigator(
            Func<object, string, object[], object> invokeComMethod,
            Action<object, string, object[]> invokeNavigate2,
            Func<string, Tuple<int, IntPtr>> parseDisplayName,
            Func<IntPtr, uint> getPidlSize,
            Action<IntPtr> freePidl)
        {
            _invokeComMethod = invokeComMethod;
            _invokeNavigate2 = invokeNavigate2;
            _parseDisplayName = parseDisplayName;
            _getPidlSize = getPidlSize;
            _freePidl = freePidl;
        }

        public void Navigate(object window, string navigatePath)
        {
            if (window == null || string.IsNullOrEmpty(navigatePath))
            {
                return;
            }

            if (!navigatePath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                _invokeComMethod(window, "Navigate", new object[] { navigatePath });
                return;
            }

            Tuple<int, IntPtr> parseResult = _parseDisplayName(navigatePath);
            IntPtr pidl = parseResult != null ? parseResult.Item2 : IntPtr.Zero;
            if (parseResult != null && parseResult.Item1 == 0 && pidl != IntPtr.Zero)
            {
                try
                {
                    uint size = _getPidlSize(pidl);
                    byte[] pidlBytes = new byte[size];
                    Marshal.Copy(pidl, pidlBytes, 0, (int)size);
                    object url = pidlBytes;
                    object flags = 0;
                    object targetFrame = null;
                    _invokeNavigate2(window, "Navigate2", new object[] { url, flags, targetFrame });
                }
                finally
                {
                    _freePidl(pidl);
                }

                return;
            }

            _invokeComMethod(window, "Navigate", new object[] { navigatePath });
        }

        private static Tuple<int, IntPtr> ParseDisplayName(string path)
        {
            IntPtr pidl;
            uint dummyOut;
            int hr = NativeMethods.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out dummyOut);
            return Tuple.Create(hr, pidl);
        }
    }
}
