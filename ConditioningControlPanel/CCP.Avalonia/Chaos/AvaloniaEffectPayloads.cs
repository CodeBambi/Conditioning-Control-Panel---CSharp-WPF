using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Chaos;

public sealed class FlashPayload : EffectPayload
{
    public override string DisplayName => "flash";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Flash;

    public override void Fire()
    {
        try
        {
            int amount = Scale(1, 3);
            int duration = (int)(Scale(900, 2000) * GlobalDurationMult);
            int size = Scale(68, 143);
            var flash = App.Services?.GetService<IFlashService>();
            flash?.TriggerFlashOnce(null, duration, true, false);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<FlashPayload>>()?.LogDebug("FlashPayload: {E}", ex.Message);
        }
    }
}

public sealed class SubliminalPayload : EffectPayload
{
    public override string DisplayName => "subliminal";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Subliminal;

    public override void Fire()
    {
        try
        {
            App.Services?.GetService<ISubliminalService>()?.FlashSubliminal();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<SubliminalPayload>>()?.LogDebug("SubliminalPayload: {E}", ex.Message);
        }
    }
}

public sealed class OverlayPayload : EffectPayload
{
    private readonly string _kind;
    public OverlayPayload(string overlayKind) => _kind = overlayKind;

    public override string DisplayName => _kind;
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Overlay;

    public override void Fire()
    {
        try
        {
            int duration = (int)(Scale(1500, 4500) * GlobalDurationMult);
            if (_kind == "braindrain")
            {
                // braindrain = a full-screen wash of a random flash-pool image/gif — a compositor
                // layer driven through the owning chaos service (WS2/WP3 Phase F #5). WPF parity:
                // chaos keeps the faint ~10%/10s Show() defaults (EffectPayload.cs:135).
                (AvaloniaChaosApp.Chaos
                    as ConditioningControlPanel.Avalonia.Services.AvaloniaChaosService)?.ShowChaosFlashWash();
            }
            else
            {
                double opacity = ScaleD(0.25, 0.70);
                App.Services?.GetService<IOverlayService>()?.ShowOverlayTimed(_kind, duration, opacity);
            }
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<OverlayPayload>>()?.LogDebug("OverlayPayload: {E}", ex.Message);
        }
    }
}

public sealed class VideoPayload : EffectPayload
{
    public override string DisplayName => "video";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Video;

    public override void Fire()
    {
        try
        {
            App.Services?.GetService<IVideoService>()?.TriggerVideo();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<VideoPayload>>()?.LogDebug("VideoPayload: {E}", ex.Message);
        }
    }
}

public sealed class HtLinkPayload : EffectPayload
{
    public override string DisplayName => "HT link";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.HtLink;

    public override void Fire()
    {
        try
        {
            var url = AvaloniaHtLinkPool.PickRandom();
            if (string.IsNullOrWhiteSpace(url)) return;
            var host = App.Services?.GetService<IBrowserHost>();
            if (host != null)
            {
                Task.Run(async () => await host.PopOutAsync(new Uri(url)));
            }
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<HtLinkPayload>>()?.LogDebug("HtLinkPayload: {E}", ex.Message);
        }
    }
}

public sealed class AudioPayload : EffectPayload
{
    public override string DisplayName => "whisper";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Audio;

    public override void Fire()
    {
        try
        {
            App.Services?.GetService<ISubliminalService>()?.FlashSubliminal();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<AudioPayload>>()?.LogDebug("AudioPayload: {E}", ex.Message);
        }
    }
}

public sealed class BambiFreezePayload : EffectPayload
{
    public override string DisplayName => "freeze";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BambiFreeze;

    public override void Fire()
    {
        try
        {
            // Avalonia has no dedicated BambiFreeze trigger; fall back to a subliminal flash.
            App.Services?.GetService<ISubliminalService>()?.FlashSubliminal();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<BambiFreezePayload>>()?.LogDebug("BambiFreezePayload: {E}", ex.Message);
        }
    }
}

public sealed class BouncingTextPayload : EffectPayload
{
    public override string DisplayName => "bouncing text";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BouncingText;

    public const double DURATION_SEC = 8.0;
    public const double OPACITY = 0.85;
    public const int TEXT_SIZE = 120;
    public const int SPEED = 5;

    public override void Fire()
    {
        try
        {
            var svc = App.Services?.GetService<IBouncingTextService>();
            if (svc == null || svc.IsRunning) return;

            string? phrase = PickAffirmation();
            var pool = phrase != null ? new List<string> { phrase } : null;
            svc.Start(pool);

            var life = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.5, DURATION_SEC * GlobalDurationMult))
            };
            life.Tick += (_, _) =>
            {
                life.Stop();
                try { svc.Stop(); } catch { }
            };
            life.Start();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<BouncingTextPayload>>()?.LogDebug("BouncingTextPayload: {E}", ex.Message);
        }
    }

    private static string? PickAffirmation()
    {
        try
        {
            var settings = App.Services?.GetService<ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
            var pool = settings?.BouncingTextPool;
            if (pool == null) return null;
            var enabled = new List<string>();
            foreach (var kv in pool) if (kv.Value) enabled.Add(kv.Key);
            if (enabled.Count == 0) return null;
            return enabled[Random.Shared.Next(enabled.Count)];
        }
        catch { return null; }
    }
}

public sealed class GifCascadePayload : EffectPayload
{
    public override string DisplayName => "gif cascade";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.GifCascade;

    public const double SPAWN_RATE_PER_SEC = 1.67;
    public const double DURATION_SEC = 6.0;
    public const double GIF_SIZE = 400;
    public const double START_SCALE = 0.45;
    public const double FALL_SPEED = 3.6;
    public const double OPACITY = 0.9;

    public override void Fire()
    {
        try
        {
            // The cascade is a compositor layer driven through the owning chaos service
            // (WS2/WP3 Phase F #7); knobs unchanged (WPF GifCascadePayload consts).
            (AvaloniaChaosApp.Chaos as ConditioningControlPanel.Avalonia.Services.AvaloniaChaosService)
                ?.ShowChaosGifCascade(
                    spawnRatePerSec: SPAWN_RATE_PER_SEC,
                    durationSec: DURATION_SEC * GlobalDurationMult,
                    gifSize: GIF_SIZE,
                    fallSpeed: FALL_SPEED,
                    opacity: OPACITY,
                    startScale: START_SCALE);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<ILogger<GifCascadePayload>>()?.LogDebug("GifCascadePayload: {E}", ex.Message);
        }
    }
}
