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
    /// <para>ponytail: <see cref="TryLoad"/> keeps no decode cache, and that is a measured decision.
    /// The one exception is <see cref="FirstOf"/> (program art chains), which caches hits AND
    /// misses per (active mod, path, width), so a mod switch reads fresh without invalidation.
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
        internal static Bitmap? TryLoad(string? resourceName, int? decodeWidth = null)
        {
            if (string.IsNullOrWhiteSpace(resourceName)) return null;

            var overridePath = CoreModArt.OverridePath(resourceName);
            if (overridePath != null)
            {
                try { if (File.Exists(overridePath)) { using var file = File.OpenRead(overridePath); return Decode(file, decodeWidth); } }
                catch (Exception ex) { Log.Warning(ex, "[ModArt] mod override {Path} would not load", overridePath); }
            }

            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{resourceName}");
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                return Decode(stream, decodeWidth);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModArt] built-in {Name} would not load", resourceName);
                return null;
            }
        }
        private static readonly System.Collections.Generic.Dictionary<string, Bitmap?> WardrobeCache = new();

        /// <summary>WPF WardrobeArt.GetImage: a registry item's PNG off disk at 384px (Core
        /// WardrobeCatalog.ArtPath), cached misses included; null when the id or its art is absent.</summary>
        internal static Bitmap? Wardrobe(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (WardrobeCache)
            {
                if (WardrobeCache.TryGetValue(id, out var cached)) return cached;
                Bitmap? art = null;
                try
                {
                    var path = ConditioningControlPanel.Services.WardrobeCatalog.ArtPath(id);
                    if (path != null && File.Exists(path)) { using var file = File.OpenRead(path); art = Decode(file, 384); }
                }
                catch (Exception ex) { Log.Debug("WardrobeCatalog: art for {Id} failed to load: {E}", id, ex.Message); }
                return WardrobeCache[id] = art;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<(string, int), Bitmap?> BannerCache = new();

        /// <summary>WPF CosmeticsCatalog.GetBannerImage (1024) / GetBannerThumbnail (256) as a brush: a gradient
        /// preset, or the scene art decoded at <paramref name="decodeWidth"/>, UniformToFill. Null for an unknown id
        /// or art that will not load (the caller keeps its default gradient). Decodes, misses included, are cached.</summary>
        internal static IBrush? Banner(string? id, int decodeWidth, AlignmentY alignY = AlignmentY.Center)
        {
            var option = ConditioningControlPanel.Services.CosmeticsPool.FindBanner(id);
            if (option == null) return null;
            if (option.Gradient is { } g)
                return new LinearGradientBrush
                {
                    StartPoint = new global::Avalonia.RelativePoint(0, 0, global::Avalonia.RelativeUnit.Relative),
                    EndPoint = new global::Avalonia.RelativePoint(1, 1, global::Avalonia.RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(g.From), 0), new GradientStop(Color.Parse(g.Via), 0.5), new GradientStop(Color.Parse(g.To), 1) }
                };
            Bitmap? art;
            lock (BannerCache)
            {
                if (!BannerCache.TryGetValue((option.Id, decodeWidth), out art))
                {
                    try { using var stream = AssetLoader.Open(new Uri($"avares://CCP.Avalonia/{option.PackPath}")); art = Decode(stream, decodeWidth); }
                    catch (Exception ex) { Log.Debug("CosmeticsCatalog: banner {Id} failed to load: {E}", option.Id, ex.Message); }
                    BannerCache[(option.Id, decodeWidth)] = art;
                }
            }
            return art == null ? null : new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentY = alignY };
        }

        /// <summary>WPF CosmeticsCatalog.GetAvatarImage: a preset avatar's PNG off disk at 256px, cached misses
        /// included; null when the id or its art is absent.</summary>
        internal static Bitmap? AvatarPreset(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (WardrobeCache)
            {
                var key = "avatar:" + id;
                if (WardrobeCache.TryGetValue(key, out var cached)) return cached;
                Bitmap? art = null;
                try
                {
                    var path = ConditioningControlPanel.Services.CosmeticsPool.AvatarPath(id);
                    if (path != null && File.Exists(path)) { using var file = File.OpenRead(path); art = Decode(file, 256); }
                }
                catch (Exception ex) { Log.Debug("CosmeticsCatalog: avatar preset {Id} failed to load: {E}", id, ex.Message); }
                return WardrobeCache[key] = art;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<(string, string, int?), Bitmap?> FirstOfCache = new();

        /// <summary>
        /// The first of <paramref name="resourceNames"/> that loads (mod override, then built-in), or
        /// null - WPF ProgramArt's resolver chain. Cached per active mod like WPF ModResourceResolver,
        /// so repainting a list of cards decodes each file once.
        /// </summary>
        internal static Bitmap? FirstOf(System.Collections.Generic.IEnumerable<string> resourceNames, int? decodeWidth = null)
        {
            var mod = CoreMods.ActiveModId;
            foreach (var name in resourceNames)
            {
                Bitmap? art;
                lock (FirstOfCache)
                {
                    if (!FirstOfCache.TryGetValue((mod, name, decodeWidth), out art))
                        FirstOfCache[(mod, name, decodeWidth)] = art = TryLoad(name, decodeWidth);
                }
                if (art != null) return art;
            }
            return null;
        }

        /// <summary>WPF's DecodePixelWidth: decode straight to the width a surface shows, never larger.</summary>
        private static Bitmap Decode(Stream stream, int? width)
            => width is int w ? Bitmap.DecodeToWidth(stream, w) : new Bitmap(stream);

        /// <summary>
        /// A feature card's hero strip and side plate, painted from one feature PNG the way the WPF
        /// cards' HeroArtBrush/SideArtBrush are (UniformToFill, hero right-aligned at 0.9), and
        /// repainted on every mod switch while <paramref name="owner"/> is on screen.
        /// </summary>
        internal static void BindFeaturePlates(Control owner, string resourceName, Border? hero, Border? side)
        {
            void Paint()
            {
                // One decode for both plates at WPF's larger cap (side plate DecodePixelWidth=800).
                var art = TryLoad(resourceName, 800);
                if (art == null) return;
                if (hero != null) hero.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, Opacity = 0.9 };
                if (side != null) side.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            }
            void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(Paint);

            // Painted on attach only: a view built but never shown costs no decode.
            owner.AttachedToVisualTree += (_, _) => { CoreMods.ModChanged += OnModChanged; Paint(); };
            owner.DetachedFromVisualTree += (_, _) => CoreMods.ModChanged -= OnModChanged;
        }
    }
}
