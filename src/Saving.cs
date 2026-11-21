using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Stickies_Window
{
    // Save / Save As / Open, like any other program:
    //  - Ctrl+S saves to the file this note is already saved in (and asks where only the first time)
    //  - Ctrl+Shift+S always asks where
    //  - once a note has a file, every change is saved to it automatically
    //  - a .sticky file keeps the text AND the note's place, size and color, so Open brings it back as it was
    partial class NoteForm
    {
        bool linkedDirty;           // changed since the linked file was last written
        string lastLinkedMeta;      // position/size/color the linked file was last written with
        static string lastDir;      // last folder used in a Save / Open dialog

        public bool HasFile { get { return !string.IsNullOrEmpty(Data.FilePath); } }

        // The things (besides the text) that a .sticky file remembers
        string MetaKey()
        {
            if (!HasFile || !Data.FilePath.EndsWith(StickyFile.Extension, StringComparison.OrdinalIgnoreCase)) return "";
            return string.Join("|", new[] {
                Data.X.ToString(), Data.Y.ToString(), Data.Width.ToString(), Data.Height.ToString(),
                Data.ColorIndex.ToString(), Palette.For(Data).ToArgb().ToString(),
                Data.OnTop.ToString(), Data.Translucent.ToString(), Data.Collapsed.ToString() });
        }

        // Called right after a file was opened into this note: nothing to write back yet
        public void MarkLinkedClean()
        {
            SyncData();
            linkedDirty = false;
            lastLinkedMeta = MetaKey();
            dirty = true;       // the app's own copy still needs saving
        }

        // Writes the note to a file in the format its extension says. Returns false on failure.
        bool WriteFile(string path, bool silent)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("The folder no longer exists: " + dir);
                string ext = Path.GetExtension(path).ToLowerInvariant();

                if (ext == StickyFile.Extension)
                {
                    SyncData();
                    StickyFile.Write(path, Data, text.Rtf);
                }
                else
                {
                    string tmp = path + ".tmp";
                    if (ext == ".txt") File.WriteAllText(tmp, text.Text, Encoding.UTF8);
                    else text.SaveFile(tmp, RichTextBoxStreamType.RichText);
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (silent) NoteManager.Instance.ReportSaveError(ex);
                else MessageBox.Show(this, "Couldn't save the note:\n\n" + ex.Message, "Stickies_Window",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // Automatic saving to the note's file (runs with the normal autosave)
        public void SaveLinkedIfNeeded()
        {
            if (!HasFile) return;
            SyncData();
            string meta = MetaKey();
            if (!linkedDirty && meta == lastLinkedMeta) return;
            if (WriteFile(Data.FilePath, true))
            {
                linkedDirty = false;
                lastLinkedMeta = meta;
            }
        }

        // True if the note's file now matches the note (used before closing)
        public bool FlushLinked()
        {
            if (!HasFile) return false;
            SaveLinkedIfNeeded();
            return !linkedDirty && lastLinkedMeta == MetaKey();
        }

        // Ctrl+S
        public bool SaveNote()
        {
            if (HasFile && WriteFile(Data.FilePath, false))
            {
                linkedDirty = false;
                SyncData();
                lastLinkedMeta = MetaKey();
                return true;
            }
            return SaveAs();     // never saved before (or the old file can't be written): ask where
        }

        // Ctrl+Shift+S
        public bool SaveAs()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Sticky note (*.sticky)|*.sticky|Rich Text (*.rtf)|*.rtf|Plain Text (*.txt)|*.txt";
                dlg.DefaultExt = "sticky";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.FileName = HasFile ? Path.GetFileNameWithoutExtension(Data.FilePath) : SafeName(FirstLine);
                string start = HasFile ? Path.GetDirectoryName(Data.FilePath) : lastDir;
                if (start != null && Directory.Exists(start)) dlg.InitialDirectory = start;
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;

                if (!WriteFile(dlg.FileName, false)) return false;
                lastDir = Path.GetDirectoryName(dlg.FileName);
                Data.FilePath = dlg.FileName;       // from now on, changes go to this file
                linkedDirty = false;
                SyncData();
                lastLinkedMeta = MetaKey();
                NoteManager.Instance.QueueSave();
                return true;
            }
        }

        // Ctrl+O
        public void OpenFilesDialog()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Sticky notes and text (*.sticky;*.rtf;*.txt)|*.sticky;*.rtf;*.txt|" +
                             "Sticky notes (*.sticky)|*.sticky|Rich Text (*.rtf)|*.rtf|Plain Text (*.txt)|*.txt|All files (*.*)|*.*";
                dlg.Multiselect = true;
                if (lastDir != null && Directory.Exists(lastDir)) dlg.InitialDirectory = lastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                lastDir = Path.GetDirectoryName(dlg.FileNames[0]);
                foreach (string f in dlg.FileNames) NoteManager.Instance.OpenFile(f);
            }
        }
    }
}
