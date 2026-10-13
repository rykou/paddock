using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using VB = Microsoft.VisualBasic.FileIO;

namespace Paddock
{
    // One fence: a borderless window pinned to the bottom of the z-order, showing the contents of a real folder.
    // Dropping a file on it moves the file into that folder.
    class FenceForm : Form
    {
        public readonly FenceData Data;
        readonly TrayContext owner;
        readonly Panel header = new Panel();
        readonly Label title = new Label();
        readonly Label chevron = new Label();
        readonly ListView list = new ListView();
        readonly ImageList icons = new ImageList();
        readonly Timer hoverTimer = new Timer { Interval = 150 };
        readonly Timer reloadTimer = new Timer { Interval = 250 };
        readonly Timer fadeTimer = new Timer { Interval = 15 };
        bool shown = true, fadingIn;
        readonly Timer slideTimer = new Timer { Interval = 15 };
        readonly Stopwatch slideClock = new Stopwatch();
        int slideFrom, slideTo;
        const int SlideMs = 180;
        FileSystemWatcher watcher;
        readonly int grip, minWidth, minHeight, collapsedHeight;

        // A collapsed fence opens while the pointer is over it ("peek") and closes when it leaves.
        bool peeking, suppressPeek, menuOpen;
        int hoverTicks;
        TextBox renameBox;
        List<FenceForm> covered;

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point pt);
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int WM_WINDOWPOSCHANGING = 0x46, WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1;
        const int HTCLIENT = 1, HTCAPTION = 2;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2;

        static readonly KeyValuePair<string, Color>[] Presets =
        {
            new KeyValuePair<string, Color>("Graphite", Color.FromArgb(32, 42, 58)),
            new KeyValuePair<string, Color>("Midnight", Color.FromArgb(18, 18, 24)),
            new KeyValuePair<string, Color>("Slate", Color.FromArgb(71, 85, 105)),
            new KeyValuePair<string, Color>("Ocean", Color.FromArgb(14, 116, 144)),
            new KeyValuePair<string, Color>("Forest", Color.FromArgb(22, 101, 52)),
            new KeyValuePair<string, Color>("Plum", Color.FromArgb(107, 33, 168)),
            new KeyValuePair<string, Color>("Rose", Color.FromArgb(190, 24, 93)),
            new KeyValuePair<string, Color>("Amber", Color.FromArgb(217, 119, 6)),
            new KeyValuePair<string, Color>("Snow", Color.FromArgb(241, 245, 249)),
        };

        public FenceForm(TrayContext owner, FenceData data)
        {
            this.owner = owner;
            Data = data;
            float scale = DeviceDpi / 96f;
            grip = (int)(5 * scale);
            minWidth = (int)(140 * scale);
            minHeight = (int)(90 * scale);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(minWidth, minHeight);
            Padding = new Padding(grip);
            Bounds = OnScreen(new Rectangle(data.X, data.Y, data.W, data.H));

            icons.ColorDepth = ColorDepth.Depth32Bit;

            list.Dock = DockStyle.Fill;
            list.View = View.LargeIcon;
            list.BorderStyle = BorderStyle.None;
            list.LargeImageList = icons;
            list.AllowDrop = true;
            list.ShowItemToolTips = true;
            list.ItemActivate += (s, e) => OpenSelected();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RecycleSelected(); };
            list.ItemDrag += OnItemDrag;

            header.Dock = DockStyle.Top;
            header.Height = (int)(26 * scale);
            collapsedHeight = header.Height + 2 * grip;

            title.Dock = DockStyle.Fill;
            title.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            title.AutoEllipsis = true;
            title.AllowDrop = true;
            title.MouseDown += TitleMouseDown;

            chevron.Dock = DockStyle.Right;
            chevron.Width = header.Height;
            chevron.TextAlign = ContentAlignment.MiddleCenter;
            chevron.Cursor = Cursors.Hand;
            chevron.Click += (s, e) => ToggleCollapse();

            header.Controls.Add(title);
            header.Controls.Add(chevron);
            Controls.Add(list);
            Controls.Add(header);

