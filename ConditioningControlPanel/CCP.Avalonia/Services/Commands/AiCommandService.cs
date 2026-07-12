using System.Collections.Concurrent;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Commands;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using SubliminalCmdData = ConditioningControlPanel.Models.CommandData.Subliminal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.Commands;

/// <summary>
/// Avalonia implementation of <see cref="IAiCommandService"/> — the AI-triggers-effects dispatcher.
/// Faithfully ports WPF <c>Services/Commands/AiCommandService.cs</c> (the dispatcher + per-type
/// effect calls) onto the Core effect seams, for the <c>AllowAiToControlEffects</c> feature.
/// </summary>
/// <remarks>
/// <para><b>Dispatch pipeline</b> (matches WPF <c>ExecuteCommand</c> verbatim): null-data drop →
/// master gate (<c>AllowAiToControlEffects</c>) → per-effect gate (<c>IsEffectAllowed</c>) →
/// per-response cap (<c>MaxCommandsPerResponse=3</c>, counted only for survivors) → token tracking
/// (getbacktome only) → <see cref="ExecuteCore"/> dispatch. <c>CancelAllCommands</c> cancels every
/// in-flight getbacktome follow-up.</para>
/// <para><b>Effect fidelity</b>: each command dispatches to its Core effect seam. A command is
/// functional ONLY to the extent the underlying effect is ported in the Avalonia head — e.g.
/// <c>flash_image</c>/<c>subliminal</c> dispatch to stub seams (no-op until those engines port);
/// <c>bubbles</c>/<c>overlay</c>/<c>video</c>/<c>lockcard</c>/<c>bounce</c>/<c>haptics</c> dispatch
/// to real seams. The <c>bubbles</c> frequency override is best-effort (the spawn rate is
/// settings-driven; a runtime override is a filed seam gap — see task board).</para>
/// <para><b>Cycle safety</b>: <see cref="IAiService"/> is resolved LAZILY (via
/// <see cref="IServiceProvider"/>) because the AI providers inject <c>IAiCommandService?</c>
/// (optional) — a direct <c>IAiService</c> ctor dependency would create a singleton resolution
/// cycle. <see cref="IAvatarWindowService"/> is also lazy (it is not guaranteed registered).</para>
/// </remarks>
public sealed class AiCommandService : IAiCommandService
{
    public const int MaxCommandsPerResponse = 3;
    private const int MaxGetBackToMeDepth = 2;

    private readonly IFlashService _flash;
    private readonly IBubbleService _bubbles;
    private readonly ISubliminalService _subliminal;
    private readonly IOverlayService _overlay;
    private readonly IVideoService _video;
    private readonly IAudioPlayer _audio;
    private readonly IHapticsService _haptics;
    private readonly IBouncingTextService _bouncing;
    private readonly ILockCardService _lockCard;
    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _env;
    private readonly IServiceProvider _services;
    // AI-7: the Companion-tab "Live actions" feed. Optional so the dispatcher still constructs
    // in tests/heads that don't surface the feed; null-guarded at the call site.
    private readonly IAiLiveActionsFeed? _feed;
    private readonly ILogger<AiCommandService>? _logger;

