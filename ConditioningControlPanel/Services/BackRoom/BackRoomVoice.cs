using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// THE SPOKEN WORD (CONTRACT 10.21). The slot's subliminal beat used to be spoken by the browser's
/// own speechSynthesis, which is a placeholder voice on whatever the WebView happened to have. The
/// host now owns the line, in one order:
/// <list type="number">
/// <item><b>clip</b>: the player's OWN audio for that phrase. An enabled keyword trigger whose
/// <see cref="KeywordTrigger.Keyword"/> is the phrase and that carries a PlayAudio action (or the
/// legacy <c>AudioFilePath</c>) wins; else <see cref="KeywordTriggerService.FindLinkedAudio"/>, which
/// is the active mod's <c>resources/sounds/flashes_audio</c> then <c>Resources/sub_audio</c> - the same
/// precedence the subliminal whisper already uses, so the room speaks in the voice the app does.</item>
/// <item><b>preset</b>: a bundled Back Room word clip, <c>Resources/Audio/backroom/words/words.json</c>
/// (normalised phrase -&gt; file name). Ships empty; the owner generates the files.</item>
/// <item><b>tts</b>: Windows speech, rendered to a wav here and played like any other clip. No package:
/// <c>Windows.Media.SpeechSynthesis</c> comes with the project's own <c>net8.0-windows10.0.19041.0</c>.</item>
/// <item><b>none</b>: the page keeps its speechSynthesis as the last fallback (the phone playtest, where
/// there is no host at all, and any path that threw here).</item>
/// </list>
///
/// <para>Everything plays through <see cref="AudioService.PlayOneShot"/>, so the room lands on the
/// device the audio picker chose and at the whisper's own volume curve.</para>
///
/// <para>The reversal easter egg is REAL here: the decoded samples are written back to front, which is
/// the thing the page could only fake by spelling the word backwards.</para>
///
/// <para>Does file I/O and speech synthesis: the bridge calls it off the UI thread.</para>
/// </summary>
internal sealed class BackRoomVoice : IBackRoomVoice
{
    /// <summary>Bundled preset clips, next to the manifest that names them.</summary>
    internal const string WordsFolder = @"Resources\Audio\backroom\words";
    internal const string ManifestName = "words.json";

