using System;
using System.IO;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The header probe of WPF 7.1.5 Services/Media/AnimatedWebp.cs (the decode + attach half of that
/// file is WPF bitmap code and stays with the head that needs it; partial so it can land beside this).
/// </summary>
internal static partial class AnimatedWebp
{
    /// <summary>
    /// Cheap 21-byte header probe: RIFF/WEBP container with a VP8X extended header whose
    /// animation flag (0x02, per the WebP container spec / libwebp ANIMATION_FLAG) is set.
    /// Still webps (VP8/VP8L simple format, or VP8X without the flag) return false.
    /// </summary>
    public static bool IsAnimated(string path)
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
