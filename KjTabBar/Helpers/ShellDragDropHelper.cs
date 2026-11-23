using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using OleDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using KjTabBar.Models;

namespace KjTabBar.Helpers
{
    internal static class ShellDragDropHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
            ref Guid iid, out NativeMethods.IShellItem item);

        [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDropTarget
        {
            void DragEnter(OleDataObject data, DragDropKeyStates keys, NativeMethods.POINT point, ref DragDropEffects effect);
            void DragOver(DragDropKeyStates keys, NativeMethods.POINT point, ref DragDropEffects effect);
            void DragLeave();
            [PreserveSig] int Drop(OleDataObject data, DragDropKeyStates keys, NativeMethods.POINT point, ref DragDropEffects effect);
        }

        internal static DragDropEffects DropWithShellMenu(string destination, IDataObject data,
            DragDropKeyStates keys, NativeMethods.POINT point, DragDropEffects allowed)
        {
            OleDataObject source = data as OleDataObject;
            allowed &= DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
            if (source == null || allowed == DragDropEffects.None) return DragDropEffects.None;

            Guid itemId = typeof(NativeMethods.IShellItem).GUID;
            Guid handlerId = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5"); // BHID_SFUIObject
            Guid targetId = typeof(IDropTarget).GUID;
            NativeMethods.IShellItem item = null;
            IntPtr pointer = IntPtr.Zero;
            IDropTarget target = null;
            try
            {
                SHCreateItemFromParsingName(destination, IntPtr.Zero, ref itemId, out item);
                Marshal.ThrowExceptionForHR(item.BindToHandler(IntPtr.Zero, ref handlerId, ref targetId, out pointer));
                target = (IDropTarget)Marshal.GetObjectForIUnknown(pointer);

                // WPF receives Drop after the button is released. Tell the Shell how the drag began.
                DragDropEffects effect = allowed;
                target.DragEnter(source, keys | DragDropKeyStates.RightMouseButton, point, ref effect);
                if (effect == DragDropEffects.None)
                {
                    target.DragLeave();
                    return DragDropEffects.None;
                }
                effect = allowed;
                int result = target.Drop(source, keys, point, ref effect);
                Marshal.ThrowExceptionForHR(result);
                return result == 0 ? effect : DragDropEffects.None;
            }
            finally
            {
                if (target != null) Marshal.ReleaseComObject(target);
                if (pointer != IntPtr.Zero) Marshal.Release(pointer);
                if (item != null) Marshal.ReleaseComObject(item);
            }
        }

        /// <summary>
        /// CIDA (Shell IDList Array) 構造体を解析してパスを取得する
        /// CIDA: [cidl:uint] [aoffset[0]:uint (親PIDL)] [aoffset[1..n]:uint (子PIDL)]
        /// </summary>
        public static string[] ParseCIDA(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return null;

            uint cidl = BitConverter.ToUInt32(bytes, 0);
            if (cidl == 0) return null;

            uint maxCidl = (uint)((bytes.Length - 4) / 4 - 1);
            if (cidl > maxCidl) return null;

            // cidl + 1 個のオフセットが必要 (親1つ + 子cidl個)
            int headerSize = 4 + ((int)cidl + 1) * 4;
            if (bytes.Length < headerSize) return null;

            uint parentOffset = BitConverter.ToUInt32(bytes, 4);
            int parentPidlSize;
            if (parentOffset < headerSize || !TryGetPidlSizeWithinCidaBuffer(bytes, parentOffset, out parentPidlSize)) return null;

            List<string> paths = new List<string>();
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr pData = handle.AddrOfPinnedObject();
                IntPtr parentPidl = IntPtr.Add(pData, (int)parentOffset);

                for (uint i = 0; i < cidl; i++)
                {
                    uint childOffset = BitConverter.ToUInt32(bytes, (int)(8 + i * 4));
                    int childPidlSize;
                    if (childOffset < headerSize || !TryGetPidlSizeWithinCidaBuffer(bytes, childOffset, out childPidlSize))
                    {
                        continue;
                    }

                    IntPtr childPidl = IntPtr.Add(pData, (int)childOffset);

                    // 親PIDLと子PIDLを結合して絶対PIDLを作成
                    IntPtr absolutePidl = NativeMethods.ILCombine(parentPidl, childPidl);
                    if (absolutePidl != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr pName;
                            int hr = NativeMethods.SHGetNameFromIDList(absolutePidl, NativeMethods.SIGDN.DESKTOPABSOLUTEPARSING, out pName);
                            if (hr == 0 && pName != IntPtr.Zero)
                            {
                                string path = Marshal.PtrToStringAuto(pName);
                                Marshal.FreeCoTaskMem(pName);
                                if (!string.IsNullOrEmpty(path))
                                {
                                    paths.Add(path.TrimEnd('\0'));
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.ILFree(absolutePidl);
                        }
                    }
                }
            }
            finally
            {
                handle.Free();
            }

            return paths.Count > 0 ? paths.ToArray() : null;
        }

        internal static bool TryGetPidlSizeWithinCidaBuffer(byte[] bytes, uint offset, out int size)
        {
            size = 0;
            if (bytes == null || offset > int.MaxValue || offset >= bytes.Length)
            {
                return false;
            }

            int startOffset = (int)offset;
            int currentOffset = startOffset;
            while (true)
            {
                int remainingBytes = bytes.Length - currentOffset;
                if (remainingBytes < 2)
                {
                    return false;
                }

                ushort itemSize = BitConverter.ToUInt16(bytes, currentOffset);
                if (itemSize == 0)
                {
                    size = currentOffset - startOffset + 2;
                    return size <= bytes.Length - startOffset;
                }

                if (itemSize < 2 || itemSize > remainingBytes)
                {
                    return false;
                }

                currentOffset += itemSize;
            }
        }
    }
}
