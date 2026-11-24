using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace Stickies_Window
{
    // One note's saved settings. The note's text is saved separately as <Id>.rtf
    public class NoteData
    {
        public string Id;
        public int X, Y, Width, Height;
        public int ColorIndex;
        public int CustomColor;          // ARGB; 0 = use the palette color above
        public bool OnTop;
        public bool Translucent;
        public bool Collapsed;
        public DateTime Created;
        public string FilePath;          // file this note is saved in (Save / Save As / Open), if any
        public int TextBase;             // ARGB of the standard text color last applied to this note (0 = unknown)
        public int HighlightBase;        // ARGB of the highlight color last applied to this note (0 = unknown)

        public NoteData Clone() { return (NoteData)MemberwiseClone(); }
    }

    // A formatting shortcut you changed (shortcuts you never touched use their defaults)
    public class ShortcutBinding
    {
        public string Action;
        public int KeyData;     // System.Windows.Forms.Keys value
    }

    // Everything saved in notes.xml (lives in the notes folder, travels with your notes)
    public class StoreData
    {
        public List<NoteData> Notes = new List<NoteData>();

        // How new notes get their color:
        // 0 = cycle, 1 = random, 2 = always the same color,
        // 3 = start with my color (first note when no notes are open), then cycle
        public int ColorMode = 3;
        public int NextColorIndex = 0;
        public int DefaultColor = 0;         // "my color" for modes 2 and 3 (palette slot)
        public int DefaultCustomColor = 0;   // ARGB; "my color" when it is not a palette slot (0 = use slot)

        // Where new notes open: 0..8 = top-left, top, top-right, left, center, right, bottom-left, bottom, bottom-right
        public int NewNotePosition = 4;      // center


        // Look of text in new notes
        public string DefaultFontName = "Consolas";
        public float DefaultFontSize = 17.25f;
        public int DefaultFontStyle = 0;     // System.Drawing.FontStyle flags (bold, italic, underline)
        public int DefaultTextColor = 0;     // ARGB; 0 = standard dark text

        // The 12 color slots (ARGB). First 6 are the built-in ones; the last 6 are yours (0 = empty)
        public List<int> PaletteColors = new List<int>();
        // Slots switched off: skipped when cycling and hidden from the Color menu
        public List<int> DisabledColors = new List<int>();
        // Names of the color slots ("" = the standard name)
        public List<string> ColorNames = new List<string>();
        // Text color to use on each of the 12 note colors (ARGB, 0 = the normal default text color)
        public List<int> SlotTextColors = new List<int>();
        // Highlight color (Ctrl+Shift+H) to use on each of the 18 note colors (ARGB, 0 = Auto)
        public List<int> SlotHighlightColors = new List<int>();

        // Look of the note windows
        public bool RoundedCorners = false;      // false = sharp 90 degree corners (like macOS Stickies)
        public bool TitleBarIcons = true;        // show small icons on the title bar buttons while the mouse is over the bar
        public bool MacCaret = true;             // thin rounded blinking text cursor like macOS
        public bool SnapNotes = true;            // notes stick to each other's edges like magnets when dragged close

        // Formatting
        public bool TaskStrike = true;           // checking a task strikes through its text
        public List<ShortcutBinding> Shortcuts = new List<ShortcutBinding>();   // only the ones you changed
    }

    // Per-computer settings, always in %AppData%\Stickies_Window\config.xml
    public class AppConfig
    {
        public string DataFolder;                  // where notes are saved
        public bool SetupDone;                     // true once the user has picked a folder
        public const int OldNewNoteHotkey = (int)(Keys.Control | Keys.Alt | Keys.N);     // the default before it changed
        public const int DefaultNewNoteHotkey = (int)(Keys.Alt | Keys.Shift | Keys.T);
        public int HotkeyKeys = DefaultNewNoteHotkey;   // global "new note" shortcut
        public bool HotkeyEnabled = true;
        public int SettingsHotkeyKeys = (int)(Keys.Alt | Keys.Oemtilde);          // open Settings from anywhere
        public bool SettingsHotkeyEnabled = true;
        public int FrontHotkeyKeys = (int)(Keys.Alt | Keys.Shift | Keys.F);      // bring all notes to front
        public bool FrontHotkeyEnabled = true;
    }

    public static class Store
    {
        // Small fixed folder: holds config.xml, log.txt, error.txt
        public static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stickies_Window");

        // Where notes.xml and the .rtf files are
        public static string Folder = ConfigFolder;

        public static AppConfig Config = new AppConfig();

        static string ConfigPath { get { return Path.Combine(ConfigFolder, "config.xml"); } }
        static string IndexPath { get { return Path.Combine(Folder, "notes.xml"); } }

        public static string RtfPath(string id) { return Path.Combine(Folder, id + ".rtf"); }

        // ---------- config ----------
        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (var s = File.OpenRead(ConfigPath))
                    {
                        var c = (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(s);
                        if (c != null) Config = c;
                    }
                }
            }
            catch { Config = new AppConfig(); }
            Folder = string.IsNullOrEmpty(Config.DataFolder) ? ConfigFolder : Config.DataFolder;

            // a saved config still holding the old default new-note shortcut gets the new default
            if (Config.HotkeyKeys == AppConfig.OldNewNoteHotkey)
            {
                Config.HotkeyKeys = AppConfig.DefaultNewNoteHotkey;
                SaveConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                using (var s = File.Create(ConfigPath))
                    new XmlSerializer(typeof(AppConfig)).Serialize(s, Config);
            }
            catch { }
        }

        // ---------- folder handling ----------
        public static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Makes sure we can create and write in the folder
        public static bool EnsureWritable(string folder, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool HasLibrary(string folder)
        {
            try { return File.Exists(Path.Combine(folder, "notes.xml")); } catch { return false; }
        }

        // Copies the notes (never deletes the originals)
        public static void CopyLibrary(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string name in new[] { "notes.xml", "notes.xml.bak" })
            {
                string src = Path.Combine(from, name);
                if (File.Exists(src)) File.Copy(src, Path.Combine(to, name), true);
            }
            foreach (string f in Directory.GetFiles(from, "*.rtf"))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        }

        public static void SetFolder(string folder)
        {
            Folder = folder;
            Config.DataFolder = folder;
            Config.SetupDone = true;
            SaveConfig();
        }

        // ---------- notes.xml ----------
        // Reads notes.xml. If it is damaged, keeps a copy of it and falls back to the last good backup
        public static StoreData Load()
        {
            foreach (string p in new[] { IndexPath, IndexPath + ".bak" })
            {
                if (!File.Exists(p)) continue;
                try
                {
                    using (var s = File.OpenRead(p))
                    {
                        var d = (StoreData)new XmlSerializer(typeof(StoreData)).Deserialize(s);
                        if (d != null) return d;
                    }
                }
                catch
                {
                    try { File.Copy(p, p + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
                }
            }
            return new StoreData();
        }

        // Writes to a temp file first, keeps the previous version as notes.xml.bak
        public static void Save(StoreData d)
        {
            Directory.CreateDirectory(Folder);
            string tmp = IndexPath + ".tmp";
            using (var s = File.Create(tmp))
                new XmlSerializer(typeof(StoreData)).Serialize(s, d);
            if (File.Exists(IndexPath)) File.Copy(IndexPath, IndexPath + ".bak", true);
            File.Copy(tmp, IndexPath, true);
            File.Delete(tmp);
        }

        public static void Delete(string id)
        {
            try { File.Delete(RtfPath(id)); } catch { }
        }
    }

    // 18 color slots: 6 built-in colors plus 12 empty ones for your own. Every slot can be renamed
    public static class Palette
    {
        public const int BuiltIn = 6, Slots = 18;

        static readonly string[] DefaultNames =
        {
            "Yellow", "Pink", "Gray", "Beige", "Blue", "Green",
            "Custom 1", "Custom 2", "Custom 3", "Custom 4", "Custom 5", "Custom 6",
            "Custom 7", "Custom 8", "Custom 9", "Custom 10", "Custom 11", "Custom 12"
        };

        // Names you gave the colors (the standard name when left empty)
        public static string[] Names = (string[])DefaultNames.Clone();

        public static string Name(int i)
        {
            if (i < 0 || i >= Slots) return "";
            return string.IsNullOrEmpty(Names[i]) ? DefaultNames[i] : Names[i];
        }

        public static string StandardName(int i) { return DefaultNames[i]; }

        // Morandi palette
        public static readonly Color[] Defaults =
        {
            Color.FromArgb(0xF2, 0xE6, 0xB3),   // Yellow: pale, muted, like aged paper
            Color.FromArgb(0xE3, 0xD0, 0xCC),   // Pink: dusty and warm
            Color.FromArgb(0xBF, 0xBE, 0xBD),   // Gray: neutral
            Color.FromArgb(0xD8, 0xD3, 0xC9),   // Beige: warm and earthy
            Color.FromArgb(0x99, 0xB9, 0xC6),   // Blue: serene and muted
            Color.FromArgb(0xA9, 0xB4, 0xA1)    // Green: soft sage
        };

        // Every built-in color uses the normal text color (pure black)
        static readonly int[] BuiltInText = { 0, 0, 0, 0, 0, 0 };

        // Colors earlier versions used as built-in defaults. If a saved settings file still holds one of
        // these in a built-in slot, it is treated as "default" so it upgrades to the current colors
        static readonly Color[][] OlderDefaults =
        {
            new[] { Color.FromArgb(0xF3,0xE4,0xAA), Color.FromArgb(0xC9,0xDB,0xE8), Color.FromArgb(0xCF,0xE0,0xC3),
                    Color.FromArgb(0xEE,0xD3,0xD3), Color.FromArgb(0xDC,0xD3,0xE8), Color.FromArgb(0xE2,0xE0,0xDC) },
            new[] { Color.FromArgb(0xFE,0xF4,0x9C), Color.FromArgb(0xAD,0xF4,0xFF), Color.FromArgb(0xB2,0xFF,0xA0),
                    Color.FromArgb(0xFF,0xC7,0xC7), Color.FromArgb(0xB6,0xCA,0xFF), Color.FromArgb(0xEE,0xEE,0xEE) },
            new[] { Color.FromArgb(0xFF,0xFF,0x00), Color.FromArgb(0x00,0x00,0xFF), Color.FromArgb(0x00,0xFF,0x64),
                    Color.FromArgb(0x00,0xFF,0xFF), Color.FromArgb(0x40,0x00,0x00), Color.FromArgb(0xF3,0xE6,0xD8) }
        };

        // Empty slots are Color.Empty
        public static Color[] Colors = MakeDefaults();
        public static bool[] Disabled = new bool[Slots];
        public static int[] HighlightColors = new int[Slots];  // highlight color you chose for notes of each color, 0 = Auto
        public static int[] TextColors = new int[Slots];     // text color you chose for notes of each color, 0 = not set

        // Soft tone used for the menus and the tray icon
        public static readonly Color Text = Color.FromArgb(0x3A, 0x35, 0x30);

        // The standard text color inside notes
        public static readonly Color NoteText = Color.Black;

        static Color[] MakeDefaults()
        {
            var c = new Color[Slots];
            for (int i = 0; i < BuiltIn; i++) c[i] = Defaults[i];
            return c;
        }

        public static bool IsFilled(int i) { return i >= 0 && i < Slots && !Colors[i].IsEmpty; }

        // In use: has a color and is not switched off
        public static bool IsActive(int i) { return IsFilled(i) && !Disabled[i]; }

        public static List<int> ActiveIndexes()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++) if (IsActive(i)) l.Add(i);
            if (l.Count == 0) { Disabled[0] = false; l.Add(0); }   // never end up with nothing
            return l;
        }

        public static Color Get(int i)
        {
            if (!IsFilled(i)) i = 0;
            return Colors[i];
        }

        // The text color that goes with a built-in color by default (0 = none, the normal text color)
        public static int BuiltInTextFor(int i)
        {
            return (i >= 0 && i < BuiltIn) ? BuiltInText[i] : 0;
        }

        // The text color for notes of this color: yours if you set one, else the built-in pairing, else 0
        public static int TextFor(int i)
        {
            if (i < 0 || i >= Slots) return 0;
            return TextColors[i] != 0 ? TextColors[i] : BuiltInTextFor(i);
        }

        public static Color FromArgbInt(int argb)
        {
            return Color.FromArgb(argb | unchecked((int)0xFF000000));
        }

        // The color a note is drawn with (its own custom color wins)
        public static Color For(NoteData d)
        {
            return d.CustomColor != 0 ? FromArgbInt(d.CustomColor) : Get(d.ColorIndex);
        }

        // ---------- contrast helpers (so the title bar, scrollbar and highlight suit any note color) ----------
        static double Channel(int v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        // Relative luminance, 0 (black) to 1 (white)
        public static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        public static bool IsDark(Color c) { return Luminance(c) < 0.22; }

        // Contrast ratio between two colors, 1 (none) to 21 (black on white)
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            if (la < lb) { double t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Title bar: a darker shade of a light note, a lighter shade of a dark note, so it always shows up
        public static Color Bar(Color note)
        {
            return IsDark(note) ? Blend(note, Color.White, 0.28) : Blend(note, Color.Black, 0.14);
        }

        // Color for the title bar's buttons, icons and title: the note's text color when it reads well on the bar
        public static Color Ink(Color bar, Color preferred)
        {
            if (Contrast(bar, preferred) >= 3.0) return preferred;
            return IsDark(bar) ? Color.White : Text;
        }

        static readonly Color[] HighlightCandidates =
        {
            Color.FromArgb(0xFF,0xEB,0x3B), Color.FromArgb(0xFF,0xC1,0x07), Color.FromArgb(0xFF,0x8A,0x65),
            Color.FromArgb(0xF4,0x8F,0xB1), Color.FromArgb(0xCE,0x93,0xD8), Color.FromArgb(0x80,0xDE,0xEA),
            Color.FromArgb(0x81,0xC7,0x84), Color.FromArgb(0x8E,0x24,0xAA), Color.FromArgb(0xC6,0x28,0x28),
            Color.FromArgb(0x1B,0x5E,0x20), Color.FromArgb(0xBF,0x36,0x0C), Color.FromArgb(0x00,0x60,0x64),
            Color.FromArgb(0x28,0x35,0x93)
        };

        static double Distance(Color a, Color b)
        {
            double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(2 * dr * dr + 4 * dg * dg + 3 * db * db);
        }

        // A highlight that stands out from the note color and keeps the text readable on top of it
        public static Color Highlight(Color note, Color text)
        {
            Color best = HighlightCandidates[0];
            double bestDist = -1;
            foreach (Color c in HighlightCandidates)
            {
                if (Contrast(c, text) < 4.5) continue;       // text must stay readable
                double d = Distance(c, note);
                if (d > bestDist) { bestDist = d; best = c; }
            }
            if (bestDist >= 0) return best;
            // nothing reads well with this text color: take the most readable one
            double bestContrast = -1;
            foreach (Color c in HighlightCandidates)
            {
                double k = Contrast(c, text);
                if (k > bestContrast) { bestContrast = k; best = c; }
            }
            return best;
        }

        // Load / save the user's palette
        public static void Apply(List<int> saved, List<int> disabled, List<int> textColors = null, List<string> names = null, List<int> highlights = null)
        {
            Colors = MakeDefaults();
            Disabled = new bool[Slots];
            TextColors = new int[Slots];
            HighlightColors = new int[Slots];
            if (highlights != null)
                for (int i = 0; i < highlights.Count && i < Slots; i++) HighlightColors[i] = highlights[i];
            Names = (string[])DefaultNames.Clone();
            if (names != null)
                for (int i = 0; i < names.Count && i < Slots; i++)
                    if (!string.IsNullOrEmpty(names[i]) && names[i].Trim().Length > 0) Names[i] = names[i].Trim();
            if (textColors != null)
                for (int i = 0; i < textColors.Count && i < Slots; i++) TextColors[i] = textColors[i];
            if (saved != null)
                for (int i = 0; i < saved.Count && i < Slots; i++)
                {
                    if (saved[i] == 0) continue;
                    Color c = FromArgbInt(saved[i]);
                    if (i < BuiltIn && IsOlderDefault(i, c)) continue;   // upgrade old defaults
                    Colors[i] = c;
                }
            if (disabled != null)
                foreach (int d in disabled) if (d >= 0 && d < Slots) Disabled[d] = true;
            ActiveIndexes();
        }

        static bool IsOlderDefault(int i, Color c)
        {
            foreach (Color[] set in OlderDefaults) if (c.ToArgb() == set[i].ToArgb()) return true;
            return false;
        }

        public static List<int> Export()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++)
            {
                Color c = Colors[i];
                // built-in colors you haven't changed are saved as 0, so future default changes reach you
                if (c.IsEmpty || (i < BuiltIn && c.ToArgb() == Defaults[i].ToArgb())) l.Add(0);
                else l.Add(c.ToArgb());
            }
            return l;
        }

        public static List<string> ExportNames()
        {
            var l = new List<string>();
            for (int i = 0; i < Slots; i++) l.Add(Names[i] == DefaultNames[i] ? "" : Names[i]);
            return l;
        }

        public static List<int> ExportHighlightColors()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++) l.Add(HighlightColors[i]);
            return l;
        }

        public static List<int> ExportTextColors()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++) l.Add(TextColors[i]);
            return l;
        }

        public static List<int> ExportDisabled()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++) if (Disabled[i]) l.Add(i);
            return l;
        }
    }
}