            foreach (Control c in new Control[] { list, title })
            {
                c.DragEnter += OnDragEnterFiles;
                c.DragDrop += OnDropFiles;
            }

            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) =>
            {
                // Right-clicking an item gets the full Windows menu; empty space and the title get the fence menu.
                if (menu.SourceControl == list)
                {
                    var item = list.HitTest(list.PointToClient(Cursor.Position)).Item;
                    if (item != null)
                    {
                        e.Cancel = true;
                        if (!item.Selected) { list.SelectedItems.Clear(); item.Selected = true; }
                        Point at = Cursor.Position;
                        BeginInvoke((Action)(() => ShowShellMenu(at)));
                        return;
                    }
                }
                BuildMenu(menu);
            };
            menu.Opened += (s, e) => menuOpen = true;
            menu.Closed += (s, e) => menuOpen = false;
            title.ContextMenuStrip = menu;
            chevron.ContextMenuStrip = menu;
            list.ContextMenuStrip = menu;

            ResizeEnd += (s, e) => SaveBounds();
            hoverTimer.Tick += (s, e) => HoverTick();
            fadeTimer.Tick += (s, e) => FadeTick();
            slideTimer.Tick += (s, e) => SlideTick();

            title.Text = data.Title;
            EnsureFolder();
            MigrateLegacyItems();
            RefreshStyle();
            ApplyCollapse(false);
            StartWatching();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: keeps the fence out of Alt+Tab
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCorners();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { watcher?.Dispose(); reloadTimer.Dispose(); fadeTimer.Dispose(); slideTimer.Dispose(); hoverTimer.Dispose(); icons.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (ShellMenu.HandleMessage(ref m)) return;

            if (m.Msg == WM_WINDOWPOSCHANGING)
            {
                // Whatever is asked, sit directly above the desktop: under the apps normally, over them after Win+D.
                var pos = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                IntPtr slot = DesktopLayer.SlotAbove();
                if (slot == Handle) pos.flags |= SWP_NOZORDER; // already there
                else
                {
                    pos.hwndInsertAfter = slot;
                    pos.flags &= ~SWP_NOZORDER;
                }
                Marshal.StructureToPtr(pos, m.LParam, false);
            }

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT && !Data.Locked)
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int col = p.X < grip ? 0 : p.X >= ClientSize.Width - grip ? 2 : 1;
                int row = !Expanded ? 1 : p.Y < grip ? 0 : p.Y >= ClientSize.Height - grip ? 2 : 1;
                // HTTOPLEFT, HTTOP, HTTOPRIGHT / HTLEFT, -, HTRIGHT / HTBOTTOMLEFT, HTBOTTOM, HTBOTTOMRIGHT
                int[,] codes = { { 13, 12, 14 }, { 10, HTCLIENT, 11 }, { 16, 15, 17 } };
                m.Result = (IntPtr)codes[row, col];
            }
        }

        static Rectangle OnScreen(Rectangle r)
        {
            // A fence saved on a monitor that is no longer attached would otherwise be unreachable.
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) return r;
            var area = Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(area.Left + 40, area.Top + 40, r.Width, r.Height);
        }

        void SaveBounds()
        {
            Data.X = Left; Data.Y = Top; Data.W = Width;
            if (Expanded && !slideTimer.Enabled) Data.H = Height;
            owner.Save();
        }

        // ---- collapse --------------------------------------------------------------------------

        bool Expanded => !Data.Collapsed || peeking;

        int ExpandedHeight => Math.Max(Data.H, minHeight);

        // Where the fence will be once fully open, which is not yet its Bounds while it is still sliding.
        public Rectangle ExpandedBounds => new Rectangle(Left, Top, Width, ExpandedHeight);

        void ApplyCollapse(bool animate = true)
        {
            bool expanded = Expanded;
            int target = expanded ? ExpandedHeight : collapsedHeight;
            chevron.Text = Data.Collapsed ? "▾" : "▴";
            hoverTimer.Enabled = Data.Collapsed;
            if (!animate || !Visible || Height == target)
            {
                slideTimer.Stop();
                FinishSlide(target, expanded);
                return;
            }
            MinimumSize = new Size(minWidth, collapsedHeight);
            if (expanded) list.Visible = true;
            slideFrom = Height;
            slideTo = target;
            slideClock.Restart();
            slideTimer.Start();
        }

        // Slides the bottom edge to its new height, fast at first and settling at the end.
        void SlideTick()
        {
            double t = Math.Min(1.0, slideClock.ElapsedMilliseconds / (double)SlideMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            Height = slideFrom + (int)Math.Round((slideTo - slideFrom) * eased);
            if (t >= 1)
            {
                slideTimer.Stop();
                FinishSlide(slideTo, Expanded);
            }
        }

        void FinishSlide(int target, bool expanded)
        {
            list.Visible = expanded;
            MinimumSize = new Size(minWidth, collapsedHeight);
            Height = target;
            MinimumSize = new Size(minWidth, expanded ? minHeight : collapsedHeight);
        }

        void ToggleCollapse()
        {
            Data.Collapsed = !Data.Collapsed;
            EndPeek();
            hoverTicks = 0;
            suppressPeek = true; // the pointer is on the header right now; wait for it to leave first
            ApplyCollapse();
            owner.Save();
        }

        bool PointerIsOver()
        {
            Point p = Cursor.Position;
            return Bounds.Contains(p) && OwnsWindowAt(p);
        }

        // Re-applies the stacking rule in WndProc after something else (Win+D, an app being restored) moved the desktop.
        public void Resettle() => SetWindowPos(Handle, DesktopLayer.HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        public bool OwnsWindowAt(Point p)
        {
            if (!Visible) return false;
            IntPtr under = WindowFromPoint(p);
            return under == Handle || IsChild(Handle, under);
        }

        // ---- show / hide -----------------------------------------------------------------------

        public bool Busy => menuOpen || renameBox != null;

        double FullOpacity => Math.Max(30, Math.Min(100, Data.Opacity)) / 100.0;

        // Fades the fence in or out over roughly a sixth of a second.
        public void FadeTo(bool show)
        {
            if (shown == show) return;
            shown = show;
            fadingIn = show;
            if (show && !Visible)
            {
                Opacity = 0;
                Visible = true;
            }
            fadeTimer.Start();
        }

        void FadeTick()
        {
            double step = FullOpacity / 10;
            if (fadingIn)
            {
                Opacity = Math.Min(FullOpacity, Opacity + step);
                if (Opacity >= FullOpacity - 0.001) fadeTimer.Stop();
            }
            else if (Opacity - step <= 0.02)
            {
                fadeTimer.Stop();
                Visible = false;
                Opacity = FullOpacity;
            }
            else Opacity -= step;
        }

        void HoverTick()
        {
            if (!PointerIsOver())
            {
                suppressPeek = false;
                hoverTicks = 0;
                if (peeking && MouseButtons == MouseButtons.None && !menuOpen && renameBox == null)
                {
                    EndPeek();
                    ApplyCollapse();
                }
                return;
            }
            if (!peeking && !suppressPeek && ++hoverTicks >= 2)
            {
                peeking = true;
                ApplyCollapse();
                covered = owner.HideOverlapping(this);
            }
        }

        void EndPeek()
        {
            peeking = false;
            if (covered == null) return;
            owner.Restore(covered);
            covered = null;
        }

        // ---- style -----------------------------------------------------------------------------

        public void RefreshStyle()
        {
            Color c = Color.FromArgb(255, Color.FromArgb(Data.Argb));
            bool dark = c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 140;
            Color fore = dark ? Color.White : Color.Black;
            Color bar = dark ? ControlPaint.Light(c, 0.25f) : ControlPaint.Dark(c, 0.08f);
            BackColor = c;
            list.BackColor = c;
            list.ForeColor = fore;
            header.BackColor = title.BackColor = chevron.BackColor = bar;
            title.ForeColor = chevron.ForeColor = fore;
            title.TextAlign = Data.TitleAlign == 1 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;
            // Centred text is nudged right so the chevron on the other side does not pull it off centre.
            title.Padding = Data.TitleAlign == 1 ? new Padding(grip + 2, 0, 0, 0) : new Padding(chevron.Width, 0, 0, 0);
            if (!fadeTimer.Enabled) Opacity = FullOpacity;
            ApplyCorners();

            if (icons.ImageSize.Width != IconPixels) ReloadItems();
        }

        void ApplyCorners()
        {
            if (!IsHandleCreated) return;
            int pref = Data.Rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
            try { DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
            catch { } // rounded corners need Windows 11; older systems keep square ones
        }

        static Color? WindowsAccent()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    if (key?.GetValue("AccentColor") is int abgr)
                        return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
            }
            catch { }
            return null;
        }

        void SetColour(Color c)
        {
            Data.Argb = Color.FromArgb(255, c).ToArgb();
            Restyle();
        }

        void Restyle()
        {
            RefreshStyle();
            owner.Save();
        }

        // ---- title bar -------------------------------------------------------------------------

        void TitleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks >= 2) { ToggleCollapse(); return; }
            if (Data.Locked) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        void BeginRename()
        {
            if (renameBox != null) return;
            var box = new TextBox
            {
                Text = Data.Title,
                Bounds = header.Bounds,
                TextAlign = HorizontalAlignment.Center,
                Font = title.Font,
            };
            renameBox = box;
            bool done = false;
            Action<bool> finish = commit =>
            {
                if (done) return;
                done = true;
                if (commit && box.Text.Trim().Length > 0)
                {
                    Data.Title = box.Text.Trim();
                    title.Text = Data.Title;
                    owner.Save();
                }
                Controls.Remove(box);
                box.Dispose();
                renameBox = null;
            };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; finish(false); }
            };
            box.LostFocus += (s, e) => finish(true);
            Controls.Add(box);
            box.BringToFront();
            Activate();
            box.Focus();
            box.SelectAll();
        }

        // ---- contents --------------------------------------------------------------------------

        string Folder => Data.Folder;

        int IconPixels => Data.IconSize == 32 || Data.IconSize == 64 ? Data.IconSize : 48;

        void EnsureFolder()
        {
            if (!string.IsNullOrEmpty(Data.Folder)) { Directory.CreateDirectory(Data.Folder); return; }
            string root = Store.FenceRoot;
            string name = string.Concat(Data.Title.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
            if (name.Length == 0) name = "Fence";
            string path = Path.Combine(root, name);
            for (int n = 2; Directory.Exists(path); n++) path = Path.Combine(root, name + " " + n);
            Directory.CreateDirectory(path);
            Data.Folder = path;
            owner.Save();
        }

        // Fences made before 0.4 only pointed at files. Keep those entries as shortcuts; the originals stay put.
        void MigrateLegacyItems()
        {
            if (Data.Items.Count == 0) return;
            foreach (string path in Data.Items)
            {
                try
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".lnk" || ext == ".url")
                    {
                        string copy = Path.Combine(Folder, Path.GetFileName(path));
                        if (File.Exists(path) && !File.Exists(copy)) File.Copy(path, copy);
                    }
                    else if (File.Exists(path) || Directory.Exists(path)) CreateShortcut(path);
                }
                catch { }
            }
            Data.Items.Clear();
            owner.Save();
        }

        void StartWatching()
        {
            IntPtr unused = Handle; // the watcher marshals its events through this window
            reloadTimer.Tick += (s, e) => { reloadTimer.Stop(); ReloadItems(); };
            watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                SynchronizingObject = this,
            };
            watcher.Created += (s, e) => QueueReload();
            watcher.Deleted += (s, e) => QueueReload();
            watcher.Renamed += (s, e) => QueueReload();
            watcher.EnableRaisingEvents = true;
        }

        void QueueReload()
        {
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        static string DisplayName(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string name = ext == ".lnk" || ext == ".url"
                ? Path.GetFileNameWithoutExtension(path)
                : Path.GetFileName(path.TrimEnd('\\'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        void ReloadItems()
        {
            list.BeginUpdate();
            list.Items.Clear();
            icons.Images.Clear();
            icons.ImageSize = new Size(IconPixels, IconPixels);
            try
            {
                var entries = Directory.EnumerateFileSystemEntries(Folder)
                    .Where(p => (File.GetAttributes(p) & FileAttributes.Hidden) == 0)
                    .OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase);
                foreach (string path in entries)
                {
                    icons.Images.Add(ShellIcons.Get(path, IconPixels));
                    list.Items.Add(new ListViewItem(DisplayName(path), icons.Images.Count - 1) { Tag = path, ToolTipText = path });
                }
            }
            catch { }
            list.EndUpdate();
        }

        string[] SelectedPaths() => list.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToArray();

        // Moves (or copies) real files and folders; the shell shows its own progress and conflict dialogs.
        static void Transfer(string source, string target, bool copy)
        {
            if (Directory.Exists(source))
            {
                if (copy) VB.FileSystem.CopyDirectory(source, target, VB.UIOption.AllDialogs);
                else VB.FileSystem.MoveDirectory(source, target, VB.UIOption.AllDialogs);
            }
            else if (copy) VB.FileSystem.CopyFile(source, target, VB.UIOption.AllDialogs);
            else VB.FileSystem.MoveFile(source, target, VB.UIOption.AllDialogs);
        }

        void TransferAll(string[] paths, string targetFolder, bool copy)
        {
            string target = targetFolder.TrimEnd('\\');
            foreach (string path in paths)
            {
                string source = path.TrimEnd('\\');
                string name = Path.GetFileName(source);
                if (name.Length == 0) continue; // a drive root
                if (string.Equals(Path.GetDirectoryName(source), target, StringComparison.OrdinalIgnoreCase)) continue;
                if ((target + "\\").StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)) continue; // into itself
                try { Transfer(source, Path.Combine(target, name), copy); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show(this, name + ": " + ex.Message, "Paddock", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            QueueReload();
        }

        void OnDragEnterFiles(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            bool copy = (e.KeyState & 8) != 0 || (e.AllowedEffect & DragDropEffects.Move) == 0; // Ctrl held
            e.Effect = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        }

        void OnDropFiles(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] paths)) return;
            bool copy = (e.KeyState & 8) != 0 || (e.AllowedEffect & DragDropEffects.Move) == 0;
            // The move is done here, so tell the drag source nothing happened or it would try to delete the files too.
            e.Effect = DragDropEffects.None;
            BeginInvoke((Action)(() => TransferAll(paths, Folder, copy)));
        }

        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            string[] paths = SelectedPaths();
            if (paths.Length == 0) return;
            list.DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
            QueueReload();
        }

        void CreateShortcut(string target)
        {
            string lnk = Path.Combine(Folder, DisplayName(target) + ".lnk");
            if (File.Exists(lnk)) return;
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target) ?? target;
            shortcut.Save();
        }

        void AddShortcuts(string[] targets)
        {
            foreach (string target in targets)
            {
                try { CreateShortcut(target); }
                catch (Exception ex) { MessageBox.Show(this, target + ": " + ex.Message, "Paddock", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            QueueReload();
        }

        void BrowseForShortcut()
        {
            using (var dlg = new OpenFileDialog { Title = "Add a shortcut to the fence", Multiselect = true, DereferenceLinks = false, Filter = "All files|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddShortcuts(dlg.FileNames);
        }

        void BrowseForFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Add a shortcut to a folder" })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddShortcuts(new[] { dlg.SelectedPath });
        }

        void OpenSelected()
        {
            foreach (ListViewItem item in list.SelectedItems)
            {
                try { Process.Start(new ProcessStartInfo((string)item.Tag) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, item.Text + ": " + ex.Message, "Paddock", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void ShowShellMenu(Point at)
        {
            string[] paths = SelectedPaths();
            if (paths.Length == 0) return;
            const int MoveToDesktop = 1;
            var extras = new[] { new KeyValuePair<int, string>(MoveToDesktop, "Move to desktop") };
            menuOpen = true; // keeps a peeking or revealed fence open while the menu is up
            try
            {
                ShellMenu.Show(this, paths, at, extras, id =>
                {
                    if (id == MoveToDesktop) TransferAll(paths, Desktop, false);
                });
            }
            finally { menuOpen = false; }
            QueueReload();
        }

        static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        void RecycleSelected()
        {
            foreach (string path in SelectedPaths())
            {
                try
                {
                    if (Directory.Exists(path)) VB.FileSystem.DeleteDirectory(path, VB.UIOption.AllDialogs, VB.RecycleOption.SendToRecycleBin);
                    else VB.FileSystem.DeleteFile(path, VB.UIOption.AllDialogs, VB.RecycleOption.SendToRecycleBin);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Paddock", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            QueueReload();
        }

        // ---- menu ------------------------------------------------------------------------------

        static ToolStripMenuItem Check(string text, bool on, Action click)
        {
            var item = new ToolStripMenuItem(text) { Checked = on };
            item.Click += (s, e) => click();
            return item;
        }

        static Bitmap Swatch(Color c)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(c))
            {
                g.FillRectangle(brush, 1, 1, 14, 14);
                g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
            }
            return bmp;
        }

        void BuildMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            menu.Items.Add("Add shortcut to a file...", null, (s, e) => BrowseForShortcut());
            menu.Items.Add("Add shortcut to a folder...", null, (s, e) => BrowseForFolder());
            menu.Items.Add("Open fence folder", null, (s, e) => Process.Start("explorer.exe", "\"" + Folder + "\""));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Data.Collapsed ? "Expand" : "Collapse", null, (s, e) => ToggleCollapse());
            menu.Items.Add("Rename fence", null, (s, e) => BeginRename());
            menu.Items.Add(Check("Lock position and size", Data.Locked, () => { Data.Locked = !Data.Locked; owner.Save(); }));
            menu.Items.Add(BuildCustomizeMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("New fence", null, (s, e) => owner.NewFence());
            menu.Items.Add("Delete fence", null, (s, e) => DeleteFence());
        }

        ToolStripMenuItem BuildCustomizeMenu()
        {
            var colour = new ToolStripMenuItem("Colour");
            foreach (var preset in Presets)
            {
                Color c = preset.Value;
                colour.DropDownItems.Add(preset.Key, Swatch(c), (s, e) => SetColour(c));
            }
            Color? accent = WindowsAccent();
            if (accent.HasValue)
                colour.DropDownItems.Add("Windows accent", Swatch(accent.Value), (s, e) => SetColour(accent.Value));
            colour.DropDownItems.Add(new ToolStripSeparator());
            colour.DropDownItems.Add("Custom...", null, (s, e) => PickColour());

            var opacity = new ToolStripMenuItem("Opacity");
            foreach (int pct in new[] { 100, 90, 85, 75, 60, 45, 30 })
            {
                int value = pct;
                opacity.DropDownItems.Add(Check(pct + "%", Data.Opacity == pct, () => { Data.Opacity = value; Restyle(); }));
            }

            var iconSize = new ToolStripMenuItem("Icon size");
            foreach (var option in new[] { new KeyValuePair<string, int>("Small", 32), new KeyValuePair<string, int>("Medium", 48), new KeyValuePair<string, int>("Large", 64) })
            {
                int value = option.Value;
                iconSize.DropDownItems.Add(Check(option.Key, icons.ImageSize.Width == value, () => { Data.IconSize = value; Restyle(); }));
            }

            var align = new ToolStripMenuItem("Title alignment");
            align.DropDownItems.Add(Check("Left", Data.TitleAlign == 1, () => { Data.TitleAlign = 1; Restyle(); }));
            align.DropDownItems.Add(Check("Centre", Data.TitleAlign != 1, () => { Data.TitleAlign = 0; Restyle(); }));

            var customize = new ToolStripMenuItem("Customize");
            customize.DropDownItems.Add(colour);
            customize.DropDownItems.Add(opacity);
            customize.DropDownItems.Add(iconSize);
            customize.DropDownItems.Add(align);
            customize.DropDownItems.Add(Check("Rounded corners", Data.Rounded, () => { Data.Rounded = !Data.Rounded; Restyle(); }));
            customize.DropDownItems.Add(new ToolStripSeparator());
            customize.DropDownItems.Add("Apply this style to all fences", null, (s, e) => owner.ApplyStyleToAll(Data));
            return customize;
        }

        void PickColour()
        {
            using (var dlg = new ColorDialog { Color = Color.FromArgb(Data.Argb), FullOpen = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) SetColour(dlg.Color);
        }

        void DeleteFence()
        {
            string question = "Delete the fence \"" + Data.Title + "\"? Everything inside it is moved back to the desktop.";
            if (MessageBox.Show(this, question, "Paddock", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            EndPeek();
            watcher.EnableRaisingEvents = false;
            try
            {
                TransferAll(Directory.GetFileSystemEntries(Folder), Desktop, false);
                Directory.Delete(Folder); // only succeeds once empty, so anything that failed to move is kept
            }
            catch { }
            owner.Delete(this);
        }
    }
}
