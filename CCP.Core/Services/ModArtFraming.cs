using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where in a source image a given UI surface's crop window sits.
    ///
    /// <para><b>Focal point + zoom, deliberately not a rect.</b> A rect is what WPF's
    /// <c>ImageBrush.Viewbox</c> wants, but it bakes the surface's aspect ratio into the stored
    /// value: restyle a chip from 1.64:1 to 1.4:1 and every stored rect starts mis-framing. A
    /// centre plus a zoom survives that, because <see cref="ModArtFramingRegistry.ToViewbox"/>
    /// re-derives the rect from whatever aspect the surface has TODAY. It is also the form a
    /// human can reason about, which matters because the mod editor writes these numbers by
    /// dragging a preview and an author may later read them back in mod.json.</para>
    ///
    /// <para>Serialized inside <c>mod.json</c> under <c>artFraming</c>, keyed resource path →
    /// surface id → framing. See <see cref="Models.ModManifest.ArtFraming"/>.</para>
    /// </summary>
    public sealed class ModArtFraming
    {
        /// <summary>Horizontal centre of the crop window, 0..1 across the source image.</summary>
        [Newtonsoft.Json.JsonProperty("centerX")]
        public double CenterX { get; set; } = 0.5;

        /// <summary>Vertical centre of the crop window, 0..1 down the source image.</summary>
        [Newtonsoft.Json.JsonProperty("centerY")]
        public double CenterY { get; set; } = 0.5;

        /// <summary>
        /// How far in. 1.0 = the largest window of the surface's aspect that fits the source
        /// (i.e. exactly what <c>UniformToFill</c> would show); 2.0 shows half that width and
        /// height. Below 1.0 is meaningless — there is nothing outside the image to show — so
        /// <see cref="ModArtFramingRegistry.ToViewbox"/> clamps it.
        /// </summary>
        [Newtonsoft.Json.JsonProperty("zoom")]
        public double Zoom { get; set; } = 1.0;

        /// <summary>Deep copy. The editor mutates live copies while previewing.</summary>
        public ModArtFraming Clone() => new() { CenterX = CenterX, CenterY = CenterY, Zoom = Zoom };

        /// <summary>True when this is the no-op framing (dead centre, no zoom).</summary>
        public bool IsDefault => Zoom <= 1.0 + Epsilon
                                && Math.Abs(CenterX - 0.5) < Epsilon
                                && Math.Abs(CenterY - 0.5) < Epsilon;

        internal const double Epsilon = 0.0005;
    }

    /// <summary>How a surface darkens its art so text over it stays legible. The editor's
    /// preview paints the matching scrim so what an author frames is what they will see.</summary>
    public enum ArtScrim
    {
        /// <summary>No overlay.</summary>
        None,
        /// <summary>Caption sits in a dark band at the foot (rail chips, Play cards).</summary>
        FootBand,
        /// <summary>Live controls over the whole face, so it darkens throughout (Lockdown/Blink cards).</summary>
        FullFace,
    }

    /// <summary>
    /// One place in the UI that shows framed art, described geometrically. The registry is the
    /// single source of truth for "what shape is this slot": the runtime derives its Viewbox from
    /// it and the mod editor draws its preview from it, so the two cannot silently disagree.
    /// </summary>
    /// <param name="Id">Stable key stored in mod.json. NEVER rename one — same rule as a slot key.</param>
    /// <param name="DisplayName">Shown in the editor above the preview.</param>
    /// <param name="AspectRatio">
    /// Width ÷ height of the on-screen frame. Exact for a fixed-size surface; a REPRESENTATIVE
    /// value when <paramref name="FluidWidth"/> is set — see that parameter.
    /// </param>
    /// <param name="CornerRadius">Frame corner radius in DIPs, for the editor's mock.</param>
    /// <param name="Scrim">Which darkening the real surface lays over the art.</param>
    /// <param name="FluidWidth">
    /// True when the real frame's width follows the window rather than a fixed number: the Play
    /// cards sit in 2- and 3-column <c>UniformGrid</c>s over a resizable window, and the hero and
    /// page headers span whatever their container gives them. Their height IS fixed (138 for a card
    /// plate), so the ASPECT moves with the window — a 2-column card is about 4.3:1 where a
    /// 3-column one is about 2.9:1.
    ///
    /// <para>Consequence, and it is a real limitation rather than a rounding error: the stored
    /// window is cut to <see cref="AspectRatio"/>, and at runtime <c>UniformToFill</c> centre-crops
    /// whatever difference remains between that and the live frame. A subject framed hard against
    /// the top or bottom of the preview can therefore lose some of that edge in the app, by an
    /// amount that changes with window width. The editor marks these surfaces so an author is told
    /// rather than surprised. The proper fix is to recompute the Viewbox from the live control on
    /// layout — which the focal-point-plus-zoom storage format was chosen to make possible — and is
    /// deliberately left as follow-up rather than smuggled into this change.</para>
    /// </param>
    public sealed record ArtSurface(
        string Id,
        string DisplayName,
        double AspectRatio,
        double CornerRadius,
        ArtScrim Scrim,
        bool FluidWidth = false);

    /// <summary>
    /// One (resource path, surface) pair the app frames, plus the crop the SHIPPED art uses.
    /// </summary>
    /// <param name="ResourcePath">
    /// The mod compatibility surface, e.g. <c>features/fyp.png</c>. Several bindings can share
    /// one path — that is the whole point: one file feeding differently-shaped frames is exactly
    /// what a baked-in crop gets wrong.
    /// </param>
    /// <param name="SurfaceId">An <see cref="ArtSurface.Id"/>.</param>
    /// <param name="ShippedViewbox">
    /// The hand-tuned rect the embedded art is framed by today, lifted verbatim out of the XAML
    /// that used to hold it. Applies to BUILT-IN art only — see
    /// <see cref="ModArtFramingRegistry.ResolveViewbox"/> for why mod art must never inherit it.
    /// </param>
    /// <param name="Framable">
    /// False when this pair must NOT be offered to an author to frame, even though it still needs a
    /// shipped rect. The Goon card is the case: its brush is <c>Stretch="Uniform"</c> over a solid
    /// plate on purpose, so its square wordmark letterboxes instead of cropping. A crop window is
    /// meaningless there — the region would be shrunk inside the plate rather than filling it, so a
    /// preview showing edge-to-edge art would be lying about the one surface this registry exists to
    /// keep honest. Un-framed mod art on such a brush is given the whole image instead.
    /// </param>
    public sealed record ArtSlotBinding(
        string ResourcePath,
        string SurfaceId,
        ArtViewbox ShippedViewbox,
        bool Framable = true);

    /// <summary>
    /// The slot geometry table, and the arithmetic that turns a stored framing into an
    /// <c>ImageBrush.Viewbox</c>.
    ///
    /// <para><b>The bug this exists to fix.</b> The premium rail chips carried per-image
    /// <c>Viewbox</c> rects hand-tuned to the EMBEDDED art (they frame the illustration and push
    /// the burned-in wordmark out of the chip). Mod art was swapped in by mutating
    /// <c>ImageBrush.ImageSource</c> and nothing else, so an author's picture was cropped by a
    /// rectangle drawn for a completely different picture — which is why some slots looked fine
    /// and others were nonsense. The Play door cards had the mirror-image fault: no rect at all,
    /// so a blind centre crop, except <c>goon_game.png</c> whose one hardcoded rect mod art also
    /// inherited.</para>
    ///
    /// <para>Pure arithmetic and static data — no Dispatcher, no WPF controls, no file IO — so it
    /// is testable the same way <c>ModImageSlotRules</c> is.</para>
    /// </summary>
    public static class ModArtFramingRegistry
    {
        // ─── Surfaces ────────────────────────────────────────────────
        // AspectRatio values are derived from the real control dimensions; the comment on each
        // says where. If a surface is restyled, change it HERE and both the runtime crop and the
        // editor's preview follow — that is the reason this table exists.

        /// <summary>Premium quick-launch rail chip: 69x42 (SettingsTabView PremiumChipStyle
        /// Height=42, PremiumRailContent Width=69).</summary>
        public const string SurfaceRailChip = "railChip";

        /// <summary>The two taller rail launchers that carry live controls (Lockdown, Blink).
        /// Same 69 width, ~62 tall once the stepper row is in.</summary>
        public const string SurfaceRailCard = "railCard";

        /// <summary>Play door card header plate: full card width, 138 tall (PlayCardArtPlate).</summary>
        public const string SurfacePlayCard = "playCard";

        /// <summary>
        /// Same plate with its height overridden to 168 — the Remote Control and Goon cards do this
        /// inline (<c>Style="{StaticResource PlayCardArtPlate}" Height="168"</c>). Its own surface
        /// rather than a shared 138 because the 30 DIP difference is ~18% of the frame's width once
        /// UniformToFill re-crops, which is a visibly worse crop and a preview ~20% too wide.
        /// </summary>
        public const string SurfacePlayCardTall = "playCardTall";

        /// <summary>
        /// The Loom strip's art column: a FIXED 216 wide against a MinHeight 118 card
        /// (PlayTabView.xaml ~1305), so ~1.83:1 — nothing like a card plate. Its art also fades out
        /// to the RIGHT under a horizontal scrim so a busy illustration never runs into the title;
        /// that is cosmetic and costs the framing nothing.
        /// </summary>
        public const string SurfaceLoomStrip = "loomStrip";

        /// <summary>Play door full-width hero banner at the top of the wall.</summary>
        public const string SurfacePlayHero = "playHero";

        /// <summary>The Companion door's permissions-grid header plate (AiPermissionsGrid.xaml,
        /// Height=138 with the card's padding cancelled).</summary>
        public const string SurfacePageHeader = "pageHeader";

        /// <summary>
        /// The Graded Intake header's art strip — a FIXED 240x68 box anchored to the right of a
        /// 72-tall border (GradedIntakeTabView.xaml). Split out of <see cref="SurfacePageHeader"/>
        /// because sharing one id made a single preview stand for two genuinely different shapes:
        /// this is a narrow 3.5:1 tile, the permissions header is a wide ~5:1 plate, and an author
        /// framing for one was silently framing wrong for the other.
        /// </summary>
        public const string SurfaceIntakeStrip = "intakeStrip";

        // NOTE: there is deliberately NO dashTile surface. The dashboard mosaic paints its tiles
        // through MainWindow.Presets.cs, which never calls ApplyArtFraming, so declaring one would
        // have put a Frame button in front of authors and written numbers nothing reads. Wire the
        // mosaic first if that surface is ever wanted.
        private static readonly ArtSurface[] _surfaces =
        {
            // Fixed-size surfaces: the preview is exact.
            new(SurfaceRailChip,   "Rail chip",       69.0 / 42.0,   8,  ArtScrim.FootBand),
            new(SurfaceRailCard,   "Rail launcher",   69.0 / 62.0,   8,  ArtScrim.FullFace),
            // 240x68: the 240-wide host inside a 72-tall border, less its 2px border top and
            // bottom. Its art also fades out to the left under an OpacityMask, which the preview
            // does not reproduce — the framing consequence is nil, the left edge is simply softer.
            new(SurfaceIntakeStrip, "Intake strip",   240.0 / 68.0,  16, ArtScrim.None),
            // 216 wide is FIXED, so this one is exact in the width that matters. The card's height is
            // a MinHeight rather than a Height, so a taller card would make it slightly wider-looking
            // than 1.83:1 — the opposite direction from an over-crop, and not worth a fluid marker.
            new(SurfaceLoomStrip,  "Loom strip",      216.0 / 118.0, 15, ArtScrim.None),

            // Fluid-width surfaces: height is fixed, width follows the window, so these aspects are
            // REPRESENTATIVE and the runtime centre-crops the remainder. See ArtSurface.FluidWidth.
            // playCard: a 138-tall plate whose negative margin bleeds it ~44 past the card, in a
            // 3-column zone at the design width (~350 card + 44). A 2-column zone is nearer 4.3:1.
            new(SurfacePlayCard,   "Play card header", 394.0 / 138.0, 16, ArtScrim.FootBand, FluidWidth: true),
            new(SurfacePlayCardTall, "Play card header (tall)", 394.0 / 168.0, 16, ArtScrim.FootBand, FluidWidth: true),
            // playHero: the descent portal spans the wall and has no fixed height at all.
            new(SurfacePlayHero,   "Play hero banner", 1100.0 / 240.0, 16, ArtScrim.FootBand, FluidWidth: true),
            // pageHeader: 138 tall, full card width plus the 40 its negative margin reclaims.
            new(SurfacePageHeader, "Page header",      680.0 / 138.0, 14, ArtScrim.FootBand, FluidWidth: true),
        };

        /// <summary>Every framed surface, in the order the editor should present them.</summary>
        public static IReadOnlyList<ArtSurface> Surfaces => _surfaces;

        /// <summary>The surface with this id, or null when the id is unknown (an older mod
        /// naming a surface this build does not have, or a typo in a hand-edited mod.json).</summary>
        public static ArtSurface? FindSurface(string? surfaceId) =>
            surfaceId == null ? null : _surfaces.FirstOrDefault(s => s.Id == surfaceId);

        // ─── Bindings ────────────────────────────────────────────────
        // ShippedViewbox rects are lifted VERBATIM from the XAML that held them, so built-in art
        // is framed byte-identically to before this table existed. Do not "tidy" the numbers.

        private static readonly ArtSlotBinding[] _bindings =
        {
            // Rail chips — rects from SettingsTabView.xaml's Art* ImageBrush resources.
            new("features/takeover.png",       SurfaceRailChip, new ArtViewbox(0,    0.06, 1,    0.54)),
            new("features/awareness.png",      SurfaceRailChip, new ArtViewbox(0.02, 0.05, 0.40, 0.43)),
            new("features/vibe.png",           SurfaceRailChip, new ArtViewbox(0,    0.07, 1,    0.61)),
            new("features/lab_quiz_hero.png",  SurfaceRailChip, new ArtViewbox(0.22, 0.04, 0.62, 0.66)),
            new("features/remote_control.png", SurfaceRailChip, new ArtViewbox(0.60, 0.03, 0.35, 0.62)),
            new("features/fyp.png",            SurfaceRailChip, new ArtViewbox(0.20, 0.33, 0.50, 0.545)),
            // The two taller launchers.
            new("features/blink_trainer.png",  SurfaceRailCard, new ArtViewbox(0.05, 0.06, 0.44, 0.72)),
            new("lockdown_icon.png",           SurfaceRailCard, new ArtViewbox(0.08, 0,    0.25, 1)),

            // Play door cards — PlayTabView.xaml. Only Goon carried a rect; the rest were a bare
            // UniformToFill, which is the full-image window and is what ArtViewbox(0,0,1,1) means.
            // Goon keeps its rect for OUR art but is not framable: Stretch=Uniform over a plate.
            // Its plate is one of the two overridden to 168, hence playCardTall — the aspect is moot
            // for a non-framable pair (the shipped rect is used verbatim and mod art gets the whole
            // image), but a row that lies about its own shape is a trap for whoever reads it next.
            new("features/goon_game.png",           SurfacePlayCardTall, new ArtViewbox(0.03, 0.22, 0.94, 0.68), Framable: false),
            new("features/lab_gaze_hero.png",       SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            new("features/lab_focusgaze_hero.png",  SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            new("features/lab_quiz_hero.png",       SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            new("features/blink_trainer.png",       SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            // Remote's plate overrides the style height to 168 (PlayTabView.xaml:645).
            new("features/remote_control.png",      SurfacePlayCardTall, new ArtViewbox(0, 0, 1, 1)),
            new("features/fyp.png",                 SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            new("lockdown_icon.png",                SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            new("features/justdrop.png",            SurfacePlayCard, new ArtViewbox(0, 0, 1, 1)),
            // NOT a card plate at all: a fixed 216-wide column in the Loom strip.
            new("features/loom.png",                SurfaceLoomStrip, new ArtViewbox(0, 0, 1, 1)),

            // Play hero banner.
            new("features/dtrh.png", SurfacePlayHero, new ArtViewbox(0, 0, 1, 1)),

            // Page headers. lab_quiz_hero paints the narrow Intake strip, NOT the wide permissions
            // plate — the two were one id until the review caught that a single preview cannot be
            // right for a 3.5:1 tile and a 5:1 plate at once.
            new("features/lab_quiz_hero.png",     SurfaceIntakeStrip, new ArtViewbox(0, 0, 1, 1)),
            new("features/lab_aimemory_hero.png", SurfacePageHeader,  new ArtViewbox(0, 0, 1, 1)),
        };

        /// <summary>Every (path, surface) pair the app frames.</summary>
        public static IReadOnlyList<ArtSlotBinding> Bindings => _bindings;

        /// <summary>
        /// Every surface this resource path paints. More than one means the author is framing one
        /// image for several different shapes and the editor should show them all — the shared-slot
        /// case (<c>features/fyp.png</c> is a rail chip AND a Play card) that a baked crop ruins.
        /// </summary>
        public static IEnumerable<ArtSlotBinding> BindingsFor(string resourcePath) =>
            _bindings.Where(b => string.Equals(b.ResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The bindings an AUTHOR may frame — what the editor should offer. Excludes pairs whose
        /// surface cannot honestly show a crop (<see cref="ArtSlotBinding.Framable"/>), so no Frame
        /// button appears for a slot where the preview would have to lie.
        /// </summary>
        public static IEnumerable<ArtSlotBinding> FramableBindingsFor(string resourcePath) =>
            BindingsFor(resourcePath).Where(b => b.Framable);

        /// <summary>Whether this pair may be framed by an author. Unknown pairs are not framable.</summary>
        public static bool IsFramable(string resourcePath, string surfaceId) =>
            _bindings.Any(b => string.Equals(b.ResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase)
                               && b.SurfaceId == surfaceId && b.Framable);

        /// <summary>The shipped rect for a pair, or null when the pair is not in the table.</summary>
        public static ArtViewbox? ShippedViewbox(string resourcePath, string surfaceId) =>
            _bindings.FirstOrDefault(b =>
                string.Equals(b.ResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase)
                && b.SurfaceId == surfaceId)?.ShippedViewbox;

        // ─── Arithmetic ──────────────────────────────────────────────

        /// <summary>
        /// Turn a stored framing into an <c>ImageBrush.Viewbox</c> rect (relative units, which is
        /// WPF's default <c>ViewboxUnits</c>).
        ///
        /// <para>At <c>zoom == 1</c> the window is the largest rect of the surface's aspect that
        /// fits inside the source, which is precisely what <c>UniformToFill</c> already shows —
        /// so a default framing is a no-op and costs nothing. Higher zoom shrinks the window
        /// about the focal point. The window is then shifted, not cropped, when the focal point
        /// would push it off an edge: sliding keeps the author's chosen scale, where clamping the
        /// rect would silently change the aspect and re-introduce stretching.</para>
        /// </summary>
        /// <param name="framing">The stored centre and zoom. Null yields the full-fill window.</param>
        /// <param name="sourceAspect">Source image width ÷ height, in pixels.</param>
        /// <param name="surfaceAspect">The frame's width ÷ height.</param>
        public static ArtViewbox ToViewbox(ModArtFraming? framing, double sourceAspect, double surfaceAspect)
        {
            // A non-finite or absurd aspect (a zero-height decode, a corrupt header) must not
            // produce a NaN rect — WPF renders nothing at all for one, which reads as "the mod's
            // art is missing" rather than "the crop maths broke".
            if (!IsUsable(sourceAspect) || !IsUsable(surfaceAspect))
                return new ArtViewbox(0, 0, 1, 1);

            var zoom = framing == null ? 1.0 : framing.Zoom;
            if (double.IsNaN(zoom) || double.IsInfinity(zoom)) zoom = 1.0;
            zoom = Math.Clamp(zoom, 1.0, MaxZoom);

            // The UniformToFill window, in relative units. One axis is always the full 1.0 — the
            // one the surface is proportionally hungrier for — and the other is inset.
            double w, h;
            if (surfaceAspect >= sourceAspect)
            {
                // Frame is wider than the image: take the full width, crop height.
                w = 1.0;
                h = sourceAspect / surfaceAspect;
            }
            else
            {
                // Frame is taller than the image: take the full height, crop width.
                w = surfaceAspect / sourceAspect;
                h = 1.0;
            }

            w /= zoom;
            h /= zoom;

            var cx = framing?.CenterX ?? 0.5;
            var cy = framing?.CenterY ?? 0.5;
            if (double.IsNaN(cx) || double.IsInfinity(cx)) cx = 0.5;
            if (double.IsNaN(cy) || double.IsInfinity(cy)) cy = 0.5;
            cx = Math.Clamp(cx, 0.0, 1.0);
            cy = Math.Clamp(cy, 0.0, 1.0);

            // Slide the window fully inside the image rather than shrinking it.
            var x = Math.Clamp(cx - w / 2.0, 0.0, Math.Max(0.0, 1.0 - w));
            var y = Math.Clamp(cy - h / 2.0, 0.0, Math.Max(0.0, 1.0 - h));

            return new ArtViewbox(x, y, w, h);
        }

        /// <summary>
        /// The Viewbox a surface should paint with — the one decision the runtime needs.
        ///
        /// <list type="bullet">
        /// <item><b>Built-in art:</b> the shipped rect, untouched. Existing framing is preserved
        /// exactly; this whole mechanism is invisible for anyone not running a mod.</item>
        /// <item><b>Mod art WITH framing:</b> the author's centre and zoom.</item>
        /// <item><b>Mod art WITHOUT framing:</b> the full-fill window — an honest centre crop.
        /// <b>Never the shipped rect</b>, which was tuned to the picture we drew and is the bug.
        /// An author who has not framed anything gets a predictable centre crop they can reason
        /// about instead of an arbitrary window.</item>
        /// </list>
        /// </summary>
        /// <param name="resourcePath">The resource path, e.g. <c>features/fyp.png</c>.</param>
        /// <param name="surfaceId">An <see cref="ArtSurface.Id"/>.</param>
        /// <param name="isModSupplied">
        /// Whether an author's file is what resolved for this path — <c>ModResourceResolver.HasModOverride</c>.
        /// </param>
        /// <param name="sourceAspect">Source image width ÷ height in pixels.</param>
        /// <param name="framing">The mod's framing for this pair, or null if it supplied none.</param>
        public static ArtViewbox ResolveViewbox(string resourcePath, string surfaceId, bool isModSupplied,
                                          double sourceAspect, ModArtFraming? framing)
        {
            if (!isModSupplied)
                return ShippedViewbox(resourcePath, surfaceId) ?? new ArtViewbox(0, 0, 1, 1);

            var surface = FindSurface(surfaceId);
            if (surface == null)
                return new ArtViewbox(0, 0, 1, 1);

            // A non-framable pair takes the whole image rather than a crop window, whatever a
            // hand-edited mod.json claims: the surface letterboxes onto a plate, so a window would
            // be shrunk inside it instead of filling it. The editor never offers one, so this is
            // purely the guard for a manifest written by hand.
            if (framing != null && !IsFramable(resourcePath, surfaceId))
                return new ArtViewbox(0, 0, 1, 1);

            return ToViewbox(framing, sourceAspect, surface.AspectRatio);
        }

        /// <summary>
        /// Ceiling on zoom. Past this the window is a handful of source pixels blown up across a
        /// card, which reads as a bug rather than a choice; the editor's slider stops here too.
        /// </summary>
        public const double MaxZoom = 6.0;

        /// <summary>Parse "0.5,0.33" leniently — for the hand-edited mod.json case. Invariant
        /// culture only: a decimal comma would be indistinguishable from the separator.</summary>
        public static bool TryParsePoint(string? text, out double x, out double y)
        {
            x = y = 0.5;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split(',');
            if (parts.Length != 2) return false;

            // Parsed into locals and only published on FULL success: TryParse zeroes its out
            // parameter when it fails, so parsing straight into x/y would hand a caller that
            // ignores the bool a top-left corner instead of the centre this promises.
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
                return false;

            x = px;
            y = py;
            return true;
        }

        private static bool IsUsable(double aspect) =>
            !double.IsNaN(aspect) && !double.IsInfinity(aspect) && aspect > 0.0001 && aspect < 10000;
    }
}
