using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Stickies_Window
{
    // Pick a color by hex code (#FEF49C) or RGB numbers, with a preview
    class ColorPickerForm : Form
    {
        readonly TextBox hex = new TextBox();
        readonly NumericUpDown nr = new NumericUpDown(), ng = new NumericUpDown(), nb = new NumericUpDown();
        readonly Panel swatch = new Panel();
        readonly Label info = new Label();
        Color color;
        bool busy;

        static int S(int v) { return NoteForm.S(v); }

        // OK = color changed in 'color'; Abort = "Clear slot" pressed; Cancel = nothing
        public static DialogResult Pick(IWin32Window owner, string title, ref Color color, bool allowClear)
        {
            using (var f = new ColorPickerForm(title, color, allowClear))
            {
                DialogResult r = f.ShowDialog(owner);
                if (r == DialogResult.OK) color = f.color;
                return r;
            }
        }

        // Accepts "#FEF49C", "FEF49C", "#FA3", "rgb(254, 244, 156)" or "254 244 156"
        public static bool TryParse(string text, out Color c)
        {
            c = Color.Empty;
            if (text == null) return false;
            string t = text.Trim();
            Match m = Regex.Match(t, @"^#?([0-9a-fA-F]{6})$");
            if (m.Success)
            {
                int v = Convert.ToInt32(m.Groups[1].Value, 16);
                c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
                return true;
            }
            m = Regex.Match(t, @"^#?([0-9a-fA-F])([0-9a-fA-F])([0-9a-fA-F])$");
            if (m.Success)
            {
                int r = Convert.ToInt32(m.Groups[1].Value + m.Groups[1].Value, 16);
                int g = Convert.ToInt32(m.Groups[2].Value + m.Groups[2].Value, 16);
                int b = Convert.ToInt32(m.Groups[3].Value + m.Groups[3].Value, 16);
                c = Color.FromArgb(r, g, b);
                return true;
            }
            m = Regex.Match(t, @"^(?:rgb\s*\()?\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*\)?$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int r = int.Parse(m.Groups[1].Value), g = int.Parse(m.Groups[2].Value), b = int.Parse(m.Groups[3].Value);
                if (r > 255 || g > 255 || b > 255) return false;
                c = Color.FromArgb(r, g, b);
                return true;
            }
            return false;
        }

        public static string ToHex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        ColorPickerForm(string title, Color initial, bool allowClear)
        {
            color = Color.FromArgb(initial.R, initial.G, initial.B);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(380), S(206));

            AddLabel("Hex code:", S(14), S(18), S(70));
            hex.SetBounds(S(88), S(14), S(130), S(24));
            Controls.Add(hex);

            AddLabel("Red:", S(14), S(54), S(70));
            AddLabel("Green:", S(14), S(84), S(70));
            AddLabel("Blue:", S(14), S(114), S(70));
            int y = S(50);
            foreach (var n in new[] { nr, ng, nb })
            {
                n.Minimum = 0; n.Maximum = 255;
                n.SetBounds(S(88), y, S(70), S(24));
                n.ValueChanged += OnRgbChanged;
                Controls.Add(n);
                y += S(30);
            }

            swatch.BorderStyle = BorderStyle.FixedSingle;
            swatch.SetBounds(S(244), S(14), S(122), S(82));
            Controls.Add(swatch);
            info.SetBounds(S(230), S(102), S(146), S(36));
            info.TextAlign = ContentAlignment.TopCenter;
            Controls.Add(info);

            var hint = new Label { Text = "You can paste a hex code like #FEF49C or rgb(254, 244, 156).", ForeColor = SystemColors.GrayText };
            hint.SetBounds(S(14), S(146), S(352), S(32));
            Controls.Add(hint);

            var visual = new Button { Text = "Pick visually…" };
            visual.SetBounds(S(14), S(168), S(110), S(28));
            visual.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog())
                {
                    dlg.FullOpen = true; dlg.Color = color;
                    if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(dlg.Color, true);
                }
            };
            Controls.Add(visual);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            ok.SetBounds(S(214), S(168), S(72), S(28));
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(S(294), S(168), S(72), S(28));
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (allowClear)
            {
                var clear = new Button { Text = "Empty slot", DialogResult = DialogResult.Abort };
                clear.SetBounds(S(130), S(168), S(78), S(28));
                Controls.Add(clear);
            }

            hex.TextChanged += OnHexChanged;
            hex.Leave += (s, e) => { Color c; if (TryParse(hex.Text, out c)) { busy = true; hex.Text = ToHex(c); busy = false; } };
            SetColor(color, true);
        }

        void AddLabel(string t, int x, int y, int w)
        {
            var l = new Label { Text = t, AutoSize = false };
            l.SetBounds(x, y + S(3), w, S(20));
            Controls.Add(l);
        }

        void SetColor(Color c, bool updateHex)
        {
            color = Color.FromArgb(c.R, c.G, c.B);
            busy = true;
            nr.Value = c.R; ng.Value = c.G; nb.Value = c.B;
            if (updateHex) { hex.Text = ToHex(color); hex.BackColor = SystemColors.Window; }
            busy = false;
            ShowPreview();
        }

        void ShowPreview()
        {
            swatch.BackColor = color;
            info.Text = ToHex(color) + "\nrgb(" + color.R + ", " + color.G + ", " + color.B + ")";
        }

        void OnHexChanged(object sender, EventArgs e)
        {
            if (busy) return;
            Color c;
            if (TryParse(hex.Text, out c)) { hex.BackColor = SystemColors.Window; SetColor(c, false); }
            else hex.BackColor = Color.MistyRose;
        }

        void OnRgbChanged(object sender, EventArgs e)
        {
            if (busy) return;
            SetColor(Color.FromArgb((int)nr.Value, (int)ng.Value, (int)nb.Value), true);
        }
    }
}
