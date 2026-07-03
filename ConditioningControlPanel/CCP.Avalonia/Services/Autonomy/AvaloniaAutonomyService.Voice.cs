using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Autonomy;

/// <summary>
/// "Hey Bambi" user-driven mic input (ported from the WPF AutonomyService.Voice / .VoiceCommands).
/// Wake-word loop → listen against the closed command grammar → dispatch to a feature + confirm
/// in-character (voiced via the bark manifest); unrecognised speech falls through to a spoken mantra.
/// Self-protecting: nothing arms unless mic consent is given AND the offline engine is available.
///
/// Parity notes vs WPF:
///  - Dispatched (have an Avalonia seam): all effect toggles, takeover on/off, volume/mute,
///    panic, stop-listening, pause/resume (ISessionService), and help (spoken hint).
///  - Intentionally NOT dispatched (no Avalonia seam yet) and therefore correctly UN-acked — saying
///    one does nothing and voices no false "Okay~": video_pause/video_resume (IVideoService has no
///    pause/resume), freeze_once (TriggerBambiFreeze), shake_once (screen shake).
///  - Push-to-talk needs a key-name→virtual-key map; the hook is here but PTT is a no-op for now.
/// </summary>
public sealed partial class AvaloniaAutonomyService
{
    private int _voiceBusyFlag;
    private CancellationTokenSource? _wakeLoopCts;
    private CancellationTokenSource? _wakeWaitCts;
    private Task? _wakeLoopTask;
    private VoiceCommandSpec? _lastVoiceIntent;
    private int _preMuteVolume = 70;

    private Dictionary<string, Action>? _dispatch;

