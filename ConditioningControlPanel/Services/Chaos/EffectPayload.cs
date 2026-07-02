using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The kinds of one-shot effect a Chaos bubble can carry. Each maps to a
/// concrete <see cref="EffectPayload"/> that wraps an existing App.* trigger.
/// </summary>
public enum EffectBubblePayloadKind
{
    Flash,
    Subliminal,
    Overlay,
    Video,
    HtLink,
    Audio,
    BambiFreeze,
    BouncingText,
    GifCascade
}

/// <summary>
/// A Chaos bubble's payload — the effect that fires when the bubble is popped
/// (benign "treat") or when a live bubble's fuse expires undefused
/// ("detonation"). Concrete payloads are thin wrappers over the app's existing
/// one-shot triggers (FlashService, SubliminalService, …). <see cref="Strength"/>
/// (0..100, derived from bubble size at spawn) scales the effect: bigger bubble
/// ⇒ stronger payload.
///
/// All <see cref="Fire"/> implementations are invoked on the UI thread (from a
/// DispatcherTimer tick or a click handler) and must be self-contained — the
/// underlying App.* services own their own dispatching/threading.
/// </summary>
public abstract class EffectPayload
{
    /// <summary>Short human label for the bubble tag, payload feed and bark context.</summary>
    public abstract string DisplayName { get; }

    public abstract EffectBubblePayloadKind Kind { get; }

    /// <summary>0..100, scaled from bubble size at spawn. Drives effect intensity.</summary>
    public int Strength { get; set; }

    /// <summary>Per-instance multiplier on this payload's time-based duration(s). 1.0 = nominal.
    /// Dashboard trigger bubbles raise it so effects linger longer than the brisk chaos cadence.</summary>
    public double DurationMult { get; set; } = 1.0;

    /// <summary>True when this payload was built for a dashboard "Trigger Bubble" rather than a
    /// chaos-run bubble (set in ChaosBubbleVariants.Build). Some payloads behave differently on
    /// the calm dashboard — e.g. video must not arm a random start segment there (#456/#458).
    /// A per-instance flag, NOT App.Chaos.IsRunning at fire time: ambient bubbles can pop while
    /// a chaos run happens to be active, and a run can end between spawn and pop.</summary>
    public bool Ambient { get; set; }

    /// <summary>
    /// Global multiplier on time-based payload durations (flashes/overlays linger). Set &gt;1 by
    /// the darter slow-mo power-up so "images last longer" while time is slowed; restored to 1
    /// when it ends. 1.0 = normal.
    /// </summary>
    public static double GlobalDurationMult { get; set; } = 1.0;

    /// <summary>Run the wrapped one-shot effect, scaled by <see cref="Strength"/>.</summary>
    public abstract void Fire();

    /// <summary>Lerp helper: map Strength 0..100 onto [min,max].</summary>
    protected int Scale(int min, int max) =>
        min + (int)Math.Round((max - min) * Math.Clamp(Strength, 0, 100) / 100.0);

    protected double ScaleD(double min, double max) =>
        min + (max - min) * Math.Clamp(Strength, 0, 100) / 100.0;
}

/// <summary>Fires a flash burst — more/bigger/faster images at higher Strength.</summary>
public sealed class FlashPayload : EffectPayload
{
    public override string DisplayName => "flash";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Flash;

    public override void Fire()
    {
        try
        {
            int amount = Scale(1, 3);
            // Was 250–700ms — flashes barely registered mid-run. Long enough to actually
            // be seen (plus the fade tail), still well under the bubble cadence.
            int duration = (int)(Scale(900, 2000) * GlobalDurationMult * DurationMult);
            // Chaos-run flashes read better big: +50% over the original 45–95 band.
            int size = Scale(68, 143);
            App.Flash?.TriggerFlashOnce(amount, duration, size, suppressHaptic: false);
        }
        catch (Exception ex) { App.Logger?.Debug("FlashPayload: {E}", ex.Message); }
    }
}

/// <summary>Flashes a subliminal from the active pool.</summary>
public sealed class SubliminalPayload : EffectPayload
{
    public override string DisplayName => "subliminal";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Subliminal;

