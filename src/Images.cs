using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Stickies_Window
{
    // Pictures in a note.
    //
    // The Windows text box keeps pasted pictures as raw uncompressed bitmaps and stretches them with a rough
    // scaling mode. That makes notes heavy (laggy) and resized pictures ugly. So pictures are handled here instead:
    // they are scaled once with high quality to the size they will be shown at, stored compactly as PNG, and
    // shown 1:1 (never stretched). Resizing re-renders the picture crisply.
    //
    // (The text box can only place a picture inline, in the line of text, like a big character: you can type
    // before and after it on the same line. It cannot float a picture so that text flows around or over it.)
    partial class NoteForm
    {
        // ---------- paste / drop ----------
        // Ctrl+V and the Paste menu: pictures go through here. Returns false for anything else (normal paste).
        public bool TryPasteImage()
        {
            try
            {
                if (!Clipboard.ContainsImage()) return false;
                // copied from Word, Excel or a web page: text is on the clipboard too, so paste that as usual
                if (Clipboard.ContainsText() || Clipboard.ContainsData(DataFormats.Rtf)) return false;
                using (Image img = ReadClipboardImage())
                    return img != null && InsertPicture(img);
            }
            catch { return false; }
        }

        // PNG keeps transparency; otherwise the normal clipboard image
        static Image ReadClipboardImage()
        {
            IDataObject d = Clipboard.GetDataObject();
            if (d != null && d.GetDataPresent("PNG"))
            {
                var ms = d.GetData("PNG") as MemoryStream;
                if (ms != null)
                {
                    try { using (Image tmp = Image.FromStream(ms)) return new Bitmap(tmp); } catch { }
                }
            }
            return Clipboard.GetImage();
        }

        // Widest a picture may be: the text area
        int MaxPictureWidth { get { return Math.Max(S(40), text.ClientSize.Width - S(10)); } }

        // Scales a picture to w x h with good quality. Big reductions are done in halving steps
        // (one big jump looks jagged).
        static Bitmap Resample(Image src, int w, int h, Color background)
        {
            Image cur = src;
            bool owned = false;
            while (cur.Width > w * 2 && cur.Height > h * 2)
            {
                int nw = Math.Max(w, cur.Width / 2), nh = Math.Max(h, cur.Height / 2);
                var half = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(half))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(cur, new Rectangle(0, 0, nw, nh), 0, 0, cur.Width, cur.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                if (owned) cur.Dispose();
                cur = half;
                owned = true;
            }

            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(background);       // the text box ignores transparency, so blend onto the note color
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(cur, new Rectangle(0, 0, w, h), 0, 0, cur.Width, cur.Height, GraphicsUnit.Pixel, ia);
                }
            }
            if (owned) cur.Dispose();
            return result;
        }

        static void AppendHex(StringBuilder sb, byte[] data, int offset)
        {
            for (int i = offset; i < data.Length; i++) sb.Append(data[i].ToString("x2"));
        }

        // RTF for one picture shown at exactly its own pixel size (1:1). As PNG (small), or as a plain bitmap
        // (accepted by every version of the text box) if the PNG form is refused.
        static string PictureRtf(Bitmap bmp, bool asBitmap)
        {
            double dpi = 96.0 * DpiScale;
            int gw = (int)Math.Round(bmp.Width * 1440.0 / dpi), gh = (int)Math.Round(bmp.Height * 1440.0 / dpi);
            var sb = new StringBuilder();
            sb.Append("{\\rtf1\\ansi{\\pict");

            using (var ms = new MemoryStream())
            {
                if (!asBitmap)
                {
                    bmp.Save(ms, ImageFormat.Png);
                    sb.Append("\\pngblip");
                }
                else
                {
                    // 24-bit device independent bitmap: the .bmp file without its 14 byte file header
                    using (var flat = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(flat)) g.DrawImageUnscaled(bmp, 0, 0);
                        flat.Save(ms, ImageFormat.Bmp);
                    }
                    sb.Append("\\dibitmap0");
                }
                sb.Append("\\picw").Append(bmp.Width).Append("\\pich").Append(bmp.Height)
                  .Append("\\picwgoal").Append(gw).Append("\\pichgoal").Append(gh).Append(' ');
                AppendHex(sb, ms.ToArray(), asBitmap ? 14 : 0);
            }
            sb.Append("}}");
            return sb.ToString();
        }

        // Puts the RTF in place of the selection; true if the text box actually took it
        bool TryInsertRtf(string rtf)
        {
            int start = text.SelectionStart;
            try { text.SelectedRtf = rtf; } catch { return false; }
            return text.SelectionStart > start;
        }

        // Replaces the selection with this picture (PNG first, plain bitmap if that is refused)
        bool PutPicture(Bitmap bmp)
        {
            return TryInsertRtf(PictureRtf(bmp, false)) || TryInsertRtf(PictureRtf(bmp, true));
        }

        // Puts a picture into the note at the caret. The picture is made to fit the note's width.
        public bool InsertPicture(Image src)
        {
            int w = src.Width, h = src.Height;
            int maxW = MaxPictureWidth;
            if (w > maxW) { h = (int)Math.Round((double)h * maxW / w); w = maxW; }
            if (w < 1 || h < 1) return false;
            using (Bitmap bmp = Resample(src, w, h, text.BackColor))
                return PutPicture(bmp);
        }

        // ---------- changing the size of a picture ----------
        // An inline picture is one "character", so a selection of exactly one character that holds a picture
        public bool IsImageSelected()
        {
            if (text.SelectionLength != 1) return false;
            string rtf = text.SelectedRtf;
            return rtf != null && rtf.IndexOf("\\pict", StringComparison.Ordinal) >= 0;
        }

        // ---------- reading a picture back ----------
        // The text box may hand a picture back in a different form than it was given (PNG, a bitmap, or a
        // Windows metafile wrapping a bitmap). These readers pull the actual pixels out of any of them.

        // A device independent bitmap (header + palette + pixels) as an image
        static Bitmap BitmapFromDib(byte[] d, int at, int length)
        {
            if (length < 40 || at < 0 || at + length > d.Length) return null;
            int header = BitConverter.ToInt32(d, at);
            int bitCount = BitConverter.ToInt16(d, at + 14);
            int compression = BitConverter.ToInt32(d, at + 16);
            int colorsUsed = BitConverter.ToInt32(d, at + 32);
            int palette = bitCount <= 8 ? (colorsUsed != 0 ? colorsUsed : 1 << bitCount) * 4
                                        : (colorsUsed != 0 ? colorsUsed * 4 : 0);
            if (compression == 3 && header == 40) palette += 12;           // BI_BITFIELDS masks
            int offBits = 14 + header + palette;

            var file = new byte[14 + length];
            file[0] = (byte)'B'; file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(offBits).CopyTo(file, 10);
            Array.Copy(d, at, file, 14, length);
            using (var ms = new MemoryStream(file))
            using (var tmp = new Bitmap(ms))
                return new Bitmap(tmp);
        }

        // The bitmap inside a Windows metafile (a metafile made from a pasted bitmap holds one drawing record)
        static Bitmap BitmapFromWmf(byte[] d)
        {
            int pos = (d.Length > 22 && BitConverter.ToUInt32(d, 0) == 0x9AC6CDD7) ? 22 : 0;   // optional placeable header
            pos += 18;                                                                         // metafile header
            while (pos + 6 <= d.Length)
            {
                int bytes = BitConverter.ToInt32(d, pos) * 2;
                int func = BitConverter.ToUInt16(d, pos + 4);
                if (bytes < 6 || pos + bytes > d.Length) break;
                int dibAt = -1;
                if (func == 0x0F43) dibAt = pos + 28;          // StretchDIBits
                else if (func == 0x0B41) dibAt = pos + 26;     // DibStretchBlt
                else if (func == 0x0D33) dibAt = pos + 24;     // SetDIBitsToDevice
                else if (func == 0x0940) dibAt = pos + 22;     // DibBitBlt
                if (dibAt > 0 && dibAt + 40 < pos + bytes) return BitmapFromDib(d, dibAt, pos + bytes - dibAt);
                if (func == 0) break;
                pos += bytes;
            }
            return null;
        }

        // Finds the picture in a piece of RTF and returns its pixels and the width it is shown at
        static bool ReadPicture(string rtf, out Bitmap pixels, out double shownWidth)
        {
            pixels = null;
            shownWidth = 0;
            foreach (Match m in Regex.Matches(rtf, @"\{\\pict(?<p>(?:\s*\\[a-zA-Z]+-?\d*)*)\s+(?<h>[0-9a-fA-F\s]+)\}"))
            {
                string props = m.Groups["p"].Value;
                string hex = Regex.Replace(m.Groups["h"].Value, @"\s", "");
                if (hex.Length < 16) continue;
                var data = new byte[hex.Length / 2];
                for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                try
                {
                    if (props.Contains("\\pngblip") || props.Contains("\\jpegblip"))
                        using (var ms = new MemoryStream(data))
                        using (Image img = Image.FromStream(ms)) pixels = new Bitmap(img);
                    else if (props.Contains("\\dibitmap")) pixels = BitmapFromDib(data, 0, data.Length);
                    else if (props.Contains("\\wmetafile")) pixels = BitmapFromWmf(data);
                }
                catch { pixels = null; }
                if (pixels == null) continue;

                double dpi = 96.0 * DpiScale;
                Match gw = Regex.Match(props, @"\\picwgoal(\d+)"), sx = Regex.Match(props, @"\\picscalex(\d+)");
                shownWidth = gw.Success
                    ? int.Parse(gw.Groups[1].Value) * (sx.Success ? int.Parse(sx.Groups[1].Value) / 100.0 : 1.0) * dpi / 1440.0
                    : pixels.Width;
                return true;
            }
            return false;
        }

        // factor = 1.25 for 25% larger, 0.8 for smaller; fitWidth = make it as wide as the note's text area
        public void ResizeSelectedImage(double factor, bool fitWidth)
        {
            try
            {
                if (!IsImageSelected()) return;
                string rtf = text.SelectedRtf;
                int pos = text.SelectionStart;

                Bitmap src;
                double shownW;
                if (!ReadPicture(rtf, out src, out shownW))
                {
                    // leave a clue in the log about what the text box handed back
                    Log.Write("picture could not be read: " + Regex.Replace(rtf.Length > 300 ? rtf.Substring(0, 300) : rtf, @"\s+", " "));
                    MessageBox.Show(this, "This picture is in a form that can't be resized here. " +
                        "Copy it and paste it into the note again.", "Stickies_Window",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (src)
                {
                    double wantW = fitWidth ? MaxPictureWidth : shownW * factor;
                    int w = (int)Math.Round(Math.Max(16, Math.Min(4000, wantW)));
                    int h = (int)Math.Round((double)src.Height * w / src.Width);
                    using (Bitmap bmp = Resample(src, w, Math.Max(1, h), text.BackColor))
                        PutPicture(bmp);
                }
                text.Select(pos, 1);          // keep the picture selected so it can be resized again
            }
            catch (Exception ex) { Log.Write("resize image: " + ex.Message); }
        }
    }
}
