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
    /// <para><b>Three private copies are still out there, and all three are BYTE-FOR-BYTE this
    /// method</b> - same order, same catches, same log shape - so folding each one in is a delete
    /// plus a call-site rename, not a port:</para>
    /// <list type="bullet">
    ///   <item><c>TubeFitDialog.TryLoadImage</c> (CCP.Avalonia/Views/Dialogs/TubeFitDialog.axaml.cs)</item>
    ///   <item><c>AvatarTubeWindow.TryLoadImage</c> (CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs)</item>
    ///   <item><c>ModCreatorWindow.TryLoadSlotHint</c> (CCP.Avalonia/Views/Windows/ModCreatorWindow.axaml.cs)</item>
    /// </list>
    /// <para>Six <c>Views/Features/*FeatureControl.axaml.cs</c> notes and
    /// <c>EmiRingWindow.LoadThumb</c> still point a future wirer at
    /// "TubeFitDialog.TryLoadImage is the decode pattern"; THIS is the answer they should name.
    /// None of those files is reachable from Helpers/, so the layer that owns them makes the swap.</para>
    ///
    /// <para>ponytail: no decode cache, and that is a decision rather than a gap. WPF's resolver
    /// keys one on (event skin, mod id, path) because it serves per-frame art; every caller here
    /// loads a handful of small PNGs once at build-time of a view. Add one the first time a caller
    /// loads inside a render or a tick. <c>Controls/TierBadge</c> keeps its own three-bitmap cache
    /// and does NOT come through here on purpose - tier livery is commerce chrome that a mod must
    /// not be able to restyle, which is why the WPF badge also reaches past ModResourceResolver.</para>
    ///
    /// <para>ponytail: the event-skin tier of the WPF chain is missing, and that half is Core's -
    /// <see cref="CoreModArt"/> has no event-skin provider, so an event cannot outrank a mod on
    /// this head. Needs a provider on CCP.Core/Services/CoreModArt.cs before anything here can
    /// use it.</para>
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
