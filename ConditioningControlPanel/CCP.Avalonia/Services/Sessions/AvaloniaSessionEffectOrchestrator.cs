using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Services.BubbleCount;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Sessions;

/// <summary>
/// Avalonia implementation of <see cref="ISessionEffectOrchestrator"/>.
/// Starts/stops feature services according to the current session settings.
/// Services are resolved lazily on first use so heavy dependencies such as
/// LibVLC are not created during cold startup.
/// </summary>
public sealed class AvaloniaSessionEffectOrchestrator : ISessionEffectOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaSessionEffectOrchestrator>? _logger;

    private IFlashService? _flash;
    private IVideoService? _video;
    private ISubliminalService? _subliminal;
    private IMindWipeService? _mindWipe;
    private IBouncingTextService? _bouncingText;
    private IOverlayService? _overlay;
    private ILockCardService? _lockCard;
    private IBubbleService? _bubbles;
    private IBubbleCountService? _bubbleCount;
    private IPopQuizService? _popQuiz;

    public AvaloniaSessionEffectOrchestrator(
        IServiceProvider services,
        ISettingsService settings,
        ILogger<AvaloniaSessionEffectOrchestrator>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    private IFlashService? Flash => _flash ??= _services.GetService<IFlashService>();
    private IVideoService? Video => _video ??= _services.GetService<IVideoService>();
    private ISubliminalService? Subliminal => _subliminal ??= _services.GetService<ISubliminalService>();
    private IMindWipeService? MindWipe => _mindWipe ??= _services.GetService<IMindWipeService>();
    private IBouncingTextService? BouncingText => _bouncingText ??= _services.GetService<IBouncingTextService>();
    private IOverlayService? Overlay => _overlay ??= _services.GetService<IOverlayService>();
    private ILockCardService? LockCard => _lockCard ??= _services.GetService<ILockCardService>();
    private IBubbleService? Bubbles => _bubbles ??= _services.GetService<IBubbleService>();
    private IBubbleCountService? BubbleCount => _bubbleCount ??= _services.GetService<IBubbleCountService>();
    private IPopQuizService? PopQuiz => _popQuiz ??= _services.GetService<IPopQuizService>();

    public void StartEffects(Session session)
    {
        if (session?.Settings == null) return;
        var s = session.Settings;

        _logger?.LogInformation("Starting session effects for {SessionName}", session.Name);

        TryRun("overlay", () => Overlay?.Start());

        if (s.FlashEnabled) TryRun("flash", () => Flash?.Start());
        if (s.MandatoryVideosEnabled) TryRun("video", () => Video?.Start());
        if (s.SubliminalEnabled) TryRun("subliminal", () => Subliminal?.Start());
        if (s.MindWipeEnabled)
        {
            var appSettings = _settings.Current;
            TryRun("mindwipe", () => MindWipe?.Start(appSettings.MindWipeFrequency, appSettings.MindWipeVolume / 100.0));
        }
        if (s.BouncingTextEnabled) TryRun("bouncing text", () => BouncingText?.Start(s.BouncingTextPhrases));
        if (s.BubblesEnabled) TryRun("bubbles", () => Bubbles?.Start());
        if (s.BubbleCountEnabled) TryRun("bubble count", () => BubbleCount?.Start());
        if (s.LockCardEnabled) TryRun("lock card", () => LockCard?.Start());
        if (s.PopQuizEnabled) TryRun("pop quiz", () => PopQuiz?.Start());

        // Refresh overlays after individual effect services have started so that
        // pink/spiral/brain-drain windows reflect current settings.
        TryRun("overlay refresh", () => Overlay?.RefreshOverlays());
    }

    public void StopEffects()
    {
        _logger?.LogInformation("Stopping all session effects");

        // Note: session log begin/end is owned by Core SessionService so the log gets the
        // real duration/XP/completed flag. Stopping effects alone does not end the log.
        TryRun("flash", () => Flash?.Stop());
        TryRun("video", () => Video?.Stop());
        TryRun("subliminal", () => Subliminal?.Stop());
        TryRun("mindwipe", () => MindWipe?.Stop());
        TryRun("bouncing text", () => BouncingText?.Stop());
        TryRun("bubble count", () => BubbleCount?.Stop());
        TryRun("lock card", () => LockCard?.Stop());
        TryRun("bubbles", () => Bubbles?.Stop());
        TryRun("pop quiz", () => PopQuiz?.Stop());
        TryRun("overlay", () => Overlay?.Stop());
    }

    public void TickEffects(Session session, TimeSpan elapsed)
    {
        // Seam only for now: per-tick scheduling (delayed pink/spiral/bubble starts,
        // intermittent bubble bursts) is ported in a follow-up change.
        _logger?.LogTrace("Session effect tick at {Elapsed} for {SessionName}", elapsed, session.Name);
    }

    private void TryRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Session effect '{Name}' failed", name);
        }
    }
}
