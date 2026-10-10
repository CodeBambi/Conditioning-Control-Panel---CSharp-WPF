using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF <c>Services/KeywordTriggerService.cs</c> (platform#1): feeds typed characters
/// into Core's <see cref="KeywordTriggerEngine"/> and performs the actions it dispatches, plus the
/// Pavlov achievement and the keyword quest credit (progression#41).
///
/// <para>Windows: characters come from the panic key's existing WH_KEYBOARD_LL hook
/// (<see cref="Win32PanicKey.KeyDown"/>), never a second hook. The handler runs inside that hook's
/// callback BEFORE the panic check, so it only snapshots modifier state and posts; it can never
/// throw into the callback (everything is caught), so the panic press is never skipped.</para>
///
/// <para>ponytail: Linux has no global typed-text source (X11PanicKey grabs one key, not a stream;
/// a whole-keyboard XInput2 raw listener is needed). Typed triggers are Windows-only there until one
/// lands; <see cref="KeywordTriggerEngine.CheckTextForMatches"/> is ready for clipboard / OCR text.</para>
/// </summary>
internal static class KeywordTriggerHead
{
    internal static KeywordTriggerEngine Engine { get; } = new();
    private static bool _started;

    private const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
        VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        Engine.Start();
        Engine.TriggerFired += OnTriggerFired;
        Engine.Dispatch += fire => _ = DispatchAsync(fire);
        if (OperatingSystem.IsWindows())
        {
            Engine.ForegroundResolver = ResolveForegroundWindows;
            Win32PanicKey.KeyDown += OnHookKeyDown;
        }
    }

    // ------------------------------------------------------------------ input (hook thread)

    /// <summary>On the hook thread, inside the panic callback: snapshot and post, nothing else.</summary>
    private static void OnHookKeyDown(int vk, string? _)
    {
        try
        {
            if (!Engine.IsActive || CoreSettings.Current?.KeywordTriggersEnabled != true) return;
            var shift = GetAsyncKeyState(VK_SHIFT) < 0;
            var ctrl = GetAsyncKeyState(VK_CONTROL) < 0;
            var alt = GetAsyncKeyState(VK_MENU) < 0;
            var caps = (GetKeyState(VK_CAPITAL) & 1) != 0;
            Dispatcher.UIThread.Post(() => OnKey(vk, shift, ctrl, alt, caps));
        }
        catch { /* never into the panic hook */ }
    }

    /// <summary>WPF OnKeyPressed, on the UI thread.</summary>
    internal static void OnKey(int vk, bool shift, bool ctrl, bool alt, bool caps)
    {
        try
        {
            switch (vk)
            {
                case VK_RETURN or VK_ESCAPE or VK_TAB: Engine.OnKey(KeywordBufferKey.Clear); return;
                case VK_BACK: Engine.OnKey(KeywordBufferKey.Backspace); return;
                case VK_SPACE: Engine.OnKey(KeywordBufferKey.Space); return;
            }
            if (Translate(vk, shift, ctrl, alt, caps) is { } ch) Engine.OnChar(ch);
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword trigger key failed"); }
    }

    /// <summary>WPF TranslateVkCode (ToUnicode with Shift / CapsLock / AltGr and the foreground
    /// window's layout). Flag 4 leaves the dead-key state alone so typing in the user's app is not
    /// disturbed. Control characters are dropped (a Ctrl chord is not text).</summary>
    private static char? Translate(int vk, bool shift, bool ctrl, bool alt, bool caps)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var state = new byte[256];
        if (shift) state[VK_SHIFT] = 0x80;
        if (ctrl) state[VK_CONTROL] = 0x80;
        if (alt) state[VK_MENU] = 0x80;
        if (caps) state[VK_CAPITAL] = 0x01;
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        var buf = new StringBuilder(4);
        var n = ToUnicodeEx((uint)vk, MapVirtualKey((uint)vk, 0), state, buf, buf.Capacity, 4, layout);
        if (n != 1) return null;
        var c = buf[0];
        return char.IsControl(c) ? null : c;
    }

    private static ForegroundApp? ResolveForegroundWindows()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return new ForegroundApp(p.ProcessName, pid == (uint)Environment.ProcessId);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ progression

    /// <summary>WPF GamificationBridge.OnKeywordTriggerFired + the quest credit in DispatchResponseAsync
    /// (one per fired trigger, merged or not).</summary>
    private static void OnTriggerFired(KeywordTrigger t, string source)
    {
        Log.Information("Keyword trigger fired ({Source}): '{Keyword}' id={Id}", source, t.Keyword, t.Id);
        try { App.Achievements?.TrackKeywordTriggerFired(); } catch (Exception ex) { Log.Warning(ex, "Pavlov tracker failed"); }
        try { App.Quests?.TrackKeywordTrigger(); } catch (Exception ex) { Log.Debug(ex, "Keyword quest credit failed"); }
    }

    // ------------------------------------------------------------------ dispatch

    private static Window? Host =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>WPF DispatchActionsAsync: duck once if any clip asks, run every action, unduck after the
    /// longest clip (+0.5 s).</summary>
    internal static async Task DispatchAsync(KeywordFire fire)
    {
        var s = CoreSettings.Current;
        long duckGen = -1;
        var didDuck = false;
        double maxAudio = 0;
        if (fire.Actions.OfType<PlayAudioAction>().Any(a => a.DuckSystemAudio) && s?.AudioDuckingEnabled == true)
        {
            CoreAudio.Duck(s.DuckingLevel);
            duckGen = CoreAudio.DuckGeneration;
            didDuck = true;
        }
        try
        {
            foreach (var action in fire.Actions)
            {
                var dur = await RunAsync(action, fire.Trigger);
                if (dur > maxAudio) maxAudio = dur;
            }
        }
        catch (Exception ex) { Log.Error("Keyword trigger action dispatch error: {Error}", ex.Message); }
        if (!didDuck) return;
        await Task.Delay(maxAudio > 0 ? TimeSpan.FromSeconds(maxAudio + 0.5) : TimeSpan.FromMilliseconds(500));
        CoreAudio.Unduck(duckGen);
    }

    private static async Task<double> RunAsync(KeywordAction action, KeywordTrigger trigger)
    {
        switch (action)
        {
            case PlayAudioAction audio:
                return await PlayAudioAsync(audio);
            case VisualEffectAction visual:
                await Dispatcher.UIThread.InvokeAsync(() => FireVisualEffect(visual.Effect, trigger));
                return 0;
            case HighlightAction:
                // Needs OCR word boxes (WPF passes matchedWords only from a screen read); a typed match has none.
                return 0;
            case HapticAction haptic:
                _ = CoreHaptics.Service?.TriggerKeywordPatternAsync(trigger.Keyword, haptic.Intensity);
                return 0;
            case AddXpAction xp when xp.Amount > 0:
                double amount = xp.Amount;
                if (CoreSession.IsSessionRunning) amount *= CoreSettings.Current?.KeywordSessionMultiplier ?? 1.5;
                CoreProgression.AddXP(amount, "KeywordTrigger");
                return 0;
            case AvatarCommentAction comment:
                AvatarComment(comment, trigger);
                return 0;
            case ExtendSessionAction ext:
                Log.Information("Keyword trigger: ExtendSessionAction stubbed (+{Min}m), as WPF", ext.Minutes);
                return 0;
            case ChasterAddTimeAction chas:
                ChasterAddTime(chas, trigger);
                return 0;
            default:
                return 0;
        }
    }

    /// <summary>WPF KeywordTriggerService.FindLinkedAudio: the active mod's clip, then Resources/sub_audio
    /// (Core's SubliminalWhisper lookup, the same one the subliminal whisper uses).</summary>
    internal static string? FindLinkedAudio(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        try
        {
            return SubliminalWhisper.FindLinkedAudio(keyword,
                SubliminalWhisper.ModAudioDir(App.Mods?.ActiveMod?.InstalledPath),
                Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio"),
                App.Mods?.ActiveModId);
        }
        catch { return null; }
    }

    /// <summary>WPF ResolveAudioPath: rooted passes through, else Resources/, else Resources/sub_audio/.</summary>
    internal static string ResolveAudioPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var res = ContentLocator.Resolve(Path.Combine("Resources", path));
        if (File.Exists(res)) return res;
        var sub = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
        return File.Exists(sub) ? sub : path;
    }

    /// <summary>WPF DispatchPlayAudioAsync + PlayTriggerAudio (same curve: (vol x master)^1.5, floor 0.05).</summary>
    private static async Task<double> PlayAudioAsync(PlayAudioAction audio)
    {
        if (string.IsNullOrEmpty(audio.FilePath)) return 0;
        var path = ResolveAudioPath(audio.FilePath);
        if (!File.Exists(path)) { Log.Warning("PlayAudioAction: could not resolve '{Path}'", audio.FilePath); return 0; }
        var master = (CoreSettings.Current?.MasterVolume ?? 100) / 100.0f;
        var curved = Math.Max(0.05f, (float)Math.Pow(audio.Volume / 100.0f * master, 1.5));
        double last = 0;
        for (var i = 0; i < Math.Max(1, audio.PlayCount); i++)
        {
            if (i > 0 && audio.DelayBetweenMs > 0) await Task.Delay(audio.DelayBetweenMs);
            var started = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            CoreAudio.PlayOneShot(path, curved, "keyword-trigger",
                d => started.TrySetResult(d.TotalSeconds), () => started.TrySetResult(0));
            last = await Task.WhenAny(started.Task, Task.Delay(5000)) == started.Task ? started.Task.Result : 0;
        }
        return last;
    }

    /// <summary>WPF FireVisualEffect, on the UI thread, through the shell's effect door (portal panic
    /// bind first) and only where click-through overlays exist.</summary>
    internal static void FireVisualEffect(KeywordVisualEffect effect, KeywordTrigger trigger)
    {
        try
        {
            switch (effect)
            {
                case KeywordVisualEffect.SubliminalFlash:
                    if (PickSubliminal() is { } text) Overlay(host => SubliminalOverlay.Show(host, text));
                    break;
                case KeywordVisualEffect.ExactSubliminal:
                    Overlay(host => SubliminalOverlay.Show(host, trigger.Keyword.ToUpperInvariant()));
                    Engine.MuteKeywordEcho(trigger.Keyword, 3000);   // the echo would re-arm OCR (WPF)
                    break;
                case KeywordVisualEffect.ImageFlash:
                    Overlay(host => FlashOverlay.TriggerOnce(host));
                    break;
                case KeywordVisualEffect.MindWipe:
                    if ((CoreMindWipe.ClipCountProvider?.Invoke() ?? 0) > 0) CoreMindWipe.TriggerOnce();
                    break;
                case KeywordVisualEffect.Bubbles:
                    // ponytail: WPF BubbleService.SpawnOnce spawns even with ambient bubbles off; the port's
                    // BubbleOverlay.Spawn only adds to a running field.
                    BubbleOverlay.Spawn();
                    break;
                case KeywordVisualEffect.OverlayPulse:
                    // ponytail: no OverlayService.PulseOverlays twin on this head (studio lane).
                    break;
                case KeywordVisualEffect.BrainDrain:
                    // WPF new BrainDrainMeltPayload().Fire(): ten seconds of haze on the user's own
                    // blur dial, one drain at a time (BubbleOverlay.FireDrain is that payload).
                    Overlay(host => BubbleOverlay.FireDrain(host));
                    break;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Keyword visual effect failed"); }
    }

    private static void Overlay(Action<Window> show)
    {
        if (!X11Overlay.IsAvailable || Host is null) return;
        CompanionEffects.StartEffect(() => { if (Host is { } h) show(h); });
    }

    /// <summary>One enabled line from the user's subliminal pool (WPF SubliminalService.FlashSubliminal).</summary>
    private static string? PickSubliminal()
    {
        var pool = CoreSettings.Current?.SubliminalPool;
        var lines = pool?.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
        if (lines == null || lines.Length == 0)
            lines = CoreMods.GetDefaultSubliminalPool().Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
        return lines.Length == 0 ? null : lines[Random.Shared.Next(lines.Length)];
    }

    // ------------------------------------------------------------------ avatar comment (ai#13)

    /// <summary>WPF DispatchAvatarComment: AI line when available (GetKeywordCommentAsync), else a canned
    /// phrase from the category; fire-and-forget.</summary>
    private static void AvatarComment(AvatarCommentAction a, KeywordTrigger trigger)
    {
        var ai = App.Ai;
        var aiAvailable = ai?.IsAvailable == true;
        if (a.RequireAiAvailable && !aiAvailable)
        {
            if (Canned(a.FallbackPhraseCategory) is { } canned) ShowAvatarLine(canned, false, 0);
            return;
        }
        var keyword = trigger.Keyword;
        _ = Task.Run(async () =>
        {
            try
            {
                string? line = null;
                var fromAi = false;
                if (aiAvailable && ai != null)
                {
                    line = await ai.GetKeywordCommentAsync(keyword, a.PromptTemplate);
                    fromAi = !string.IsNullOrEmpty(line);
                }
                if (string.IsNullOrEmpty(line)) line = Canned(a.FallbackPhraseCategory);
                if (!string.IsNullOrEmpty(line)) ShowAvatarLine(line, fromAi, 0);
            }
            catch (Exception ex) { Log.Debug("Keyword avatar comment failed: {Error}", ex.Message); }
        });
    }

    private static string? Canned(string? category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        // ponytail: WPF asks App.CompanionPhrases (the user's edited pool) first; no port twin yet.
        var phrases = CoreMods.GetPhrases(category);
        return phrases == null || phrases.Length == 0 ? null : phrases[Random.Shared.Next(phrases.Length)];
    }

    /// <summary>WPF ShowAvatarLine: wait while the companion speaks (750 ms x 16), then GigglePriority.</summary>
    private static void ShowAvatarLine(string line, bool aiGenerated, int attempt) => Dispatcher.UIThread.Post(() =>
    {
        var tube = Views.AvatarTube.AvatarTubeWindow.Live;
        if (tube == null) return;
        if (tube.IsSpeaking)
        {
            if (attempt >= 16) return;
            DispatcherTimer.RunOnce(() => ShowAvatarLine(line, aiGenerated, attempt + 1), TimeSpan.FromMilliseconds(750));
            return;
        }
        tube.RunOnAvatar(() => tube.GigglePriority(line, playSound: true, aiGenerated: aiGenerated));
    });

    // ------------------------------------------------------------------ Chaster

    private static readonly object ChasterGate = new();
    private static KeywordTriggerChasterCap? _chasterCap;
    private static string ChasterCapPath => Path.Combine(CorePaths.UserData, "chaster_trigger_caps.json");

    /// <summary>WPF DispatchChasterAddTime: Circe's tab, within the per-trigger day allowance.</summary>
    private static void ChasterAddTime(ChasterAddTimeAction chas, KeywordTrigger trigger)
    {
        var chaster = ChasterHead.Service;
        if (chaster == null) return;
        var id = string.IsNullOrEmpty(trigger.Id) ? "keyword:" + (trigger.Keyword ?? "") : trigger.Id;
        var requested = Math.Clamp(chas.Minutes, 0, 60) * 60;
        var now = DateTime.Now;
        lock (ChasterGate)
        {
            _chasterCap ??= KeywordTriggerChasterCap.Load(ChasterCapPath);
            var allowed = _chasterCap.Allowance(id, requested, now);
            if (allowed <= 0)
            {
                Log.Information("Keyword trigger: Chaster time for '{Keyword}' is at its day cap, nothing booked", trigger.Keyword);
                return;
            }
            var booking = chaster.NoteSeconds("watcher", allowed);
            if (booking.AppliedSeconds <= 0) return;
            _chasterCap.Record(id, booking.AppliedSeconds, now);
            _chasterCap.Save(ChasterCapPath);
        }
    }

    // ------------------------------------------------------------------ Win32

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int cch, uint flags, IntPtr layout);
}
