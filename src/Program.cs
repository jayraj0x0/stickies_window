using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stickies_Window
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        const string MutexName = "Stickies_Window-SingleInstance-v2";
        public const string ShowEventName = "Stickies_Window-ShowNotes-v2";

        // Files to open (when you double-click a .sticky file, or "Open with" Stickies_Window)
        public static readonly List<string> StartupFiles = new List<string>();

        // Another copy is already running: hand it the file names through small request files
        public static string RequestFolder { get { return System.IO.Path.Combine(Store.ConfigFolder, "open-requests"); } }

        [STAThread]
        static void Main(string[] args)
        {
            foreach (string a in args)
                try { if (System.IO.File.Exists(a)) StartupFiles.Add(System.IO.Path.GetFullPath(a)); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstInstance;
            using (var mutex = new System.Threading.Mutex(true, MutexName, out firstInstance))
            {
                // Already running: ask that copy to show its notes
                if (!firstInstance)
                {
                    try
                    {
                        if (StartupFiles.Count > 0)
                        {
                            System.IO.Directory.CreateDirectory(RequestFolder);
                            foreach (string f in StartupFiles)
                                System.IO.File.WriteAllText(System.IO.Path.Combine(RequestFolder, Guid.NewGuid().ToString("N") + ".req"), f);
                        }
                        using (var ev = System.Threading.EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                    }
                    catch
                    {
                        MessageBox.Show(
                            "Stickies_Window is already running but not responding.\n\n" +
                            "Open Task Manager, end \"Stickies_Window\", then open it again.",
                            "Stickies_Window", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                // We are the only real copy: close any old stuck copies left from older versions
                try
                {
                    var me = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
                    {
                        if (p.Id == me.Id) continue;
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                }
                catch { }

                // Any crash shows a message instead of closing silently
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ShowError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

                try
                {
                    try { SetProcessDPIAware(); } catch { }
                    Log.Write("start");
                    Application.Run(new NoteManager());
                    Log.Write("exit");
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        // Shows the error and saves it to error.txt in the notes folder
        static void ShowError(Exception ex)
        {
            string msg = ex == null ? "Unknown error" : ex.ToString();
            try
            {
                System.IO.Directory.CreateDirectory(Store.ConfigFolder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(Store.ConfigFolder, "error.txt"), msg);
            }
            catch { }
            Log.Write("error: " + msg);
            MessageBox.Show("Stickies_Window hit an error:\n\n" + msg, "Stickies_Window",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Small log in the notes folder, to find problems
    static class Log
    {
        public static void Write(string line)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Store.ConfigFolder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(Store.ConfigFolder, "log.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
        }
    }

    // Owns all notes, the tray icon and saving
    class NoteManager : ApplicationContext
    {
        public static NoteManager Instance;
        public StoreData Data;

        readonly List<NoteForm> notes = new List<NoteForm>();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer saveTimer;
        readonly Control uiThread = new Control();
        readonly System.Threading.EventWaitHandle showEvent;
        HotkeyWindow hotkey;
        ToolStripMenuItem trayNewNote;
        bool settingsOpen;
        DateTime lastSaveWarning = DateTime.MinValue;
        readonly Random rng = new Random();
        readonly List<ClosedNote> closed = new List<ClosedNote>();
        ToolStripMenuItem trayReopen, trayHidden;

        class ClosedNote { public NoteData Data; public string Rtf; }

        public NoteManager()
        {
            Instance = this;
            InitStorage();
            Data = Store.Load();
            Palette.Apply(Data.PaletteColors, Data.DisabledColors);
            Log.Write("loaded " + Data.Notes.Count + " notes");

            // Opening the app again brings all notes to the front
            uiThread.CreateControl();
            showEvent = new System.Threading.EventWaitHandle(false,
                System.Threading.EventResetMode.AutoReset, Program.ShowEventName);
            System.Threading.ThreadPool.RegisterWaitForSingleObject(showEvent, (st, timedOut) =>
            {
                try { uiThread.BeginInvoke((Action)(() => { if (!ProcessOpenRequests()) BringAllToFront(); })); } catch { }
            }, null, -1, false);

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 800;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveAll(); };

            var menu = new ContextMenuStrip();
            trayNewNote = new ToolStripMenuItem("New Note", null, (s, e) => NewNote());
            menu.Items.Add(trayNewNote);
            trayReopen = new ToolStripMenuItem("Reopen Closed Note", null, (s, e) => ReopenClosed());
            trayReopen.ShortcutKeyDisplayString = "Ctrl+Shift+T";
            menu.Items.Add(trayReopen);
            trayHidden = new ToolStripMenuItem("Minimized Notes");
            menu.Items.Add(trayHidden);
            menu.Opening += (s, e) =>
            {
                trayReopen.Enabled = closed.Count > 0;
                trayHidden.DropDownItems.Clear();
                foreach (var n in notes.ToList())
                {
                    if (n.Visible) continue;
                    NoteForm note = n;
                    string title = note.FirstLine;
                    if (title.Length == 0) title = "(empty note)";
                    if (title.Length > 40) title = title.Substring(0, 40) + "…";
                    trayHidden.DropDownItems.Add(title.Replace("&", "&&"), null, (ss, ee) => note.RestoreNote());
                }
                trayHidden.Enabled = trayHidden.DropDownItems.Count > 0;
            };
            var arrange = new ToolStripMenuItem("Arrange By");
            AddArrangeItems(arrange);
            menu.Items.Add(arrange);
            menu.Items.Add("Bring All to Front", null, (s, e) => BringAllToFront());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings…", null, (s, e) => ShowSettings(null));
            menu.Items.Add("Open Notes Folder", null, (s, e) => OpenNotesFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit Stickies_Window", null, (s, e) => Quit());

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "Stickies_Window";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) BringAllToFront(); };
            tray.Visible = true;

            foreach (var d in Data.Notes.ToList()) Open(d, false);
            if (notes.Count == 0 && Program.StartupFiles.Count == 0) NewNote();
            foreach (string file in Program.StartupFiles) OpenFile(file);
            FileAssociation.Register();
            StartupHelper.Refresh();
            Log.Write("ready, " + notes.Count + " notes open");

            // Global shortcut: new note from anywhere
            hotkey = new HotkeyWindow();
            hotkey.Pressed += OnHotkey;
            ApplyHotkeys(true);

            // Save everything when Windows shuts down or you sign out
            Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => SaveAll();
        }

        public NoteForm NewNote(string rtf = null, string plain = null, bool fromHotkey = false)
        {
            var d = new NoteData();
            d.Id = Guid.NewGuid().ToString("N");
            d.Width = NoteForm.S(260);
            d.Height = NoteForm.S(220);
            Point loc = NewNoteLocation(d.Width, d.Height);
            d.X = loc.X;
            d.Y = loc.Y;
            PickColorForNewNote(d);
            d.Created = DateTime.Now;
            Data.Notes.Add(d);

            NoteForm f = Open(d, true);
            if (rtf != null) f.LoadRtf(rtf);
            else if (plain != null) f.LoadPlain(plain);
            if (fromHotkey) f.ForceToFront(); else f.Activate();
            f.FocusText();
            QueueSave();
            return f;
        }

        NoteForm Open(NoteData d, bool isNew)
        {
            var f = new NoteForm(d, isNew);
            notes.Add(f);
            f.Show();
            return f;
        }

        // Deletes a note for good
        public void Remove(NoteForm f)
        {
            Log.Write("note deleted:\n" + Environment.StackTrace);
            notes.Remove(f);
            Data.Notes.Remove(f.Data);
            Store.Delete(f.Data.Id);
            SaveIndex();
        }

        public void QueueSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void SaveAll()
        {
            foreach (var n in notes) n.SaveContent();
            SaveIndex();
        }

        void SaveIndex()
        {
            foreach (var n in notes) n.SyncData();
            try { Store.Save(Data); } catch (Exception ex) { ReportSaveError(ex); }
        }

        public void BringAllToFront()
        {
            foreach (var n in notes) n.ForceToFront();
        }

        public void AddArrangeItems(ToolStripMenuItem parent)
        {
            parent.DropDownItems.Add("Color", null, (s, e) =>
                Arrange((a, b) => a.Data.ColorIndex.CompareTo(b.Data.ColorIndex)));
            parent.DropDownItems.Add("Content", null, (s, e) =>
                Arrange((a, b) => string.Compare(a.FirstLine, b.FirstLine, StringComparison.CurrentCultureIgnoreCase)));
            parent.DropDownItems.Add("Date", null, (s, e) =>
                Arrange((a, b) => a.Data.Created.CompareTo(b.Data.Created)));
            parent.DropDownItems.Add("Position on Screen", null, (s, e) =>
                Arrange((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left)));
        }

        // Stacks notes from the top-left corner in the chosen order
        void Arrange(Comparison<NoteForm> order)
        {
            var list = notes.ToList();
            list.Sort(order);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int step = NoteForm.S(26);
            int x = wa.Left + NoteForm.S(20), y = wa.Top + NoteForm.S(20);
            foreach (var n in list)
            {
                if (y + NoteForm.S(60) > wa.Bottom) { y = wa.Top + NoteForm.S(20); x += NoteForm.S(60); }
                n.Location = new Point(x, y);
                n.BringToFront();
                x += step;
                y += step;
            }
            QueueSave();
        }

        // ---------- where a new note opens ----------
        // Position setting 0..8: rows top/middle/bottom, columns left/center/right
        Point NewNoteLocation(int w, int h)
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int pos = Math.Max(0, Math.Min(8, Data.NewNotePosition));
            int col = pos % 3, row = pos / 3;
            int margin = NoteForm.S(60);
            int off = (notes.Count % 10) * NoteForm.S(24);   // each extra note steps inward a bit

            int x = col == 0 ? wa.Left + margin : col == 1 ? wa.Left + (wa.Width - w) / 2 : wa.Right - w - margin;
            int y = row == 0 ? wa.Top + margin : row == 1 ? wa.Top + (wa.Height - h) / 2 : wa.Bottom - h - margin;
            x += col == 2 ? -off : off;
            y += row == 2 ? -off : off;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
            return new Point(x, y);
        }

        // ---------- new note color ----------
        void PickColorForNewNote(NoteData d)
        {
            List<int> active = Palette.ActiveIndexes();
            int mode = Data.ColorMode;
            bool firstNote = notes.Count == 0;     // no other note is open

            if (mode == 1)
            {
                d.ColorIndex = active[rng.Next(active.Count)];
                return;
            }

            // "My color": always (mode 2), or only for the first note when none are open (mode 3)
            if (mode == 2 || (mode == 3 && firstNote))
            {
                if (Data.DefaultCustomColor != 0)
                {
                    d.ColorIndex = active[0];
                    d.CustomColor = Data.DefaultCustomColor;
                    if (mode == 3) Data.NextColorIndex = active[0];
                }
                else
                {
                    int idx = Palette.IsFilled(Data.DefaultColor) ? Data.DefaultColor : active[0];
                    d.ColorIndex = idx;
                    if (mode == 3) Data.NextColorIndex = (idx + 1) % Palette.Slots;
                }
                return;
            }

            // Cycle through the colors that are switched on
            int n = Palette.Slots;
            int start = ((Data.NextColorIndex % n) + n) % n;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                if (Palette.IsActive(i))
                {
                    d.ColorIndex = i;
                    Data.NextColorIndex = (i + 1) % n;
                    return;
                }
            }
            d.ColorIndex = active[0];
        }

        // ---------- reopen closed notes ----------
        public void RememberClosed(NoteData d, string rtf)
        {
            closed.Add(new ClosedNote { Data = d.Clone(), Rtf = rtf });
            if (closed.Count > 25) closed.RemoveAt(0);
        }

        // Ctrl+Shift+T: brings back the most recently closed note
        public bool ReopenClosed()
        {
            if (closed.Count == 0) { System.Media.SystemSounds.Beep.Play(); return false; }
            ClosedNote c = closed[closed.Count - 1];
            closed.RemoveAt(closed.Count - 1);
            NoteData d = c.Data;
            d.Id = Guid.NewGuid().ToString("N");
            Data.Notes.Add(d);
            NoteForm f = Open(d, true);
            f.LoadRtf(c.Rtf);
            f.ForceToFront();
            f.FocusText();
            QueueSave();
            return true;
        }

        // Where the other notes are (used for snapping notes together)
        public List<Rectangle> OtherBounds(ICollection<NoteForm> exclude)
        {
            var l = new List<Rectangle>();
            foreach (var n in notes)
                if (!exclude.Contains(n) && n.Visible) l.Add(n.Bounds);
            return l;
        }

        // Two notes count as snapped together when an edge of one sits against an edge of the other
        // (not when they merely overlap, and not when they only touch at a corner)
        static bool Touching(Rectangle a, Rectangle b)
        {
            Rectangle x = Rectangle.Intersect(Rectangle.Inflate(a, 3, 3), b);
            if (x.IsEmpty) return false;
            return Math.Min(x.Width, x.Height) <= NoteForm.S(8) && Math.Max(x.Width, x.Height) >= NoteForm.S(24);
        }

        // The note plus every note chained to it by snapped edges
        public List<NoteForm> SnappedGroup(NoteForm start)
        {
            var group = new List<NoteForm>();
            group.Add(start);
            for (int i = 0; i < group.Count; i++)
                foreach (var n in notes)
                    if (!group.Contains(n) && n.Visible && Touching(group[i].Bounds, n.Bounds)) group.Add(n);
            return group;
        }

        // Ctrl+Shift+Alt+F: keep this note and every note snapped to it on top, and bring them forward.
        // If they are all on top already, it switches that off for the group.
        public void ToggleKeepGroupOnTop(NoteForm from)
        {
            List<NoteForm> group = SnappedGroup(from);
            bool allOn = true;
            foreach (var n in group) if (!n.Data.OnTop) allOn = false;
            foreach (var n in group) n.SetOnTop(!allOn);
            if (!allOn)
            {
                foreach (var n in group) if (n != from) n.ForceToFront();
                from.ForceToFront();       // last, so this note keeps the focus
            }
            QueueSave();
        }

        // ---------- opening files ----------
        // Opens a .sticky note (just where and how it was saved), or a .rtf / .txt file, as a sticky note
        public NoteForm OpenFile(string path)
        {
            try
            {
                foreach (var n in notes)
                    if (n.HasFile && Store.SamePath(n.Data.FilePath, path)) { n.RestoreNote(); return n; }   // already open

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                NoteForm f;
                if (ext == StickyFile.Extension)
                {
                    StickyFileData s = StickyFile.Read(path);
                    var d = new NoteData();
                    d.Id = Guid.NewGuid().ToString("N");
                    d.X = s.X; d.Y = s.Y;
                    d.Width = Math.Max(s.Width, NoteForm.S(150));
                    d.Height = Math.Max(s.Height, NoteForm.S(60));
                    d.ColorIndex = (s.ColorIndex >= 0 && s.ColorIndex < Palette.Slots) ? s.ColorIndex : 0;
                    if (s.ColorArgb != 0)
                    {
                        Color saved = Palette.FromArgbInt(s.ColorArgb);
                        // keep using the palette color if it is still the same color, otherwise keep this exact color
                        d.CustomColor = (Palette.IsFilled(d.ColorIndex) && Palette.Colors[d.ColorIndex].ToArgb() == saved.ToArgb())
                            ? 0 : saved.ToArgb();
                    }
                    d.OnTop = s.OnTop; d.Translucent = s.Translucent; d.Collapsed = s.Collapsed;
                    d.Created = DateTime.Now;
                    d.FilePath = path;
                    Data.Notes.Add(d);
                    f = Open(d, true);
                    f.LoadRtf(s.Rtf ?? "");
                }
                else
                {
                    string content = System.IO.File.ReadAllText(path);
                    f = ext == ".rtf" ? NewNote(content, null) : NewNote(null, content);
                    f.Data.FilePath = path;
                }
                f.MarkLinkedClean();
                f.ForceToFront();
                f.FocusText();
                QueueSave();
                return f;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open " + path + "\n\n" + ex.Message, "Stickies_Window",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        // Files handed over by another copy of the program. Returns true if there were any
        bool ProcessOpenRequests()
        {
            bool any = false;
            try
            {
                if (!System.IO.Directory.Exists(Program.RequestFolder)) return false;
                foreach (string req in System.IO.Directory.GetFiles(Program.RequestFolder, "*.req"))
                {
                    string path = null;
                    try { path = System.IO.File.ReadAllText(req).Trim(); System.IO.File.Delete(req); } catch { }
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) { OpenFile(path); any = true; }
                }
            }
            catch { }
            return any;
        }

        public void RefreshLook()
        {
            foreach (var n in notes) n.RefreshLook();
        }

        // ---------- storage ----------
        // First run: ask where to save. Later runs: only ask if the folder stopped working
        void InitStorage()
        {
            Store.LoadConfig();
            bool first = !Store.Config.SetupDone;
            string err;
            bool ok = Store.EnsureWritable(Store.Folder, out err);
            if (!first && ok) return;

            if (!first)
                MessageBox.Show("Stickies_Window can't use your notes folder:\n\n" + Store.Folder + "\n\n" + err +
                    "\n\nPlease choose another folder. Your notes in the old folder are not touched.",
                    "Stickies_Window", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            for (;;)
            {
                string chosen = FolderForm.Ask(first, Store.Folder);
                if (chosen == null) chosen = ok ? Store.Folder : Store.ConfigFolder;

                if (!Store.SamePath(chosen, Store.Folder))
                {
                    if (!Store.EnsureWritable(chosen, out err))
                    {
                        MessageBox.Show("Can't save to that folder:\n\n" + err, "Stickies_Window",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    try
                    {
                        // Bring existing notes along, unless that folder already has its own
                        if (!Store.HasLibrary(chosen) && Store.HasLibrary(Store.Folder))
                            Store.CopyLibrary(Store.Folder, chosen);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Couldn't copy your notes there:\n\n" + ex.Message, "Stickies_Window",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                }
                Store.SetFolder(chosen);
                break;
            }
        }

        // Switches to another notes folder while running. Returns false if nothing changed
        public bool ChangeFolder(string target)
        {
            if (Store.SamePath(target, Store.Folder)) return true;
            string err;
            if (!Store.EnsureWritable(target, out err))
            {
                MessageBox.Show("Can't save to that folder:\n\n" + err, "Stickies_Window",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            bool targetHas = Store.HasLibrary(target);
            if (targetHas && MessageBox.Show(
                "That folder already contains Stickies notes.\n\nSwitch to those notes? " +
                "Your current notes stay in the old folder.", "Stickies_Window",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            saveTimer.Stop();
            SaveAll();
            try
            {
                if (targetHas)
                {
                    foreach (var n in notes.ToList()) n.CloseSilently();
                    notes.Clear();
                }
                else Store.CopyLibrary(Store.Folder, target);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't move your notes:\n\n" + ex.Message, "Stickies_Window",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Store.SetFolder(target);
            if (targetHas)
            {
                Data = Store.Load();
                Palette.Apply(Data.PaletteColors, Data.DisabledColors);
                foreach (var d in Data.Notes.ToList()) Open(d, false);
                if (notes.Count == 0) NewNote();
            }
            else SaveAll();
            Log.Write("notes folder changed to " + target);
            return true;
        }

        public void OpenNotesFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Store.Folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.Folder + "\"");
            }
            catch { }
        }

        // Tells the user when saving fails (at most once a minute)
        public void ReportSaveError(Exception ex)
        {
            Log.Write("save error: " + ex.Message);
            if ((DateTime.Now - lastSaveWarning).TotalSeconds < 60) return;
            lastSaveWarning = DateTime.Now;
            try
            {
                tray.ShowBalloonTip(6000, "Stickies_Window",
                    "Couldn't save your notes to " + Store.Folder + "\n" + ex.Message, ToolTipIcon.Warning);
            }
            catch { }
        }

        // ---------- settings / shortcut ----------
        public void ShowSettings(IWin32Window owner)
        {
            if (settingsOpen) return;
            settingsOpen = true;
            try { using (var f = new SettingsForm()) f.ShowDialog(owner); }
            finally { settingsOpen = false; }
        }

        public static string HotkeyText(int keys)
        {
            return HotkeyBox.Describe((Keys)keys);
        }

        // The three global shortcuts: 1 = new note, 2 = settings, 3 = bring all to front
        const int HkNew = 1, HkSettings = 2, HkFront = 3;

        void OnHotkey(int id)
        {
            if (id == HkNew) NewNote(null, null, true);
            else if (id == HkSettings) { try { uiThread.BeginInvoke((Action)(() => ShowSettings(null))); } catch { } }
            else if (id == HkFront) BringAllToFront();
        }

        // Registers all global shortcuts. Returns false if another program already uses one of them
        public bool ApplyHotkeys(bool quiet)
        {
            hotkey.UnregisterAll();
            var c = Store.Config;
            var clashes = new List<string>();
            TryRegister(HkNew, c.HotkeyEnabled, c.HotkeyKeys, "New note", clashes);
            TryRegister(HkSettings, c.SettingsHotkeyEnabled, c.SettingsHotkeyKeys, "Settings", clashes);
            TryRegister(HkFront, c.FrontHotkeyEnabled, c.FrontHotkeyKeys, "Bring all to front", clashes);

            trayNewNote.ShortcutKeyDisplayString = hotkey.IsRegistered(HkNew) ? HotkeyText(c.HotkeyKeys) : null;
            if (clashes.Count == 0) return true;

            string msg = "These shortcuts are already used by another program:\n\n  " + string.Join("\n  ", clashes.ToArray()) +
                         "\n\nPick a different shortcut in Settings, General.";
            if (!quiet) MessageBox.Show(msg, "Stickies_Window", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else try { tray.ShowBalloonTip(7000, "Stickies_Window", msg.Replace("\n\n", "\n"), ToolTipIcon.Warning); } catch { }
            return false;
        }

        void TryRegister(int id, bool enabled, int keys, string name, List<string> clashes)
        {
            if (!enabled) return;
            if (hotkey.Register(id, (Keys)keys)) return;
            Log.Write("hotkey failed: " + name + " " + HotkeyText(keys));
            clashes.Add(name + "  (" + HotkeyText(keys) + ")");
        }

        public void Quit()
        {
            saveTimer.Stop();
            SaveAll();
            if (hotkey != null) hotkey.UnregisterAll();
            tray.Visible = false;
            tray.Dispose();
            foreach (var n in notes.ToList()) n.CloseSilently();
            ExitThread();
        }

        // A small yellow note for the tray
        static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = NoteForm.RoundRect(new Rectangle(3, 3, 26, 26), 5))
                using (var b = new SolidBrush(Palette.Colors[0]))
                using (var p = new Pen(Color.FromArgb(120, Palette.Text), 1.5f))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
                using (var p = new Pen(Color.FromArgb(150, Palette.Text), 2f))
                {
                    g.DrawLine(p, 9, 13, 23, 13);
                    g.DrawLine(p, 9, 19, 19, 19);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
