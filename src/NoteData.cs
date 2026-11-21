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
        public int ColorMode = 0;
        public int NextColorIndex = 0;
        public int DefaultColor = 0;         // "my color" for modes 2 and 3 (palette slot)
        public int DefaultCustomColor = 0;   // ARGB; "my color" when it is not a palette slot (0 = use slot)

        // Where new notes open: 0..8 = top-left, top, top-right, left, center, right, bottom-left, bottom, bottom-right
        public int NewNotePosition = 0;

        // "Emphasize" (Ctrl+Shift+B): extra weight on top of bold
        public float EmphasisSize = 2f;      // points added
        public int EmphasisHighlight = 0;    // ARGB; 0 = no highlight
        public int EmphasisColor = 0;        // ARGB; 0 = keep text color

        // Look of text in new notes
        public string DefaultFontName = "Segoe UI";
        public float DefaultFontSize = 11f;
        public int DefaultFontStyle = 0;     // System.Drawing.FontStyle flags (bold, italic, underline)
        public int DefaultTextColor = 0;     // ARGB; 0 = standard dark text

        // The 12 color slots (ARGB). First 6 are the built-in ones; the last 6 are yours (0 = empty)
        public List<int> PaletteColors = new List<int>();
        // Slots switched off: skipped when cycling and hidden from the Color menu
        public List<int> DisabledColors = new List<int>();

        // Look of the note windows
        public bool RoundedCorners = false;      // false = sharp 90 degree corners (like macOS Stickies)
        public bool TitleBarIcons = true;        // show small icons on the title bar buttons while the mouse is over the bar
        public bool MacCaret = true;             // thin rounded blinking text cursor like macOS
        public bool SnapNotes = true;            // notes stick to each other's edges like magnets when dragged close

        // Formatting
        public bool TaskStrike = true;           // checking a task strikes through its text
        public int HighlightColor = unchecked((int)0xFFFFD966);   // Ctrl+Shift+H
        public List<ShortcutBinding> Shortcuts = new List<ShortcutBinding>();   // only the ones you changed
    }

    // Per-computer settings, always in %AppData%\Stickies_Window\config.xml
    public class AppConfig
    {
        public string DataFolder;                  // where notes are saved
        public bool SetupDone;                     // true once the user has picked a folder
        public int HotkeyKeys = (int)(Keys.Control | Keys.Alt | Keys.N);   // global "new note" shortcut
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

    // 12 color slots: the 6 macOS Stickies colors (softened) plus 6 empty ones for your own colors
    public static class Palette
    {
        public const int BuiltIn = 6, Slots = 12;

        public static readonly string[] Names =
        {
            "Yellow", "Blue", "Green", "Pink", "Purple", "Gray",
            "Custom 1", "Custom 2", "Custom 3", "Custom 4", "Custom 5", "Custom 6"
        };

        // The exact colors macOS Stickies uses
        public static readonly Color[] Defaults =
        {
            Color.FromArgb(0xFE, 0xF4, 0x9C),   // Yellow
            Color.FromArgb(0xAD, 0xF4, 0xFF),   // Blue
            Color.FromArgb(0xB2, 0xFF, 0xA0),   // Green
            Color.FromArgb(0xFF, 0xC7, 0xC7),   // Pink
            Color.FromArgb(0xB6, 0xCA, 0xFF),   // Purple
            Color.FromArgb(0xEE, 0xEE, 0xEE)    // Gray
        };

        // The softer colors earlier versions used as defaults. If a saved settings file still holds
        // one of these in a built-in slot, it is treated as "default" so it upgrades to the new colors
        static readonly Color[] LegacyDefaults =
        {
            Color.FromArgb(0xF3, 0xE4, 0xAA),
            Color.FromArgb(0xC9, 0xDB, 0xE8),
            Color.FromArgb(0xCF, 0xE0, 0xC3),
            Color.FromArgb(0xEE, 0xD3, 0xD3),
            Color.FromArgb(0xDC, 0xD3, 0xE8),
            Color.FromArgb(0xE2, 0xE0, 0xDC)
        };

        // Empty slots are Color.Empty
        public static Color[] Colors = MakeDefaults();
        public static bool[] Disabled = new bool[Slots];

        public static readonly Color Text = Color.FromArgb(0x3A, 0x35, 0x30);

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

        public static Color FromArgbInt(int argb)
        {
            return Color.FromArgb(argb | unchecked((int)0xFF000000));
        }

        // The color a note is drawn with (its own custom color wins)
        public static Color For(NoteData d)
        {
            return d.CustomColor != 0 ? FromArgbInt(d.CustomColor) : Get(d.ColorIndex);
        }

        // Title bar: the note color, slightly darker
        public static Color Bar(Color c)
        {
            return Color.FromArgb(Mix(c.R, Text.R), Mix(c.G, Text.G), Mix(c.B, Text.B));
        }

        static int Mix(int a, int b) { return (int)(a * 0.92 + b * 0.08); }

        // Load / save the user's palette
        public static void Apply(List<int> saved, List<int> disabled)
        {
            Colors = MakeDefaults();
            Disabled = new bool[Slots];
            if (saved != null)
                for (int i = 0; i < saved.Count && i < Slots; i++)
                {
                    if (saved[i] == 0) continue;
                    Color c = FromArgbInt(saved[i]);
                    if (i < BuiltIn && c.ToArgb() == LegacyDefaults[i].ToArgb()) continue;   // upgrade old defaults
                    Colors[i] = c;
                }
            if (disabled != null)
                foreach (int d in disabled) if (d >= 0 && d < Slots) Disabled[d] = true;
            ActiveIndexes();
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

        public static List<int> ExportDisabled()
        {
            var l = new List<int>();
            for (int i = 0; i < Slots; i++) if (Disabled[i]) l.Add(i);
            return l;
        }
    }
}
