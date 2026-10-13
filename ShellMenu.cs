using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Paddock
{
    // The full Windows right-click menu for files (Open with, Send to, Properties, items other apps add),
    // plus a few of Paddock's own entries at the end.
    static class ShellMenu
    {
        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenum);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
        }

        [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
        }

        [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
            [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
            [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd, lpVerb, lpParameters, lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon, lpTitle, lpVerbW, lpParametersW, lpDirectoryW, lpTitleW;
            public POINT ptInvoke;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll")]
        static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv, out IntPtr ppidlLast);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hmenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr hmenu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        const uint CMF_NORMAL = 0, CMF_EXTENDEDVERBS = 0x100;
        const uint MF_STRING = 0, MF_SEPARATOR = 0x800;
        const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
        const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
        const int WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_INITMENUPOPUP = 0x117, WM_MENUCHAR = 0x120;
        const int FirstShellId = 100;

        // Submenus such as "Open with" and "Send to" are filled in lazily through messages to the owner window.
        static IContextMenu2 active2;
        static IContextMenu3 active3;

        // Called first from the owner's WndProc; returns true when the shell menu consumed the message.
        public static bool HandleMessage(ref Message m)
        {
            if (active2 == null && active3 == null) return false;
            int msg = m.Msg;
            if (msg != WM_INITMENUPOPUP && msg != WM_DRAWITEM && msg != WM_MEASUREITEM && msg != WM_MENUCHAR) return false;
            if (active3 != null)
            {
                if (active3.HandleMenuMsg2((uint)msg, m.WParam, m.LParam, out IntPtr result) != 0) return false;
                m.Result = result;
                return true;
            }
            if (msg == WM_MENUCHAR || active2.HandleMenuMsg((uint)msg, m.WParam, m.LParam) != 0) return false;
            m.Result = msg == WM_INITMENUPOPUP ? IntPtr.Zero : (IntPtr)1;
            return true;
        }

        // Shows the menu for files that share one parent folder. Paddock's own entries use ids below 100.
        public static void Show(Control owner, string[] paths, Point at, IList<KeyValuePair<int, string>> extras, Action<int> onExtra)
        {
            var absolute = new List<IntPtr>();
            IShellFolder parent = null;
            object menuObject = null;
            IntPtr hmenu = IntPtr.Zero;
            try
            {
                var children = new IntPtr[paths.Length];
                var iidFolder = typeof(IShellFolder).GUID;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (SHParseDisplayName(paths[i], IntPtr.Zero, out IntPtr pidl, 0, out uint unused) != 0) return;
                    absolute.Add(pidl);
                    if (SHBindToParent(pidl, ref iidFolder, out IShellFolder folder, out children[i]) != 0) return;
                    if (parent == null) parent = folder;
                    else Marshal.ReleaseComObject(folder);
                }

                var iidMenu = typeof(IContextMenu).GUID;
                if (parent.GetUIObjectOf(owner.Handle, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out menuObject) != 0) return;
                var menu = (IContextMenu)menuObject;

                hmenu = CreatePopupMenu();
                uint flags = CMF_NORMAL | ((Control.ModifierKeys & Keys.Shift) != 0 ? CMF_EXTENDEDVERBS : 0);
                if (menu.QueryContextMenu(hmenu, 0, FirstShellId, 0x7FFF, flags) < 0) return;
                if (extras.Count > 0)
                {
                    AppendMenu(hmenu, MF_SEPARATOR, UIntPtr.Zero, null);
                    foreach (var extra in extras) AppendMenu(hmenu, MF_STRING, (UIntPtr)(uint)extra.Key, extra.Value);
                }

                active2 = menuObject as IContextMenu2;
                active3 = menuObject as IContextMenu3;
                SetForegroundWindow(owner.Handle);
                int cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, at.X, at.Y, owner.Handle, IntPtr.Zero);
                active2 = null;
                active3 = null;

                if (cmd <= 0) return;
                if (cmd < FirstShellId) { onExtra(cmd); return; }
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX)),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = owner.Handle,
                    lpVerb = (IntPtr)(cmd - FirstShellId),
                    lpVerbW = (IntPtr)(cmd - FirstShellId),
                    nShow = 1, // SW_SHOWNORMAL
                    ptInvoke = new POINT { X = at.X, Y = at.Y },
                };
                menu.InvokeCommand(ref info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.Message, "Paddock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                active2 = null;
                active3 = null;
                if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
                if (menuObject != null) Marshal.ReleaseComObject(menuObject);
                if (parent != null) Marshal.ReleaseComObject(parent);
                foreach (IntPtr pidl in absolute) Marshal.FreeCoTaskMem(pidl);
            }
        }
    }
}
