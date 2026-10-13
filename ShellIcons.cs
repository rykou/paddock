using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Paddock
{
    // Fetches the icon the shell would show for a path, at 32, 48 or 64 pixels.
    static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);

        [DllImport("comctl32.dll")]
        static extern IntPtr ImageList_GetIcon(IntPtr himl, int index, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        const uint SHGFI_SYSICONINDEX = 0x4000;
        const int SHIL_LARGE = 0, SHIL_EXTRALARGE = 2;
        const uint ILD_TRANSPARENT = 1;

        public static Bitmap Get(string path, int size)
        {
            // 64px is scaled up from the 48px list: the 256px list pads small-only icons into a corner.
            int shellList = size <= 32 ? SHIL_LARGE : SHIL_EXTRALARGE;
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_SYSICONINDEX) != IntPtr.Zero)
                {
                    var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                    if (SHGetImageList(shellList, ref iid, out IntPtr himl) == 0)
                    {
                        IntPtr hIcon = ImageList_GetIcon(himl, info.iIcon, ILD_TRANSPARENT);
                        if (hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                using (var icon = Icon.FromHandle(hIcon))
                                    return Scale(icon.ToBitmap(), size);
                            }
                            finally { DestroyIcon(hIcon); }
                        }
                    }
                }
            }
            catch { }
            return Scale(SystemIcons.Application.ToBitmap(), size);
        }

        static Bitmap Scale(Bitmap source, int size)
        {
            if (source.Width == size && source.Height == size) return source;
            var scaled = new Bitmap(size, size);
            using (source)
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, size, size);
            }
            return scaled;
        }
    }
}
