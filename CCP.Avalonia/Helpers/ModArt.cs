using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// The head's half of <see cref="CoreModArt"/>: turn a Resources-relative logical name into a
    /// decoded bitmap, mod override first, this head's shipped <c>avares://</c> copy second.
    ///
    /// <para>This is the Avalonia twin of the WPF head's
    /// <c>Services.ModResourceResolver.ResolveImage</c>, split the way the seam requires - Core
    /// answers "is there an override and where", and the decode plus the built-in fallback stay
    /// here because <c>avares://</c> is this head's packaging and Core must never learn it.</para>
    ///
    /// <para>Order matters and is the WPF order: probing the shipped copy first would always hit
    /// the bundled PNG and make mod art unreachable. Null means "neither exists", which every
    /// caller must treat as the WPF null path (draw the glyph, collapse the plate) rather than as
    /// a failure.</para>
    ///
    /// <para>ponytail: <c>TubeFitDialog</c>, <c>AvatarTubeWindow</c> and <c>ModCreatorWindow</c>
    /// still carry private copies of this two-step; fold them in when a layer owns those files.
    /// No decode cache yet - WPF's resolver keys one on (event skin, mod id, path), and every
    /// caller here loads a handful of small PNGs once, so add one when a caller loads per-frame.
    /// The event-skin tier of the WPF chain is missing too, and that half is Core's:
    /// <see cref="CoreModArt"/> has no event-skin provider yet, so an event cannot outrank a mod
    /// on this head.</para>
    /// </summary>
    internal static class ModArt
    {
        /// <summary>
        /// The mod's override, else this head's shipped copy, else null.
        /// </summary>
        /// <param name="resourceName">
        /// Forward-slash path relative to <c>Resources/</c>, e.g. "bubble.png",
        /// "features/flash.png", "achievements/lv_10.png". Traversal is rejected inside
        /// <see cref="CoreModArt.OverridePath"/>.
        /// </param>
        internal static Bitmap? TryLoad(string? resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return null;

            var overridePath = CoreModArt.OverridePath(resourceName);
            if (overridePath != null)
            {
                try { if (File.Exists(overridePath)) return new Bitmap(overridePath); }
                catch (Exception ex) { Log.Warning(ex, "[ModArt] mod override {Path} would not load", overridePath); }
            }

            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{resourceName}");
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModArt] built-in {Name} would not load", resourceName);
                return null;
            }
        }
    }
}
