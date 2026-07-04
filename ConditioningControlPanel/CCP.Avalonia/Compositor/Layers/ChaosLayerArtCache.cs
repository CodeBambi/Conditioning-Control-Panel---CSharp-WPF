using System;
using System.Collections.Concurrent;
using System.IO;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Decoded <see cref="SKImage"/> cache for chaos layers that draw word-art PNGs
/// (effect banner, announcer). Mirrors the AvaloniaChaosArt bitmap cache contract:
/// keyed by full path, null "no such file / decode failure" results cached too,
/// bounded and dropped wholesale when full (chaos art is a small, mostly-static
/// pool — a cheap re-decode beats unbounded native memory). Decoding happens at
/// Show time (content change), never per frame (UCE zero-per-frame-alloc rule).
/// </summary>
internal static class ChaosLayerArtCache
{
    private static readonly ConcurrentDictionary<string, SKImage?> _cache = new();
    private const int MaxEntries = 64;

    public static SKImage? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_cache.Count >= MaxEntries && !_cache.ContainsKey(path))
            _cache.Clear();
        return _cache.GetOrAdd(path, static p => Decode(p));
    }

    private static SKImage? Decode(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var bmp = SKBitmap.Decode(path);
            return bmp == null ? null : SKImage.FromBitmap(bmp);
        }
        catch
        {
            return null;
        }
    }
}