    public override void Fire()
    {
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("SubliminalPayload: {E}", ex.Message); }
    }
}

/// <summary>Snaps an overlay (pink_filter / spiral / braindrain) on for a few seconds.</summary>
public sealed class OverlayPayload : EffectPayload
{
    private readonly string _kind;
    private readonly double? _braindrainOpacity;
    // braindrainOpacity: when set, the braindrain wash uses this opacity (0..1) for the
    // computed duration instead of its faint ~10%/10s default. Used by the dashboard "glitch"
    // trigger bubble (~30%); null keeps chaos's original faint wash untouched.
    public OverlayPayload(string overlayKind, double? braindrainOpacity = null)
    {
        _kind = overlayKind;
        _braindrainOpacity = braindrainOpacity;
    }

    public override string DisplayName => _kind;
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Overlay;

    public override void Fire()
    {
        try
        {
            int duration = (int)(Scale(1500, 4500) * GlobalDurationMult * DurationMult);
            if (_kind == "braindrain")
            {
                // braindrain = a full-screen wash of a random flash-pool image/gif.
                if (_braindrainOpacity is double o) ChaosFlashOverlay.Show(duration, o);  // glitch: ~30%
                else ChaosFlashOverlay.Show();                                            // chaos: faint ~10%/10s default
            }
            else
            {
                double opacity = ScaleD(0.25, 0.70);   // pink_filter / spiral take a plain 0..1 opacity
                App.Overlay?.ShowOverlayTimed(_kind, duration, opacity);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("OverlayPayload: {E}", ex.Message); }
    }
}

/// <summary>Triggers a mandatory video (silent no-op if the video pool is empty).</summary>
public sealed class VideoPayload : EffectPayload
{
    public override string DisplayName => "video";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Video;

    /// <summary>Seconds of video the chaos tape shows — a random slice, ended by the chaos hard cap.</summary>
    public const double SEGMENT_SEC = 15;

    public override void Fire()
    {
        try
        {
            // Only chaos caps playback at ~15s, making a random start a "random slice".
            // Dashboard trigger bubbles reuse this payload uncapped, so arming there made
            // the full mandatory video start midway (#456/#458).
            if (!Ambient)
                App.Video?.ArmRandomSegment(SEGMENT_SEC);
            App.Video?.TriggerVideo(silentIfEmpty: true);
        }
        catch (Exception ex) { App.Logger?.Debug("VideoPayload: {E}", ex.Message); }
    }
}

/// <summary>Opens a HypnoTube link from the pool in the embedded browser, auto-fullscreen.</summary>
public sealed class HtLinkPayload : EffectPayload
{
    public override string DisplayName => "HT link";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.HtLink;

    public override void Fire()
    {
        try
        {
            var url = HtLinkPool.PickRandom();
            if (string.IsNullOrWhiteSpace(url)) return;
            if (Application.Current?.MainWindow is MainWindow mw)
                mw.NavigateToUrlInBrowser(url!, autoPlayFullscreen: true);
        }
        catch (Exception ex) { App.Logger?.Debug("HtLinkPayload: {E}", ex.Message); }
    }
}

/// <summary>Plays a one-shot subliminal whisper (reuses the subliminal audio path).</summary>
public sealed class AudioPayload : EffectPayload
{
    public override string DisplayName => "whisper";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Audio;

    public override void Fire()
    {
        // No dedicated SFX pool yet — reuse the subliminal whisper for an audio sting.
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("AudioPayload: {E}", ex.Message); }
    }
}

/// <summary>Triggers a Bambi Freeze (special subliminal + audio).</summary>
public sealed class BambiFreezePayload : EffectPayload
{
    public override string DisplayName => "freeze";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BambiFreeze;

    public override void Fire()
    {
        try { App.Subliminal?.TriggerBambiFreeze(); }
        catch (Exception ex) { App.Logger?.Debug("BambiFreezePayload: {E}", ex.Message); }
    }
}

