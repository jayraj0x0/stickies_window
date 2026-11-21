using System;
using System.Drawing;
using System.Windows.Forms;

namespace Stickies_Window
{
    // Find and Replace window (Ctrl+F / Ctrl+H) - one per note, stays open while you work
    class FindReplaceForm : Form
    {
        static string lastFind = "", lastReplace = "";
        static bool lastCase, lastWhole;

        readonly RichTextBox rtb;
        readonly TextBox txtFind = new TextBox();
        readonly TextBox txtReplace = new TextBox();
        readonly Label lblReplace = new Label();
        readonly CheckBox chkCase = new CheckBox();
        readonly CheckBox chkWhole = new CheckBox();
        readonly Button btnNext = new Button();
        readonly Button btnReplace = new Button();
        readonly Button btnAll = new Button();
        readonly Button btnClose = new Button();
        readonly Label status = new Label();

        public string FindText { get { return txtFind.Text; } }

        public FindReplaceForm(Form owner, RichTextBox box)
        {
            rtb = box;
            Owner = owner;
            Text = "Find and Replace";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = owner.TopMost;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(NoteForm.S(380), NoteForm.S(170));

            int lx = NoteForm.S(12), tx = NoteForm.S(86), tw = NoteForm.S(282), h = NoteForm.S(23);
            var lblFind = new Label { Text = "Find what:", AutoSize = false };
            lblFind.SetBounds(lx, NoteForm.S(15), NoteForm.S(72), h);
            txtFind.SetBounds(tx, NoteForm.S(12), tw, h);

            lblReplace.Text = "Replace with:";
            lblReplace.SetBounds(lx, NoteForm.S(45), NoteForm.S(72), h);
            txtReplace.SetBounds(tx, NoteForm.S(42), tw, h);

            chkCase.Text = "Match case";
            chkCase.SetBounds(tx, NoteForm.S(74), NoteForm.S(120), h);
            chkWhole.Text = "Whole word";
            chkWhole.SetBounds(tx + NoteForm.S(126), NoteForm.S(74), NoteForm.S(130), h);

            btnNext.Text = "Find Next";
            btnReplace.Text = "Replace";
            btnAll.Text = "Replace All";
            btnClose.Text = "Close";
            btnNext.Click += (s, e) => { if (!FindNext()) SetStatus("Not found"); };
            btnReplace.Click += (s, e) => ReplaceOne();
            btnAll.Click += (s, e) => ReplaceAll();
            btnClose.Click += (s, e) => Close();

            status.SetBounds(lx, NoteForm.S(142), NoteForm.S(356), h);
            status.ForeColor = SystemColors.GrayText;

            Controls.AddRange(new Control[] { lblFind, txtFind, lblReplace, txtReplace, chkCase, chkWhole,
                btnNext, btnReplace, btnAll, btnClose, status });
            AcceptButton = btnNext;
            CancelButton = btnClose;

            rtb.HideSelection = false;   // keep the found text highlighted while this window has focus
            txtFind.Text = lastFind;
            txtReplace.Text = lastReplace;
            chkCase.Checked = lastCase;
            chkWhole.Checked = lastWhole;
            FormClosed += (s, e) =>
            {
                lastFind = txtFind.Text; lastReplace = txtReplace.Text;
                lastCase = chkCase.Checked; lastWhole = chkWhole.Checked;
            };
        }

        // replace = true shows the "Replace with" row and buttons
        public void Prepare(bool replace, string selected)
        {
            lblReplace.Visible = txtReplace.Visible = btnReplace.Visible = btnAll.Visible = replace;
            int by = replace ? NoteForm.S(104) : NoteForm.S(74);
            chkCase.Top = chkWhole.Top = replace ? NoteForm.S(74) : NoteForm.S(44);
            int bw = NoteForm.S(86), bh = NoteForm.S(26), gap = NoteForm.S(6), x = NoteForm.S(12);
            btnNext.SetBounds(x, by, bw, bh);
            if (replace)
            {
                btnReplace.SetBounds(x + bw + gap, by, bw, bh);
                btnAll.SetBounds(x + 2 * (bw + gap), by, bw, bh);
                btnClose.SetBounds(x + 3 * (bw + gap), by, bw - NoteForm.S(30), bh);
                status.Top = NoteForm.S(138);
                ClientSize = new Size(NoteForm.S(380), NoteForm.S(168));
                Text = "Find and Replace";
            }
            else
            {
                chkWhole.Left = txtFind.Left + NoteForm.S(126);
                btnClose.SetBounds(x + bw + gap, by, bw, bh);
                status.Top = NoteForm.S(106);
                ClientSize = new Size(NoteForm.S(380), NoteForm.S(134));
                Text = "Find";
            }
            if (!string.IsNullOrEmpty(selected) && selected.IndexOf('\n') < 0 && selected.IndexOf('\r') < 0)
                txtFind.Text = selected;
            status.Text = "";
            txtFind.Focus();
            txtFind.SelectAll();
        }

        RichTextBoxFinds Options()
        {
            RichTextBoxFinds o = RichTextBoxFinds.None;
            if (chkCase.Checked) o |= RichTextBoxFinds.MatchCase;
            if (chkWhole.Checked) o |= RichTextBoxFinds.WholeWord;
            return o;
        }

        void SetStatus(string s) { status.Text = s; }

        // Finds the next match after the selection, wrapping around the end
        public bool FindNext()
        {
            string f = txtFind.Text;
            if (f.Length == 0) return false;
            int start = Math.Min(rtb.SelectionStart + rtb.SelectionLength, rtb.TextLength);
            int i = rtb.Find(f, start, Options());
            if (i < 0 && start > 0)
            {
                i = rtb.Find(f, 0, Options());
                if (i >= 0) SetStatus("Search wrapped to the top");
            }
            else if (i >= 0) SetStatus("");
            if (i < 0) return false;
            rtb.ScrollToCaret();
            return true;
        }

        bool SelectionMatches()
        {
            string f = txtFind.Text;
            if (f.Length == 0 || rtb.SelectionLength != f.Length) return false;
            return string.Equals(rtb.SelectedText, f,
                chkCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        // Replace: swaps the highlighted match, then jumps to the next one
        void ReplaceOne()
        {
            if (txtFind.Text.Length == 0) return;
            if (SelectionMatches()) rtb.SelectedText = txtReplace.Text;
            if (!FindNext()) SetStatus("No more matches");
        }

        void ReplaceAll()
        {
            string f = txtFind.Text, r = txtReplace.Text;
            if (f.Length == 0) return;
            int count = 0, pos = 0;
            rtb.SuspendLayout();
            while (pos <= rtb.TextLength)
            {
                int i = rtb.Find(f, pos, Options());
                if (i < 0) break;
                rtb.SelectedText = r;     // Find already selected the match
                pos = i + r.Length;
                count++;
            }
            rtb.ResumeLayout();
            SetStatus(count == 0 ? "Not found" : "Replaced " + count + (count == 1 ? " match" : " matches"));
        }
    }
}
