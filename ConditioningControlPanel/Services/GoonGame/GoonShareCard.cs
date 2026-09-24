using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// <c>share-card {id, action: copy|save, name, png}</c> - the Goon Game's end-of-match card
    /// (Resources/web/goon/ui/shareCard.js). The page tries the browser clipboard first; this is
    /// the road when WebView2 refuses it, and the only road for Save.
    ///
    /// WHY IT IS ADMISSIBLE ON THIS BRIDGE: the page hands over BYTES and nothing else. They must
    /// be a real PNG (magic bytes, decoded by WIC, capped in size and pixels) before they touch the
    /// clipboard, and a save goes only where the user points the system dialog. The page's file
    /// name is a suggestion, reduced to [A-Za-z0-9._-] and forced to .png. No path, no URL, no
    /// shell open. Every request answers exactly one <c>share-card-result {id, ok, error}</c>.
    /// </summary>
    internal static class GoonShareCard
    {
        /// <summary>A 2400x1260 card is ~3 MB of PNG, ~4 MB of base64. Twelve is generous.</summary>
        public const int MaxBase64Chars = 12 * 1024 * 1024;
        public const int MaxPixels = 4096;

        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly Regex IdRx = new("^[A-Za-z0-9]{1,24}$", RegexOptions.Compiled);

        public sealed record Request(string Id, string Action, string FileName, byte[] Png);

        /// <summary>Pure: parse and check a frame. Null + error when it is not a card we will touch.</summary>
        public static Request? Parse(JObject o, out string error)
        {
            error = "";
            var id = (string?)o["id"] ?? "";
            if (!IdRx.IsMatch(id)) { error = "bad-id"; return null; }
            var action = (string?)o["action"] ?? "";
            if (action != "copy" && action != "save") { error = "bad-action"; return null; }
            var b64 = (string?)o["png"] ?? "";
            if (b64.Length == 0 || b64.Length > MaxBase64Chars) { error = "too-big"; return null; }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (FormatException) { error = "bad-format"; return null; }
            if (!IsPng(bytes)) { error = "bad-format"; return null; }
            return new Request(id, action, SafeFileName((string?)o["name"]), bytes);
        }

        public static bool IsPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < PngMagic.Length) return false;
            for (int i = 0; i < PngMagic.Length; i++) if (bytes[i] != PngMagic[i]) return false;
            return true;
        }

        /// <summary>'goon-game-2026-09-24.png' shape; anything else is squashed to it.</summary>
        public static string SafeFileName(string? name)
        {
            var s = Regex.Replace(name ?? "", "[^A-Za-z0-9._-]+", "-").Trim('.', '-');
            if (s.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
            if (s.Length == 0) s = "goon-game";
            if (s.Length > 60) s = s[..60];
            return s + ".png";
        }

        /// <summary>UI thread. Answers through <paramref name="reply"/> exactly once.</summary>
        public static void Handle(JObject o, Window? owner, Action<object> reply)
        {
            var req = Parse(o, out var error);
            var id = req?.Id ?? ((string?)o["id"] ?? "");
            if (req == null)
            {
                App.Logger?.Debug("GoonShareCard: refused ({E})", error);
                reply(new { type = "share-card-result", id, ok = false, error });
                return;
            }
            try
            {
                var bmp = Decode(req.Png);
                if (bmp == null) { reply(new { type = "share-card-result", id, ok = false, error = "bad-format" }); return; }
                if (req.Action == "copy")
                {
                    var ok = CopyToClipboard(req.Png, bmp);
                    reply(new { type = "share-card-result", id, ok, error = ok ? "" : "clipboard-busy" });
                }
                else
                {
                    var r = SaveWithDialog(req.Png, req.FileName, owner);
                    reply(new { type = "share-card-result", id, ok = r == "", error = r });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("GoonShareCard: {A} failed: {E}", req.Action, ex.Message);
                reply(new { type = "share-card-result", id, ok = false, error = "failed" });
            }
        }

        private static BitmapSource? Decode(byte[] png)
        {
            using var ms = new MemoryStream(png);
            var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return null;
            var f = dec.Frames[0];
            if (f.PixelWidth <= 0 || f.PixelHeight <= 0 || f.PixelWidth > MaxPixels || f.PixelHeight > MaxPixels) return null;
            f.Freeze();
            return f;
        }

        /// <summary>
        /// PNG format (what Discord, browsers and most chat apps read, alpha intact) plus the
        /// classic bitmap formats (everything else). Retried: the clipboard is a shared lock and
        /// another app holding it for a moment is normal.
        /// </summary>
        private static bool CopyToClipboard(byte[] png, BitmapSource bmp)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetData("PNG", new MemoryStream(png), false);
                    data.SetImage(bmp);
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            return false;
        }

        private static string SaveWithDialog(byte[] png, string fileName, Window? owner)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileName,
                DefaultExt = ".png",
                Filter = ConditioningControlPanel.Localization.Loc.Get("goon_share_png_filter") + " (*.png)|*.png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
                OverwritePrompt = true,
            };
            var picked = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (picked != true) return "cancelled";
            File.WriteAllBytes(dlg.FileName, png);
            App.Logger?.Information("GoonShareCard: saved a match card ({N} bytes)", png.Length);
            return "";
        }
    }
}
