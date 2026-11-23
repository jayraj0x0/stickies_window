using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Stickies_Window
{
    // What a .sticky file holds: the text (as RTF) plus where and how the note looked, so opening
    // it brings the note back just as it was written
    public class StickyFileData
    {
        public int Version = 1;
        public int X, Y, Width, Height;
        public int ColorIndex;
        public int ColorArgb;          // the color the note actually had (0 = unknown)
        public bool OnTop, Translucent, Collapsed;
        public string Rtf;
    }

    static class StickyFile
    {
        public const string Extension = ".sticky";

        public static void Write(string path, NoteData d, string rtf)
        {
            var s = new StickyFileData();
            s.X = d.X; s.Y = d.Y; s.Width = d.Width; s.Height = d.Height;
            s.ColorIndex = d.ColorIndex;
            s.ColorArgb = Palette.For(d).ToArgb();
            s.OnTop = d.OnTop; s.Translucent = d.Translucent; s.Collapsed = d.Collapsed;
            // XML can't hold control characters; RTF never needs them
            s.Rtf = Regex.Replace(rtf ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

            string tmp = path + ".tmp";
            using (var f = File.Create(tmp))
                new XmlSerializer(typeof(StickyFileData)).Serialize(f, s);   // written aside first so a crash can't wreck the file
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        public static StickyFileData Read(string path)
        {
            using (var f = File.OpenRead(path))
                return (StickyFileData)new XmlSerializer(typeof(StickyFileData)).Deserialize(f);
        }
    }

    // "Start with Windows" (per user, no admin needed)
    static class StartupHelper
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Stickies_Window";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static bool Set(bool on, out string error)
        {
            error = null;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, Command);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // If it is on but the program was moved, point it at the new location
        public static void Refresh()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    object v = k.GetValue(ValueName);
                    if (v != null && !string.Equals(v.ToString(), Command, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(ValueName, Command);
                }
            }
            catch { }
        }
    }

    // Double-clicking a .sticky file opens it in Stickies_Window
    static class FileAssociation
    {
        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        const string ProgId = "Stickies_Window.Note";

        public static void Register()
        {
            try
            {
                string exe = Application.ExecutablePath;
                string command = "\"" + exe + "\" \"%1\"";
                bool changed = false;
                changed |= Set(@"Software\Classes\" + StickyFile.Extension, null, ProgId);
                changed |= Set(@"Software\Classes\" + ProgId, null, "Sticky note");
                changed |= Set(@"Software\Classes\" + ProgId + @"\DefaultIcon", null, "\"" + exe + "\",0");
                changed |= Set(@"Software\Classes\" + ProgId + @"\shell\open\command", null, command);
                if (changed) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED
            }
            catch { }
        }

        // Writes a registry value only if it differs; returns true if it changed
        static bool Set(string subKey, string name, string value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(subKey))
            {
                object cur = k.GetValue(name);
                if (cur != null && cur.ToString() == value) return false;
                k.SetValue(name, value);
                return true;
            }
        }
    }
}