    /// <summary>A manifest may only name a plain file inside its own folder.</summary>
    private static readonly Regex SafeFileName = new("^[a-z0-9][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex NotWord = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    /// <summary>Longer than any word the deal can hold; a pasted paragraph is not synthesised.</summary>
    internal const int MaxTextLength = 200;
    /// <summary>A reversal decodes the whole clip into memory; 60 s of stereo 48 kHz floats is the cap.</summary>
    internal const int MaxReverseSamples = 60 * 48000 * 2;

    /// <summary>Soft and slow, the subliminal whisper's pace rather than an announcer's.</summary>
    internal const double TtsRate = 0.75, TtsPitch = 0.9;

    private readonly Func<string, string?> _clip;
    private readonly Func<string, string?> _preset;
    private readonly Func<string, string?> _tts;
    private readonly Func<string, string?> _reverse;
    private readonly Func<string, int> _durationMs;
    private readonly Func<string, bool> _play;
    private readonly Action _stop;
    private readonly Action<string>? _log;

    /// <summary>The app's live sources.</summary>
    public BackRoomVoice()
        : this(FindPlayerClip, FindPresetClip, RenderTts, ReverseToCache, ProbeDurationMs, PlayThroughApp, StopApp,
               msg => App.Logger?.Debug("BackRoomVoice: {Msg}", msg))
    {
    }

    /// <summary>Seams for the suite: every source and sink the chain reads is passed in.</summary>
    internal BackRoomVoice(Func<string, string?> clip, Func<string, string?> preset, Func<string, string?> tts,
        Func<string, string?> reverse, Func<string, int> durationMs, Func<string, bool> play, Action stop,
        Action<string>? log = null)
    {
        _clip = clip;
        _preset = preset;
        _tts = tts;
        _reverse = reverse;
        _durationMs = durationMs;
        _play = play;
        _stop = stop;
        _log = log;
    }

    // ============================ the chain ============================

    /// <summary>The phrase as the manifest and the clip lookup see it: lower case, punctuation
    /// stripped, runs of anything else collapsed to one space. "Let Go!" and "let  go" are one word.</summary>
    internal static string Normalize(string? text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty
           : NotWord.Replace(text.Trim().ToLowerInvariant(), " ").Trim();

    /// <summary>The preset clip's file stem: the normalised phrase with spaces as hyphens
    /// ("Let Go!" -&gt; <c>let-go</c>, so the file is <c>let-go.mp3</c>).</summary>
    internal static string Slug(string? text) => Normalize(text).Replace(' ', '-');

    /// <summary>Which of the four sources owns this phrase, and the file behind it. Pure given the
    /// lookups, so the suite can hold the order without touching the audio stack.</summary>
    internal static (string Source, string? Path) Resolve(string text,
        Func<string, string?> clip, Func<string, string?> preset, Func<string, string?> tts)
    {
        var key = Normalize(text);
        if (key.Length == 0) return ("none", null);
        if (Try(clip, text) is { } c) return ("clip", c);
        if (Try(preset, key) is { } p) return ("preset", p);
        if (Try(tts, text) is { } t) return ("tts", t);
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
            var (source, path) = Resolve(text, _clip, _preset, _tts);
            if (path == null) return new("none", 0);

            if (reversed)
            {
                string? flipped = null;
                try { flipped = _reverse(path); } catch (Exception ex) { _log?.Invoke("reverse failed (" + ex.GetType().Name + ")"); }
                // A reversal that could not be rendered still plays forwards: the page has already
                // mirrored and reversed the spelling, so the beat is not silent either way.
                if (flipped != null) path = flipped;
            }

            int ms = 0;
            try { ms = _durationMs(path); } catch { ms = 0; }

            // A muted app still owns the word: the source is reported so the page stays quiet
            // instead of shouting the browser voice over a deliberate mute.
            bool played;
            try { played = _play(path); }
            catch (Exception ex) { _log?.Invoke("play failed (" + ex.GetType().Name + ")"); played = false; }

            _log?.Invoke("word spoke " + source + (reversed ? " reversed" : "") + " " + ms + " ms");
            return new(source, played ? ms : 0);
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

    // ============================ 1. the player's own clip ============================

    /// <summary>The phrase's own audio, if the player gave it one. An ENABLED keyword trigger for the
    /// phrase wins (its first enabled PlayAudio action, else the legacy flat field); otherwise the
    /// app's usual linked-audio search (active mod's flashes_audio, then Resources/sub_audio).</summary>
    private static string? FindPlayerClip(string text)
    {
        var key = Normalize(text);
        try
        {
            foreach (var t in App.Settings?.Current?.KeywordTriggers ?? new List<KeywordTrigger>())
            {
                if (t == null || !t.Enabled || Normalize(t.Keyword) != key) continue;
                var fromAction = t.Actions?
                    .OfType<PlayAudioAction>()
                    .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.FilePath))
                    .Select(a => ResolveRelative(a.FilePath!))
                    .FirstOrDefault(File.Exists);
                if (fromAction != null) return fromAction;
                if (!string.IsNullOrWhiteSpace(t.AudioFilePath))
                {
                    var flat = ResolveRelative(t.AudioFilePath!);
                    if (File.Exists(flat)) return flat;
                }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("BackRoomVoice: trigger clip lookup failed ({Type})", ex.GetType().Name); }

        try
        {
            var linked = App.KeywordTriggers?.FindLinkedAudio(text);
            if (!string.IsNullOrWhiteSpace(linked) && File.Exists(linked)) return linked;
        }
        catch (Exception ex) { App.Logger?.Debug("BackRoomVoice: linked audio lookup failed ({Type})", ex.GetType().Name); }
        return null;
    }

    /// <summary>The same rule <c>KeywordTriggerService.ResolveAudioPath</c> applies: rooted paths pass
    /// through, a relative one is looked for under Resources then Resources/sub_audio.</summary>
    private static string ResolveRelative(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var res = ContentLocator.Resolve(Path.Combine("Resources", path));
        if (File.Exists(res)) return res;
        var sub = ContentLocator.Resolve(Path.Combine("Resources", "sub_audio", path));
        return File.Exists(sub) ? sub : path;
    }

    // ============================ 2. the bundled preset clip ============================

    private static readonly object ManifestGate = new();
    private static Dictionary<string, string>? _manifest;
    private static DateTime _manifestAt;

    /// <summary>The words folder inside the install (and inside any content pack that mirrors it).</summary>
    internal static string WordsRoot() => ContentLocator.ResolveDirectory(WordsFolder);

    /// <summary><c>words.json</c> as normalised phrase -&gt; file name, both validated. An unreadable or
    /// absent manifest is an empty map, never a throw. Re-read at most once a minute, so the owner can
    /// drop clips in without a restart.</summary>
    internal static IReadOnlyDictionary<string, string> Manifest()
    {
        lock (ManifestGate)
        {
            if (_manifest != null && DateTime.UtcNow - _manifestAt < TimeSpan.FromMinutes(1)) return _manifest;
            _manifestAt = DateTime.UtcNow;
            _manifest = ReadManifest(Path.Combine(WordsRoot(), ManifestName));
            return _manifest;
        }
    }

    /// <summary>Parse one manifest file. <c>{ "version": 1, "words": { "let go": "let-go.mp3" } }</c>.
    /// Keys are normalised on the way in, so the file may spell a phrase however it likes; a value that
    /// is not a plain file name inside the folder is dropped.</summary>
    internal static Dictionary<string, string> ReadManifest(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(file)) return map;
            var words = JObject.Parse(File.ReadAllText(file))["words"] as JObject;
            if (words == null) return map;
            foreach (var row in words.Properties())
            {
                var key = Normalize(row.Name);
                var name = (row.Value as JValue)?.Value as string;
                if (key.Length == 0 || string.IsNullOrWhiteSpace(name)) continue;
                name = name.Trim();
                if (!SafeFileName.IsMatch(name.ToLowerInvariant()) || name.Contains("..", StringComparison.Ordinal)) continue;
                map[key] = name;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("BackRoomVoice: words.json unreadable ({Type})", ex.GetType().Name); }
        return map;
    }

    /// <summary>The bundled clip for an already normalised phrase, or null.</summary>
    private static string? FindPresetClip(string key)
    {
        if (!Manifest().TryGetValue(key, out var name)) return null;
        var path = Path.Combine(WordsRoot(), name);
        return File.Exists(path) ? path : null;
    }

    // ============================ 3. Windows speech ============================

    /// <summary>Rendered wavs (and reversals) live here, keyed by content, so the second time a word
    /// comes up it starts on the frame the zoom does.</summary>
    internal static string CacheRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-backroom-voice");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CachePath(string kind, string of)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(of))).ToLowerInvariant()[..24];
        return Path.Combine(CacheRoot(), kind + "-" + hash + ".wav");
    }

