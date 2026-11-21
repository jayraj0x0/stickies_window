using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Stickies_Window
{
    // One formatting command that can have a keyboard shortcut
    class ActionDef
    {
        public readonly string Id, Title;
        public readonly Keys Default;
        public ActionDef(string id, string title, Keys def) { Id = id; Title = title; Default = def; }
    }

    // The table of formatting shortcuts (defaults here, your changes are saved in notes.xml)
    static class FormatActions
    {
        const Keys Ctl = Keys.Control, Sh = Keys.Shift;

        public static readonly ActionDef[] All =
        {
            new ActionDef("bold", "Bold", Ctl | Keys.B),
            new ActionDef("italic", "Italic", Ctl | Keys.I),
            new ActionDef("underline", "Underline", Ctl | Keys.U),
            new ActionDef("strike", "Strikethrough", Ctl | Keys.J),
            new ActionDef("highlight", "Highlight", Ctl | Sh | Keys.H),
            new ActionDef("task", "Task list (add / remove checkbox)", Ctl | Sh | Keys.X),
            new ActionDef("taskcheck", "Check / uncheck task", Ctl | Keys.Return),
            new ActionDef("h1", "Heading 1", Ctl | Sh | Keys.D1),
            new ActionDef("h2", "Heading 2", Ctl | Sh | Keys.D2),
            new ActionDef("emphasize", "Emphasize (extra weight)", Ctl | Sh | Keys.B)
        };

        // Shortcuts that are built in and can't be given to a formatting command
        static readonly Dictionary<Keys, string> reserved = BuildReserved();

        static Dictionary<Keys, string> BuildReserved()
        {
            var d = new Dictionary<Keys, string>();
            d[Ctl | Keys.N] = "New note";
            d[Ctl | Keys.T] = "New note";
            d[Ctl | Sh | Keys.T] = "Reopen closed note";
            d[Ctl | Sh | Keys.F] = "Show fonts";
            d[Ctl | Sh | Keys.C] = "Show colors";
            d[Ctl | Keys.W] = "Close note";
            d[Ctl | Keys.P] = "Print";
            d[Ctl | Keys.Y] = "Redo";
            d[Ctl | Keys.Z] = "Undo";
            d[Ctl | Keys.X] = "Cut";
            d[Ctl | Keys.C] = "Copy";
            d[Ctl | Keys.V] = "Paste";
            d[Ctl | Keys.A] = "Select all";
            d[Ctl | Keys.F] = "Find";
            d[Ctl | Keys.H] = "Replace";
            d[Ctl | Keys.G] = "Find next";
            d[Ctl | Keys.S] = "Save";
            d[Ctl | Sh | Keys.S] = "Save As";
            d[Ctl | Keys.O] = "Open";
            d[Ctl | Keys.Oemcomma] = "Settings";
            d[Ctl | Keys.Oemplus] = "Bigger text";
            d[Ctl | Keys.OemMinus] = "Smaller text";
            d[Ctl | Keys.Q] = "Align left";
            d[Ctl | Keys.E] = "Center";
            d[Ctl | Keys.R] = "Align right";
            d[Ctl | Keys.M] = "Collapse";
            d[Ctl | Keys.Alt | Keys.F] = "Float on top";
            d[Ctl | Keys.Alt | Sh | Keys.F] = "Keep snapped notes on top";
            d[Ctl | Keys.Alt | Keys.T] = "Translucent";
            return d;
        }

        public static string ReservedName(Keys k)
        {
            string name;
            return reserved.TryGetValue(k, out name) ? name : null;
        }

        public static ActionDef Def(string id)
        {
            foreach (ActionDef a in All) if (a.Id == id) return a;
            return null;
        }

        // The shortcut for a command: your change if you made one, otherwise the default
        public static Keys Get(string id)
        {
            if (NoteManager.Instance != null)
                foreach (ShortcutBinding b in NoteManager.Instance.Data.Shortcuts)
                    if (b.Action == id) return (Keys)b.KeyData;
            ActionDef d = Def(id);
            return d != null ? d.Default : Keys.None;
        }

        // Which command (if any) a pressed key belongs to
        public static string Find(Keys k)
        {
            foreach (ActionDef a in All) if (Get(a.Id) == k) return a.Id;
            return null;
        }

        public static string Text(string id) { return HotkeyBox.Describe(Get(id)); }
    }

    // Formatting commands: bold/italic/underline/strike, code, highlight, link, tasks, headings, emphasize.
    // Character formatting is sent straight to the rich text box (EM_SETCHARFORMAT), so it only changes
    // what it names and keeps every other style in the selection (sizes, colors, fonts...).
    partial class NoteForm
    {
        // ---------- native character formatting ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CHARFORMAT2
        {
            public int cbSize;
            public uint dwMask;
            public uint dwEffects;
            public int yHeight;
            public int yOffset;
            public int crTextColor;
            public byte bCharSet;
            public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
            public ushort wWeight;
            public short sSpacing;
            public int crBackColor;
            public int lcid;
            public int dwReserved;
            public short sStyle;
            public ushort wKerning;
            public byte bUnderlineType;
            public byte bAnimation;
            public byte bRevAuthor;
            public byte bUnderlineColor;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CHARFORMAT2 lp);

        const int EM_GETCHARFORMAT = 0x043A, EM_SETCHARFORMAT = 0x0444, SCF_SELECTION = 1;
        const uint CFM_BOLD = 0x1, CFM_ITALIC = 0x2, CFM_UNDERLINE = 0x4, CFM_STRIKEOUT = 0x8,
                   CFM_COLOR = 0x40000000, CFM_BACKCOLOR = 0x04000000, CFM_SIZE = 0x80000000,
                   CFM_FACE = 0x20000000, CFM_CHARSET = 0x08000000;
        const uint CFE_BOLD = 0x1, CFE_ITALIC = 0x2, CFE_UNDERLINE = 0x4, CFE_STRIKEOUT = 0x8,
                   CFE_AUTOBACKCOLOR = 0x04000000;
        const string BoxOpen = "\u2610", BoxDone = "\u2611", SymbolFont = "Segoe UI Symbol";

        static CHARFORMAT2 NewCf(uint mask)
        {
            var cf = new CHARFORMAT2();
            cf.cbSize = Marshal.SizeOf(typeof(CHARFORMAT2));
            cf.dwMask = mask;
            return cf;
        }

        static CHARFORMAT2 GetCf(RichTextBox rtb)
        {
            CHARFORMAT2 cf = NewCf(0);
            SendMessage(rtb.Handle, EM_GETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
            return cf;
        }

        static void SetCf(RichTextBox rtb, CHARFORMAT2 cf)
        {
            SendMessage(rtb.Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref cf);
        }

        // True only if the whole selection (or the typing position) has the effect
        static bool EffectOn(RichTextBox rtb, uint bit)
        {
            CHARFORMAT2 cf = GetCf(rtb);
            return (cf.dwMask & bit) != 0 && (cf.dwEffects & bit) != 0;
        }

        static void SetEffect(RichTextBox rtb, uint bit, bool on)
        {
            CHARFORMAT2 cf = NewCf(bit);
            cf.dwEffects = on ? bit : 0u;
            SetCf(rtb, cf);
        }

        // Point size of the selection, or -1 if it is mixed
        static float SizeOf(RichTextBox rtb)
        {
            CHARFORMAT2 cf = GetCf(rtb);
            return (cf.dwMask & CFM_SIZE) == 0 ? -1f : cf.yHeight / 20f;
        }

        static void SetSize(RichTextBox rtb, float points)
        {
            CHARFORMAT2 cf = NewCf(CFM_SIZE);
            cf.yHeight = (int)Math.Round(points * 20);
            SetCf(rtb, cf);
        }

        static void SetFace(RichTextBox rtb, string face)
        {
            CHARFORMAT2 cf = NewCf(CFM_FACE | CFM_CHARSET);
            cf.szFaceName = face;
            cf.bCharSet = 1;   // DEFAULT_CHARSET
            SetCf(rtb, cf);
        }

        static void SetTextColor(RichTextBox rtb, Color c)
        {
            CHARFORMAT2 cf = NewCf(CFM_COLOR);
            cf.crTextColor = ColorTranslator.ToWin32(c);
            SetCf(rtb, cf);
        }

        static void SetBack(RichTextBox rtb, Color c)
        {
            CHARFORMAT2 cf = NewCf(CFM_BACKCOLOR);
            cf.crBackColor = ColorTranslator.ToWin32(c);
            SetCf(rtb, cf);
        }

        static void ClearBack(RichTextBox rtb)
        {
            CHARFORMAT2 cf = NewCf(CFM_BACKCOLOR);
            cf.dwEffects = CFE_AUTOBACKCOLOR;
            SetCf(rtb, cf);
        }

        // True only if the whole selection has exactly this background
        static bool BackIs(RichTextBox rtb, Color c)
        {
            CHARFORMAT2 cf = GetCf(rtb);
            return (cf.dwMask & CFM_BACKCOLOR) != 0 && (cf.dwEffects & CFE_AUTOBACKCOLOR) == 0 &&
                   cf.crBackColor == ColorTranslator.ToWin32(c);
        }

        const uint CFE_AUTOCOLOR = 0x40000000;

        // Changes every stretch of text that has the old standard color (or "automatic" color) to the new one.
        // Text you colored yourself is left alone.
        void RecolorRuns(Color oldC, Color newC)
        {
            int ss = text.SelectionStart, sl = text.SelectionLength, n = text.TextLength;
            int oldW = ColorTranslator.ToWin32(oldC);
            SendMessage(text.Handle, 0x000B, (IntPtr)0, IntPtr.Zero);       // WM_SETREDRAW off: no flicker while we work
            try
            {
                int run = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool match = false;
                    if (i < n)
                    {
                        text.Select(i, 1);
                        CHARFORMAT2 cf = GetCf(text);
                        match = (cf.dwMask & CFM_COLOR) != 0 &&
                                ((cf.dwEffects & CFE_AUTOCOLOR) != 0 || cf.crTextColor == oldW);
                    }
                    if (match) { if (run < 0) run = i; }
                    else if (run >= 0)
                    {
                        text.Select(run, i - run);
                        SetTextColor(text, newC);
                        run = -1;
                    }
                }
            }
            finally
            {
                text.Select(ss, sl);
                SendMessage(text.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                text.Invalidate();
            }
        }

        // ---------- dispatch ----------
        public void RunFormat(string id)
        {
            try
            {
                switch (id)
                {
                    case "bold": ToggleEffect(CFM_BOLD, CFE_BOLD); break;
                    case "italic": ToggleEffect(CFM_ITALIC, CFE_ITALIC); break;
                    case "underline": ToggleEffect(CFM_UNDERLINE, CFE_UNDERLINE); break;
                    case "strike": ToggleEffect(CFM_STRIKEOUT, CFE_STRIKEOUT); break;
                    case "highlight": ToggleHighlight(); break;
                    case "task": ToggleTask(); break;
                    case "taskcheck": CheckTask(); break;
                    case "h1": ToggleHeading(1); break;
                    case "h2": ToggleHeading(2); break;
                    case "emphasize": Emphasize(); break;
                }
            }
            catch (Exception ex) { Log.Write("format " + id + ": " + ex.Message); }
        }

        // Press once to switch it on, press again to switch it off
        void ToggleEffect(uint mask, uint effect)
        {
            SetEffect(text, mask, !EffectOn(text, effect));
        }

        // ---------- line helpers ----------
        // Start of the paragraph containing pos, and where it ends (before the line break)
        void ParaBounds(int pos, out int start, out int end)
        {
            string t = text.Text;
            pos = Math.Max(0, Math.Min(pos, t.Length));
            start = pos == 0 ? 0 : t.LastIndexOf('\n', pos - 1) + 1;
            end = t.IndexOf('\n', pos);
            if (end < 0) end = t.Length;
        }

        // First selected line start .. last selected line end (line breaks not included)
        void LinesRange(out int a, out int b)
        {
            string t = text.Text;
            int s = Math.Min(text.SelectionStart, t.Length);
            int e = Math.Min(s + text.SelectionLength, t.Length);
            if (e > s && t[e - 1] == '\n') e--;
            a = s == 0 ? 0 : t.LastIndexOf('\n', s - 1) + 1;
            int nl = t.IndexOf('\n', e);
            b = nl < 0 ? t.Length : nl;
        }

        // ---------- highlight ----------
        static HashSet<string> installedFonts;

        static bool HasFont(string name)
        {
            if (installedFonts == null)
            {
                installedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FontFamily ff in FontFamily.Families) installedFonts.Add(ff.Name);
            }
            return installedFonts.Contains(name);
        }

        void ToggleHighlight()
        {
            Color hl = Palette.FromArgbInt(NoteManager.Instance.Data.HighlightColor);
            if (BackIs(text, hl)) ClearBack(text);
            else SetBack(text, hl);
        }

        // ---------- headings (1 and 2) ----------
        static readonly float[] HeadingScale = { 2.0f, 1.5f };

        // Bold + bigger text on the selected lines. Press the same shortcut again to undo
        void ToggleHeading(int level)
        {
            int ss = text.SelectionStart, sl = text.SelectionLength;
            int a, b;
            LinesRange(out a, out b);
            float baseSize = text.Font.Size;
            float target = baseSize * HeadingScale[level - 1];

            text.Select(a, Math.Min(1, b - a));
            float now = SizeOf(text);
            bool already = now > 0 && Math.Abs(now - target) < 0.6f;

            text.Select(a, b - a);
            SetSize(text, already ? baseSize : target);
            SetEffect(text, CFE_BOLD, already ? text.Font.Bold : true);
            if (a != b) text.Select(ss, sl);
        }

        // ---------- emphasize ----------
        // Extra weight for a word when Bold is already your normal text: bold + bigger
        // (+ optional highlight and color from Settings). Press again to undo.
        void Emphasize()
        {
            var st = NoteManager.Instance.Data;
            float delta = Math.Max(0f, st.EmphasisSize);
            float baseSize = text.Font.Size;
            bool useBack = st.EmphasisHighlight != 0, useColor = st.EmphasisColor != 0;
            Color hl = useBack ? Palette.FromArgbInt(st.EmphasisHighlight) : Color.Empty;
            Color ec = useColor ? Palette.FromArgbInt(st.EmphasisColor) : Color.Empty;

            bool on;
            float now = SizeOf(text);
            if (delta > 0) on = now > 0 && now >= baseSize + delta * 0.75f;
            else if (useBack) on = BackIs(text, hl);
            else on = false;

            if (!on)
            {
                if (delta > 0) SetSize(text, baseSize + delta);
                SetEffect(text, CFE_BOLD, true);
                if (useBack) SetBack(text, hl);
                if (useColor) SetTextColor(text, ec);
            }
            else
            {
                if (delta > 0) SetSize(text, baseSize);
                SetEffect(text, CFE_BOLD, text.Font.Bold);
                if (useBack) ClearBack(text);
                if (useColor) SetTextColor(text, DefaultText);
            }
        }

        // ---------- links ----------
        // Typed or pasted web addresses become clickable on their own (DetectUrls); this opens them
        // Opens a link. (Older notes may hold links stored as "text#address".)
        static void OpenLink(string linkText)
        {
            string url = linkText;
            Match m = Regex.Match(linkText, @"^(.*?)#((?:https?|ftp|file|mailto):.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (m.Success) url = m.Groups[2].Value;
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        // ---------- task list ----------
        enum TaskOp { Add, Remove, Check, Uncheck }

        class LineSpan { public int Start, End; }

        static bool IsBox(char c) { return c == '\u2610' || c == '\u2611'; }

        List<LineSpan> SelectedLines()
        {
            int a, b;
            LinesRange(out a, out b);
            string t = text.Text;
            var list = new List<LineSpan>();
            int pos = a;
            while (true)
            {
                int nl = t.IndexOf('\n', pos);
                int e = (nl < 0 || nl > b) ? b : nl;
                list.Add(new LineSpan { Start = pos, End = e });
                if (nl < 0 || nl >= b) break;
                pos = nl + 1;
            }
            return list;
        }

        // Index of the checkbox on a line (after any indent), or -1
        static int BoxIndex(string t, LineSpan l)
        {
            int i = l.Start;
            while (i < l.End && (t[i] == ' ' || t[i] == '\t')) i++;
            return (i < l.End && IsBox(t[i])) ? i : -1;
        }

        // Ctrl+Shift+X: add a checkbox to the line(s), or take it away again if they all have one
        void ToggleTask()
        {
            string t = text.Text;
            List<LineSpan> lines = SelectedLines();
            bool allBoxed = true;
            foreach (LineSpan l in lines) if (BoxIndex(t, l) < 0) allBoxed = false;
            TaskOp op = allBoxed ? TaskOp.Remove : TaskOp.Add;      // lines that already have a box are skipped when adding
            RunTaskOps(lines, delegate(LineSpan l) { return op; });
        }

        // Ctrl+Enter: check or uncheck (lines without a box get one)
        void CheckTask()
        {
            string t = text.Text;
            List<LineSpan> lines = SelectedLines();
            RunTaskOps(lines, delegate(LineSpan l)
            {
                int i = BoxIndex(t, l);
                if (i < 0) return TaskOp.Add;
                return t[i] == '\u2610' ? TaskOp.Check : TaskOp.Uncheck;
            });
        }

        // Applies an operation to each line (last line first so positions stay valid),
        // then puts the selection back where it was
        void RunTaskOps(List<LineSpan> lines, Func<LineSpan, TaskOp> pick)
        {
            string t = text.Text;
            int ss = text.SelectionStart, sl = text.SelectionLength;
            var editAt = new List<int>();
            var editBy = new List<int>();

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                LineSpan l = lines[i];
                int box = BoxIndex(t, l);
                TaskOp op = pick(l);
                if (op == TaskOp.Add && box >= 0) continue;                 // already has one
                if (op == TaskOp.Remove && box < 0) continue;
                int at, by;
                EditTaskLine(l.Start, l.End, op, box, out at, out by);
                editAt.Add(at);
                editBy.Add(by);
            }

            int ns = ss, ne = ss + sl;
            for (int k = 0; k < editAt.Count; k++)
            {
                ns = AdjustPos(ns, editAt[k], editBy[k]);
                ne = AdjustPos(ne, editAt[k], editBy[k]);
            }
            text.Select(ns, Math.Max(0, ne - ns));
        }

        // Where a position ends up after 'by' characters were added (or removed) at 'at'
        static int AdjustPos(int pos, int at, int by)
        {
            if (by > 0) return pos >= at ? pos + by : pos;
            if (by < 0 && pos > at) return pos - Math.Min(-by, pos - at);
            return pos;
        }

        // Rebuilds one line off to the side and puts it back in ONE edit, so a single Undo reverts the
        // checkbox and the strikethrough together.
        void EditTaskLine(int start, int end, TaskOp op, int box, out int at, out int by)
        {
            int indent = box >= 0 ? box - start : LeadingSpace(start, end);
            at = start + indent;
            by = 0;

            bool strikeOn = op == TaskOp.Check && NoteManager.Instance.Data.TaskStrike;
            string symbol = HasFont(SymbolFont) ? SymbolFont : null;

            string rtf = null;
            if (end > start)
            {
                text.Select(start, end - start);
                rtf = text.SelectedRtf;
            }

            using (var tmp = new RichTextBox())
            {
                tmp.Font = text.Font;
                tmp.ForeColor = DefaultText;
                tmp.BackColor = text.BackColor;
                if (!string.IsNullOrEmpty(rtf)) tmp.Rtf = rtf;
                if (tmp.TextLength > 0 && tmp.Text.EndsWith("\n"))
                {
                    tmp.Select(tmp.TextLength - 1, 1);
                    tmp.SelectedText = "";
                }

                switch (op)
                {
                    case TaskOp.Add:
                        tmp.Select(indent, 0);
                        tmp.SelectedText = BoxOpen + " ";
                        by = 2;
                        break;

                    case TaskOp.Remove:
                        {
                            int len = (tmp.TextLength > indent + 1 && tmp.Text[indent + 1] == ' ') ? 2 : 1;
                            tmp.Select(indent, len);
                            tmp.SelectedText = "";
                            by = -len;
                            break;
                        }

                    default:    // Check / Uncheck: swap the glyph
                        tmp.Select(indent, 1);
                        tmp.SelectedText = op == TaskOp.Check ? BoxDone : BoxOpen;
                        break;
                }

                // the glyph gets a font that really has it (otherwise it shows as an empty box)
                if (op != TaskOp.Remove && symbol != null)
                {
                    tmp.Select(indent, 1);
                    SetFace(tmp, symbol);
                }

                // strikethrough for the text after the checkbox: on when checked, off otherwise
                int from = op == TaskOp.Remove ? indent : indent + 2;
                if (tmp.TextLength > from)
                {
                    tmp.Select(from, tmp.TextLength - from);
                    SetEffect(tmp, CFE_STRIKEOUT, strikeOn);
                }

                string outRtf = StripFinalPar(tmp.Rtf);
                text.Select(start, end - start);
                text.SelectedRtf = outRtf;
            }
        }

        int LeadingSpace(int start, int end)
        {
            string t = text.Text;
            int i = start;
            while (i < end && (t[i] == ' ' || t[i] == '\t')) i++;
            return i - start;
        }

        // RichEdit always ends a document with a paragraph mark; dropping it keeps the line count unchanged
        static string StripFinalPar(string rtf)
        {
            string t = rtf.TrimEnd('\0', '\r', '\n', ' ');
            Match m = Regex.Match(t, @"\\par\s*\}$");
            if (!m.Success) return rtf;
            return t.Substring(0, m.Index) + "}";
        }

        // Clicking a checkbox checks / unchecks it
        public bool TryToggleTaskAt(Point p)
        {
            try
            {
                if (text.TextLength == 0) return false;
                string t = text.Text;
                int idx = text.GetCharIndexFromPosition(p);
                if (idx < 0 || idx >= t.Length || !IsBox(t[idx])) return false;
                Point gp = text.GetPositionFromCharIndex(idx);
                int w = (int)(text.Font.Height * 1.2);
                if (p.X < gp.X || p.X > gp.X + w) return false;
                if (Math.Abs(p.Y - gp.Y) > text.Font.Height * 2) return false;

                int a, b;
                ParaBounds(idx, out a, out b);
                var line = new List<LineSpan>();
                line.Add(new LineSpan { Start = a, End = b });
                RunTaskOps(line, delegate(LineSpan l) { return t[idx] == '\u2610' ? TaskOp.Check : TaskOp.Uncheck; });
                return true;
            }
            catch { return false; }
        }
    }
}
