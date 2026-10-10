using System;
using System.Text.RegularExpressions;
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

        /// <summary>The picture's own size from its IHDR chunk, checked against <see cref="MaxPixels"/> (WPF
        /// Decode's guard) before anything decodes it. The clipboard and save half is the head's
        /// (CCP.Avalonia GameWindow.GoonShell.cs).</summary>
        public static bool PixelsOk(byte[] png)
        {
            if (!IsPng(png) || png.Length < 24) return false;
            if (png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R') return false;
            long w = ((long)png[16] << 24) | ((long)png[17] << 16) | ((long)png[18] << 8) | png[19];
            long h = ((long)png[20] << 24) | ((long)png[21] << 16) | ((long)png[22] << 8) | png[23];
            return w > 0 && h > 0 && w <= MaxPixels && h <= MaxPixels;
        }
    }
}
