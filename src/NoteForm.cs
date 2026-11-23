using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stickies_Window
{
    // One sticky note window
    partial class NoteForm : Form
    {
        // ---------- screen scaling ----------
        static float dpiScale = 0f;
        static float DpiScale
        {
            get
            {
                if (dpiScale == 0f)
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
                return dpiScale;
            }
        }
        public static int S(int v) { return (int)Math.Round(v * DpiScale); }

        // ---------- Windows API ----------
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, HTCLIENT = 1;
        public const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
            HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        // ---------- state ----------
        public readonly NoteData Data;
        readonly TitleBar bar;
        readonly BodyPanel body;
        readonly Panel viewport;
        readonly NoteText text;
        int clickActivatedAt = -100000;
        bool dirty;
        bool closingSilently;
        int expandedHeight;
        bool isFull;                  // full screen on
        Rectangle restoreBounds;      // size and place to go back to

        public NoteForm(NoteData d, bool isNew)
        {
            Data = d;
            FormBorderStyle = FormBorderStyle.None;
            ResizeRedraw = true;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Stickies_Window";
            MinimumSize = new Size(S(150), S(TitleBar.H));
            Padding = new Padding(0);

            var store = NoteManager.Instance.Data;
            text = new NoteText();
            text.Note = this;
            text.BorderStyle = BorderStyle.None;
            text.DetectUrls = true;
            text.AcceptsTab = true;
            text.ScrollBars = RichTextBoxScrollBars.ForcedVertical;   // hidden: it sits outside the viewport; the thin overlay bar replaces it
            text.MacCaret = store.MacCaret;
            Icon = NoteManager.AppIcon;
            text.ForeColor = DefaultText;
            text.Font = MakeFont(store.DefaultFontName, store.DefaultFontSize, (FontStyle)store.DefaultFontStyle);
            text.AllowDrop = true;
            text.EnableAutoDragDrop = false;
            text.TextChanged += (s, e) => { dirty = true; linkedDirty = true; bar.Invalidate(); NoteManager.Instance.QueueSave(); };
            text.LinkClicked += (s, e) => OpenLink(e.LinkText);
            text.DragEnter += OnFileDragEnter;
            text.DragDrop += OnFileDrop;

            // The text sits in a body panel that leaves a gap under the title bar (so the caret isn't
            // jammed against it) and a margin at the sides and bottom. The title bar spans the full width.
            body = new BodyPanel(this);
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(S(10), S(8), S(12), S(8));    // right side leaves room for the thin scrollbar

            // The text box is made a little wider than the viewport, so its own (chunky) scrollbar is
            // clipped away. BodyPanel draws a thin overlay scrollbar instead.
            viewport = new Panel();
            viewport.Dock = DockStyle.Fill;
            viewport.Controls.Add(text);
            viewport.Resize += (s, e) => LayoutText();
            body.Controls.Add(viewport);
            body.Attach(text, viewport);
            LayoutText();

            bar = new TitleBar(this);
            bar.Dock = DockStyle.Top;
            bar.Height = S(TitleBar.H);

            Controls.Add(body);
            Controls.Add(bar);

            Bounds = new Rectangle(d.X, d.Y, Math.Max(d.Width, S(150)), Math.Max(d.Height, S(60)));

            // If the note is off every screen, move it back onto the main screen
            bool onScreen = false;
            var grab = new Rectangle(Left, Top, Width, S(TitleBar.H));
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.IntersectsWith(grab)) onScreen = true;
            if (!onScreen)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Left + S(80), wa.Top + S(80));
            }
            expandedHeight = Height;

            string path = Store.RtfPath(d.Id);
            if (!isNew && File.Exists(path))
            {
                try { text.LoadFile(path, RichTextBoxStreamType.RichText); } catch { }
            }
            dirty = false;
            ApplyNoteTextColor();       // text color that goes with this note's color (may recolor older text)

            ApplyColor();
            TopMost = d.OnTop;
            Opacity = d.Translucent ? 0.75 : 1.0;
            BuildMenu();
            if (d.Collapsed) SetCollapsed(true, false);

            Move += (s, e) => NoteManager.Instance.QueueSave();
            Resize += (s, e) => { if (!Data.Collapsed && !isFull) expandedHeight = Height; NoteManager.Instance.QueueSave(); };
            Activated += (s, e) => bar.Invalidate();
            Deactivate += (s, e) => bar.Invalidate();
        }

        void LayoutText()
        {
            text.SetBounds(0, 0, viewport.Width + SystemInformation.VerticalScrollBarWidth, viewport.Height);
        }

        static Font MakeFont(string name, float size, FontStyle style)
        {
            try { return new Font(name, size, style); }
            catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, 11f); }
        }

        bool shadowApplied;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (NoteManager.Instance != null && NoteManager.Instance.Data.RoundedCorners)
                    cp.ClassStyle |= 0x00020000;      // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            shadowApplied = NoteManager.Instance.Data.RoundedCorners;
            ApplyCorners();
        }

        // Windows 11 only: 1 = square corners, 2 = rounded (older Windows is always square)
        void ApplyCorners()
        {
            try
            {
                int pref = NoteManager.Instance.Data.RoundedCorners ? 2 : 1;
                DwmSetWindowAttribute(Handle, 33, ref pref, 4);
            }
            catch { }
        }

        // Thickness of the black border
        public static int BorderPx { get { return Math.Max(2, S(2)); } }

        // The solid black border around the whole note. Only sharp-cornered notes have it: rounded notes
        // have no border and use the soft Windows shadow instead.
        // r = the whole note's rectangle in the coordinates of the control being painted
        public static void DrawOutline(Graphics g, Rectangle r, bool rounded)
        {
            if (rounded) return;
            int bw = BorderPx;
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(Color.Black))
            {
                g.FillRectangle(b, r.X, r.Y, bw, r.Height);                    // left
                g.FillRectangle(b, r.Right - bw, r.Y, bw, r.Height);          // right
                g.FillRectangle(b, r.X, r.Y, r.Width, bw);                    // top
                g.FillRectangle(b, r.X, r.Bottom - bw, r.Width, bw);          // bottom
            }
        }

        // Width of the border line that snapped notes share (none on rounded notes)
        static int SharedBorderPx { get { return NoteManager.Instance.Data.RoundedCorners ? 0 : BorderPx; } }

        // Each child control paints its own part of the border
        public void PaintOutline(Control c, Graphics g)
        {
            DrawOutline(g, new Rectangle(-c.Left, -c.Top, ClientSize.Width, ClientSize.Height),
                NoteManager.Instance.Data.RoundedCorners);
        }

        // The title bar is only shown while this note is the active one
        public bool IsActiveNote { get { return ContainsFocus || Form.ActiveForm == this; } }

        // True for a moment after a click woke this note up, so that click doesn't press an (invisible) button
        public bool WasJustClickActivated { get { return Environment.TickCount - clickActivatedAt < 400; } }

        // ---------- snapping notes together ----------
        const int WM_MOVING = 0x216, WM_SIZING = 0x214, WM_MOUSEACTIVATE = 0x21;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        static int SnapRange { get { return S(12); } }

        // Keeps the smallest nudge that is within range
        static void Closer(ref int best, int d)
        {
            if (Math.Abs(d) <= SnapRange && (best == int.MaxValue || Math.Abs(d) < Math.Abs(best))) best = d;
        }

        // How far the moving rectangles must shift to line up with, or stick to, the fixed ones
        static void ComputeSnap(List<Rectangle> moving, List<Rectangle> fixedRects, out int dx, out int dy)
        {
            int bw = SharedBorderPx, t = SnapRange;
            dx = int.MaxValue; dy = int.MaxValue;
            foreach (Rectangle m in moving)
                foreach (Rectangle o in fixedRects)
                {
                    bool vNear = m.Top < o.Bottom + t && m.Bottom > o.Top - t;
                    bool hNear = m.Left < o.Right + t && m.Right > o.Left - t;
                    if (vNear)
                    {
                        Closer(ref dx, (o.Right - bw) - m.Left);    // my left edge meets their right edge (borders share a line)
                        Closer(ref dx, (o.Left + bw) - m.Right);    // my right edge meets their left edge
                        Closer(ref dx, o.Left - m.Left);            // left edges line up
                        Closer(ref dx, o.Right - m.Right);          // right edges line up
                    }
                    if (hNear)
                    {
                        Closer(ref dy, (o.Bottom - bw) - m.Top);
                        Closer(ref dy, (o.Top + bw) - m.Bottom);
                        Closer(ref dy, o.Top - m.Top);
                        Closer(ref dy, o.Bottom - m.Bottom);
                    }
                }
            if (dx == int.MaxValue) dx = 0;
            if (dy == int.MaxValue) dy = 0;
        }

        // ---------- moving a whole group (hold Shift while dragging the title bar) ----------
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        const int WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;
        bool pendingCaptionMove;
        List<NoteForm> dragGroup;                     // null unless a group is being dragged
        Dictionary<NoteForm, Point> dragStart;

        void StartGroupDrag()
        {
            List<NoteForm> g = NoteManager.Instance.SnappedGroup(this);
            if (g.Count < 2) return;
            dragGroup = g;
            dragStart = new Dictionary<NoteForm, Point>();
            foreach (NoteForm n in g) dragStart[n] = n.Location;
        }

        // While dragging: snap (the whole group counts as one piece), and carry the group along
        void HandleMoving(IntPtr lp)
        {
            RECT r = (RECT)Marshal.PtrToStructure(lp, typeof(RECT));

            if (NoteManager.Instance.Data.SnapNotes)
            {
                int px = r.Left - Left, py = r.Top - Top;     // where this note is about to go
                var moving = new List<Rectangle>();
                ICollection<NoteForm> own = dragGroup != null ? (ICollection<NoteForm>)dragGroup : new NoteForm[] { this };
                foreach (NoteForm n in own)
                {
                    Rectangle b = n.Bounds;
                    b.Offset(px, py);
                    moving.Add(b);
                }
                int dx, dy;
                ComputeSnap(moving, NoteManager.Instance.OtherBounds(own), out dx, out dy);
                r.Left += dx; r.Right += dx; r.Top += dy; r.Bottom += dy;
            }

            if (dragGroup != null)
            {
                int sx = r.Left - dragStart[this].X, sy = r.Top - dragStart[this].Y;
                foreach (NoteForm n in dragGroup)
                {
                    if (n == this) continue;
                    Point o = dragStart[n];
                    SetWindowPos(n.Handle, IntPtr.Zero, o.X + sx, o.Y + sy, 0, 0, 0x1 | 0x4 | 0x10);   // no size, no z-order, no activate
                }
            }
            Marshal.StructureToPtr(r, lp, false);
        }

        // While resizing: only the edges being dragged snap
        void SnapSizing(int edge, IntPtr lp)
        {
            RECT r = (RECT)Marshal.PtrToStructure(lp, typeof(RECT));
            bool left = edge == 1 || edge == 4 || edge == 7;
            bool right = edge == 2 || edge == 5 || edge == 8;
            bool top = edge == 3 || edge == 4 || edge == 5;
            bool bottom = edge == 6 || edge == 7 || edge == 8;
            int bw = SharedBorderPx, t = SnapRange;
            int dl = int.MaxValue, dr = int.MaxValue, dt = int.MaxValue, db = int.MaxValue;
            foreach (Rectangle o in NoteManager.Instance.OtherBounds(new NoteForm[] { this }))
            {
                bool vNear = r.Top < o.Bottom + t && r.Bottom > o.Top - t;
                bool hNear = r.Left < o.Right + t && r.Right > o.Left - t;
                if (vNear && left) { Closer(ref dl, (o.Right - bw) - r.Left); Closer(ref dl, o.Left - r.Left); }
                if (vNear && right) { Closer(ref dr, (o.Left + bw) - r.Right); Closer(ref dr, o.Right - r.Right); }
                if (hNear && top) { Closer(ref dt, (o.Bottom - bw) - r.Top); Closer(ref dt, o.Top - r.Top); }
                if (hNear && bottom) { Closer(ref db, (o.Top + bw) - r.Bottom); Closer(ref db, o.Bottom - r.Bottom); }
            }
            if (dl != int.MaxValue) r.Left += dl;
            if (dr != int.MaxValue) r.Right += dr;
            if (dt != int.MaxValue) r.Top += dt;
            if (db != int.MaxValue) r.Bottom += db;
            Marshal.StructureToPtr(r, lp, false);
        }

        // Resize from the left, right and bottom edges
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE && Form.ActiveForm != this) clickActivatedAt = Environment.TickCount;
            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                // Shift held while dragging the title bar: take the snapped notes along
                if (pendingCaptionMove && (Control.ModifierKeys & Keys.Shift) != 0) StartGroupDrag();
                pendingCaptionMove = false;
            }
            else if (m.Msg == WM_EXITSIZEMOVE) { dragGroup = null; dragStart = null; }
            else if (m.Msg == WM_MOVING) { HandleMoving(m.LParam); m.Result = (IntPtr)1; return; }
            else if (m.Msg == WM_SIZING && NoteManager.Instance.Data.SnapNotes)
            {
                SnapSizing(m.WParam.ToInt32(), m.LParam);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && !isFull && (int)m.Result == HTCLIENT)
            {
                long lp = (long)m.LParam;
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = S(6);
                bool l = p.X < g, r = p.X >= Width - g, b = !Data.Collapsed && p.Y >= Height - g;
                if (b && l) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (b && r) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (l) m.Result = (IntPtr)HTLEFT;
                else if (r) m.Result = (IntPtr)HTRIGHT;
                else if (b) m.Result = (IntPtr)HTBOTTOM;
            }
        }

        // Called by the title bar to move or resize the window
        public void BeginNc(int hit)
        {
            pendingCaptionMove = hit == HTCAPTION;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)hit, IntPtr.Zero);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- look ----------
        // ---------- text color that goes with the note color ----------
        static Color GlobalText()
        {
            StoreData st = NoteManager.Instance.Data;
            return st.DefaultTextColor != 0 ? Palette.FromArgbInt(st.DefaultTextColor) : Palette.NoteText;
        }

        // What the standard text color was before pure black became the default, for notes saved by older versions
        static Color OlderGlobalText()
        {
            StoreData st = NoteManager.Instance.Data;
            return st.DefaultTextColor != 0 ? Palette.FromArgbInt(st.DefaultTextColor) : Palette.Text;
        }

        // The standard text color for this note: the one that goes with its note color (set in Settings > Colors,
        // or the built-in pairing such as white on Blue), otherwise the normal default text color
        public Color DefaultText
        {
            get
            {
                int slot = Data.ColorIndex;
                if (Data.CustomColor == 0)
                {
                    int t = Palette.TextFor(slot);
                    if (t != 0) return Palette.FromArgbInt(t);
                }
                return GlobalText();
            }
        }

        // Highlight color for this note: the one chosen for its note color in Settings > Colors,
        // or (Auto) one picked to suit the note color and its text color
        public Color HighlightNow
        {
            get
            {
                int slot = Data.ColorIndex;
                if (Data.CustomColor == 0 && slot >= 0 && slot < Palette.Slots && Palette.HighlightColors[slot] != 0)
                    return Palette.FromArgbInt(Palette.HighlightColors[slot]);
                return Palette.Highlight(Palette.For(Data), DefaultText);
            }
        }

        // Keeps the note's standard text color and highlight color in step with its note color and the Settings.
        // Only text that still has the old standard color is changed; words you colored yourself stay as they are.
        public void ApplyNoteTextColor()
        {
            Color want = DefaultText;
            Color old = Data.TextBase != 0 ? Palette.FromArgbInt(Data.TextBase) : OlderGlobalText();
            Color wantHL = HighlightNow;
            bool needText = old.ToArgb() != want.ToArgb();
            bool needHL = Data.HighlightBase != wantHL.ToArgb();

            if (text.TextLength == 0) { if (needText) text.ForeColor = want; }
            else if (needText || needHL) RecolorRuns(old, want, needText, needHL, wantHL);

            Data.TextBase = want.ToArgb();
            Data.HighlightBase = wantHL.ToArgb();
        }

        public void RefreshLook()
        {
            text.MacCaret = NoteManager.Instance.Data.MacCaret;
            text.Invalidate();
            // the shadow belongs to the window itself, so switching between sharp and rounded rebuilds the window once
            if (IsHandleCreated && shadowApplied != NoteManager.Instance.Data.RoundedCorners) RecreateHandle();
            ApplyColor();
            ApplyNoteTextColor();
            ApplyCorners();
            Invalidate(true);
        }

        // Brings the note to the front and focuses it, even when another app is in use
        public void ForceToFront()
        {
            if (IsMinimized) WindowState = FormWindowState.Normal;
            Show();
            TopMost = true;
            TopMost = Data.OnTop;
            Activate();
        }

        public void FocusText() { if (!Data.Collapsed) text.Focus(); }

        void ApplyColor()
        {
            BackColor = Palette.For(Data);
            body.BackColor = BackColor;
            viewport.BackColor = BackColor;
            text.BackColor = BackColor;
            bar.Invalidate();
        }

        public string FirstLine
        {
            get
            {
                foreach (string line in text.Lines)
                {
                    string t = line.Trim();
                    if (t.Length > 0) return t;
                }
                return "";
            }
        }

        // ---------- collapse ----------
        public void ToggleCollapse() { SetCollapsed(!Data.Collapsed, true); }

        void SetCollapsed(bool collapse, bool save)
        {
            if (collapse && isFull) ExitFullScreen();
            if (collapse)
            {
                expandedHeight = Height;
                Data.Collapsed = true;
                body.Visible = false;
                Height = S(TitleBar.H);
            }
            else
            {
                Data.Collapsed = false;
                body.Visible = true;
                Height = expandedHeight;
                text.Focus();
            }
            bar.Invalidate();
            if (save) NoteManager.Instance.QueueSave();
        }

        // ---------- full screen and minimize ----------
        public bool IsFull { get { return isFull; } }

        // The note fills the screen it is on (not covering the taskbar). Press again to go back.
        public void ToggleFullScreen()
        {
            if (isFull) { ExitFullScreen(); return; }
            if (Data.Collapsed) SetCollapsed(false, false);
            restoreBounds = Bounds;
            isFull = true;
            Bounds = Screen.FromControl(this).WorkingArea;
            bar.Invalidate();
            NoteManager.Instance.QueueSave();
        }

        void ExitFullScreen()
        {
            if (!isFull) return;
            isFull = false;
            expandedHeight = restoreBounds.Height;
            Bounds = restoreBounds;
            bar.Invalidate();
            NoteManager.Instance.QueueSave();
        }

        // Minimize: the note goes to the taskbar like any other window, so Alt+Tab and the taskbar bring it back.
        // (It only has a taskbar button while it is minimized.)
        bool minimizedByUs, sawMinimized;

        public bool IsMinimized { get { return WindowState == FormWindowState.Minimized; } }

        public void MinimizeNote()
        {
            if (IsMinimized) return;
            SyncData();                                   // remember where it is
            Text = FirstLine.Length > 0 ? FirstLine : "Sticky note";
            minimizedByUs = true;
            sawMinimized = false;
            ShowInTaskbar = true;
            WindowState = FormWindowState.Minimized;
        }

        public void RestoreNote() { ForceToFront(); }

        // Back from the taskbar: the taskbar button goes away again
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!minimizedByUs) return;
            if (WindowState == FormWindowState.Minimized) { sawMinimized = true; return; }
            if (sawMinimized && WindowState == FormWindowState.Normal)
            {
                minimizedByUs = sawMinimized = false;
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed || IsMinimized) return;
                    ShowInTaskbar = false;
                    ForceToFront();
                }));
            }
        }

        // ---------- saving ----------
        public void SyncData()
        {
            // in full screen, remember the normal size so that is what gets saved
            Rectangle b = IsMinimized ? RestoreBounds : Bounds;
            Data.X = isFull ? restoreBounds.X : b.X;
            Data.Y = isFull ? restoreBounds.Y : b.Y;
            Data.Width = isFull ? restoreBounds.Width : b.Width;
            Data.Height = isFull ? restoreBounds.Height : (Data.Collapsed ? expandedHeight : b.Height);
        }

        public void SaveContent()
        {
            SaveOwnCopy();
            SaveLinkedIfNeeded();       // and the note's own file, if it has one
        }

        void SaveOwnCopy()
        {
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(Store.Folder);
                string path = Store.RtfPath(Data.Id);
                string tmp = path + ".tmp";
                text.SaveFile(tmp, RichTextBoxStreamType.RichText);   // write aside first, so a crash can't wreck the note
                File.Copy(tmp, path, true);
                File.Delete(tmp);
                dirty = false;
            }
            catch (Exception ex)
            {
                if (NoteManager.Instance != null) NoteManager.Instance.ReportSaveError(ex);
            }
        }

        public void LoadRtf(string rtf)
        {
            try { text.Rtf = rtf; } catch { text.Text = rtf; }
        }

        public void LoadPlain(string plain) { text.Text = plain; }

        // ---------- closing ----------
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (closingSilently || e.CloseReason != CloseReason.UserClosing)
            {
                SaveContent();
                base.OnFormClosing(e);
                return;
            }
            e.Cancel = true;
            CloseNote();
        }

        public void CloseSilently()
        {
            closingSilently = true;
            Close();
        }

        // Same as macOS Stickies: closing a note deletes it. If the note is saved in a file, its file is
        // brought up to date and nothing is asked; otherwise offer to save it first.
        public void CloseNote()
        {
            if (text.TextLength > 0 && !FlushLinked())
            {
                DialogResult r = MessageBox.Show(this,
                    "Do you want to save this note to a file before closing it?\n\n" +
                    "If you don't, the note's contents will be lost.",
                    "Stickies_Window", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes && !SaveNote()) return;
            }
            SyncData();
            NoteManager.Instance.RememberClosed(Data, text.Rtf);
            NoteManager.Instance.Remove(this);
            dirty = false;
            closingSilently = true;
            Close();
        }

        // ---------- drag files in ----------
        void OnFileDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnFileDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                try
                {
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp")
                    {
                        using (var img = Image.FromFile(f)) InsertImage(new Bitmap(img));
                    }
                    else if (ext == StickyFile.Extension) NoteManager.Instance.OpenFile(f);
                    else if (ext == ".rtf") text.SelectedRtf = File.ReadAllText(f);
                    else if (ext == ".txt") text.SelectedText = File.ReadAllText(f);
                }
                catch { }
            }
        }

        // Pastes an image, then puts the old clipboard back
        void InsertImage(Image img)
        {
            DataObject backup = null;
            try
            {
                IDataObject cur = Clipboard.GetDataObject();
                if (cur != null)
                {
                    backup = new DataObject();
                    foreach (string fmt in cur.GetFormats(false))
                    {
                        try { object o = cur.GetData(fmt); if (o != null) backup.SetData(fmt, o); } catch { }
                    }
                }
            }
            catch { }

            try { Clipboard.SetImage(img); text.Paste(); } catch { }
            try { if (backup != null) Clipboard.SetDataObject(backup, true); } catch { }
        }

        // ---------- shared drawing helper ----------
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // The note's text area; sends shortcuts to the note first
    class NoteText : RichTextBox
    {
        public NoteForm Note;

        // ---- macOS-style text cursor: 2px wide (scaled), rounded ends, text color, 500 ms on / 500 ms off ----
        [DllImport("user32.dll")] static extern bool DestroyCaret();
        const int WM_PAINT = 0x000F;
        public bool MacCaret = true;
        readonly System.Windows.Forms.Timer blink = new System.Windows.Forms.Timer();
        bool caretOn = true;
        Rectangle lastCaret = Rectangle.Empty;

        public NoteText()
        {
            blink.Interval = 500;
            blink.Tick += (s, e) => { caretOn = !caretOn; RefreshCaret(); };
        }

        Rectangle CaretRect()
        {
            try
            {
                Point p = GetPositionFromCharIndex(SelectionStart);
                Font f = SelectionFont ?? Font;
                return new Rectangle(p.X, p.Y, NoteForm.S(2), f.Height);
            }
            catch { return Rectangle.Empty; }
        }

        void RefreshCaret()
        {
            if (!IsHandleCreated) return;
            Rectangle now = CaretRect();
            Rectangle area = lastCaret.IsEmpty ? now : (now.IsEmpty ? lastCaret : Rectangle.Union(now, lastCaret));
            if (!area.IsEmpty) Invalidate(Rectangle.Inflate(area, 2, 2));
        }

        // Caret shows solid while you type or move it, then blinks
        void RestartBlink()
        {
            if (!MacCaret) return;
            caretOn = true;
            blink.Stop();
            if (Focused) blink.Start();
            RefreshCaret();
        }

        protected override void OnSelectionChanged(EventArgs e) { base.OnSelectionChanged(e); RestartBlink(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); RestartBlink(); }
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            blink.Stop();
            caretOn = false;
            RefreshCaret();
        }

        void DrawCaret()
        {
            if (!Focused || SelectionLength != 0 || ReadOnly) return;
            Rectangle r = CaretRect();
            lastCaret = r;
            if (!caretOn || r.IsEmpty || !r.IntersectsWith(ClientRectangle)) return;
            Color c = SelectionColor;
            if (c.IsEmpty) c = ForeColor;
            using (Graphics g = CreateGraphics())
            using (var b = new SolidBrush(c))
            using (var path = NoteForm.RoundRect(r, Math.Max(1, r.Width / 2)))
            {
                g.SetClip(ClientRectangle);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(b, path);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (!MacCaret) return;
            if (m.Msg == WM_PAINT) DrawCaret();
            if (Focused) DestroyCaret();       // hide the thin system caret, ours replaces it
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) blink.Dispose();
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Note != null && Note.HandleKey(keyData)) return true;
            // the rich text box has its own Ctrl+B/I/U; if you moved those shortcuts, don't let the old keys act
            if (keyData == (Keys.Control | Keys.L)) return true;
            if ((keyData == (Keys.Control | Keys.B) || keyData == (Keys.Control | Keys.I) || keyData == (Keys.Control | Keys.U))
                && FormatActions.Find(keyData) == null) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Clicking a task checkbox ticks it
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Note != null && ModifierKeys == Keys.None && e.Clicks == 1)
                Note.TryToggleTaskAt(e.Location);
        }
    }

    // Holds the text. Paints its part of the border, lets the edges be grabbed to resize the note, and draws
    // the thin macOS-style overlay scrollbar: a rounded translucent thumb with no track that fades in when you
    // scroll or move the mouse near the right edge, and fades out again when you stop.
    class BodyPanel : Panel
    {
        const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1, SB_VERT = 1, EM_SETSCROLLPOS = 0x4DE;
        const uint SIF_ALL = 0x17;

        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref POINT p);

        readonly NoteForm note;
        NoteText text;
        Panel viewport;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        float alpha;                 // 0 = hidden, 1 = fully shown
        int showUntil;               // keep showing until this tick count (after scrolling)
        bool hoverStrip, dragging;
        int dragOffset;
        Point mouse;
        int lastPos = -1, lastRange = -1, lastPage = -1;

        public BodyPanel(NoteForm n)
        {
            note = n;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            timer.Interval = 30;
            timer.Tick += OnTick;
            timer.Start();
        }

        public void Attach(NoteText t, Panel v) { text = t; viewport = v; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        // ---- scroll position of the text box (the real scrollbar is hidden but still keeps the numbers) ----
        bool GetMetrics(out int pos, out int range, out int page)
        {
            pos = range = page = 0;
            if (text == null || !text.IsHandleCreated) return false;
            var si = new SCROLLINFO();
            si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
            si.fMask = SIF_ALL;
            if (!GetScrollInfo(text.Handle, SB_VERT, ref si)) return false;
            range = si.nMax - si.nMin + 1;
            page = (int)si.nPage;
            pos = si.nPos - si.nMin;
            return page > 0 && range > page;
        }

        void ScrollTo(int y)
        {
            var p = new POINT();
            p.X = 0; p.Y = Math.Max(0, y);
            SendMessage(text.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p);
        }

        Rectangle TrackRect()
        {
            int w = NoteForm.S(6);
            return new Rectangle(Width - NoteForm.BorderPx - NoteForm.S(2) - w, viewport.Top + NoteForm.S(2),
                w, Math.Max(10, viewport.Height - NoteForm.S(4)));
        }

        Rectangle ThumbRect(int pos, int range, int page)
        {
            Rectangle tr = TrackRect();
            int len = Math.Min(tr.Height, Math.Max(NoteForm.S(26), (int)((long)tr.Height * page / range)));
            int maxPos = range - page;
            int y = tr.Top + (maxPos <= 0 ? 0 : (int)((long)(tr.Height - len) * Math.Min(pos, maxPos) / maxPos));
            return new Rectangle(tr.Left, y, tr.Width, len);
        }

        Rectangle StripRect()
        {
            return new Rectangle(Width - NoteForm.S(20), viewport.Top, NoteForm.S(20), viewport.Height);
        }

        // ---- fade in / out ----
        void OnTick(object sender, EventArgs e)
        {
            if (text == null || viewport == null || !Visible || !note.Visible) return;
            int pos, range, page;
            bool scrollable = GetMetrics(out pos, out range, out page);
            bool changed = pos != lastPos || range != lastRange || page != lastPage;
            if (changed)
            {
                if (lastPos != -1 && pos != lastPos) showUntil = Environment.TickCount + 1300;   // scrolled: show for a moment
                lastPos = pos; lastRange = range; lastPage = page;
            }

            float target = (scrollable && (dragging || hoverStrip || Environment.TickCount < showUntil)) ? 1f : 0f;
            float old = alpha;
            alpha += (target - alpha) * 0.35f;
            if (Math.Abs(target - alpha) < 0.03f) alpha = target;
            if (alpha != old || (changed && alpha > 0f)) Invalidate(StripRect());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, ClientRectangle);
            note.PaintOutline(this, g);

            int pos, range, page;
            if (alpha > 0.01f && viewport != null && GetMetrics(out pos, out range, out page))
            {
                Rectangle t = ThumbRect(pos, range, page);
                bool hot = dragging || (hoverStrip && t.Contains(mouse));
                int a = (int)((hot ? 150 : 90) * alpha);          // about 35% at rest, stronger on hover / drag
                Color thumb = Palette.IsDark(BackColor) ? Color.White : Color.Black;     // light thumb on dark notes
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(Color.FromArgb(a, thumb)))
                using (var path = NoteForm.RoundRect(t, t.Width / 2))
                    g.FillPath(br, path);
            }
        }

        // ---- mouse: drag the thumb, or click the strip to jump ----
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            int pos, range, page;
            hoverStrip = viewport != null && e.X >= Width - NoteForm.S(20) && GetMetrics(out pos, out range, out page);
            if (dragging) DragTo(e.Y);
            if (hoverStrip || dragging) Invalidate(StripRect());
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverStrip = false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int pos, range, page;
            if (e.Button == MouseButtons.Left && viewport != null && e.X >= Width - NoteForm.S(20) && GetMetrics(out pos, out range, out page))
            {
                Rectangle t = ThumbRect(pos, range, page);
                dragOffset = t.Contains(e.Location) ? e.Y - t.Top : t.Height / 2;
                dragging = true;
                Capture = true;
                DragTo(e.Y);
                return;
            }
            // anywhere else in the margin: click into the text
            if (text != null) { text.Focus(); text.Select(text.TextLength, 0); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Capture = false; Invalidate(StripRect()); }
        }

        void DragTo(int y)
        {
            int pos, range, page;
            if (!GetMetrics(out pos, out range, out page)) return;
            Rectangle tr = TrackRect();
            Rectangle th = ThumbRect(pos, range, page);
            int free = tr.Height - th.Height;
            if (free <= 0) return;
            int top = Math.Max(tr.Top, Math.Min(y - dragOffset, tr.Bottom - th.Height));
            double ratio = (double)(top - tr.Top) / free;
            ScrollTo((int)Math.Round(ratio * (range - page)));
        }

        // On the outer few pixels, let the note window itself answer (it returns the resize handles).
        // The right edge is only 4px so the scrollbar thumb can be grabbed.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = (long)m.LParam;
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = NoteForm.S(6);
                if (p.X < g || p.X >= Width - NoteForm.S(4) || p.Y >= Height - g) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            }
            base.WndProc(ref m);
        }
    }

    // Thin title bar: close box (left), always-on-top pin and collapse box (right)
    class TitleBar : Control
    {
        public const int H = 22;
        static readonly Font TitleFont = new Font("Segoe UI", 9f);

        readonly NoteForm note;
        bool hover;
        Color inkColor = Palette.Text;      // color of the buttons, icons and title: follows the note's text color
        string lastTip;
        readonly ToolTip tip = new ToolTip();
        Point downAt;
        bool maybeDrag;

        public TitleBar(NoteForm n)
        {
            note = n;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        Rectangle CloseBox
        {
            get { int s = NoteForm.S(11); return new Rectangle(NoteForm.S(7), (Height - s) / 2, s, s); }
        }

        // Boxes on the right, counted from the right edge: collapse, full screen, minimize, pin
        Rectangle Slot(int i)
        {
            int s = NoteForm.S(11), gap = NoteForm.S(7);
            return new Rectangle(Width - gap - (i + 1) * s - i * gap, (Height - s) / 2, s, s);
        }

        Rectangle CollapseBox { get { return Slot(0); } }
        Rectangle FullBox { get { return Slot(1); } }
        Rectangle MinBox { get { return Slot(2); } }
        Rectangle PinBox { get { return Slot(3); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Active note: a full-width bar. Any other note: the bar disappears into the note color
            bool active = note.IsActiveNote;
            Color fill = active ? Palette.Bar(Palette.For(note.Data)) : Palette.For(note.Data);
            inkColor = Palette.Ink(fill, note.DefaultText);
            using (var b = new SolidBrush(fill))
                g.FillRectangle(b, ClientRectangle);

            if (active)
            {
                bool pinned = note.Data.OnTop;
                DrawBox(g, CloseBox, false);
                DrawBox(g, PinBox, pinned);
                DrawBox(g, MinBox, false);
                DrawBox(g, FullBox, note.IsFull);
                DrawBox(g, CollapseBox, false);

                if (hover && NoteManager.Instance.Data.TitleBarIcons)
                {
                    DrawCloseIcon(g, CloseBox);
                    DrawPinIcon(g, PinBox, pinned);
                    DrawMinimizeIcon(g, MinBox);
                    DrawFullIcon(g, FullBox, note.IsFull);
                    DrawCollapseIcon(g, CollapseBox, note.Data.Collapsed);
                }
            }

            // a collapsed note shows its first line in the bar
            if (note.Data.Collapsed)
            {
                int left = CloseBox.Right + NoteForm.S(8);
                int right = (active ? PinBox.Left : Width - NoteForm.S(8)) - NoteForm.S(8);
                var r = Rectangle.FromLTRB(left, 0, Math.Max(left, right), Height);
                TextRenderer.DrawText(g, note.FirstLine, TitleFont, r, inkColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            note.PaintOutline(this, g);
        }

        void DrawBox(Graphics g, Rectangle r, bool strong)
        {
            using (var path = NoteForm.RoundRect(r, NoteForm.S(3)))
            using (var b = new SolidBrush(Color.FromArgb(strong ? 120 : 45, inkColor)))
                g.FillPath(b, path);
        }

        Pen IconPen()
        {
            return new Pen(Color.FromArgb(215, inkColor), Math.Max(1.3f, NoteForm.S(1))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        // x
        void DrawCloseIcon(Graphics g, Rectangle r)
        {
            float m = r.Width * 0.3f;
            using (var p = IconPen())
            {
                g.DrawLine(p, r.Left + m, r.Top + m, r.Right - m, r.Bottom - m);
                g.DrawLine(p, r.Right - m, r.Top + m, r.Left + m, r.Bottom - m);
            }
        }

        // pushpin: filled head when pinned
        void DrawPinIcon(Graphics g, Rectangle r, bool pinned)
        {
            float cx = r.Left + r.Width / 2f, u = r.Width / 11f;
            var head = new RectangleF(cx - 2.2f * u, r.Top + 1.4f * u, 4.4f * u, 3.8f * u);
            using (var p = IconPen())
            {
                if (pinned) using (var b = new SolidBrush(Color.FromArgb(235, inkColor))) g.FillRectangle(b, head);
                else g.DrawRectangle(p, head.X, head.Y, head.Width, head.Height);
                g.DrawLine(p, cx - 3.3f * u, r.Top + 5.9f * u, cx + 3.3f * u, r.Top + 5.9f * u);
                g.DrawLine(p, cx, r.Top + 5.9f * u, cx, r.Bottom - 1.2f * u);
            }
        }

        // underline: send the note away to the tray
        void DrawMinimizeIcon(Graphics g, Rectangle r)
        {
            float m = r.Width * 0.27f;
            using (var p = IconPen())
                g.DrawLine(p, r.Left + m, r.Bottom - m - 0.5f, r.Right - m, r.Bottom - m - 0.5f);
        }

        // one square to go full screen, two overlapping squares to go back
        void DrawFullIcon(Graphics g, Rectangle r, bool full)
        {
            float u = r.Width / 11f;
            using (var p = IconPen())
            {
                if (!full) g.DrawRectangle(p, r.Left + 2.5f * u, r.Top + 2.5f * u, 6f * u, 6f * u);
                else
                {
                    g.DrawRectangle(p, r.Left + 4f * u, r.Top + 1.5f * u, 5f * u, 5f * u);
                    g.DrawRectangle(p, r.Left + 1.5f * u, r.Top + 4f * u, 5f * u, 5f * u);
                }
            }
        }

        // chevron up (roll the note up), chevron down once rolled up
        void DrawCollapseIcon(Graphics g, Rectangle r, bool collapsed)
        {
            float u = r.Width / 11f, cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float dy = collapsed ? -1.6f * u : 1.6f * u;
            using (var p = IconPen())
            {
                g.DrawLine(p, cx - 3f * u, cy + dy, cx, cy - dy);
                g.DrawLine(p, cx, cy - dy, cx + 3f * u, cy + dy);
            }
        }

        int Edge(Point p)
        {
            if (note.IsFull) return 0;
            int e = NoteForm.S(5);
            bool top = p.Y < e, l = p.X < e, r = p.X >= Width - e;
            if (note.Data.Collapsed)
            {
                if (l) return NoteForm.HTLEFT;
                if (r) return NoteForm.HTRIGHT;
                return 0;
            }
            if (top && l) return NoteForm.HTTOPLEFT;
            if (top && r) return NoteForm.HTTOPRIGHT;
            if (top) return NoteForm.HTTOP;
            if (l) return NoteForm.HTLEFT;
            if (r) return NoteForm.HTRIGHT;
            return 0;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // the buttons are invisible on an inactive note, so the click that wakes it up must not press one
            bool live = !note.WasJustClickActivated;
            if (live && CloseBox.Contains(e.Location)) { note.CloseNote(); return; }
            if (live && CollapseBox.Contains(e.Location)) { note.ToggleCollapse(); return; }
            if (live && PinBox.Contains(e.Location)) { note.ToggleOnTop(); return; }
            if (live && MinBox.Contains(e.Location)) { note.MinimizeNote(); return; }
            if (live && FullBox.Contains(e.Location)) { note.ToggleFullScreen(); return; }
            if (e.Clicks == 2) { maybeDrag = false; note.ToggleCollapse(); return; }
            if (note.IsFull) return;       // can't drag or resize while full screen

            int edge = Edge(e.Location);
            if (edge != 0) { note.BeginNc(edge); return; }

            // Start moving only after the mouse moves a little, so double-click still works
            downAt = e.Location;
            maybeDrag = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (maybeDrag && e.Button == MouseButtons.Left)
            {
                Size d = SystemInformation.DragSize;
                if (Math.Abs(e.X - downAt.X) > d.Width / 2 || Math.Abs(e.Y - downAt.Y) > d.Height / 2)
                {
                    maybeDrag = false;
                    note.BeginNc(NoteForm.HTCAPTION);
                }
                return;
            }

            // tooltip for the boxes
            string tt = !note.IsActiveNote ? null
                : CloseBox.Contains(e.Location) ? "Close"
                : PinBox.Contains(e.Location) ? (note.Data.OnTop ? "Stop keeping on top" : "Keep on top of other windows")
                : MinBox.Contains(e.Location) ? "Minimize (bring back from the tray icon)"
                : FullBox.Contains(e.Location) ? (note.IsFull ? "Back to normal size" : "Full screen")
                : CollapseBox.Contains(e.Location) ? (note.Data.Collapsed ? "Expand" : "Collapse") : null;
            if (tt != lastTip) { lastTip = tt; tip.SetToolTip(this, tt ?? ""); }

            int edge = Edge(e.Location);
            if (edge == NoteForm.HTLEFT || edge == NoteForm.HTRIGHT) Cursor = Cursors.SizeWE;
            else if (edge == NoteForm.HTTOP) Cursor = Cursors.SizeNS;
            else if (edge == NoteForm.HTTOPLEFT) Cursor = Cursors.SizeNWSE;
            else if (edge == NoteForm.HTTOPRIGHT) Cursor = Cursors.SizeNESW;
            else Cursor = Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            maybeDrag = false;
        }
    }
}
