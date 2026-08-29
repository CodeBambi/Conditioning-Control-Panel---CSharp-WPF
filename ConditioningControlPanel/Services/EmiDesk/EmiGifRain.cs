using System;
using System.Windows;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// EMI's gif rain: the Chaos gif cascade, run for her own window rather than for a chaos payload.
///
/// THE SEAM IS A CALL, NOT A COPY. <see cref="ChaosGifCascadeOverlay"/> is already a public static
/// surface that owns its own window, its own faller pool and its own image source (the same
/// <c>EffectiveAssetsPath/images</c> folder the flashes draw from), and it does not care who asked.
/// Extracting "the minimal shared piece" would have meant forking a renderer that ships today and
/// keeping two of them in step forever, so this file passes the cascade's OWN named tunables
/// through unchanged and touches exactly one of them: how long the spawn window lasts. The chaos
/// behaviour is therefore byte-identical, because it is literally the same code path.
/// </summary>
public static class EmiGifRain
{
    /// <summary>True while rain is on screen (hers or a chaos run's; there is only one overlay).</summary>
    public static bool IsRaining
    {
        get
        {
            try { return ChaosGifCascadeOverlay.IsRaining; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Rain gifs down the screen for <paramref name="duration"/>. A no-op when there are no local
    /// images, when the dispatcher is going away, or when it is already raining: a second cascade
    /// on top of the first is noise, not more rain.
    /// </summary>
    public static void Start(TimeSpan duration)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Start(duration)));
                return;
            }

            if (!EmiOffers.HasImages())
            {
                Log.Debug("[EmiDesk] gif rain skipped: no local images");
                return;
            }
            if (IsRaining)
            {
                Log.Debug("[EmiDesk] gif rain skipped: already raining");
                return;
            }

            double seconds = duration.TotalSeconds;
            if (double.IsNaN(seconds) || seconds <= 0) seconds = GifCascadePayload.DURATION_SEC;
            seconds = Math.Max(1.0, Math.Min(30.0, seconds));

            // Every dial except the duration comes straight off GifCascadePayload's own constants,
            // so hers rains exactly like the chaos one does.
            ChaosGifCascadeOverlay.Show(
                spawnRatePerSec: GifCascadePayload.SPAWN_RATE_PER_SEC,
                durationSec: seconds,
                gifSize: GifCascadePayload.GIF_SIZE,
                fallSpeed: GifCascadePayload.FALL_SPEED,
                opacity: GifCascadePayload.OPACITY,
                startScale: GifCascadePayload.START_SCALE);

            Log.Information("[EmiDesk] gif rain for {Seconds:0.#}s", seconds);

            // The cascade is its own topmost window, so she has to climb back over it or she spends
            // the whole ten seconds behind her own effect.
            EmiOffers.ReassertTopmost();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] gif rain failed");
        }
    }

    /// <summary>Stop the rain now (her dismiss, a teardown). Safe when nothing is raining.</summary>
    public static void Stop()
    {
        try { ChaosGifCascadeOverlay.CloseActive(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] gif rain stop failed"); }
    }
}
