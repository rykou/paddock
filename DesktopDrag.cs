using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Accessibility;

namespace Paddock
{
    // Watches the empty desktop for two gestures: a left-button drag offers "Create fence here" on release,
    // and a double-click toggles every fence.
    // The mouse hook only observes; every event is passed on untouched.
    class DesktopDrag : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData, flags, time;
            public IntPtr extraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromPoint(POINT pt, [MarshalAs(UnmanagedType.Interface)] out IAccessible acc, [MarshalAs(UnmanagedType.Struct)] out object child);

        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
        const uint GA_ROOT = 2;
        const int ROLE_SYSTEM_LISTITEM = 34;
        const int MinWidth = 80, MinHeight = 60;

        readonly Action<Rectangle> createFence;
        readonly Action toggleFences;
        readonly Control sync = new Control();
        readonly HookProc proc; // held so the delegate outlives the native hook
        readonly IntPtr hook;

        bool tracking, startedOnIcon;
        POINT start;
        uint lastDownTime;
        POINT lastDown;
        CreatePopup popup;

        public DesktopDrag(Action<Rectangle> createFence, Action toggleFences)
        {
            this.createFence = createFence;
            this.toggleFences = toggleFences;
            IntPtr unused = sync.Handle;
            proc = OnMouse;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            ClosePopup();
            sync.Dispose();
        }

        // Runs for every mouse event in the session, so it only records state and defers the real work.
        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    POINT pt = info.pt;
                    if (popup != null && !popup.Bounds.Contains(pt.X, pt.Y)) sync.BeginInvoke((Action)ClosePopup);
                    tracking = msg == WM_LBUTTONDOWN && IsDesktop(pt);
                    if (tracking)
                    {
                        start = pt;
                        startedOnIcon = false;
                        sync.BeginInvoke((Action)(() => startedOnIcon = IsIcon(pt)));

                        Size slop = SystemInformation.DoubleClickSize;
                        bool second = lastDownTime != 0 && info.time - lastDownTime <= (uint)SystemInformation.DoubleClickTime
                            && Math.Abs(pt.X - lastDown.X) <= slop.Width && Math.Abs(pt.Y - lastDown.Y) <= slop.Height;
                        lastDownTime = second ? 0 : info.time;
                        lastDown = pt;
                        // Queued after the icon check above, so a double-click that opens an icon is left alone.
                        if (second) sync.BeginInvoke((Action)(() => { if (!startedOnIcon) toggleFences(); }));
                    }
                    else lastDownTime = 0;
                }
                else if (msg == WM_LBUTTONUP && tracking)
                {
                    tracking = false;
                    POINT end = ((MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))).pt;
                    var rect = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
                    if (rect.Width >= MinWidth && rect.Height >= MinHeight)
                        sync.BeginInvoke((Action)(() => { if (!startedOnIcon) ShowPopup(rect, new Point(end.X, end.Y)); }));
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        public static bool IsDesktopAt(Point p) => IsDesktop(new POINT { X = p.X, Y = p.Y });

        static bool IsDesktop(POINT pt)
        {
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return false;
            string root = ClassOf(GetAncestor(hwnd, GA_ROOT));
            if (root != "Progman" && root != "WorkerW") return false;
            string cls = ClassOf(hwnd);
            return cls == "SysListView32" || cls == "SHELLDLL_DefView" || cls == root;
        }

        // A drag that starts on a desktop icon moves the icon; it is not a rubber-band selection.
        static bool IsIcon(POINT pt)
        {
            try
            {
                if (AccessibleObjectFromPoint(pt, out IAccessible acc, out object child) != 0 || acc == null) return false;
                return acc.get_accRole(child) is int role && role == ROLE_SYSTEM_LISTITEM;
            }
            catch { return false; }
        }

        void ShowPopup(Rectangle rect, Point at)
        {
            ClosePopup();
            popup = new CreatePopup(() => { ClosePopup(); createFence(rect); });
            var area = Screen.FromPoint(at).WorkingArea;
            popup.Location = new Point(Math.Min(at.X + 4, area.Right - popup.Width), Math.Min(at.Y + 4, area.Bottom - popup.Height));
            popup.FormClosed += (s, e) => { if (popup == s) popup = null; };
            popup.Show();
        }

        void ClosePopup()
        {
            var p = popup;
            popup = null;
            if (p != null && !p.IsDisposed) p.Close();
        }

        // Small always-on-top prompt that never takes focus from the desktop.
        class CreatePopup : Form
        {
            readonly Timer timeout = new Timer { Interval = 6000 };

            public CreatePopup(Action onClick)
            {
                var label = new Label
                {
                    Text = "Create fence here",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = SystemFonts.MenuFont,
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand,
                };
                Color normal = Color.FromArgb(43, 43, 43), hover = Color.FromArgb(0, 99, 177);
                label.BackColor = normal;
                label.MouseEnter += (s, e) => label.BackColor = hover;
                label.MouseLeave += (s, e) => label.BackColor = normal;
                label.Click += (s, e) => onClick();

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(90, 90, 90);
                Padding = new Padding(1);
                Size textSize = TextRenderer.MeasureText(label.Text, label.Font);
                Size = new Size(textSize.Width + 36, textSize.Height + 18);
                Controls.Add(label);

                timeout.Tick += (s, e) => Close();
                timeout.Start();
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x80 | 0x8; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                    return cp;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) timeout.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
