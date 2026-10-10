using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// The head's doors for the transfer cache (own-media sending and the received inbox). WPF reads
    /// these straight off App / DtrhAssetManifest / SkiaSharp / WinRT; Core has none of them, so the
    /// head seeds each one once (first seeding wins) and the stores stay pure file IO.
    /// </summary>
    internal static class TransferPlatform
    {
        /// <summary>The profile folder the cache lives under (WPF App.UserDataPath). Tests point it at a temp dir.</summary>
        public static Func<string> UserDataRoot { get; set; } = () => CorePaths.UserData;

        /// <summary>The user's ACTIVE local library (WPF DtrhAssetManifest.EnumerateActive): full path,
        /// assets-relative forward-slashed path, bytes, picture or video. Local disk only, on purpose:
        /// remote media never becomes something the app offers to hand to a duel partner.</summary>
        public static Func<IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)>> ActivePool { get; set; }
            = () => Array.Empty<(string, string, long, bool)>();

        /// <summary>Frame count of a GIF (WPF SKCodec.FrameCount). Null seam or a throw = "animated",
        /// as WPF treats a gif it cannot open.</summary>
        public static Func<string, int>? GifFrameCount { get; set; }

        /// <summary>The page-side base of the cache folder (WPF's https://ccp.cache/ virtual host; this
        /// head serves the same folder off the loopback asset server).</summary>
        public static Func<string> CacheUrlBase { get; set; } = () => "https://ccp.cache/";

        /// <summary>A library file's page url from its assets-relative path (WPF https://ccp.assets/&lt;rel&gt;,
        /// each segment escaped).</summary>
        public static Func<string, string> AssetUrl { get; set; } = rel =>
            "https://ccp.assets/" + string.Join('/', rel.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

        /// <summary>Post work to the UI thread (the page bridge is thread-affine). Default: run inline.</summary>
        public static Action<Action> OnUi { get; set; } = a => a();

        /// <summary>WPF AnimatedWebp.IsAnimated: the VP8X header's animation flag, 21 bytes in.</summary>
        public static bool WebpIsAnimated(string path)
        {
            try
            {
                Span<byte> h = stackalloc byte[21];
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Read(h) < h.Length) return false;
                return h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                    && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P'
                    && h[12] == (byte)'V' && h[13] == (byte)'P' && h[14] == (byte)'8' && h[15] == (byte)'X'
                    && (h[20] & 0x02) != 0;
            }
            catch { return false; }
        }
    }
}