    /// <summary>Windows speech to a wav in the cache. Soft, slow and female-leaning when the machine has
    /// such a voice; whatever the default is otherwise. Null when the machine has no speech at all.</summary>
    private static string? RenderTts(string text)
    {
        var target = CachePath("tts", TtsRate.ToString("0.00", CultureInfo.InvariantCulture) + "|" + text);
        if (File.Exists(target)) return target;
        try
        {
            using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            var voice = PickVoice();
            if (voice != null) synth.Voice = voice;
            try { synth.Options.SpeakingRate = TtsRate; synth.Options.AudioPitch = TtsPitch; }
            catch (Exception ex) { App.Logger?.Debug("BackRoomVoice: voice options refused ({Type})", ex.GetType().Name); }

            using var stream = synth.SynthesizeTextToStreamAsync(text).AsTask().GetAwaiter().GetResult();
            var size = (uint)stream.Size;
            if (size == 0) return null;
            using var input = stream.GetInputStreamAt(0);
            using var reader = new Windows.Storage.Streams.DataReader(input);
            reader.LoadAsync(size).AsTask().GetAwaiter().GetResult();
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            File.WriteAllBytes(target, bytes);
            return target;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BackRoomVoice: tts unavailable ({Type})", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>A female voice in the machine's own language first, then any female one, then null
    /// (which leaves the system default). Voice names are not logged.</summary>
    private static Windows.Media.SpeechSynthesis.VoiceInformation? PickVoice()
    {
        try
        {
            var all = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices;
            if (all == null || all.Count == 0) return null;
            var tag = Windows.Media.SpeechSynthesis.SpeechSynthesizer.DefaultVoice?.Language ?? "en";
            var lang = tag.Split('-')[0];
            var female = all.Where(v => v.Gender == Windows.Media.SpeechSynthesis.VoiceGender.Female).ToList();
            return female.FirstOrDefault(v => v.Language?.StartsWith(lang, StringComparison.OrdinalIgnoreCase) == true)
                   ?? female.FirstOrDefault();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BackRoomVoice: voice list unavailable ({Type})", ex.GetType().Name);
            return null;
        }
    }

    // ============================ the easter egg: real backwards audio ============================

    /// <summary>The clip decoded, its sample FRAMES written back to front (so stereo stays stereo), and
    /// cached as a wav. Null when the clip cannot be decoded or is longer than the cap.</summary>
    internal static string? ReverseToCache(string source)
    {
        string target;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(source).Ticks.ToString(CultureInfo.InvariantCulture);
            target = CachePath("rev", source + "|" + stamp);
            if (File.Exists(target)) return target;
        }
        catch { return null; }

        using var reader = new AudioFileReader(source);
        int channels = Math.Max(1, reader.WaveFormat.Channels);
        var samples = new List<float>(Math.Min(MaxReverseSamples, 1 << 18));
        var buffer = new float[8192];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < n; i++) samples.Add(buffer[i]);
            if (samples.Count > MaxReverseSamples) return null;
        }
        int frames = samples.Count / channels;
        if (frames < 2) return null;

