using System;
using ConditioningControlPanel.Services.Billboard.Board;

namespace CCP.Core.Tests.Board;

/// <summary>
/// Core has no PNG codec (the Avalonia head plugs Skia into <see cref="BoardPicture.PngDecoder"/>).
/// These tests stand in a tiny raw format behind the real PNG signature: 8 signature bytes, width
/// and height as int32, then the 0xAARRGGBB pixels. The WPF suite encoded real PNGs; what is under
/// test here is the layout, the cache and the rules, not the codec (the head pins Skia's decode).
/// </summary>
internal static class BoardTestPng
{
    private static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    static BoardTestPng() => Install();

    /// <summary>Installs the stand-in decoder unless a real one is already set.</summary>
    public static void Install() => BoardPicture.PngDecoder ??= Decode;

    public static byte[] Png(int w, int h, Func<int, int, int> argb)
    {
        Install();
        var bytes = new byte[8 + 8 + w * h * 4];
        Array.Copy(Sig, bytes, 8);
        BitConverter.GetBytes(w).CopyTo(bytes, 8);
        BitConverter.GetBytes(h).CopyTo(bytes, 12);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                BitConverter.GetBytes(argb(x, y)).CopyTo(bytes, 16 + (y * w + x) * 4);
        return bytes;
    }

    private static (int[] Pixels, int Width, int Height)? Decode(byte[] png)
    {
        if (png.Length < 16) return null;
        int w = BitConverter.ToInt32(png, 8), h = BitConverter.ToInt32(png, 12);
        if (w <= 0 || h <= 0 || png.Length < 16 + w * h * 4) return null;
        var px = new int[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = BitConverter.ToInt32(png, 16 + i * 4);
        return (px, w, h);
    }
}
