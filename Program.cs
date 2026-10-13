using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace Paddock
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var mutex = new Mutex(true, "Paddock.SingleInstance" + (Store.Home == null ? "" : "." + Store.Home.ToLowerInvariant().GetHashCode()), out bool first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Paddock";

        readonly Store store;
        readonly List<FenceForm> forms = new List<FenceForm>();
        readonly NotifyIcon tray;
        readonly DesktopDrag desktopDrag;
        bool hidden;
        readonly Timer revealTimer = new Timer { Interval = 100 };
        readonly Timer layerTimer = new Timer { Interval = 250 };

        public TrayContext()
        {
            store = Store.Load();
            if (store == null)
            {
                store = new Store();
                store.Fences.Add(new FenceData());
            }

            var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows() };
            startup.Click += (s, e) => { SetStartWithWindows(!startup.Checked); startup.Checked = StartsWithWindows(); };

            var quickHide = new ToolStripMenuItem("Double-click desktop to hide fences") { Checked = store.QuickHide };
            quickHide.Click += (s, e) => { store.QuickHide = !store.QuickHide; quickHide.Checked = store.QuickHide; Save(); };
            var showHide = new ToolStripMenuItem("Hide fences");
            showHide.Click += (s, e) => SetHidden(!hidden);

            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) => showHide.Text = hidden ? "Show fences" : "Hide fences";
            menu.Items.Add("New fence", null, (s, e) => NewFence());
            menu.Items.Add(showHide);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quickHide);
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());

            tray = new NotifyIcon
            {
                Icon = new Icon(typeof(Program).Assembly.GetManifestResourceStream("paddock.ico"), SystemInformation.SmallIconSize),
                Text = "Paddock",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => NewFence();

            revealTimer.Tick += (s, e) => RevealUnderPointer();
            layerTimer.Tick += (s, e) => KeepOnDesktop();
            layerTimer.Start();
            foreach (var data in store.Fences) Show(data);
            desktopDrag = new DesktopDrag(NewFenceAt, () => { if (store.QuickHide) SetHidden(!hidden); });
        }

        void SetHidden(bool hide)
        {
            hidden = hide;
            foreach (var form in forms) form.FadeTo(!hide);
            revealTimer.Enabled = hide;
        }

        // In hidden mode a fence fades back in while the pointer is over the spot it occupies, then fades out again.
        void RevealUnderPointer()
        {
            Point p = Cursor.Position;
            bool onDesktop = DesktopDrag.IsDesktopAt(p);
            foreach (var form in forms)
            {
                bool over = form.Bounds.Contains(p) && (onDesktop || form.OwnsWindowAt(p));
                if (over) form.FadeTo(true);
                else if (!form.Busy && Control.MouseButtons == MouseButtons.None) form.FadeTo(false);
            }
        }

        // Win+D raises the desktop above every app and lowers it again afterwards; follow it in both directions.
        void KeepOnDesktop()
        {
            var handles = new HashSet<IntPtr>(forms.Where(f => f.IsHandleCreated).Select(f => f.Handle));
            foreach (var form in forms)
                if (form.Visible && !DesktopLayer.SitsOnDesktop(form.Handle, handles)) form.Resettle();
        }

        // While a collapsed fence is peeking open, the fences it lands on are hidden so they cannot show through it.
        public List<FenceForm> HideOverlapping(FenceForm top)
        {
            var covered = forms.Where(f => f != top && f.Visible && f.Bounds.IntersectsWith(top.ExpandedBounds)).ToList();
            foreach (var form in covered) form.FadeTo(false);
            return covered;
        }

        public void Restore(List<FenceForm> covered)
        {
            if (hidden) return;
            foreach (var form in covered)
                if (forms.Contains(form)) form.FadeTo(true);
        }

        public void ApplyStyleToAll(FenceData source)
        {
            foreach (var form in forms)
            {
                form.Data.Argb = source.Argb;
                form.Data.Opacity = source.Opacity;
                form.Data.Rounded = source.Rounded;
                form.Data.IconSize = source.IconSize;
                form.Data.TitleAlign = source.TitleAlign;
                form.RefreshStyle();
            }
            Save();
        }

        void Show(FenceData data)
        {
            if (hidden) SetHidden(false); // a new fence should never appear alone among hidden ones
            var form = new FenceForm(this, data);
            forms.Add(form);
            form.Show();
        }

        public void NewFence()
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            var data = new FenceData();
            data.X = area.Left + (area.Width - data.W) / 2 + 24 * (store.Fences.Count % 8);
            data.Y = area.Top + (area.Height - data.H) / 2 + 24 * (store.Fences.Count % 8);
            store.Fences.Add(data);
            Save();
            Show(data);
        }

        public void NewFenceAt(Rectangle bounds)
        {
            var data = new FenceData { X = bounds.X, Y = bounds.Y, W = bounds.Width, H = bounds.Height };
            store.Fences.Add(data);
            Save();
            Show(data);
        }

        public void Delete(FenceForm form)
        {
            store.Fences.Remove(form.Data);
            forms.Remove(form);
            form.Close();
            Save();
        }

        public void Save()
        {
            try { store.Save(); }
            catch (Exception ex) { tray.ShowBalloonTip(4000, "Paddock", "Could not save fences: " + ex.Message, ToolTipIcon.Warning); }
        }

        static bool StartsWithWindows()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key?.GetValue(RunName) != null;
        }

        static void SetStartWithWindows(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunName, false);
            }
        }

        protected override void ExitThreadCore()
        {
            desktopDrag.Dispose();
            revealTimer.Dispose();
            layerTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