    /// <summary>Whether the user has armed a self-initiated mic mode.</summary>
    public bool UserDrivenVoiceArmed
    {
        get
        {
            var s = _settings.Current;
            return s != null && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);
        }
    }

    /// <summary>Reconcile the wake-word loop with current settings. Safe to call any time.</summary>
    public void RefreshVoiceInputModes()
    {
        try
        {
            var s = _settings.Current;
            bool baseOk = !_disposed && s?.MicConsentGiven == true && _speech?.IsAvailable == true;

            if (baseOk && s!.SpeechWakeWordEnabled && WakeWords().Count > 0)
                StartWakeLoop();
            else
                StopWakeLoop();

            if (baseOk && s!.SpeechPushToTalkEnabled && _inputHook != null)
                StartPushToTalk();
            else
                StopPushToTalk();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaAutonomyService: RefreshVoiceInputModes failed"); }
    }

    /// <summary>User-initiated "stop the mic": cut any capture and tear down the wake loop.</summary>
    public void StopVoiceInput()
    {
        try
        {
            _speech?.StopListening();
            StopWakeLoop();
            StopPushToTalk();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaAutonomyService: StopVoiceInput failed"); }
    }

    // ── Push-to-talk (global key) ──

    private bool _pttArmed;

    private void StartPushToTalk()
    {
        if (_pttArmed || _inputHook == null) return;
        _inputHook.KeyPressed += OnPttKeyPressed;
        _pttArmed = true;
    }

    private void StopPushToTalk()
    {
        if (!_pttArmed || _inputHook == null) return;
        _inputHook.KeyPressed -= OnPttKeyPressed;
        _pttArmed = false;
    }

    private void OnPttKeyPressed(object? sender, Core.Platform.KeyboardHookEventArgs e)
    {
        if (Volatile.Read(ref _voiceBusyFlag) != 0) return;
        if (_speech?.IsAvailable != true) return;
        if (e.VirtualKeyCode == PushToTalkVk())
            RequestVoiceCommand();
    }

    /// <summary>Windows virtual-key code for the configured PTT key. Defaults to F8 (0x77).</summary>
    private int PushToTalkVk()
    {
        var name = (_settings.Current?.SpeechPushToTalkKey ?? "").Trim();
        return KeyNameToVk(name) ?? 0x77; // F8
    }

    /// <summary>
    /// Map a WPF/Avalonia Key enum NAME (how the setting is stored) to a Windows virtual-key code.
    /// Covers the keys a user would pick for push-to-talk: F1–F24, letters, digits, and a few specials.
    /// </summary>
    private static int? KeyNameToVk(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();

        // F1–F24 → 0x70–0x87
        if ((name.Length == 2 || name.Length == 3) && (name[0] == 'F' || name[0] == 'f')
            && int.TryParse(name.Substring(1), out var fn) && fn >= 1 && fn <= 24)
            return 0x70 + (fn - 1);

        // Single letter A–Z → 0x41–0x5A
        if (name.Length == 1 && char.IsLetter(name[0]))
            return char.ToUpperInvariant(name[0]);

        // Single digit 0–9 → 0x30–0x39 (also "D0".."D9" as WPF names them)
        if (name.Length == 1 && char.IsDigit(name[0])) return name[0];
        if (name.Length == 2 && (name[0] == 'D' || name[0] == 'd') && char.IsDigit(name[1])) return name[1];

        return name.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "capslock" or "capital" => 0x14,
            "insert" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "prior" => 0x21,
            "pagedown" or "next" => 0x22,
            "scrolllock" or "scroll" => 0x91,
            "pause" => 0x13,
            "left" => 0x25,
            "right" => 0x27,
            "up" => 0x26,
            "down" => 0x28,
            _ => (int?)null,
        };
    }

    private List<string> WakeWords()
    {
        var raw = _settings.Current?.SpeechWakeWords ?? "";
        return raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().ToList();
    }

    private void StartWakeLoop()
    {
        if (_wakeLoopTask is { IsCompleted: false }) return;
        _wakeLoopCts = new CancellationTokenSource();
        var ct = _wakeLoopCts.Token;
        _wakeLoopTask = Task.Run(() => WakeLoopAsync(ct), ct);
    }

    private void StopWakeLoop()
    {
        try { _wakeWaitCts?.Cancel(); } catch { }
        try { _wakeLoopCts?.Cancel(); } catch { }
        _wakeLoopCts = null;
    }

    private async Task WakeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_speech?.IsAvailable != true && _wakeWord?.IsAvailable != true) { await Task.Delay(1000, ct).ConfigureAwait(false); continue; }
                _wakeWaitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                // Prefer the offline sherpa KWS spotter when its model is installed; else the grammar wake.
                // KWS gives no transcript — RequestVoiceCommand() opens a fresh listen for the command.
                string? heard;
                if (_wakeWord?.IsAvailable == true)
                    heard = (await _wakeWord.WaitForWakeAsync(_wakeWaitCts.Token).ConfigureAwait(false)) ? "" : null;
                else
                    heard = await _speech.WaitForWakeWordAsync(WakeWords(), _wakeWaitCts.Token).ConfigureAwait(false);
                if (heard != null && !ct.IsCancellationRequested)
                    RequestVoiceCommand();
            }
            catch (OperationCanceledException) { /* re-loop or exit on ct */ }
            catch (Exception ex) { _logger?.LogDebug(ex, "AvaloniaAutonomyService: wake loop iteration failed"); await Task.Delay(500, ct).ContinueWith(_ => { }).ConfigureAwait(false); }
        }
    }

    /// <summary>Serialized funnel: one voice prompt at a time. Listens for a command, else a mantra.</summary>
    private async void RequestVoiceCommand()
    {
        if (_speech?.IsAvailable != true) return;
        if (Interlocked.CompareExchange(ref _voiceBusyFlag, 1, 0) != 0) return;
        try
        {
            // Let the wake loop's mic release before we open ours.
            try { _wakeWaitCts?.Cancel(); } catch { }
            for (int i = 0; i < 24 && _speech?.IsListening == true; i++)
                await Task.Delay(25).ConfigureAwait(false);

            bool handled = await TryHandleVoiceCommandAsync().ConfigureAwait(false);
            if (!handled) DeliverMantra();
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "AvaloniaAutonomyService: RequestVoiceCommand failed"); }
        finally { Interlocked.Exchange(ref _voiceBusyFlag, 0); }
    }

    private async Task<bool> TryHandleVoiceCommandAsync()
    {
        if (_speech?.IsAvailable != true) return false;

        var grammar = VoiceCommandGrammar.AllAliases().ToList();
        if (grammar.Count == 0) return false;

        var res = await _speech.RecognizeOneOfAsync(grammar, new RecognizeOptions
        {
            Timeout = TimeSpan.FromSeconds(6),
            OnsetTimeout = TimeSpan.FromSeconds(3),
        }).ConfigureAwait(false);

        if (res.Unavailable || res.TimedOut || !res.LoudEnough || string.IsNullOrWhiteSpace(res.Transcript))
            return false;

        var intent = VoiceCommandGrammar.Match(res.Transcript);
        if (intent == null) return false;
        if (intent.IsMantra) return false; // explicit mantra request → caller's mantra flow

        if (intent.IsReplay)
        {
            intent = _lastVoiceIntent;
            if (intent == null) return false;
        }

        // help is text-only — its entire effect is speaking the "what can I say" hint (WPF help
        // intent, AutonomyService.VoiceCommands.cs:650). Handle it directly, outside the dispatch table.
        if (string.Equals(intent.Name, "help", StringComparison.OrdinalIgnoreCase))
        {
            SpeakHelp();
            return true;
        }

        var dispatched = Dispatch(intent);
        if (!dispatched) return false; // no Avalonia seam for this intent → NO false "Okay~" ack

        if (intent.Repeatable) _lastVoiceIntent = intent;
        SpeakConfirmation(intent);
        return true;
    }

    /// <summary>Run the matched intent on the UI thread. Returns false if the intent has no Avalonia target.</summary>
    private bool Dispatch(VoiceCommandSpec intent)
    {
        var table = _dispatch ??= BuildDispatch();
        if (!table.TryGetValue(intent.Name, out var action)) return false;
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "AvaloniaAutonomyService: voice command '{Name}' failed", intent.Name); }
        });
        return true;
    }

    private Dictionary<string, Action> BuildDispatch() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["panic"]            = Panic,
        ["bubbles_on"]       = () => _bubbles?.Start(),
        ["bubbles_off"]      = () => _bubbles?.Stop(),
        ["video_on"]         = () => _video?.TriggerVideo(),
        ["video_off"]        = () => _video?.Stop(),
        ["flash_once"]       = () => _flash?.TriggerFlashOnce(null, 150, true, false),
        ["subliminals_on"]   = () => _subliminal?.Start(),
        ["subliminals_off"]  = () => _subliminal?.Stop(),
        ["bouncing_on"]      = () => _bouncingText?.Start(),
        ["bouncing_off"]     = () => _bouncingText?.Stop(),
        ["spiral_on"]        = () => _overlay?.ShowOverlaySustained("spiral", 0.5),
        ["spiral_off"]       = () => _overlay?.HideOverlaySustained("spiral"),
        ["pink_on"]          = () => _overlay?.ShowOverlaySustained("pink_filter", 0.4),
        ["pink_off"]         = () => _overlay?.HideOverlaySustained("pink_filter"),
        ["wipe_once"]        = () => _mindWipe?.TriggerOnce(),
        ["lock_once"]        = () => _lockCard?.ShowLockCard(),
        ["count_once"]       = () => _bubbleCount?.TriggerGame(forceTest: true),
        ["quiz_once"]        = () => _popQuiz?.ShowPopQuiz(),
        ["keyword_on"]       = () => _keywordTriggers?.Start(),
        ["keyword_off"]      = () => _keywordTriggers?.Stop(),
        ["deeper"]           = () => _overlay?.ShowOverlaySustained("braindrain", 0.5),
        ["takeover_on"]      = Start,
        ["takeover_off"]     = Stop,
        ["pause"]            = () => ResolveSession()?.PauseSession(),
        ["resume"]           = () => ResolveSession()?.ResumeSession(),
        ["mute"]             = () => SetMute(true),
        ["unmute"]           = () => SetMute(false),
        ["louder"]           = () => AdjustVolume(+15),
        ["quieter"]          = () => AdjustVolume(-15),
        ["stop_listening"]   = StopVoiceInput,
    };

    private void Panic()
    {
        void Try(Action a) { try { a(); } catch { } }
        Try(Stop);
        Try(() => _video?.Stop());
        Try(() => _flash?.Stop());
        Try(() => _subliminal?.Stop());
        Try(() => _bubbles?.Stop());
        Try(() => _bouncingText?.Stop());
        Try(() => _mindWipe?.Stop());
        Try(() => _bubbleCount?.Stop());
        Try(() => _overlay?.Stop());
        Try(StopVoiceInput);
    }

    private void AdjustVolume(int delta)
    {
        var s = _settings.Current;
        if (s == null) return;
        s.MasterVolume = Math.Clamp(s.MasterVolume + delta, 0, 100);
        try { _settings.Save(); } catch { }
        try { _video?.UpdateVolume(); } catch { }
    }

    private void SetMute(bool mute)
    {
        var s = _settings.Current;
        if (s == null) return;
        if (mute)
        {
            if (s.MasterVolume > 0) _preMuteVolume = s.MasterVolume;
            s.MasterVolume = 0;
        }
        else if (s.MasterVolume == 0)
        {
            s.MasterVolume = _preMuteVolume > 0 ? _preMuteVolume : 70;
        }
        try { _settings.Save(); } catch { }
        try { _video?.UpdateVolume(); } catch { }
    }

    /// <summary>Voiced (or text) in-character confirmation for a handled command, via the bark manifest.</summary>
    private void SpeakConfirmation(VoiceCommandSpec intent)
    {
        string text = "Okay~";
        string? audio = null;
        if (!string.IsNullOrEmpty(intent.VoiceRuleId) && _barkManifest?.PickVoiceLine(intent.VoiceRuleId) is { } line)
        {
            text = string.IsNullOrWhiteSpace(line.Text) ? text : line.Text;
            audio = line.Audio;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try { _avatar?.GigglePriority(text, playSound: audio != null, aiGenerated: false, phraseAudioPath: audio, barkVoice: audio != null); }
            catch { }
        });
    }

    /// <summary>
    /// Speak a "what can I say" hint via the avatar seam (WPF help intent). Text-only (no bark rule);
    /// lists only the commands that actually dispatch in this head, so it never promises a no-op.
    /// </summary>
    private void SpeakHelp()
    {
        const string helpText =
            "ooh lots! try: bubbles, flash me, a video, the spiral, deeper, quiz me, lock me, " +
            "pause, quieter — or say red to stop everything~!";
        Dispatcher.UIThread.Post(() =>
        {
            try { _avatar?.GigglePriority(helpText, playSound: false, aiGenerated: false); }
            catch { }
        });
    }

    /// <summary>
    /// Lazily resolve the session service for pause/resume dispatch. Not constructor-injected into the
    /// autonomy service, so resolve at call time from the head's provider — safe because dispatch runs
    /// well after DI is built. Returns null on heads without a session service.
    /// </summary>
    private ISessionService? ResolveSession()
    {
        try { return App.Services?.GetService<ISessionService>(); }
        catch { return null; }
    }

    /// <summary>Fallback when no command was heard: have the companion deliver a spoken mantra prompt.</summary>
    private void DeliverMantra()
    {
        var m = _mantraVoice?.NextMantra();
        if (m == null) return;
        var line = !string.IsNullOrWhiteSpace(m.PromptText) ? m.PromptText : m.Phrase;
        if (string.IsNullOrWhiteSpace(line)) return;
        var audio = _mantraVoice?.ResolveAudio(m.PromptAudio);

        Dispatcher.UIThread.Post(() =>
        {
            try { _avatar?.GigglePriority(line, playSound: audio != null, aiGenerated: false, phraseAudioPath: audio, barkVoice: audio != null); }
            catch { }
        });
    }
}
