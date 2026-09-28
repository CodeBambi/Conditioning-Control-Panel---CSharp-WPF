using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform;
using ConditioningControlPanel.Models;
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
    /// <para>All three private copies of this two-step are folded in: <c>TubeFitDialog</c> and
    /// <c>AvatarTubeWindow</c> call <see cref="TryLoad"/> directly, and
    /// <c>ModCreatorWindow.TryLoadSlotHint</c> is gone. Exactly two notes still point a future
    /// wirer at "TubeFitDialog.TryLoadImage is the decode pattern" - a method this file's fold
    /// deleted - and THIS is the answer they should name:
    /// <c>Views/Features/BubbleCountFeatureControl.axaml.cs</c> and
    /// <c>Views/Features/PinkFilterFeatureControl.axaml.cs</c>. Neither is reachable from
    /// Helpers/, so the layer that owns them makes the swap.</para>
    ///
    /// <para>ponytail: no decode cache, and that is now a measured decision rather than a guess.
    /// All 42 call sites on this head were read: every one sits in a constructor, a one-shot
    /// build (BubbleCountWindow.LoadBubbleImage, AvatarTubeWindow.LoadAvatarPoses), or a
    /// user-driven repaint (SetTubeStyle, a ModChanged handler) that stores the Bitmap it gets.
    /// Nothing decodes inside a render or a tick, so a cache would buy nothing and would need
    /// invalidating on every mod switch. WPF's resolver keys one on (event skin, mod id, path)
    /// because it also serves per-frame bubble sprites; this head does not. Add one the first
    /// time a caller loads inside a render or a tick. <c>Controls/TierBadge</c> keeps its own
    /// three-bitmap cache and does NOT come through here on purpose - tier livery is commerce chrome that a mod must
    /// not be able to restyle, which is why the WPF badge also reaches past ModResourceResolver.</para>
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
            /// <summary>
        /// A feature card's hero strip and side plate, painted from one feature PNG the way the WPF
        /// cards' HeroArtBrush/SideArtBrush are (UniformToFill, hero right-aligned at 0.9), and
        /// repainted on every mod switch while <paramref name="owner"/> is on screen.
        /// </summary>
        internal static void BindFeaturePlates(Control owner, string resourceName, Border? hero, Border side)
        {
            void Paint()
            {
                var art = TryLoad(resourceName);
                if (art == null) return;
                if (hero != null) hero.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, Opacity = 0.9 };
                side.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            }
            void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(Paint);

            Paint();
            owner.AttachedToVisualTree += (_, _) => { CoreMods.ModChanged += OnModChanged; Paint(); };
            owner.DetachedFromVisualTree += (_, _) => CoreMods.ModChanged -= OnModChanged;
        }
    }
}