    private int _batchCount;
    // getbacktome is the only token-bearing command; one in-flight CTS per token (new reuses cancel prior).
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokenCts = new();

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".aac", ".m4a" };

    public AiCommandService(
        IFlashService flash, IBubbleService bubbles, ISubliminalService subliminal, IOverlayService overlay,
        IVideoService video, IAudioPlayer audio, IHapticsService haptics, IBouncingTextService bouncing,
        ILockCardService lockCard, ISettingsService settings, IAppEnvironment env, IServiceProvider services,
        IAiLiveActionsFeed? feed = null, ILogger<AiCommandService>? logger = null)
    {
        _flash = flash; _bubbles = bubbles; _subliminal = subliminal; _overlay = overlay;
        _video = video; _audio = audio; _haptics = haptics; _bouncing = bouncing;
        _lockCard = lockCard; _settings = settings; _env = env; _services = services; _feed = feed; _logger = logger;
    }

    /// <inheritdoc/>
    public void BeginBatch() => _batchCount = 0;

    /// <inheritdoc/>
    public async void ExecuteCommand(AiCommandData commandData)
    {
        try
        {
            if (commandData.Data == null) return;

            var cp = _settings.Current?.CompanionPrompt;
            if (cp == null) { _logger?.LogDebug("AiCommandService: no CompanionPrompt settings; drop."); return; }

            // MASTER gate
            if (!cp.AllowAiToControlEffects) { _logger?.LogDebug("AiCommandService: master toggle OFF; drop {Cmd}.", commandData.Command); return; }

            // PER-EFFECT gate
            if (!IsEffectAllowed(commandData.Command, cp)) { _logger?.LogDebug("AiCommandService: effect {Cmd} disabled by user; drop.", commandData.Command); return; }

            // PER-BATCH cap (counted only for commands that passed both gates)
            if (Interlocked.Increment(ref _batchCount) > MaxCommandsPerResponse) { _logger?.LogDebug("AiCommandService: batch cap ({Cap}) reached; drop {Cmd}.", MaxCommandsPerResponse, commandData.Command); return; }

            // AI-7: surface a human-readable line in the Companion "Live actions" feed. Mirrors WPF
            // AiCommandService.cs:67 — AppendLiveAction(FormatLiveAction(c)) is called AFTER the gates
            // + per-batch cap pass and BEFORE token tracking/dispatch, so only commands that will
            // actually fire appear in the feed. The line describes the ACTION only (e.g.
            // "Flash · 5 images for 10s"); never the AI prompt or raw command JSON.
            _feed?.Append(AiLiveActionFormatter.Format(commandData));

            // TOKEN tracking (getbacktome only)
            var token = commandData.Data.Token;
            CancellationTokenSource? cts = null;
            if (!string.IsNullOrEmpty(token))
            {
                CancelToken(token);
                cts = new CancellationTokenSource();
                _tokenCts[token] = cts;
            }

            try { await ExecuteCore(commandData, cts?.Token ?? default, depth: 0).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* getbacktome cancelled — expected */ }
            catch (Exception ex) { _logger?.LogWarning(ex, "AiCommandService: ExecuteCore threw for {Cmd}.", commandData.Command); }
            finally { if (!string.IsNullOrEmpty(token)) RemoveToken(token); }
        }
        catch (Exception ex) { _logger?.LogError(ex, "AiCommandService: ExecuteCommand top-level fault for {Cmd}.", commandData.Command); }
    }

    /// <inheritdoc/>
    public void CancelAllCommands()
    {
        foreach (var token in _tokenCts.Keys.ToArray()) CancelToken(token);
    }

    /// <summary>The per-command-type dispatch. Called by <see cref="ExecuteCommand"/> (after gates) and
    /// recursively by getbacktome (depth+1, no re-gating — matches WPF CommandFactory.CreateCommand).</summary>
    private async Task ExecuteCore(AiCommandData c, CancellationToken ct, int depth)
    {
        switch (c.Command)
        {
            case AICommandType.flash_image when c.Data is FlashImage f:
                // SEAM GAP (AI-6): WPF FlashImageCommand.cs:25-33 clamps Amount(0-8) / Duration(0-10s,
                // *1000->ms) / Size(0-150) and calls App.Flash.TriggerFlashOnce(amount, durationMs, size)
                // (WPF FlashService.cs:309 -- the multi-image AI one-shot; note the model also has Opacity
                // but WPF FlashImageCommand does NOT read it). The Core IFlashService seam has no
                // equivalent: TriggerFlash() is argless (uses the configured FlashImages count) and
                // TriggerFlashOnce(imagePath, durationMs, playSound, suppressHaptic) is the SINGLE-image
                // Deeper variant (WPF TriggerFlashOnceWithImage, FlashService.cs:347), not the AI flash.
                // Threading duration through TriggerFlashOnce would change semantics (1 image vs N), so
                // nothing is threaded; amount/duration/size filed as a follow-up row (seam + Avalonia impl).
                await Dispatcher.UIThread.InvokeAsync(() => _flash.TriggerFlash());
                break;

            case AICommandType.bubbles when c.Data is Bubbles b:
                // WPF BubbleCommand.cs:24-30 clamps Frequency(0-10) and derives shouldStart = On||freq>0
                // (ported below), then calls App.Bubbles.Start(true, frequency>0?frequency:null) where the
                // bool is bypassLevelCheck (WPF BubbleService.cs:160 Start(bool, int? frequency)). SEAM GAP
                // (AI-6): Core IBubbleService.Start() is argless -- there is no runtime spawn-rate override
                // and no bypassLevelCheck on the seam (RefreshFrequency() re-reads settings; SpawnOnce()
                // spawns one bubble). The start/stop decision IS ported with parity; the per-call frequency
                // override is filed as a follow-up row (seam + Avalonia impl + settings-driven spawn rate).
                var frequency = Math.Clamp(b.Frequency, 0, 10);
                var shouldStart = b.On || frequency > 0;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (shouldStart) _bubbles.Start();
                    else _bubbles.Stop();
                });
                break;

            case AICommandType.subliminal when c.Data is SubliminalCmdData s:
                // WPF SubliminalCommand.cs:22-31 clamps Opacity(0-60) and calls
                // App.Subliminal.FlashSubliminalCustom(text, opacity). WPF signature (SubliminalService.cs:241):
                // FlashSubliminalCustom(text, int? opacity, int? overrideDurationMs, bool suppressHaptic).
                // SEAM GAP (AI-6): the Core ISubliminalService.FlashSubliminalCustom seam is
                // (text, int? overrideDurationMs, bool suppressHaptic) -- it has NO opacity parameter and
                // its 2nd positional arg is overrideDurationMs, NOT opacity. Naively threading opacity
                // there would pass a 0-60 opacity as a millisecond duration (bug), so it is intentionally
                // NOT threaded. Text clamp(80) is ported with parity; opacity filed as a follow-up row
                // (add opacity to the seam + Avalonia impl).
                var text = (s.Text ?? "").Trim();
                if (text.Length > 80) text = text.Substring(0, 80);
                if (!string.IsNullOrEmpty(text))
                    await Dispatcher.UIThread.InvokeAsync(() => _subliminal.FlashSubliminalCustom(text));
                break;

            case AICommandType.spiral when c.Data is SpiralPinkFiler sp:
                await SetOverlayAsync(sp.On, Math.Clamp(sp.Intensity, 0, 30), spiral: true);
                break;

            case AICommandType.pink when c.Data is SpiralPinkFiler pp:
                await SetOverlayAsync(pp.On, Math.Clamp(pp.Intensity, 0, 30), spiral: false);
                break;

            case AICommandType.bounce when c.Data is Bounce bn:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (bn.On) _bouncing.Start(bn.Words);
                    else _bouncing.Stop();
                });
                break;

            case AICommandType.haptic when c.Data is HapticCommandData h:
                // Adapter: no ApplyVibrationModeAsync on the seam. Synthesize a pulse via TestAsync
                // (intensityPercent, durationMs). Intensity clamped to MaxAiHapticIntensity (default 0.6).
                var maxIntensity = _settings.Current?.CompanionPrompt?.MaxAiHapticIntensity ?? 0.6;
                var hIntensity = Math.Clamp(h.Intensity, 0, maxIntensity);
                var hDurationMs = Math.Clamp(h.Duration, 0, 10) * 1000;
                if (hDurationMs > 0)
                    _ = _haptics.TestAsync((int)Math.Round(hIntensity * 100), hDurationMs);
                break;

            case AICommandType.mantra_lockscreen when c.Data is MantraLockscreen m:
                var mantra = (m.Mantra ?? "").Trim();
                if (mantra.Length > 200) mantra = mantra.Substring(0, 200);
                if (!string.IsNullOrEmpty(mantra))
                {
                    var amount = Math.Clamp(m.Amount, 0, 5);
                    await Dispatcher.UIThread.InvokeAsync(() => _lockCard.ShowLockCard(mantra, amount, customStrict: true, isTest: false));
                }
                break;

            case AICommandType.video when c.Data is Media vm:
                await PlayMediaAsync(vm, isAudio: false, ct);
                break;

            case AICommandType.audio when c.Data is Media am:
                await PlayMediaAsync(am, isAudio: true, ct);
                break;

            case AICommandType.getbacktome when c.Data is GetBackToMe g:
                if (depth >= MaxGetBackToMeDepth) return;
                var delaySec = Math.Clamp(g.Delay, 1, 600);
                try { await Task.Delay(delaySec * 1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                await SendTokenMessageAsync(g.Token, g.JsonOnly, g.Text, ct).ConfigureAwait(false);
                if (g.Commands != null)
                {
                    foreach (var sub in g.Commands)
                    {
                        if (ct.IsCancellationRequested) break;
                        await ExecuteCore(sub, ct, depth + 1).ConfigureAwait(false);
                    }
                }
                break;
        }
    }

    /// <summary>Sets the spiral or pink overlay: toggles the settings field, clamps opacity, forces the
    /// overlay on with BypassLevelCheck, refreshes, persists. Matches WPF Spiral/PinkCommand.</summary>
    private async Task SetOverlayAsync(bool on, int intensity, bool spiral)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;
            if (spiral) { settings.SpiralOpacity = intensity; settings.SpiralEnabled = on; }
            else { settings.PinkFilterOpacity = intensity; settings.PinkFilterEnabled = on; }
            _overlay.BypassLevelCheck = true;
            if (!_overlay.IsRunning) _overlay.Start();
            _overlay.RefreshOverlays();
            _settings.Save();
        });
    }

    /// <summary>Video/audio playback with WPF-parity path validation: reject <c>..</c>, resolve under
    /// <see cref="IAppEnvironment.EffectiveAssetsPath"/>, require File.Exists + allowed extension;
    /// any failure OR Random/empty → random fallback. No-op if a video is already playing (video only).</summary>
    private async Task PlayMediaAsync(Media m, bool isAudio, CancellationToken ct)
    {
        var allowed = isAudio ? AudioExts : VideoExts;
        string? validated = null;
        if (!(m.Random || string.IsNullOrEmpty(m.Path)))
            validated = GetValidatedPath(m.Path, allowed);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (isAudio)
            {
                // Random audio: scan assets/audio recursively for an allowed file.
                var path = validated ?? PickRandomAsset("audio", allowed);
                if (path != null) { _audio.SetVolume(1.0); _ = _audio.PlayAsync(path, ct); }
            }
            else
            {
                if (_video.IsPlaying) return;
                if (validated != null) _video.PlaySpecificVideo(validated, false);
                // Drift vs WPF: WPF uses App.Video.TriggerVideo() (extra stuck-state cleanup).
                // Core PlayRandomVideo plays a random video — same user-visible behavior; the
                // service owns lifecycle. Filed for headed parity verification.
                else _video.PlayRandomVideo();
            }
        });
    }

    /// <summary>Resolves a model-supplied path under the assets root, rejecting traversal + requiring a
    /// matching extension + existence. Returns null on any failure (caller falls back to random).</summary>
    private string? GetValidatedPath(string? rawPath, HashSet<string> allowedExts)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        var root = _env.EffectiveAssetsPath;
        if (string.IsNullOrEmpty(root)) return null;
        // Reject relative/UNC/traversal — only rooted-local under the assets tree, no '..'.
        if (rawPath.Contains("..", StringComparison.OrdinalIgnoreCase)) return null;
        string full;
        try
        {
            full = Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(root, rawPath);
            full = Path.GetFullPath(full);
        }
        catch { return null; }
        // Root-containment WITH a trailing separator so a sibling dir whose name is a string-prefix
        // of root (e.g. ".../assets-secret") can't slip in via an absolute path. (WPF has the same
        // gap; this hardens the Avalonia head — backport to WPF later.)
        var rootFull = Path.GetFullPath(root);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar)) rootFull += Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;
        if (!allowedExts.Contains(Path.GetExtension(full))) return null;
        return File.Exists(full) ? full : null;
    }

    /// <summary>Picks a random file of the given kind under the assets root (best-effort).</summary>
    private string? PickRandomAsset(string kindSubfolder, HashSet<string> allowedExts)
    {
        try
        {
            var root = _env.EffectiveAssetsPath;
            if (string.IsNullOrEmpty(root)) return null;
            var dir = Path.Combine(root, kindSubfolder);
            if (!Directory.Exists(dir)) return null;
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => allowedExts.Contains(Path.GetExtension(f))).ToList();
            if (files.Count == 0) return null;
            return files[Random.Shared.Next(files.Count)];
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "AiCommandService: PickRandomAsset failed."); return null; }
    }

    /// <summary>The getbacktome follow-up: speak the optional text, re-prompt the AI with the token,
    /// and (unless JsonOnly) speak the AI's reply. Mirrors WPF GetBackToMeCommand.SendTokenMessage.</summary>
    private async Task SendTokenMessageAsync(string token, bool jsonOnly, string? text, CancellationToken ct)
    {
        var avatar = _services.GetService<IAvatarWindowService>();
        var ai = _services.GetService<IAiService>();
        if (ai == null) { _logger?.LogDebug("AiCommandService: IAiService not resolvable for getbacktome; skip."); return; }

        if (!string.IsNullOrEmpty(text) && avatar != null)
            await Dispatcher.UIThread.InvokeAsync(() => avatar.GigglePriority(text!, playSound: true, aiGenerated: false));

        var result = await ai.GetBambiReplyExAsync($"[Token={token}, JsonOnly={jsonOnly}]").ConfigureAwait(false);
        if (ct.IsCancellationRequested) return;

        if (!jsonOnly && result.Refusal == null && !string.IsNullOrEmpty(result.Text) && avatar != null)
            await Dispatcher.UIThread.InvokeAsync(() => avatar.GigglePriority(result.Text!, playSound: true, aiGenerated: result.IsAiGenerated));
    }

    // ---- per-effect gate map (WPF IsEffectAllowed) ----
    // Instance (not static) so the video case can read the master video-feature switch
    // via the injected ISettingsService, mirroring WPF's reach to App.Settings?.Current.
    private bool IsEffectAllowed(AICommandType cmd, CompanionPromptSettings s) => cmd switch
    {
        AICommandType.flash_image => s.AllowAiFlash,
        // Videos also require the master video-feature toggle (MandatoryVideosEnabled),
        // mirroring WPF AiCommandService.cs:189 — the AI must not play videos while the
        // user has the video feature turned OFF (bug #512). MandatoryVideosEnabled is the
        // same property the Video feature card reads (VideoFeatureControl.axaml.cs:110).
        AICommandType.video => s.AllowAiVideo && _settings.Current?.MandatoryVideosEnabled == true,
        AICommandType.audio => s.AllowAiAudio,
        AICommandType.bubbles => s.AllowAiBubbles,
        AICommandType.subliminal => s.AllowAiSubliminal,
        AICommandType.spiral => s.AllowAiOverlay,
        AICommandType.pink => s.AllowAiOverlay,
        AICommandType.mantra_lockscreen => s.AllowAiLockCard,
        AICommandType.bounce => s.AllowAiBounce,
        AICommandType.haptic => s.AllowAiHaptic,
        AICommandType.getbacktome => s.AllowAiGetBackToMe,
        AICommandType.none => false,
        _ => false,
    };

    private void CancelToken(string token)
    {
        if (_tokenCts.TryRemove(token, out var cts))
        {
            try { cts.Cancel(); } catch (Exception ex) { _logger?.LogDebug(ex, "AiCommandService: CTS.Cancel fault."); }
            try { cts.Dispose(); } catch { }
        }
    }

    private void RemoveToken(string token)
    {
        if (_tokenCts.TryRemove(token, out var cts)) try { cts.Dispose(); } catch { }
    }
}
