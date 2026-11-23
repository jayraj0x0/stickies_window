using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Stickies_Window
{
    // The note's right-click menu and all its commands
    partial class NoteForm
    {
        ToolStripMenuItem miCustomColor, miOnTop, miTranslucent, miCollapse, miBold, miItalic, miUnderline;
        readonly ToolStripMenuItem[] miColors = new ToolStripMenuItem[Palette.Slots];
        FindReplaceForm findForm;

        static ToolStripMenuItem Item(string name, string keys, EventHandler click)
        {
            var i = new ToolStripMenuItem(name, null, click);
            if (keys != null) i.ShortcutKeyDisplayString = keys;
            return i;
        }

        void BuildMenu()
        {
            var m = new ContextMenuStrip();

            m.Items.Add(Item("New Note", "Ctrl+N / Ctrl+T", (s, e) => NoteManager.Instance.NewNote()));
            m.Items.Add(Item("Reopen Closed Note", "Ctrl+Shift+T", (s, e) => NoteManager.Instance.ReopenClosed()));
            m.Items.Add(Item("Close", "Ctrl+W", (s, e) => CloseNote()));
            m.Items.Add(new ToolStripSeparator());

            var file = new ToolStripMenuItem("File");
            file.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Open…", "Ctrl+O", (s, e) => OpenFilesDialog()),
                Item("Save", "Ctrl+S", (s, e) => SaveNote()),
                Item("Save As…", "Ctrl+Shift+S", (s, e) => SaveAs()),
                Item("Open Notes Folder", null, (s, e) => NoteManager.Instance.OpenNotesFolder()),
                new ToolStripSeparator(),
                Item("Print…", "Ctrl+P", (s, e) => PrintNote())
            });

            var edit = new ToolStripMenuItem("Edit");
            edit.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Undo", "Ctrl+Z", (s, e) => text.Undo()),
                Item("Redo", "Ctrl+Y", (s, e) => text.Redo()),
                new ToolStripSeparator(),
                Item("Cut", "Ctrl+X", (s, e) => text.Cut()),
                Item("Copy", "Ctrl+C", (s, e) => text.Copy()),
                Item("Paste", "Ctrl+V", (s, e) => text.Paste()),
                Item("Select All", "Ctrl+A", (s, e) => text.SelectAll()),
                new ToolStripSeparator(),
                Item("Find…", "Ctrl+F", (s, e) => Find()),
                Item("Replace…", "Ctrl+H", (s, e) => ShowReplace()),
                Item("Find Next", "Ctrl+G", (s, e) => FindNext())
            });

            miBold = ActionItem("bold");
            miItalic = ActionItem("italic");
            miUnderline = ActionItem("underline");
            var font = new ToolStripMenuItem("Font");
            font.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Show Fonts", "Ctrl+Shift+F", (s, e) => ShowFonts()),
                new ToolStripSeparator(),
                miBold, miItalic, miUnderline,
                ActionItem("strike"),
                new ToolStripSeparator(),
                Item("Bigger", "Ctrl++", (s, e) => ChangeSize(1)),
                Item("Smaller", "Ctrl+-", (s, e) => ChangeSize(-1)),
                new ToolStripSeparator(),
                Item("Show Colors", "Ctrl+Shift+C", (s, e) => ShowColors())
            });

            var format = new ToolStripMenuItem("Format");
            format.DropDownItems.AddRange(new ToolStripItem[]
            {
                ActionItem("highlight"),
                new ToolStripSeparator(),
                ActionItem("task"), ActionItem("taskcheck")
            });

            var align = new ToolStripMenuItem("Text");
            align.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Align Left", "Ctrl+Q", (s, e) => text.SelectionAlignment = HorizontalAlignment.Left),
                Item("Center", "Ctrl+E", (s, e) => text.SelectionAlignment = HorizontalAlignment.Center),
                Item("Align Right", "Ctrl+R", (s, e) => text.SelectionAlignment = HorizontalAlignment.Right)
            });

            var color = new ToolStripMenuItem("Color");
            for (int i = 0; i < Palette.Slots; i++)
            {
                int idx = i;
                miColors[i] = Item(Palette.Name(i), null, (s, e) => SetColor(idx));
                if (Palette.IsFilled(i)) miColors[i].Image = Swatch(Palette.Colors[i]);
                color.DropDownItems.Add(miColors[i]);
            }
            color.DropDownItems.Add(new ToolStripSeparator());
            miCustomColor = Item("Custom Color…", "Ctrl+Shift+Alt+C", (s, e) => PickCustomColor());
            color.DropDownItems.Add(miCustomColor);

            miOnTop = Item("Float on Top", "Ctrl+Alt+F", (s, e) => ToggleOnTop());
            miTranslucent = Item("Translucent", "Ctrl+Alt+T", (s, e) => ToggleTranslucent());
            miCollapse = Item("Collapse", "Ctrl+M", (s, e) => ToggleCollapse());
            var arrange = new ToolStripMenuItem("Arrange By");
            NoteManager.Instance.AddArrangeItems(arrange);
            var window = new ToolStripMenuItem("Window");
            window.DropDownItems.AddRange(new ToolStripItem[]
            {
                miOnTop, miTranslucent,
                new ToolStripSeparator(),
                miCollapse,
                Item("Full Screen", "F11", (s, e) => ToggleFullScreen()),
                Item("Minimize", "Ctrl+Shift+M", (s, e) => MinimizeNote()),
                new ToolStripSeparator(),
                Item("Keep Snapped Notes on Top", "Ctrl+Shift+Alt+F", (s, e) => NoteManager.Instance.ToggleKeepGroupOnTop(this)),
                Item("Use This Text Style for New Notes", null, (s, e) => UseAsDefault()),
                arrange,
                Item("Bring All to Front", null, (s, e) => NoteManager.Instance.BringAllToFront())
            });

            m.Items.AddRange(new ToolStripItem[] { file, edit, font, format, align, color, window });

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Settings…", "Ctrl+,", (s, e) => NoteManager.Instance.ShowSettings(this)));
            m.Items.Add(Item("Quit Stickies_Window", null, (s, e) => NoteManager.Instance.Quit()));

            // Show ticks for the current settings
            m.Opening += (s, e) =>
            {
                foreach (var kv in actionItems)
                    kv.Value.ShortcutKeyDisplayString = FormatActions.Text(kv.Key);
                miOnTop.Checked = TopMost;
                miTranslucent.Checked = Data.Translucent;
                miCollapse.Text = Data.Collapsed ? "Expand" : "Collapse";
                Font f = text.SelectionFont ?? text.Font;
                miBold.Checked = f.Bold;
                miItalic.Checked = f.Italic;
                miUnderline.Checked = f.Underline;
                for (int i = 0; i < miColors.Length; i++)
                {
                    // only colors that are filled in and switched on are listed
                    miColors[i].Visible = Palette.IsActive(i);
                    miColors[i].Text = Palette.Name(i);
                    miColors[i].Checked = (Data.CustomColor == 0 && i == Data.ColorIndex);
                    if (Palette.IsFilled(i)) miColors[i].Image = Swatch(Palette.Colors[i]);
                }
                miCustomColor.Checked = Data.CustomColor != 0;
            };

            ContextMenuStrip = m;
            text.ContextMenuStrip = m;
            bar.ContextMenuStrip = m;
        }

        static Bitmap Swatch(Color c)
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b))
            using (var br = new SolidBrush(c))
            using (var p = new Pen(Color.FromArgb(90, Palette.Text)))
            {
                g.FillRectangle(br, 1, 1, 14, 14);
                g.DrawRectangle(p, 1, 1, 13, 13);
            }
            return b;
        }

        // ---------- keyboard shortcuts ----------
        readonly Dictionary<string, ToolStripMenuItem> actionItems = new Dictionary<string, ToolStripMenuItem>();

        // A menu item for a formatting command (its shortcut text is filled in when the menu opens)
        ToolStripMenuItem ActionItem(string id)
        {
            var mi = new ToolStripMenuItem(FormatActions.Def(id).Title);
            mi.Click += (s, e) => RunFormat(id);
            actionItems[id] = mi;
            return mi;
        }

        public bool HandleKey(Keys k)
        {
            string act = FormatActions.Find(k);
            if (act != null) { RunFormat(act); return true; }
            switch (k)
            {
                case Keys.Control | Keys.N:
                case Keys.Control | Keys.T: NoteManager.Instance.NewNote(); return true;
                case Keys.Control | Keys.Shift | Keys.T: NoteManager.Instance.ReopenClosed(); return true;
                case Keys.Control | Keys.Shift | Keys.F: ShowFonts(); return true;
                case Keys.Control | Keys.W: CloseNote(); return true;
                case Keys.F11: ToggleFullScreen(); return true;
                case Keys.Control | Keys.Shift | Keys.M: MinimizeNote(); return true;
                case Keys.Control | Keys.Alt | Keys.Shift | Keys.C: PickCustomColor(); return true;
                case Keys.Control | Keys.P: PrintNote(); return true;
                case Keys.Control | Keys.Y: text.Redo(); return true;
                case Keys.Control | Keys.F: Find(); return true;
                case Keys.Control | Keys.H: ShowReplace(); return true;
                case Keys.Control | Keys.S: SaveNote(); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveAs(); return true;
                case Keys.Control | Keys.O: OpenFilesDialog(); return true;
                case Keys.Control | Keys.Oemcomma: NoteManager.Instance.ShowSettings(this); return true;
                case Keys.Control | Keys.G: FindNext(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Shift | Keys.Oemplus:
                case Keys.Control | Keys.Add: ChangeSize(1); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: ChangeSize(-1); return true;
                case Keys.Control | Keys.Shift | Keys.C: ShowColors(); return true;
                case Keys.Control | Keys.Q: text.SelectionAlignment = HorizontalAlignment.Left; return true;
                case Keys.Control | Keys.E: text.SelectionAlignment = HorizontalAlignment.Center; return true;
                case Keys.Control | Keys.R: text.SelectionAlignment = HorizontalAlignment.Right; return true;
                case Keys.Control | Keys.Alt | Keys.F: ToggleOnTop(); return true;
                case Keys.Control | Keys.Alt | Keys.Shift | Keys.F: NoteManager.Instance.ToggleKeepGroupOnTop(this); return true;
                case Keys.Control | Keys.Alt | Keys.T: ToggleTranslucent(); return true;
                case Keys.Control | Keys.M: ToggleCollapse(); return true;
            }
            return false;
        }

        // ---------- Color / Window ----------
        void SetColor(int i)
        {
            Data.ColorIndex = i;
            Data.CustomColor = 0;
            ApplyColor();
            ApplyNoteTextColor();
            NoteManager.Instance.QueueSave();
        }

        void PickCustomColor()
        {
            Color c = Palette.For(Data);
            if (ColorPickerForm.Pick(this, "Note color", ref c, false) != DialogResult.OK) return;
            Data.CustomColor = c.ToArgb();
            ApplyColor();
            ApplyNoteTextColor();
            NoteManager.Instance.QueueSave();
        }

        public void ToggleOnTop() { SetOnTop(!Data.OnTop); NoteManager.Instance.QueueSave(); }

        public void SetOnTop(bool on)
        {
            Data.OnTop = on;
            TopMost = on;
            bar.Invalidate();
        }

        void ToggleTranslucent()
        {
            Data.Translucent = !Data.Translucent;
            Opacity = Data.Translucent ? 0.75 : 1.0;
            NoteManager.Instance.QueueSave();
        }

        // New notes will use this note's font, size, style and text color (note colors are set in Settings)
        void UseAsDefault()
        {
            var store = NoteManager.Instance.Data;
            Font f = text.SelectionFont ?? text.Font;
            store.DefaultFontName = f.Name;
            store.DefaultFontSize = f.Size;
            store.DefaultFontStyle = (int)f.Style;
            store.DefaultTextColor = text.SelectionColor.ToArgb();
            NoteManager.Instance.QueueSave();
        }


        void ChangeSize(float delta)
        {
            Font f = text.SelectionFont ?? text.Font;
            float size = Math.Max(6f, Math.Min(96f, f.Size + delta));
            try { text.SelectionFont = new Font(f.FontFamily, size, f.Style); } catch { }
        }

        void ShowFonts()
        {
            using (var dlg = new FontDialog())
            {
                dlg.Font = text.SelectionFont ?? text.Font;
                dlg.ShowColor = true;
                dlg.Color = text.SelectionColor;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    text.SelectionFont = dlg.Font;
                    text.SelectionColor = dlg.Color;
                }
            }
        }

        void ShowColors()
        {
            Color c = text.SelectionColor;
            if (ColorPickerForm.Pick(this, "Text color", ref c, false) == DialogResult.OK) text.SelectionColor = c;
        }


        // ---------- Find / Replace ----------
        void Find() { OpenFindDialog(false); }
        void ShowReplace() { OpenFindDialog(true); }

        void OpenFindDialog(bool replace)
        {
            if (findForm == null || findForm.IsDisposed)
                findForm = new FindReplaceForm(this, text);
            findForm.Prepare(replace, text.SelectedText);
            if (!findForm.Visible) findForm.Show(this);
            findForm.Activate();
        }

        // Ctrl+G
        void FindNext()
        {
            if (findForm == null || findForm.IsDisposed || findForm.FindText.Length == 0) { Find(); return; }
            if (!findForm.FindNext()) System.Media.SystemSounds.Beep.Play();
        }

        // ---------- File ----------
        static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = s.Trim();
            if (s.Length > 40) s = s.Substring(0, 40).Trim();
            return s.Length == 0 ? "Untitled" : s;
        }

        void PrintNote()
        {
            string all = text.Text;
            int pos = 0;
            Font f = text.Font;
            using (var doc = new System.Drawing.Printing.PrintDocument())
            using (var dlg = new PrintDialog())
            {
                doc.DocumentName = SafeName(FirstLine);
                doc.BeginPrint += (s, e) => pos = 0;
                doc.PrintPage += (s, e) =>
                {
                    string rest = all.Substring(pos);
                    int chars, lines;
                    e.Graphics.MeasureString(rest, f, e.MarginBounds.Size, StringFormat.GenericTypographic, out chars, out lines);
                    if (chars <= 0) { e.HasMorePages = false; return; }
                    e.Graphics.DrawString(rest.Substring(0, chars), f, Brushes.Black, e.MarginBounds, StringFormat.GenericTypographic);
                    pos += chars;
                    e.HasMorePages = pos < all.Length;
                };
                dlg.Document = doc;
                dlg.UseEXDialog = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
            }
        }
    }
}