        var flat = samples.ToArray();
        for (int i = 0, j = frames - 1; i < j; i++, j--)
            for (int c = 0; c < channels; c++)
                (flat[i * channels + c], flat[j * channels + c]) = (flat[j * channels + c], flat[i * channels + c]);

        var format = WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, channels);
        var temp = target + ".part";
        using (var writer = new WaveFileWriter(temp, format)) writer.WriteSamples(flat, 0, frames * channels);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    // ============================ the app's own audio path ============================

    /// <summary>How long the clip runs, 0 when it cannot be read.</summary>
    private static int ProbeDurationMs(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return (int)Math.Clamp(reader.TotalTime.TotalMilliseconds, 0, 60_000);
        }
        catch { return 0; }
    }

    private static readonly object HandleGate = new();
    private static AudioPlaybackHandle? _handle;

    /// <summary>Through <see cref="AudioService.PlayOneShot"/>, at the subliminal whisper's own volume
    /// curve and on the device the audio picker chose. A new word cuts the one before it, which is what
    /// the page's <c>speechSynthesis.cancel()</c> did.</summary>
    private static bool PlayThroughApp(string path)
    {
        StopApp();
        var settings = App.Settings?.Current;
        var master = (settings?.MasterVolume ?? 100) / 100.0f;
        var sub = (settings?.SubAudioVolume ?? 100) / 100.0f;
        var volume = (float)Math.Pow(Math.Clamp(sub * master, 0f, 1f), 1.5);
        if (volume <= 0f) return false;   // a deliberate mute: the host still owns the word, it is just silent
        var handle = App.Audio?.PlayOneShot(path, volume, "br-word");
        lock (HandleGate) _handle = handle;
        return handle != null;
    }

    private static void StopApp()
    {
        AudioPlaybackHandle? h;
        lock (HandleGate) { h = _handle; _handle = null; }
        try { h?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
    }
}
