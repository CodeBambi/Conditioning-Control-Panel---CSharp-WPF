using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games.BackRoom;

/// <summary>
/// THE SPOKEN WORD (CONTRACT 10.21, WPF Services/BackRoom/BackRoomVoice). Only RECORDED audio ever speaks
/// (owner, 2026-09-25, NO SYNTHETIC SPEECH): the player's own clip for the phrase (an enabled keyword
/// trigger's PlayAudio action, else the app's linked-audio search), then the bundled neutral Circe clip
/// (Resources/Audio/backroom/words), else <c>none</c> and the word stays silent.
///
/// <para>Plays through <see cref="CoreAudio.PlayStoppable"/> at the room's OWN subliminal level (not the
/// app's volumes), 1.5 power curve as WPF. A new word cuts the one before it.</para>
///
/// <para>Deviation: this head has no sample decoder, so the reversal easter egg plays the clip forwards
/// (WPF's own fallback when a reversal cannot be rendered), and the clip length comes from the player's
/// started callback instead of a file probe.</para>
/// </summary>
internal sealed class BackRoomVoice : IBackRoomVoice
{
    /// <summary>Longer than any word the deal can hold; a pasted paragraph is never looked up.</summary>
    internal const int MaxTextLength = 200;
    /// <summary>How long Speak waits for the player to report the clip length before acking 0.</summary>
    internal const int StartWaitMs = 700;

    private readonly Func<string, string?> _clip;
    private readonly Func<string, string?> _preset;
    /// <summary>Play the file; returns the clip length in ms (0 unknown) or -1 when nothing played.</summary>
    private readonly Func<string, int> _play;
    private readonly Action _stop;
    private readonly Action<string>? _log;

    /// <param name="breakout">True for a standalone Breakout page: it reads its own subliminal level.</param>
    public BackRoomVoice(Func<bool> breakout)
    {
        var player = new AppPlayer(breakout);
        _clip = FindPlayerClip;
        _preset = SubliminalWhisper.FindPresetClip;
        _play = player.Play;
        _stop = player.Stop;
        _log = msg => Log.Debug("[BackRoomVoice] {Msg}", msg);
    }

    /// <summary>Seams for the suite: every source and sink the chain reads is passed in.</summary>
    internal BackRoomVoice(Func<string, string?> clip, Func<string, string?> preset, Func<string, int> play, Action stop,
        Action<string>? log = null)
    {
        _clip = clip;
        _preset = preset;
        _play = play;
        _stop = stop;
        _log = log;
    }

    /// <summary>Which recording owns this phrase: the player's clip, then the bundled preset, else "none".</summary>
    internal static (string Source, string? Path) Resolve(string text, Func<string, string?> clip, Func<string, string?> preset)
    {
        var key = SubliminalWhisper.Normalize(text);
        if (key.Length == 0) return ("none", null);
        if (Try(clip, text) is { } c) return ("clip", c);
        if (Try(preset, key) is { } p) return ("preset", p);
        return ("none", null);

        static string? Try(Func<string, string?> f, string arg)
        {
            try { var s = f(arg); return string.IsNullOrWhiteSpace(s) ? null : s; }
            catch { return null; }
        }
    }

    public BackRoomVoiceAck Speak(string text, bool reversed, int seed)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength) return new("none", 0);
            var (source, path) = Resolve(text, _clip, _preset);
            if (path == null) return new("none", 0);
            // A muted room still owns the word: the source is reported with no duration.
            int ms;
            try { ms = _play(path); }
            catch (Exception ex) { _log?.Invoke("play failed (" + ex.GetType().Name + ")"); ms = -1; }
            _log?.Invoke("word spoke " + source + " " + ms + " ms");
            return new(source, Math.Clamp(ms, 0, 60_000));
        }
        catch (Exception ex)
        {
            _log?.Invoke("speak threw (" + ex.GetType().Name + ")");
            return new("none", 0);
        }
    }

    public void Stop()
    {
        try { _stop(); } catch (Exception ex) { _log?.Invoke("stop threw (" + ex.GetType().Name + ")"); }
    }

    /// <summary>WPF FindPlayerClip: an ENABLED keyword trigger for the phrase wins (its first enabled
    /// PlayAudio action, else the legacy flat field); otherwise the app's usual linked-audio search
    /// (active mod's flashes_audio, then sub_audio for mods that may borrow it, else the neutral voice).</summary>
    private static string? FindPlayerClip(string text)
    {
        var key = SubliminalWhisper.Normalize(text);
        try
        {
            foreach (var t in CoreSettings.Current.KeywordTriggers ?? new List<KeywordTrigger>())
            {
                if (t == null || !t.Enabled || SubliminalWhisper.Normalize(t.Keyword) != key) continue;
                var fromAction = t.Actions?
                    .OfType<PlayAudioAction>()
                    .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.FilePath))
                    .Select(a => Platform.KeywordTriggerHead.ResolveAudioPath(a.FilePath!))
                    .FirstOrDefault(File.Exists);
                if (fromAction != null) return fromAction;
                if (!string.IsNullOrWhiteSpace(t.AudioFilePath))
                {
                    var flat = Platform.KeywordTriggerHead.ResolveAudioPath(t.AudioFilePath!);
                    if (File.Exists(flat)) return flat;
                }
            }
        }
        catch (Exception ex) { Log.Debug("[BackRoomVoice] trigger clip lookup failed ({Type})", ex.GetType().Name); }

        try
        {
            var linked = Platform.KeywordTriggerHead.FindLinkedAudio(text);
            if (!string.IsNullOrWhiteSpace(linked) && File.Exists(linked)) return linked;
        }
        catch (Exception ex) { Log.Debug("[BackRoomVoice] linked audio lookup failed ({Type})", ex.GetType().Name); }
        return null;
    }

    /// <summary>The app's own audio path: one voice, a new word cuts the last.</summary>
    private sealed class AppPlayer(Func<bool> breakout)
    {
        private readonly object _gate = new();
        private Action? _stop;

        public int Play(string path)
        {
            Stop();
            bool bo;
            try { bo = breakout(); } catch { bo = false; }
            var sub = BackRoomWire.SubVolume(CoreSettings.Current, bo) / 100.0f;
            var volume = (float)Math.Pow(Math.Clamp(sub, 0f, 1f), 1.5);
            if (volume <= 0f) return -1;   // a deliberate mute: the host still owns the word, it is just silent
            if (CoreAudio.PlayOneShotProvider == null && CoreAudio.PlayStoppableProvider == null) return -1;
            int length = 0;
            using var started = new ManualResetEventSlim(false);
            var gate = started;
            var stop = CoreAudio.PlayStoppable(path, volume, "br-word",
                len => { Volatile.Write(ref length, (int)Math.Min(len.TotalMilliseconds, 60_000)); try { gate.Set(); } catch (ObjectDisposedException) { } },
                () => { try { gate.Set(); } catch (ObjectDisposedException) { } });
            lock (_gate) _stop = stop;
            try { started.Wait(StartWaitMs); } catch { }
            return Volatile.Read(ref length);
        }

        public void Stop()
        {
            Action? s;
            lock (_gate) { s = _stop; _stop = null; }
            try { s?.Invoke(); } catch (Exception ex) { Log.Debug("[BackRoomVoice] stop: {E}", ex.Message); }
        }
    }
}