/// <summary>
/// A single short affirmation bouncing DVD-logo style across the screen, click-through, for a short
/// window. REUSES the existing <see cref="BouncingTextService"/> (App.BouncingText) rather than building
/// a new bouncer — sourcing its text from the existing affirmation/phrase pool — and tears it down after
/// <see cref="DURATION_SEC"/> or <see cref="MAX_BOUNCES"/>, whichever comes first. Conservative defaults.
/// No-op if the bouncing toy is already running for the user (so a payload never hijacks it).
/// </summary>
public sealed class BouncingTextPayload : EffectPayload
{
    public override string DisplayName => "bouncing text";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BouncingText;

    // ---- named tunables (conservative defaults) ----
    public const double DURATION_SEC = 8.0;   // on-screen lifetime
    public const int    MAX_BOUNCES  = 12;    // auto-stop after this many bounces (whichever first)
    public const double OPACITY      = 0.85;  // text opacity (informational; service reads its own setting)
    public const int    TEXT_SIZE    = 120;   // % of base font (informational tunable)
    public const int    SPEED        = 5;     // 1..10 bounce speed (informational tunable)

    public override void Fire()
    {
        try
        {
            var svc = App.BouncingText;
            if (svc == null || svc.IsRunning) return;   // don't hijack a user-started bouncer

            // Source a single affirmation from the existing enabled phrase pool.
            string? phrase = PickAffirmation();
            var pool = phrase != null ? new System.Collections.Generic.List<string> { phrase } : null;
            svc.Start(bypassLevelCheck: true, pool: pool);

            int bounces = 0;
            EventHandler? onBounce = null;
            var life = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(Math.Max(0.5, DURATION_SEC * GlobalDurationMult)) };

            void StopOnce()
            {
                try { life.Stop(); } catch { }
                if (onBounce != null) { try { svc.OnBounce -= onBounce; } catch { } }
                try { svc.Stop(); } catch { }
            }

            onBounce = (_, _) => { if (++bounces >= MAX_BOUNCES) StopOnce(); };
            svc.OnBounce += onBounce;
            life.Tick += (_, _) => StopOnce();
            life.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("BouncingTextPayload: {E}", ex.Message); }
    }

    private static string? PickAffirmation()
    {
        try
        {
            var pool = App.Settings?.Current?.BouncingTextPool;
            if (pool == null) return null;
            var enabled = new System.Collections.Generic.List<string>();
            foreach (var kv in pool) if (kv.Value) enabled.Add(kv.Key);
            if (enabled.Count == 0) return null;
            return enabled[_rng.Next(enabled.Count)];
        }
        catch { return null; }
    }

    private static readonly Random _rng = new();
}

/// <summary>
/// Images/gifs spawn at the top of the screen and fall/cascade downward, click-through, for a short
/// window. Sources its images from the SAME flash/braindrain image pool (EffectiveAssetsPath/images,
/// via <see cref="ChaosGifCascadeOverlay"/>). Conservative named defaults.
/// </summary>
public sealed class GifCascadePayload : EffectPayload
{
    public override string DisplayName => "gif cascade";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.GifCascade;

    // ---- named tunables (the "to start with" defaults — boons will tune these later) ----
    public const double SPAWN_RATE_PER_SEC = 1.67;  // ~10 clips across the 6s window (2026-06-10: denser, shorter)
    public const double DURATION_SEC       = 6.0;   // spawn window
    public const double GIF_SIZE           = 400;   // px (max dimension)
    public const double START_SCALE        = 0.45;  // clips spawn small at the top and grow as they slide down
    public const double FALL_SPEED         = 3.6;   // DIPs per ~16ms frame — a quick, lively slide
    public const double OPACITY            = 0.9;   // per-image opacity

    public override void Fire()
    {
        try
        {
            ChaosGifCascadeOverlay.Show(
                spawnRatePerSec: SPAWN_RATE_PER_SEC,
                durationSec: DURATION_SEC * GlobalDurationMult,
                gifSize: GIF_SIZE,
                fallSpeed: FALL_SPEED,
                opacity: OPACITY,
                startScale: START_SCALE);
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascadePayload: {E}", ex.Message); }
    }
}
