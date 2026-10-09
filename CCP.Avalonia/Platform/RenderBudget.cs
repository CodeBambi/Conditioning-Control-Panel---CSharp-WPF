// Home smoothness lane (owner, 2026-10-09: "as smooth as the WPF one; still a little bit laggy").
//
// What the owner's Release trace showed (a 30 s sampled trace on
// Home, engine on, RTX 5080 + Intel iGPU):
//   - ImmutableBitmap.Draw: 1568 calls, 3 ms EACH (4.7 s of the render thread's 30 s). A cached GPU
//     texture draws in microseconds; 3 ms is an UPLOAD. Every Home tile picture is 1376x768
//     (4.2 MB decoded) and Home shows about twenty of them, 85 MB, against Avalonia's default Skia
//     GPU resource cache of ~28 MB (SkiaOptions.MaxGpuResourceSizeBytes = 1024*600*4*12). Skia
//     evicts and re-uploads the pictures every frame, and the full-window blit (7.3 s) then waits
//     on a GPU queue full of uploads.
//   - Avalonia 12.1 on Windows draws the dirty rect into a retained layer and blits the WHOLE layer
//     into the composition surface every frame (ServerCompositionTarget.Render: the WinUI /
//     DirectComposition / swap-chain targets never claim RetainsPreviousFrameContents, only the
//     software framebuffer does). There is no dirty-rect presentation on the GPU paths; the lever is
//     to make each frame cheap, and to compose 30 frames a second, not 60.
//
// The budget below keeps every Home picture, the tile BitmapCaches and the window layer resident.
// The env switches let the owner A/B the Win32 presentation path without a rebuild:
//   CCP_WIN32_COMPOSITION = winui | dcomp | swapchain | redirection (comma list, priority order)
//   CCP_WIN32_RENDERING   = angle | wgl | vulkan | software        (comma list, priority order)
//   CCP_GPU_ADAPTER       = index or a case-insensitive substring of the adapter name
//   CCP_FRAME_STATS=1     logs composed frames per second per window every 5 s (Controls/Fx/FrameClock).

using System;
using System.Collections.Generic;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Pure rules for the GPU budget and the presentation switches (tests).</summary>
    public static class RenderBudget
    {
        /// <summary>Decoded bytes of one Home tile picture (Assets/features/*.png are 1376x768 BGRA).</summary>
        public const long TileArtBytes = 1376L * 768 * 4;

        /// <summary>Tiles a full Home can show at once (the mosaic + launcher strip + billboard art), rounded up.</summary>
        public const int HomeTilesResident = 32;

        /// <summary>Two window-sized layers (the compositor's retained layer and the fog surface) at 2560x1440.</summary>
        public const long WindowLayersBytes = 2L * 2560 * 1440 * 4;

        /// <summary>The Skia GPU resource cache: 256 MB, comfortably above the Home working set so no
        /// picture is uploaded twice. Still a cap: a long media session cannot grow it without bound.</summary>
        public const long GpuResourceCacheBytes = 256L * 1024 * 1024;

        /// <summary>What Home keeps on the GPU at once; the cache must hold it with headroom.</summary>
        public static long HomeWorkingSetBytes => HomeTilesResident * TileArtBytes + WindowLayersBytes;

        /// <summary>Parses a comma list of mode names; null when the variable is unset or names nothing.</summary>
        public static IReadOnlyList<T>? ParseModes<T>(string? env, Func<string, T?> map) where T : struct
        {
            if (string.IsNullOrWhiteSpace(env)) return null;
            var list = new List<T>();
            foreach (var raw in env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (map(raw.ToLowerInvariant()) is T m && !list.Contains(m)) list.Add(m);
            return list.Count == 0 ? null : list;
        }

        /// <summary>The adapter the app renders on: index 0 (the primary, what Avalonia picks on its
        /// own) unless CCP_GPU_ADAPTER names another by index or by a substring of its name.</summary>
        public static int ChooseAdapter(IReadOnlyList<string?> names, string? env)
        {
            if (names.Count == 0) return 0;
            if (string.IsNullOrWhiteSpace(env)) return 0;
            env = env.Trim();
            if (int.TryParse(env, out var idx)) return idx >= 0 && idx < names.Count ? idx : 0;
            for (int i = 0; i < names.Count; i++)
                if (names[i] is { } n && n.Contains(env, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
    }
}
