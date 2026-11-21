using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stickies_Window
{
    // Settings window (tabs): new notes, colors, text, general
    class SettingsForm : Form
    {
        // working copies; nothing is applied until OK
        int colorMode, fixedIndex, fixedCustom, fontStyle, textColor, hotkeyKeys, position;
        int emphHighlight, emphColor;
        string fontName;
        float fontSize, emphSize;
        bool hotkeyOn;
        int setKeys, frontKeys;
        bool setOn, frontOn;
        bool rounded, titleIcons, taskStrike, snapNotes, macCaret, startup;
        int highlightColor;
        readonly Dictionary<string, Keys> binds = new Dictionary<string, Keys>();
        readonly Dictionary<string, HotkeyBox> bindBoxes = new Dictionary<string, HotkeyBox>();
        string pendingFolder;
        readonly Color[] palette = (Color[])Palette.Colors.Clone();
        readonly bool[] off = (bool[])Palette.Disabled.Clone();

        readonly TabControl tabs = new TabControl();
        readonly ComboBox cmbMode = new ComboBox();
        readonly ComboBox cmbFixed = new ComboBox();
        readonly List<int> fixedMap = new List<int>();      // combo row -> palette slot
        readonly Panel fixedSwatch = new Panel();
        readonly Label modeHint = new Label();
        readonly RadioButton[] posButtons = new RadioButton[9];
        readonly Button[] swatches = new Button[Palette.Slots];
        readonly CheckBox[] offBoxes = new CheckBox[Palette.Slots];
        readonly Button[] textBtns = new Button[Palette.Slots];            // "Aa": text color used on each note color
        readonly int[] slotText = (int[])Palette.TextColors.Clone();
        readonly ToolTip colorTip = new ToolTip();
        readonly Label preview = new Label();
        readonly NumericUpDown numEmph = new NumericUpDown();
        readonly CheckBox chkEmphBack = new CheckBox(), chkEmphColor = new CheckBox();
        readonly Button btnEmphBack = new Button(), btnEmphColor = new Button();
        readonly CheckBox chkHotkey = new CheckBox();
        readonly HotkeyBox hotkeyBox = new HotkeyBox();
        readonly CheckBox chkSet = new CheckBox(), chkFront = new CheckBox();
        readonly HotkeyBox setBox = new HotkeyBox(), frontBox = new HotkeyBox();
        readonly RadioButton rbSharp = new RadioButton(), rbRound = new RadioButton();
        readonly CheckBox chkIcons = new CheckBox(), chkTaskStrike = new CheckBox(), chkSnap = new CheckBox(), chkCaret = new CheckBox(), chkStartup = new CheckBox();
        readonly Button btnHighlight = new Button();
        readonly Label lblFolder = new Label();
        bool loading;

        static int S(int v) { return NoteForm.S(v); }

        public SettingsForm()
        {
            var store = NoteManager.Instance.Data;
            colorMode = store.ColorMode;
            fixedIndex = store.DefaultColor;
            fixedCustom = store.DefaultCustomColor;
            position = Math.Max(0, Math.Min(8, store.NewNotePosition));
            fontName = store.DefaultFontName;
            fontSize = store.DefaultFontSize;
            fontStyle = store.DefaultFontStyle;
            textColor = store.DefaultTextColor;
            emphSize = store.EmphasisSize;
            emphHighlight = store.EmphasisHighlight;
            emphColor = store.EmphasisColor;
            hotkeyKeys = Store.Config.HotkeyKeys;
            hotkeyOn = Store.Config.HotkeyEnabled;
            setKeys = Store.Config.SettingsHotkeyKeys;
            setOn = Store.Config.SettingsHotkeyEnabled;
            frontKeys = Store.Config.FrontHotkeyKeys;
            frontOn = Store.Config.FrontHotkeyEnabled;
            rounded = store.RoundedCorners;
            titleIcons = store.TitleBarIcons;
            snapNotes = store.SnapNotes;
            macCaret = store.MacCaret;
            startup = StartupHelper.IsEnabled();
            taskStrike = store.TaskStrike;
            highlightColor = store.HighlightColor;
            foreach (ActionDef a in FormatActions.All) binds[a.Id] = FormatActions.Get(a.Id);

            Text = "Stickies_Window Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(540), S(500));

            tabs.SetBounds(S(8), S(8), S(524), S(448));
            var tNew = new TabPage("New notes");
            var tColors = new TabPage("Colors");
            var tText = new TabPage("Text");
            var tFmt = new TabPage("Formatting");
            var tWin = new TabPage("Window");
            var tGen = new TabPage("General");
            tabs.TabPages.AddRange(new[] { tNew, tColors, tText, tFmt, tWin, tGen });
            Controls.Add(tabs);

            BuildNewNotesTab(tNew);
            BuildColorsTab(tColors);
            BuildTextTab(tText);
            BuildFormattingTab(tFmt);
            BuildWindowTab(tWin);
            BuildGeneralTab(tGen);

            var ok = new Button { Text = "OK" };
            ok.SetBounds(S(356), S(464), S(84), S(28));
            ok.Click += (s, e) => { if (Apply()) { DialogResult = DialogResult.OK; Close(); } };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(S(448), S(464), S(84), S(28));
            Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;

            tabs.SelectedIndexChanged += (s, e) => RefreshAll();

            loading = true;
            cmbMode.SelectedIndex = Math.Max(0, Math.Min(3, colorMode));
            posButtons[position].Checked = true;
            numEmph.Value = (decimal)Math.Max(0f, Math.Min(12f, emphSize));
            chkHotkey.Checked = hotkeyOn;
            hotkeyBox.Value = (Keys)hotkeyKeys;
            chkSet.Checked = setOn; setBox.Value = (Keys)setKeys;
            chkFront.Checked = frontOn; frontBox.Value = (Keys)frontKeys;
            rbRound.Checked = rounded; rbSharp.Checked = !rounded;
            chkIcons.Checked = titleIcons;
            chkSnap.Checked = snapNotes;
            chkCaret.Checked = macCaret;
            chkStartup.Checked = startup;
            chkTaskStrike.Checked = taskStrike;
            foreach (var kv in bindBoxes) kv.Value.Value = binds[kv.Key];
            loading = false;
            RefreshAll();
        }

        // ---------- tab: New notes ----------
        void BuildNewNotesTab(TabPage t)
        {
            var g1 = Group("Color of new notes", S(10), S(10), S(472), S(168));
            Lbl(g1, "When I open a note:", S(12), S(28), S(130));
            cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMode.Items.AddRange(new object[] {
                "Cycle through the colors", "Random color", "Always the same color", "Start with my color, then cycle" });
            cmbMode.SetBounds(S(146), S(24), S(230), S(24));
            g1.Controls.Add(cmbMode);

            Lbl(g1, "My color:", S(12), S(62), S(130));
            cmbFixed.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFixed.SetBounds(S(146), S(58), S(230), S(24));
            g1.Controls.Add(cmbFixed);
            fixedSwatch.SetBounds(S(384), S(58), S(40), S(24));
            fixedSwatch.BorderStyle = BorderStyle.FixedSingle;
            g1.Controls.Add(fixedSwatch);

            modeHint.SetBounds(S(12), S(94), S(448), S(66));
            modeHint.ForeColor = SystemColors.GrayText;
            g1.Controls.Add(modeHint);

            cmbMode.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                colorMode = Math.Max(0, cmbMode.SelectedIndex);
                RefreshAll();
            };
            cmbFixed.SelectedIndexChanged += (s, e) =>
            {
                if (loading || cmbFixed.SelectedIndex < 0) return;
                if (cmbFixed.SelectedIndex >= fixedMap.Count)       // "Custom color…"
                {
                    Color c = FixedColor();
                    if (ColorPickerForm.Pick(this, "My color for new notes", ref c, false) == DialogResult.OK)
                        fixedCustom = c.ToArgb();
                }
                else { fixedIndex = fixedMap[cmbFixed.SelectedIndex]; fixedCustom = 0; }
                RefreshAll();
            };

            var g2 = Group("Where new notes open on screen", S(10), S(188), S(472), S(150));
            string[] arrows = { "↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘" };
            string[] where = { "top left", "top", "top right", "left", "center", "right", "bottom left", "bottom", "bottom right" };
            var tip = new ToolTip();
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var rb = new RadioButton { Appearance = Appearance.Button, Text = arrows[i], TextAlign = ContentAlignment.MiddleCenter };
                rb.SetBounds(S(20) + (i % 3) * S(46), S(26) + (i / 3) * S(38), S(42), S(34));
                rb.CheckedChanged += (s, e) => { if (rb.Checked) position = idx; };
                tip.SetToolTip(rb, where[i]);
                posButtons[i] = rb;
                g2.Controls.Add(rb);
            }
            var lbl = new Label
            {
                Text = "Pick the spot on the screen (the screen your mouse is on). " +
                       "Each extra note is nudged a little inward so notes don't hide each other.",
                ForeColor = SystemColors.GrayText
            };
            lbl.SetBounds(S(172), S(28), S(288), S(80));
            g2.Controls.Add(lbl);
            t.Controls.AddRange(new Control[] { g1, g2 });
        }

        // ---------- tab: Colors ----------
        void BuildColorsTab(TabPage t)
        {
            var g = Group("12 note colors", S(10), S(10), S(472), S(268));
            for (int i = 0; i < Palette.Slots; i++)
            {
                int idx = i;
                int col = i % 6, row = i / 6;
                int x = S(10) + col * S(76), y = S(26) + row * S(116);
                var b = new Button { FlatStyle = FlatStyle.Flat };
                b.SetBounds(x, y, S(68), S(42));
                b.FlatAppearance.BorderColor = Color.Gray;
                b.Click += (s, e) => EditSlot(idx);
                swatches[i] = b;
                g.Controls.Add(b);

                var n = new Label { Text = Palette.Names[i], TextAlign = ContentAlignment.TopCenter, AutoSize = false };
                n.SetBounds(x, y + S(46), S(68), S(18));
                g.Controls.Add(n);

                var c = new CheckBox { Text = "Disable", AutoSize = false };
                c.SetBounds(x + S(2), y + S(66), S(68), S(22));
                c.CheckedChanged += (s, e) => { if (!loading) off[idx] = c.Checked; };
                offBoxes[i] = c;
                g.Controls.Add(c);

                // text color for notes of this color (e.g. white text on a dark blue note)
                var tb = new Button { Text = "Aa", FlatStyle = FlatStyle.Flat };
                tb.SetBounds(x, y + S(90), S(68), S(22));
                tb.FlatAppearance.BorderColor = Color.Gray;
                tb.Click += (s, e) => EditSlotText(idx);
                textBtns[i] = tb;
                g.Controls.Add(tb);
            }
            t.Controls.Add(g);

            var hint = new Label
            {
                Text = "Click a square to set its color with a hex code (#FEF49C) or RGB numbers. " +
                       "The first 6 are the built-in colors; the other 6 start empty so you can add your own. " +
                       "Disabled colors are skipped when cycling and hidden from the right-click Color menu. " +
                       "\"Aa\" sets the text color used on notes of that color (for example white text on a dark note).",
                ForeColor = SystemColors.GrayText
            };
            hint.SetBounds(S(12), S(284), S(480), S(74));
            t.Controls.Add(hint);

            var reset = new Button { Text = "Reset the 6 built-in colors" };
            reset.SetBounds(S(12), S(362), S(190), S(28));
            reset.Click += (s, e) =>
            {
                for (int i = 0; i < Palette.BuiltIn; i++) palette[i] = Palette.Defaults[i];
                RefreshAll();
            };
            t.Controls.Add(reset);
        }

        void EditSlot(int i)
        {
            bool filled = !palette[i].IsEmpty;
            Color c = filled ? palette[i] : Color.White;
            DialogResult r = ColorPickerForm.Pick(this, Palette.Names[i] + " color", ref c, i >= Palette.BuiltIn && filled);
            if (r == DialogResult.OK) palette[i] = c;
            else if (r == DialogResult.Abort) { palette[i] = Color.Empty; off[i] = false; }
            else return;
            RefreshAll();
        }

        void EditSlotText(int i)
        {
            Color c = slotText[i] != 0 ? Palette.FromArgbInt(slotText[i]) : TextColor();
            DialogResult r = ColorPickerForm.Pick(this, Palette.Names[i] + " note: text color", ref c, slotText[i] != 0, "Default");
            if (r == DialogResult.OK) slotText[i] = c.ToArgb();
            else if (r == DialogResult.Abort) slotText[i] = 0;
            else return;
            RefreshAll();
        }

        // ---------- tab: Text ----------
        void BuildTextTab(TabPage t)
        {
            var g1 = Group("Default text for new notes", S(10), S(10), S(472), S(148));
            preview.SetBounds(S(12), S(26), S(300), S(56));
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.TextAlign = ContentAlignment.MiddleCenter;
            g1.Controls.Add(preview);
            var btnFont = new Button { Text = "Font && color…" };
            btnFont.SetBounds(S(324), S(26), S(130), S(28));
            btnFont.Click += (s, e) => ChooseFont();
            g1.Controls.Add(btnFont);
            var btnBlack = new Button { Text = "Reset to black" };
            btnBlack.SetBounds(S(324), S(58), S(130), S(28));
            btnBlack.Click += (s, e) => { textColor = 0; RefreshAll(); };
            g1.Controls.Add(btnBlack);
            var tip = new Label
            {
                Text = "The standard text color is pure black. Thin fonts like Consolas look heavier " +
                       "with Bold as the default style (use the Font button).",
                ForeColor = SystemColors.GrayText
            };
            tip.SetBounds(S(12), S(92), S(448), S(50));
            g1.Controls.Add(tip);

            var g2 = Group("Emphasize (Ctrl+Shift+B)", S(10), S(168), S(472), S(214));
            var info = new Label
            {
                Text = "Most fonts (Consolas included) only have Regular and Bold. If Bold is already your normal " +
                       "text, select a word and press Ctrl+Shift+B to make it stand out even more. Press again to undo.",
                ForeColor = SystemColors.GrayText
            };
            info.SetBounds(S(12), S(24), S(448), S(52));
            g2.Controls.Add(info);

            Lbl(g2, "Make it bigger by:", S(12), S(88), S(120));
            numEmph.DecimalPlaces = 1; numEmph.Increment = 0.5m; numEmph.Minimum = 0; numEmph.Maximum = 12;
            numEmph.SetBounds(S(136), S(84), S(70), S(24));
            numEmph.ValueChanged += (s, e) => { if (!loading) emphSize = (float)numEmph.Value; };
            g2.Controls.Add(numEmph);
            Lbl(g2, "points (it is always bold too)", S(212), S(88), S(240));

            chkEmphBack.Text = "Highlight it with:";
            chkEmphBack.SetBounds(S(12), S(122), S(150), S(24));
            btnEmphBack.FlatStyle = FlatStyle.Flat;
            btnEmphBack.SetBounds(S(168), S(120), S(60), S(28));
            btnEmphBack.Click += (s, e) =>
            {
                Color c = emphHighlight != 0 ? Palette.FromArgbInt(emphHighlight) : Color.FromArgb(0xFF, 0xF1, 0x76);
                if (ColorPickerForm.Pick(this, "Highlight color", ref c, false) == DialogResult.OK)
                { emphHighlight = c.ToArgb(); RefreshAll(); }
            };
            chkEmphBack.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                emphHighlight = chkEmphBack.Checked ? Color.FromArgb(0xFF, 0xF1, 0x76).ToArgb() : 0;
                RefreshAll();
            };
            g2.Controls.AddRange(new Control[] { chkEmphBack, btnEmphBack });

            chkEmphColor.Text = "Change its text color to:";
            chkEmphColor.SetBounds(S(12), S(156), S(190), S(24));
            btnEmphColor.FlatStyle = FlatStyle.Flat;
            btnEmphColor.SetBounds(S(208), S(154), S(60), S(28));
            btnEmphColor.Click += (s, e) =>
            {
                Color c = emphColor != 0 ? Palette.FromArgbInt(emphColor) : Color.Black;
                if (ColorPickerForm.Pick(this, "Emphasis text color", ref c, false) == DialogResult.OK)
                { emphColor = c.ToArgb(); RefreshAll(); }
            };
            chkEmphColor.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                emphColor = chkEmphColor.Checked ? Color.Black.ToArgb() : 0;
                RefreshAll();
            };
            g2.Controls.AddRange(new Control[] { chkEmphColor, btnEmphColor });
            chkCaret.Text = "Use the macOS-style text cursor (thin, rounded, blinking)";
            chkCaret.SetBounds(S(14), S(388), S(490), S(24));
            chkCaret.CheckedChanged += (s, e) => { if (!loading) macCaret = chkCaret.Checked; };
            t.Controls.AddRange(new Control[] { g1, g2, chkCaret });
        }

        // ---------- tab: Formatting ----------
        void BuildFormattingTab(TabPage t)
        {
            chkTaskStrike.Text = "Checking a task (Ctrl+Shift+X) strikes through its text";
            chkTaskStrike.SetBounds(S(14), S(12), S(480), S(22));
            chkTaskStrike.CheckedChanged += (s, e) => { if (!loading) taskStrike = chkTaskStrike.Checked; };
            t.Controls.Add(chkTaskStrike);

            Lbl(t, "Highlight color:", S(14), S(44), S(110));
            btnHighlight.FlatStyle = FlatStyle.Flat;
            btnHighlight.SetBounds(S(128), S(40), S(60), S(26));
            btnHighlight.Click += (s, e) =>
            {
                Color c = Palette.FromArgbInt(highlightColor);
                if (ColorPickerForm.Pick(this, "Highlight color", ref c, false) == DialogResult.OK)
                { highlightColor = c.ToArgb(); RefreshAll(); }
            };
            t.Controls.Add(btnHighlight);

            var hint = new Label
            {
                Text = "Click a box, then press the keys you want. These work inside a note.",
                ForeColor = SystemColors.GrayText
            };
            hint.SetBounds(S(14), S(76), S(480), S(20));
            t.Controls.Add(hint);

            var panel = new Panel { AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
            panel.SetBounds(S(10), S(100), S(500), S(312));
            int y = S(6);
            foreach (ActionDef a in FormatActions.All)
            {
                ActionDef def = a;
                var l = new Label { Text = def.Title, AutoSize = false };
                l.SetBounds(S(8), y + S(4), S(200), S(20));
                panel.Controls.Add(l);
                var box = new HotkeyBox();
                box.SetBounds(S(214), y, S(170), S(24));
                box.ValueChanged += (s, e) => binds[def.Id] = box.Value;
                bindBoxes[def.Id] = box;
                panel.Controls.Add(box);
                var reset = new Button { Text = "Default" };
                reset.SetBounds(S(392), y - S(1), S(70), S(26));
                reset.Click += (s, e) => { binds[def.Id] = def.Default; box.Value = def.Default; };
                panel.Controls.Add(reset);
                y += S(32);
            }
            t.Controls.Add(panel);
        }

        // ---------- tab: Window ----------
        void BuildWindowTab(TabPage t)
        {
            var g = Group("Corners", S(10), S(10), S(500), S(116));
            rbSharp.Text = "Sharp corners (like macOS Stickies)";
            rbSharp.SetBounds(S(16), S(26), S(440), S(24));
            rbRound.Text = "Rounded corners";
            rbRound.SetBounds(S(16), S(54), S(440), S(24));
            var note = new Label
            {
                Text = "Rounded corners need Windows 11. On Windows 10 notes are always square.",
                ForeColor = SystemColors.GrayText
            };
            note.SetBounds(S(16), S(84), S(470), S(24));
            rbRound.CheckedChanged += (s, e) => { if (!loading) rounded = rbRound.Checked; };
            g.Controls.AddRange(new Control[] { rbSharp, rbRound, note });

            var g2 = Group("Title bar", S(10), S(136), S(500), S(120));
            chkIcons.Text = "Show icons on the title bar buttons when the mouse is over the bar";
            chkIcons.SetBounds(S(16), S(26), S(470), S(24));
            chkIcons.CheckedChanged += (s, e) => { if (!loading) titleIcons = chkIcons.Checked; };
            var info = new Label
            {
                Text = "Left: close.  Right: keep on top (the pin), then collapse / expand. " +
                       "A note that is kept on top always shows its pin.",
                ForeColor = SystemColors.GrayText
            };
            info.SetBounds(S(16), S(56), S(470), S(50));
            g2.Controls.AddRange(new Control[] { chkIcons, info });

            var g3 = Group("Moving notes", S(10), S(266), S(500), S(96));
            chkSnap.Text = "Snap notes together like magnets when I drag them close";
            chkSnap.SetBounds(S(16), S(26), S(470), S(24));
            chkSnap.CheckedChanged += (s, e) => { if (!loading) snapNotes = chkSnap.Checked; };
            var snapInfo = new Label
            {
                Text = "Edges stick to other notes when they get close. Hold Shift while dragging a title bar to move all snapped notes together.",
                ForeColor = SystemColors.GrayText
            };
            snapInfo.SetBounds(S(16), S(50), S(470), S(36));
            g3.Controls.AddRange(new Control[] { chkSnap, snapInfo });
            t.Controls.AddRange(new Control[] { g, g2, g3 });
        }

        // ---------- tab: General ----------
        void BuildGeneralTab(TabPage t)
        {
            var g3 = Group("Shortcuts that work from anywhere", S(10), S(10), S(500), S(158));
            AddGlobalRow(g3, chkHotkey, hotkeyBox, "Open a new note", S(24),
                () => { hotkeyKeys = AppConfig.DefaultNewNoteHotkey; return hotkeyKeys; },
                v => hotkeyKeys = v, v => hotkeyOn = v);
            AddGlobalRow(g3, chkSet, setBox, "Open Settings", S(62),
                () => { setKeys = (int)(Keys.Alt | Keys.Oemtilde); return setKeys; },
                v => setKeys = v, v => setOn = v);
            AddGlobalRow(g3, chkFront, frontBox, "Bring all notes to front", S(100),
                () => { frontKeys = (int)(Keys.Alt | Keys.Shift | Keys.F); return frontKeys; },
                v => frontKeys = v, v => frontOn = v);
            var hint = new Label { Text = "Click a box, then press the keys. Uses Ctrl or Alt.", ForeColor = SystemColors.GrayText };
            hint.SetBounds(S(16), S(134), S(470), S(20));
            g3.Controls.Add(hint);

            var g4 = Group("Where notes are saved", S(10), S(178), S(500), S(106));
            lblFolder.SetBounds(S(12), S(24), S(476), S(36));
            lblFolder.AutoEllipsis = true;
            g4.Controls.Add(lblFolder);
            var btnChange = new Button { Text = "Change folder…" };
            btnChange.SetBounds(S(12), S(66), S(130), S(28));
            btnChange.Click += (s, e) => ChooseFolder();
            g4.Controls.Add(btnChange);
            var btnOpen = new Button { Text = "Open folder" };
            btnOpen.SetBounds(S(150), S(66), S(110), S(28));
            btnOpen.Click += (s, e) => NoteManager.Instance.OpenNotesFolder();
            g4.Controls.Add(btnOpen);
            var auto = new Label { Text = "Saved automatically.", ForeColor = SystemColors.GrayText };
            auto.SetBounds(S(272), S(72), S(200), S(20));
            g4.Controls.Add(auto);
            var g5 = Group("Startup", S(10), S(294), S(500), S(62));
            chkStartup.Text = "Start Stickies_Window when I sign in to Windows";
            chkStartup.SetBounds(S(16), S(26), S(470), S(24));
            chkStartup.CheckedChanged += (s, e) => { if (!loading) startup = chkStartup.Checked; };
            g5.Controls.Add(chkStartup);
            t.Controls.AddRange(new Control[] { g3, g4, g5 });
        }

        // One row: [x] label   [shortcut box]  [Default]
        void AddGlobalRow(GroupBox g, CheckBox chk, HotkeyBox box, string label, int y,
            Func<int> reset, Action<int> setKeysValue, Action<bool> setEnabled)
        {
            chk.Text = label;
            chk.SetBounds(S(16), y, S(210), S(24));
            box.SetBounds(S(232), y, S(170), S(24));
            var btn = new Button { Text = "Default" };
            btn.SetBounds(S(410), y - S(2), S(76), S(28));
            btn.Click += (s, e) => { int v = reset(); box.Value = (Keys)v; };
            chk.CheckedChanged += (s, e) => { setEnabled(chk.Checked); if (!loading) RefreshAll(); };
            box.ValueChanged += (s, e) => setKeysValue((int)box.Value);
            g.Controls.AddRange(new Control[] { chk, box, btn });
        }

        // ---------- helpers ----------
        static GroupBox Group(string title, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = title };
            g.SetBounds(x, y, w, h);
            return g;
        }

        static void Lbl(Control parent, string text, int x, int y, int w)
        {
            var l = new Label { Text = text, AutoSize = false };
            l.SetBounds(x, y, w, S(20));
            parent.Controls.Add(l);
        }

        Color FixedColor()
        {
            if (fixedCustom != 0) return Palette.FromArgbInt(fixedCustom);
            int i = Math.Max(0, Math.Min(Palette.Slots - 1, fixedIndex));
            return palette[i].IsEmpty ? palette[0] : palette[i];
        }

        Color TextColor()
        {
            return textColor != 0 ? Palette.FromArgbInt(textColor) : Palette.NoteText;
        }

        // Redraws everything from the working copies
        void RefreshAll()
        {
            bool was = loading; loading = true;

            for (int i = 0; i < Palette.Slots; i++)
            {
                bool filled = !palette[i].IsEmpty;
                swatches[i].BackColor = filled ? palette[i] : SystemColors.Control;
                swatches[i].Text = filled ? "" : "+";
                offBoxes[i].Enabled = filled;
                offBoxes[i].Checked = filled && off[i];
                textBtns[i].Enabled = filled;
                textBtns[i].BackColor = filled ? palette[i] : SystemColors.Control;
                textBtns[i].ForeColor = slotText[i] != 0 ? Palette.FromArgbInt(slotText[i]) : TextColor();
                colorTip.SetToolTip(textBtns[i], slotText[i] != 0
                    ? "Text color on " + Palette.Names[i] + " notes (click to change)"
                    : "Click to set a text color for " + Palette.Names[i] + " notes. Now: the normal default text color");
            }

            // "My color" list: every filled slot, then Custom
            fixedMap.Clear();
            cmbFixed.Items.Clear();
            for (int i = 0; i < Palette.Slots; i++)
                if (!palette[i].IsEmpty) { fixedMap.Add(i); cmbFixed.Items.Add(Palette.Names[i] + (off[i] ? " (disabled)" : "")); }
            cmbFixed.Items.Add("Custom color…");
            if (fixedCustom != 0) cmbFixed.SelectedIndex = fixedMap.Count;
            else
            {
                int row = fixedMap.IndexOf(fixedIndex);
                cmbFixed.SelectedIndex = row >= 0 ? row : 0;
            }
            fixedSwatch.BackColor = FixedColor();
            cmbFixed.Enabled = colorMode >= 2;

            string[] hints =
            {
                "Each new note takes the next color in your list (only colors that are not disabled).",
                "Each new note gets a random color from the ones that are not disabled.",
                "Every new note uses \"My color\". If that color is disabled, the first enabled color is used instead.",
                "The first note, when no other notes are open, uses \"My color\" (or the first enabled color if it is disabled). Every note you open after that cycles through your enabled colors."
            };
            modeHint.Text = hints[Math.Max(0, Math.Min(3, colorMode))];

            try { preview.Font = new Font(fontName, Math.Min(fontSize, 18f), (FontStyle)fontStyle); } catch { }
            preview.ForeColor = TextColor();
            preview.BackColor = colorMode >= 2 ? FixedColor() : palette[0];
            preview.Text = fontName + ", " + fontSize + " pt" +
                ((fontStyle & (int)FontStyle.Bold) != 0 ? ", bold" : "") +
                ((fontStyle & (int)FontStyle.Italic) != 0 ? ", italic" : "") +
                ((fontStyle & (int)FontStyle.Underline) != 0 ? ", underline" : "");

            chkEmphBack.Checked = emphHighlight != 0;
            btnEmphBack.Enabled = emphHighlight != 0;
            btnEmphBack.BackColor = emphHighlight != 0 ? Palette.FromArgbInt(emphHighlight) : SystemColors.Control;
            chkEmphColor.Checked = emphColor != 0;
            btnEmphColor.Enabled = emphColor != 0;
            btnEmphColor.BackColor = emphColor != 0 ? Palette.FromArgbInt(emphColor) : SystemColors.Control;

            hotkeyBox.Enabled = hotkeyOn;
            setBox.Enabled = setOn;
            frontBox.Enabled = frontOn;
            btnHighlight.BackColor = Palette.FromArgbInt(highlightColor);
            lblFolder.Text = pendingFolder ?? Store.Folder;
            loading = was;
        }

        void ChooseFont()
        {
            using (var dlg = new FontDialog())
            {
                dlg.Font = new Font(fontName, fontSize, (FontStyle)fontStyle);
                dlg.ShowColor = true;
                dlg.Color = TextColor();
                dlg.FontMustExist = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                fontName = dlg.Font.Name;
                fontSize = dlg.Font.Size;
                fontStyle = (int)dlg.Font.Style;
                textColor = dlg.Color.ToArgb();
                RefreshAll();
            }
        }

        void ChooseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose the folder where Stickies_Window saves your notes.";
                dlg.SelectedPath = pendingFolder ?? Store.Folder;
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                pendingFolder = Store.SamePath(dlg.SelectedPath, Store.Folder) ? null : dlg.SelectedPath;
                RefreshAll();
            }
        }

        bool Apply()
        {
            var mgr = NoteManager.Instance;

            // at least one color must stay on
            bool anyOn = false;
            for (int i = 0; i < Palette.Slots; i++) if (!palette[i].IsEmpty && !off[i]) anyOn = true;
            if (!anyOn)
            {
                MessageBox.Show(this, "At least one color has to stay enabled.", "Stickies_Window",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                tabs.SelectedIndex = 1;
                return false;
            }

            // formatting shortcuts: no duplicates, and nothing that is already a built-in shortcut
            var seen = new Dictionary<Keys, string>();
            foreach (ActionDef a in FormatActions.All)
            {
                Keys k = binds[a.Id];
                string clash = null;
                if (seen.ContainsKey(k)) clash = seen[k];
                else seen[k] = a.Title;
                string builtIn = FormatActions.ReservedName(k);
                if (clash != null || builtIn != null)
                {
                    MessageBox.Show(this, "\"" + HotkeyBox.Describe(k) + "\" can't be used for " + a.Title + ": it is already used for " +
                        (clash ?? builtIn) + ".", "Stickies_Window", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    tabs.SelectedIndex = 3;
                    return false;
                }
            }

            // global shortcuts first, so a clash keeps this window open
            var c = Store.Config;
            int o1 = c.HotkeyKeys, o2 = c.SettingsHotkeyKeys, o3 = c.FrontHotkeyKeys;
            bool e1 = c.HotkeyEnabled, e2 = c.SettingsHotkeyEnabled, e3 = c.FrontHotkeyEnabled;
            c.HotkeyKeys = hotkeyKeys; c.HotkeyEnabled = hotkeyOn;
            c.SettingsHotkeyKeys = setKeys; c.SettingsHotkeyEnabled = setOn;
            c.FrontHotkeyKeys = frontKeys; c.FrontHotkeyEnabled = frontOn;
            if (!mgr.ApplyHotkeys(false))
            {
                c.HotkeyKeys = o1; c.SettingsHotkeyKeys = o2; c.FrontHotkeyKeys = o3;
                c.HotkeyEnabled = e1; c.SettingsHotkeyEnabled = e2; c.FrontHotkeyEnabled = e3;
                mgr.ApplyHotkeys(true);
                tabs.SelectedIndex = 5;
                return false;
            }
            Store.SaveConfig();

            // start with Windows (kept in the registry, not in the notes)
            if (startup != StartupHelper.IsEnabled())
            {
                string err;
                if (!StartupHelper.Set(startup, out err))
                    MessageBox.Show(this, "Couldn't change the startup setting:\n\n" + err, "Stickies_Window",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Folder change may swap in a different set of notes
            var before = mgr.Data;
            if (pendingFolder != null)
            {
                if (!mgr.ChangeFolder(pendingFolder)) return false;
                pendingFolder = null;
            }
            if (mgr.Data == before)
            {
                var d = mgr.Data;
                d.ColorMode = colorMode;
                d.DefaultColor = fixedIndex;
                d.DefaultCustomColor = fixedCustom;
                d.NewNotePosition = position;
                d.DefaultFontName = fontName;
                d.DefaultFontSize = fontSize;
                d.DefaultFontStyle = fontStyle;
                d.DefaultTextColor = textColor;
                d.EmphasisSize = emphSize;
                d.EmphasisHighlight = emphHighlight;
                d.EmphasisColor = emphColor;
                d.RoundedCorners = rounded;
                d.TitleBarIcons = titleIcons;
                d.SnapNotes = snapNotes;
                d.MacCaret = macCaret;
                d.TaskStrike = taskStrike;
                d.HighlightColor = highlightColor;
                d.Shortcuts = new List<ShortcutBinding>();
                foreach (ActionDef a in FormatActions.All)
                    if (binds[a.Id] != a.Default)
                        d.Shortcuts.Add(new ShortcutBinding { Action = a.Id, KeyData = (int)binds[a.Id] });
                Palette.Colors = (Color[])palette.Clone();
                Palette.Disabled = (bool[])off.Clone();
                Palette.TextColors = (int[])slotText.Clone();
                d.SlotTextColors = Palette.ExportTextColors();
                d.PaletteColors = Palette.Export();
                d.DisabledColors = Palette.ExportDisabled();
            }
            mgr.RefreshLook();
            mgr.SaveAll();
            return true;
        }
    }

    // Asks where to save notes (first run, or when the old folder stopped working)
    class FolderForm : Form
    {
        readonly TextBox path = new TextBox();

        public static string Ask(bool firstRun, string current)
        {
            using (var f = new FolderForm(firstRun, current))
                return f.ShowDialog() == DialogResult.OK ? f.path.Text.Trim() : null;
        }

        FolderForm(bool firstRun, string current)
        {
            Text = firstRun ? "Welcome to Stickies_Window" : "Choose notes folder";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(480), S(200));

            var intro = new Label
            {
                Text = "Where should Stickies_Window save your notes?\n\n" +
                       "Each note is one .rtf file, plus notes.xml for position, size and color. " +
                       "If the folder already has Stickies notes, they will be loaded. " +
                       "You can change this later in Settings.",
                AutoSize = false
            };
            intro.SetBounds(S(14), S(12), S(452), S(92));
            path.Text = current;
            path.SetBounds(S(14), S(110), S(364), S(24));
            var browse = new Button { Text = "Browse…" };
            browse.SetBounds(S(386), S(108), S(80), S(28));
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Choose the folder for your notes.";
                    dlg.ShowNewFolderButton = true;
                    try { dlg.SelectedPath = path.Text; } catch { }
                    if (dlg.ShowDialog(this) == DialogResult.OK) path.Text = dlg.SelectedPath;
                }
            };
            var ok = new Button { Text = "Use this folder" };
            ok.SetBounds(S(236), S(156), S(130), S(30));
            ok.Click += (s, e) =>
            {
                string p = path.Text.Trim();
                try { p = Path.GetFullPath(p); }
                catch { MessageBox.Show(this, "That isn't a valid folder path.", "Stickies_Window"); return; }
                path.Text = p;
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = firstRun ? "Use default" : "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(S(374), S(156), S(92), S(30));
            Controls.AddRange(new Control[] { intro, path, browse, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        static int S(int v) { return NoteForm.S(v); }
    }

    // A box that records a shortcut when you press keys in it
    class HotkeyBox : TextBox
    {
        Keys value;
        public event EventHandler ValueChanged;

        public HotkeyBox() { ReadOnly = true; BackColor = SystemColors.Window; }

        public Keys Value
        {
            get { return value; }
            set { this.value = value; Text = Describe(value); }
        }

        public static string Describe(Keys k)
        {
            string s = "";
            if ((k & Keys.Control) != 0) s += "Ctrl+";
            if ((k & Keys.Alt) != 0) s += "Alt+";
            if ((k & Keys.Shift) != 0) s += "Shift+";
            return s + KeyName(k & Keys.KeyCode);
        }

        public static string KeyName(Keys key)
        {
            switch (key)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemQuestion: return "/";
                case Keys.Return: return "Enter";
            }
            string name = key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) name = name.Substring(1);
            return name;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            bool mod = (keyData & (Keys.Control | Keys.Alt)) != 0;
            if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.None) return true;
            if (!mod)
            {
                // plain Tab / Esc / Enter still work for the dialog
                if (key == Keys.Tab || key == Keys.Escape || key == Keys.Return) return base.ProcessCmdKey(ref msg, keyData);
                return true;
            }
            Value = keyData;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            return true;
        }
    }

    // Invisible window that receives the global shortcuts
    class HotkeyWindow : NativeWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        const int WM_HOTKEY = 0x312;
        public event Action<int> Pressed;
        readonly HashSet<int> registered = new HashSet<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool IsRegistered(int id) { return registered.Contains(id); }

        public bool Register(int id, Keys keys)
        {
            Unregister(id);
            uint mods = 0x4000;   // MOD_NOREPEAT
            if ((keys & Keys.Alt) != 0) mods |= 1;
            if ((keys & Keys.Control) != 0) mods |= 2;
            if ((keys & Keys.Shift) != 0) mods |= 4;
            if (!RegisterHotKey(Handle, id, mods, (uint)(keys & Keys.KeyCode))) return false;
            registered.Add(id);
            return true;
        }

        public void Unregister(int id)
        {
            if (registered.Remove(id)) UnregisterHotKey(Handle, id);
        }

        public void UnregisterAll()
        {
            foreach (int id in new List<int>(registered)) UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }
}
