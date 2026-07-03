using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Compositor.Layers;

namespace ConditioningControlPanel.Avalonia.Services.Flash;

/// <summary>
/// LRU decode cache for flash images, mirroring WPF FlashService._imageDecodeCache
/// (keyed path|decodeMax, 50 entries / 200MB byte cap). WPF added this cache after a
/// production chaos OOM (~1.3GB native heap from per-spawn decodes); the port needs it
/// for the same reason — every flash spawn without it is a full decode.
///
/// Ref-count contract: the cache holds one reference on each entry; every hit returned
/// by <see cref="GetOrDecode"/> carries an additional reference the consumer must
/// Release (FlashLayer does this when the item is removed, under its render lock).
/// Eviction releases only the cache's reference, so frames still shown by a live flash
/// are never disposed under the render thread.
/// </summary>
internal sealed class FlashImageCache
{
    // WPF parity: FlashService.cs MAX_IMAGE_CACHE_ENTRIES / MAX_IMAGE_CACHE_BYTES.
    private const int MAX_ENTRIES = 50;
    private const long MAX_BYTES = 200L * 1024 * 1024;

    // WPF parity: LoadGifFrames caps GIFs at 60 frames / 30MB per file, default 100ms delay.
    private const int MAX_GIF_FRAMES = 60;
    private const double MAX_MEMORY_MB_PER_FILE = 30.0;
    private const int DEFAULT_FRAME_DELAY_MS = 100;

    private readonly object _lock = new();
    private readonly Dictionary<string, CacheEntry> _entries = new();
    private long _bytes;

    /// <summary>
    /// Return the decoded frame set for <paramref name="path"/>, decoding on the calling
    /// thread on a miss (callers run this inside Task.Run — never on the UI thread).
    /// The returned set carries one reference owned by the caller; returns null on failure.
    /// </summary>
    public SkiaFrameSet? GetOrDecode(string path, int decodeMaxDim)
    {
        var key = path + "|" + decodeMaxDim;

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var hit))
            {
                hit.LastAccess = DateTime.UtcNow;
                hit.Set.AddRef();
                return hit.Set;
            }
        }

        // Decode outside the lock so a slow decode never blocks cache hits from other spawns.
        var set = SkiaImageDecoder.Decode(path, MAX_GIF_FRAMES, decodeMaxDim, MAX_MEMORY_MB_PER_FILE,
            DEFAULT_FRAME_DELAY_MS, maxFrameDelayMs: 0);
        if (set == null) return null;

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var raced))
            {
                // A concurrent decode of the same key won the race; use its entry and drop ours.
                raced.LastAccess = DateTime.UtcNow;
                raced.Set.AddRef();
                set.Release(); // ours never left this method — no renderer can reference it
                return raced.Set;
            }

            EvictWhileOverBudget(set.PixelBytes);

            set.AddRef(); // cache's reference (the creator reference is the caller's)
            _entries[key] = new CacheEntry(set) { LastAccess = DateTime.UtcNow };
            _bytes += set.PixelBytes;
        }

        return set;
    }

    /// <summary>Release all cache references. Entries still leased by live flashes survive until those release.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values)
                entry.Set.Release();
            _entries.Clear();
            _bytes = 0;
        }
    }

    private void EvictWhileOverBudget(long incomingBytes)
    {
        // Caller holds _lock. Least-recently-accessed first (WPF parity).
        while (_entries.Count >= MAX_ENTRIES || _bytes + incomingBytes > MAX_BYTES)
        {
            string? oldestKey = null;
            var oldestTime = DateTime.MaxValue;
            foreach (var kvp in _entries)
            {
                if (kvp.Value.LastAccess < oldestTime)
                {
                    oldestTime = kvp.Value.LastAccess;
                    oldestKey = kvp.Key;
                }
            }
            if (oldestKey == null) break;

            var evicted = _entries[oldestKey];
            _entries.Remove(oldestKey);
            _bytes -= evicted.Set.PixelBytes;
            // Releases the cache's ref only. If a live flash still leases the set, the
            // frames survive until that item releases (under the layer's render lock).
            evicted.Set.Release();
        }
    }

    private sealed class CacheEntry
    {
        public SkiaFrameSet Set { get; }
        public DateTime LastAccess { get; set; }
        public CacheEntry(SkiaFrameSet set) => Set = set;
    }
}
