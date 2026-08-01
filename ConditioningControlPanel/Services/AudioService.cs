using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles audio playback and system audio ducking.
    /// Ported from Python utils.py AudioDucker.
    /// </summary>
    public class AudioService : IDisposable
    {
        #region Fields

        private readonly Dictionary<int, float> _originalVolumes = new();
        private readonly object _lockObj = new();

        private WaveOutEvent? _soundPlayer;
        private AudioFileReader? _soundFile;

        private MMDeviceEnumerator? _deviceEnumerator;
        private int _duckCount; // Reference count — unduck only when all duckers release
        private bool _isDucked;
        private float _duckAmount = 0.8f; // Default: reduce to 20%
        private long _duckGeneration; // Incremented on ForceUnduck to invalidate stale Unduck callbacks

        // Approx end time (DateTime.UtcNow.Ticks; 0 = idle) of the subliminal/flash "whisper" clip that
        // is currently audible. The bark system reads IsWhisperAudioPlaying off this so the companion
        // doesn't talk over a whisper. Written from off-UI playback threads, read from the bark gate —
        // accessed via Interlocked so the two stay consistent.
        private long _whisperBusyUntilTicks;

        private System.Threading.Timer? _duckWatchdog; // Safety net: force-unduck if ducking exceeds max duration
        private const int DuckWatchdogMs = 300_000; // 5 minutes — safety net for leaked duck refs, must exceed longest video

        // Periodic re-enumeration during duck window — catches sessions that appear AFTER Duck() ran
        // (e.g. Discord notification sound, Steam friend ping). Without this they play un-ducked.
        private System.Threading.Timer? _duckRescanTimer;
        private const int DuckRescanIntervalMs = 2_500;

        // Sessions that couldn't be restored at Unduck time (PID gone, app silent). Retried on a timer
        // so when the app starts producing audio again its volume gets restored — fixes "stuck at 0%".
        private readonly Dictionary<int, float> _pendingRestores = new();
        private readonly Dictionary<int, string> _processNames = new(); // PID -> name, for fallback matching
        private System.Threading.Timer? _restoreRetryTimer;
        private DateTime _restoreRetryDeadline;
        private const int RestoreRetryIntervalMs = 5_000;
        private static readonly TimeSpan RestoreRetryWindow = TimeSpan.FromMinutes(3);

        // Upper bound for the "how many files are in this folder" diagnostic lines. A full recursive
        // walk of Resources/sounds (~5200 files) is far too slow to run on the UI thread (#686), and
        // the exact number never mattered — we only want to know the folder isn't empty.
        private const int FileCountProbeCap = 500;

        private bool _disposed;

        // ── Serialized WASAPI sweep worker (#750-#753) ────────────────────────────────────────
        // Duck()/Unduck() are called from the UI thread (VideoService.PlayVideo, CloseAll) and used
        // to run the whole endpoint/session enumeration inline, under _lockObj. That enumeration is
        // unbounded work: one reporter's machine has 12 render endpoints and a single misbehaving
        // one blocked the dispatcher for 20+ seconds the instant a mandatory video started. Every
        // sweep now runs here instead, on ONE thread, in submission order — so a Duck immediately
        // followed by an Unduck still nets out correctly — while the callers return immediately.
        private readonly BlockingCollection<Action> _duckWork = new();
        private Thread? _duckWorker;
        private int _duckWorkerStarted;
        private int _duckWorkDepth;                                  // queued + in-flight sweeps
        private readonly ManualResetEventSlim _duckWorkIdle = new(true);
        private const int WorkerFlushTimeoutMs = 4_000;              // app-exit restore budget
        private const int SlowSweepWarnMs = 2_000;

        // The sweeps' own device enumerator, created ON the worker thread. MMDeviceEnumerator is a
        // ThreadingModel=Both COM object, so one created on the UI thread lives in that STA and every
        // property read from another thread would marshal straight back onto the dispatcher — the
        // exact block this worker exists to remove. _deviceEnumerator stays for the UI-side lookups.
        private MMDeviceEnumerator? _sweepEnumerator;

        // Cached WebView2 process IDs to avoid slow Process.GetProcessById() on every duck
        private HashSet<int> _webView2Pids = new();
        private DateTime _webView2PidsCacheTime = DateTime.MinValue;
        private static readonly TimeSpan WebView2CacheExpiry = TimeSpan.FromSeconds(30);

        // Discovered working WaveOut device number. -1 means use WAVE_MAPPER (NAudio default).
        // We learn an explicit number only if WAVE_MAPPER refuses to open ("BadDeviceId"),
        // which happens on systems where the default audio endpoint is a virtual device that
        // can't service waveOutOpen — see bug #181. Once a working number is found it's
        // reused for the session so we don't re-enumerate on every PlaySound call.
        private int _workingWaveOutDeviceNumber = -1;
        private bool _waveOutPermanentlyUnavailable;

        // Cache of the resolved WaveOut device number for the user's chosen MMDevice
        // (Settings.AudioOutputDeviceId). Invalidated by InvalidateOutputDeviceCache().
        // -2 = not yet resolved, -1 = default (no override), >=0 = explicit waveOut driver number.
        private int _userWaveOutDeviceNumber = -2;
        private string _resolvedForDeviceId = "";

        /// <summary>
        /// Constructs a <see cref="WaveOutEvent"/>, transparently retrying with explicit
        /// device numbers if the default WAVE_MAPPER refuses to open. Honors the user's
        /// chosen output device (Settings.AudioOutputDeviceId) when set. Returns null only
        /// if no device on the system will accept waveOutOpen — in which case the user's
        /// audio stack is broken in a way we can't paper over.
        ///
        /// Public so other services can route their audio to the same chosen device.
        /// </summary>
        public WaveOutEvent? CreateWaveOut()
        {
            if (_waveOutPermanentlyUnavailable) return null;

            // Resolve the preferred device number — user choice overrides the bug-#181 fallback.
            int preferred = ResolvePreferredWaveOutDeviceNumber();

            try
            {
                var w = new WaveOutEvent();
                if (preferred >= 0) w.DeviceNumber = preferred;
                return w;
            }
            catch (NAudio.MmException ex) when (string.Equals(ex.Result.ToString(), "BadDeviceId", StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Warning("AudioService: WaveOut device #{Num} returned BadDeviceId, scanning for a usable fallback", preferred);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AudioService: WaveOut construction failed (device #{Num})", preferred);
                return null;
            }

            // Fallback: enumerate explicit device numbers and pick the first one that opens.
            int count;
            try { count = WaveOut.DeviceCount; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                if (i == preferred) continue; // already failed above
                try
                {
                    var w = new WaveOutEvent { DeviceNumber = i };
                    _workingWaveOutDeviceNumber = i;
                    App.Logger?.Information("AudioService: using WaveOut device #{Num} as fallback", i);
                    return w;
                }
                catch
                {
                    // try next
                }
            }

            _waveOutPermanentlyUnavailable = true;
            App.Logger?.Warning("AudioService: no WaveOut device on this system would accept waveOutOpen ({Count} candidates tried). Audio playback disabled this session.", count);
            return null;
        }

        #region Output Device Selection (user-chosen routing)

        /// <summary>
        /// Apply the user's preferred output device to a <see cref="WaveOutEvent"/> created
        /// elsewhere. No-op if no device is chosen or the chosen device can't be resolved.
        /// Must be called BEFORE Init() — DeviceNumber can't change once Init runs.
        /// </summary>
        public void ApplyPreferredDevice(WaveOutEvent waveOut)
        {
            if (waveOut == null) return;
            try
            {
                var num = ResolvePreferredWaveOutDeviceNumber();
                if (num >= 0) waveOut.DeviceNumber = num;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ApplyPreferredDevice(WaveOut): {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Apply the user's preferred output device to a LibVLC <see cref="LibVLCSharp.Shared.MediaPlayer"/>.
        /// LibVLC's mmdevice aout backend accepts the same MMDevice ID strings we use here.
        /// Returns true only when the device was actually applied.
        /// </summary>
        /// <remarks>
        /// MUST be called once the player is LIVE (from its Playing handler, or otherwise after
        /// Play() has started) — never at MediaPlayer-construction time. LibVLC does not create
        /// an audio output until playback begins, and <c>AudioOutputDeviceEnum</c> is empty until
        /// one exists, so a pre-Play call validated against an empty list, applied nothing and
        /// left playback on the Windows default endpoint while every NAudio path honoured the
        /// user's choice — "video plays, video has no sound, everything else is audible"
        /// (#707/#708). Idempotent: safe to call again if Playing fires more than once.
        ///
        /// Validates the stored device against the player's enumerated outputs before
        /// applying. LibVLC's SetOutputDevice silently routes audio to nowhere when the
        /// ID no longer matches a present endpoint (USB headset unplugged, default
        /// changed, driver reinstall), so silently calling it on a stale ID produces
        /// mute video playback while everything else (NAudio paths) auto-fall-back to
        /// the default device — symptom matching #270/#251/#244/#243 cluster.
        /// </remarks>
        public bool ApplyPreferredDevice(LibVLCSharp.Shared.MediaPlayer player)
        {
            if (player == null) return false;
            try
            {
                var deviceId = App.Settings?.Current?.AudioOutputDeviceId;
                if (string.IsNullOrEmpty(deviceId)) return false;

                // Already routed there — nothing to do (and no aout restart to provoke).
                try
                {
                    if (string.Equals(player.OutputDevice, deviceId, StringComparison.Ordinal)) return true;
                }
                catch { /* OutputDevice is advisory — fall through and apply as usual */ }

                bool deviceFound = false;
                int enumerated = 0;
                try
                {
                    var available = player.AudioOutputDeviceEnum;
                    if (available != null)
                    {
                        foreach (var d in available)
                        {
                            enumerated++;
                            if (string.Equals(d.DeviceIdentifier, deviceId, StringComparison.Ordinal))
                            {
                                deviceFound = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception enumEx)
                {
                    // Enumeration failed, so the saved ID CANNOT be validated. Applying it blind is
                    // exactly the "routes audio to nowhere" case the remarks above warn about, and
                    // silence is worse than playing out of the default endpoint — bail out instead.
                    App.Logger?.Debug("ApplyPreferredDevice(MediaPlayer): enumeration failed, leaving the system default in place: {Error}", enumEx.Message);
                    return false;
                }

                if (!deviceFound)
                {
                    App.Logger?.Warning("ApplyPreferredDevice(MediaPlayer): saved device {Id} not present in LibVLC outputs ({Count} enumerated) — using system default. Reselect in Settings → Audio if you want this routed elsewhere.", deviceId, enumerated);
                    return false;
                }

                player.SetOutputDevice(deviceId);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ApplyPreferredDevice(MediaPlayer): {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// One concise line describing where a LIVE LibVLC player's audio is actually going.
        /// Meaningless before the Playing event: with no aout every field reads as "requested"
        /// rather than "resolved", which is why the old post-Play "Volume=99, Mute=false" line
        /// proved nothing about audibility.
        /// </summary>
        /// <remarks>
        /// Exists to separate the two silent-video causes that look identical from the outside
        /// (#707/#708): a wrong endpoint (resolved= an ID that isn't the user's speakers) versus
        /// LibVLC falling back to the adummy output because mmdevice/directsound/waveout all
        /// failed to bind (resolved=(none) with outputs=0 while a track is selected).
        /// </remarks>
        public string DescribeLibVlcAudioRouting(LibVLCSharp.Shared.MediaPlayer player)
        {
            if (player == null) return "player=(null)";

            string wanted = "(system default)", resolved = "(unavailable)";
            string outputs = "?", track = "?", vol = "?", mute = "?";
            try { var id = App.Settings?.Current?.AudioOutputDeviceId; if (!string.IsNullOrEmpty(id)) wanted = id; } catch { }
            try { resolved = player.OutputDevice ?? "(none)"; } catch { }
            try { outputs = (player.AudioOutputDeviceEnum?.Count() ?? 0).ToString(); } catch { }
            try { track = player.AudioTrack + "/" + player.AudioTrackCount; } catch { }
            try { vol = player.Volume.ToString(); } catch { }
            try { mute = player.Mute.ToString(); } catch { }

            return $"wanted={wanted} resolved={resolved} outputs={outputs} track={track} vol={vol} mute={mute}";
        }

        /// <summary>
        /// Returns the resolved WaveOut driver number for the user's chosen output device,
        /// or -1 if no device is chosen / nothing matched (use WAVE_MAPPER).
        /// Result is cached until <see cref="InvalidateOutputDeviceCache"/> is called.
        /// </summary>
        public int ResolvePreferredWaveOutDeviceNumber()
        {
            var settings = App.Settings?.Current;
            var chosenId = settings?.AudioOutputDeviceId ?? "";

            if (string.IsNullOrEmpty(chosenId))
            {
                // No user choice — fall back to bug-#181 cache (default device or learned fallback).
                return _workingWaveOutDeviceNumber;
            }

            // Cache hit?
            if (_userWaveOutDeviceNumber != -2 && _resolvedForDeviceId == chosenId)
                return _userWaveOutDeviceNumber;

            _resolvedForDeviceId = chosenId;
            _userWaveOutDeviceNumber = -1;

            // Map MMDevice ID -> friendly name -> WaveOut driver number.
            // The two enumerations don't share IDs; matching is by product name (truncated to 32
            // chars by the legacy MM API, so we compare with a tolerant prefix/contains match).
            string? friendly = null;
            try
            {
                if (_deviceEnumerator != null)
                {
                    var dev = _deviceEnumerator.GetDevice(chosenId);
                    friendly = dev?.FriendlyName;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not look up MMDevice {Id}: {Error}", chosenId, ex.Message);
            }

            // Fallback to persisted name if MMDevice lookup failed (device may have moved IDs
            // across reboots / driver reinstall — we re-resolve by friendly name).
            if (string.IsNullOrEmpty(friendly))
                friendly = settings?.AudioOutputDeviceName ?? "";

            if (string.IsNullOrEmpty(friendly))
                return _userWaveOutDeviceNumber;

            // FriendlyName format from MMDevice is usually "Speakers (Realtek...)" — extract the
            // bracketed driver name as the highest-quality match key, then fall back to the full string.
            string driverName = friendly;
            int open = friendly.IndexOf('(');
            int close = friendly.LastIndexOf(')');
            if (open >= 0 && close > open) driverName = friendly.Substring(open + 1, close - open - 1).Trim();

            int count;
            try { count = WaveOut.DeviceCount; } catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var caps = WaveOut.GetCapabilities(i);
                    var product = caps.ProductName ?? "";
                    if (product.Length == 0) continue;

                    // WaveOut product names are truncated to 31 chars. Match by prefix in either
                    // direction so the truncation doesn't cause false negatives.
                    if (driverName.StartsWith(product, StringComparison.OrdinalIgnoreCase) ||
                        product.StartsWith(driverName, StringComparison.OrdinalIgnoreCase) ||
                        friendly.IndexOf(product, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _userWaveOutDeviceNumber = i;
                        App.Logger?.Information("AudioService: routing WaveOut to device #{Num} '{Product}' (matches MMDevice '{Friendly}')",
                            i, product, friendly);
                        return i;
                    }
                }
                catch { /* skip unreadable device */ }
            }

            App.Logger?.Warning("AudioService: chosen output device '{Friendly}' has no matching WaveOut driver — using default", friendly);
            return _userWaveOutDeviceNumber;
        }

        /// <summary>
        /// Drop the cached WaveOut device-number resolution. Call after the user changes
        /// the output device setting so the next playback re-resolves.
        /// </summary>
        public void InvalidateOutputDeviceCache()
        {
            _userWaveOutDeviceNumber = -2;
            _resolvedForDeviceId = "";
            // Also reset the bug-#181 fallback cache so a fresh default-device resolution happens
            // (the user may have changed the system default along with the CCP-specific choice).
            _workingWaveOutDeviceNumber = -1;
            _waveOutPermanentlyUnavailable = false;
        }

        /// <summary>
        /// Output device descriptor for the picker UI.
        /// </summary>
        public sealed class AudioOutputDevice
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsDefault { get; set; }
            public override string ToString() => IsDefault ? Name + " (default)" : Name;
        }

        /// <summary>
        /// Enumerate active playback endpoints for the device picker. The first entry is
        /// always the synthetic "System default" choice (empty ID).
        /// </summary>
        public List<AudioOutputDevice> EnumerateOutputDevices()
        {
            var list = new List<AudioOutputDevice>
            {
                new AudioOutputDevice { Id = "", Name = "System default", IsDefault = true }
            };

            try
            {
                _deviceEnumerator ??= new MMDeviceEnumerator();
                var defaultId = "";
                try
                {
                    var def = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultId = def?.ID ?? "";
                }
                catch { /* default may be unset */ }

                var endpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < endpoints.Count; i++)
                {
                    try
                    {
                        var dev = endpoints[i];
                        list.Add(new AudioOutputDevice
                        {
                            Id = dev.ID ?? "",
                            Name = dev.FriendlyName ?? "(unnamed)",
                            IsDefault = !string.IsNullOrEmpty(defaultId) && dev.ID == defaultId
                        });
                    }
                    catch { /* skip */ }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("EnumerateOutputDevices failed: {Error}", ex.Message);
            }

            return list;
        }

        #endregion

        // Crash recovery file for ducking state
        private static readonly string DuckingRecoveryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel", "ducking_recovery.json");

        #endregion

        #region Constructor

        public AudioService()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();

                // Check for crash recovery - restore volumes if app was killed while ducked
                RecoverFromCrash();

                App.Logger?.Information("Audio service initialized with ducking support");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio ducking not available: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Run audio health diagnostics on startup. Logs warnings for any issues found.
        /// Call after construction to verify audio subsystem is functional.
        /// </summary>
        public void RunStartupDiagnostics()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
                var subAudioDir = Path.Combine(baseDir, "Resources", "sub_audio");

                // Check sound directories exist and have files
                if (!Directory.Exists(soundsDir))
                    App.Logger?.Warning("[AudioDiag] Resources/sounds/ directory is MISSING at {Path}", soundsDir);
                else
                {
                    var soundCount = Directory.GetFiles(soundsDir, "*.*", SearchOption.AllDirectories).Length;
                    if (soundCount == 0)
                        App.Logger?.Warning("[AudioDiag] Resources/sounds/ directory exists but contains NO audio files");
                    else
                        App.Logger?.Information("[AudioDiag] Resources/sounds/: {Count} files found", soundCount);
                }

                if (!Directory.Exists(subAudioDir))
                    App.Logger?.Warning("[AudioDiag] Resources/sub_audio/ directory is MISSING at {Path}", subAudioDir);
                else
                {
                    var subCount = Directory.GetFiles(subAudioDir, "*.*").Length;
                    if (subCount == 0)
                        App.Logger?.Warning("[AudioDiag] Resources/sub_audio/ directory exists but contains NO audio files");
                    else
                        App.Logger?.Information("[AudioDiag] Resources/sub_audio/: {Count} files found", subCount);
                }

                // Check WaveOutEvent can be created (tests audio device availability).
                // CreateWaveOut transparently falls back to explicit device numbers if
                // WAVE_MAPPER refuses (bug #181), so this also primes the cached working
                // device number for subsequent PlaySound calls.
                using (var testDevice = CreateWaveOut())
                {
                    if (testDevice != null)
                        App.Logger?.Information("[AudioDiag] WaveOutEvent: OK (audio device available)");
                    else
                        App.Logger?.Warning("[AudioDiag] WaveOutEvent FAILED — no audio output device on this system accepts waveOutOpen. Check Windows audio settings, default device, and that the audio service is running.");
                }

                // Log current audio settings for diagnosis
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    App.Logger?.Information("[AudioDiag] Settings: MasterVolume={Master}%, SubAudioEnabled={SubEnabled}, SubAudioMuted={SubMuted}, SubAudioVolume={SubVol}%, FlashAudioEnabled={FlashEnabled}, AudioDuckingEnabled={DuckEnabled}",
                        settings.MasterVolume, settings.SubAudioEnabled, settings.SubAudioMuted, settings.SubAudioVolume, settings.FlashAudioEnabled, settings.AudioDuckingEnabled);

                    if (settings.MasterVolume == 0)
                        App.Logger?.Warning("[AudioDiag] MasterVolume is 0% — ALL audio will be silent");
                    if (!settings.SubAudioEnabled)
                        App.Logger?.Information("[AudioDiag] SubAudioEnabled is OFF — whisper/trigger audio will not play");
                    else if (settings.SubAudioMuted)
                        App.Logger?.Information("[AudioDiag] Whispers are MUTED (SubAudioMuted) — enabled but silenced by the user");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[AudioDiag] Diagnostics failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Play a short test sound to verify audio output is working.
        /// Returns a diagnostic message string.
        /// </summary>
        public string TestAudioPlayback()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
            var subAudioDir = Path.Combine(baseDir, "Resources", "sub_audio");

            var diagnostics = new System.Text.StringBuilder();
            diagnostics.AppendLine("=== Audio Diagnostics ===");

            // Check directories
            if (!Directory.Exists(soundsDir))
                diagnostics.AppendLine("WARNING: Resources/sounds/ directory MISSING");
            else
            {
                var count = CountFilesBounded(soundsDir, SearchOption.AllDirectories);
                diagnostics.AppendLine($"Resources/sounds/: {FormatProbeCount(count)} files");
            }

            if (!Directory.Exists(subAudioDir))
                diagnostics.AppendLine("WARNING: Resources/sub_audio/ directory MISSING");
            else
            {
                var count = CountFilesBounded(subAudioDir, SearchOption.TopDirectoryOnly);
                diagnostics.AppendLine($"Resources/sub_audio/: {FormatProbeCount(count)} files");
            }

            // Check audio device — CreateWaveOut falls back to explicit device numbers
            // if the default WAVE_MAPPER refuses (bug #181).
            using (var testDevice = CreateWaveOut())
            {
                if (testDevice == null)
                {
                    diagnostics.AppendLine("Audio device: FAILED (no output device on this system accepts waveOutOpen — check Windows audio settings, default playback device, and that the Windows Audio service is running)");
                    return diagnostics.ToString();
                }
                diagnostics.AppendLine($"Audio device: OK (device #{testDevice.DeviceNumber})");
            }

            // Try to play a sound
            var testFiles = new[]
            {
                Path.Combine(soundsDir, "chime1.mp3"),
                Path.Combine(soundsDir, "lvup.mp3"),
                Path.Combine(soundsDir, "bubbles", "Pop.mp3"),
            };

            string? playFile = null;
            foreach (var f in testFiles)
            {
                if (File.Exists(f)) { playFile = f; break; }
            }

            if (playFile == null)
            {
                diagnostics.AppendLine("WARNING: No test sound files found to play");
                return diagnostics.ToString();
            }

            try
            {
                StopSound();
                _soundFile = new AudioFileReader(playFile);
                _soundPlayer = CreateWaveOut();
                if (_soundPlayer == null)
                {
                    diagnostics.AppendLine("Playback FAILED: no usable audio output device (check Windows default playback device and audio service)");
                }
                else
                {
                    _soundFile.Volume = 0.5f; // Fixed 50% for test — bypasses curve
                    _soundPlayer.Init(_soundFile);
                    _soundPlayer.Play();
                    diagnostics.AppendLine($"Playing: {Path.GetFileName(playFile)} at 50% volume (device #{_soundPlayer.DeviceNumber})");
                    diagnostics.AppendLine("If you can't hear this, check Windows volume mixer.");
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"Playback FAILED: {ex.Message}");
            }

            // Log settings
            var s = App.Settings?.Current;
            if (s != null)
            {
                diagnostics.AppendLine($"\nMaster Volume: {s.MasterVolume}%");
                diagnostics.AppendLine($"Whispers Enabled: {s.SubAudioEnabled}");
                diagnostics.AppendLine($"Whispers Muted: {s.SubAudioMuted}");
                diagnostics.AppendLine($"Whisper Volume: {s.SubAudioVolume}%");
                diagnostics.AppendLine($"Flash Audio Enabled: {s.FlashAudioEnabled}");
                var effectiveWhisperVol = Math.Pow((s.SubAudioVolume / 100.0) * (s.MasterVolume / 100.0), 1.5) * 100;
                diagnostics.AppendLine($"Effective Whisper Volume: {effectiveWhisperVol:F1}%");
            }

            return diagnostics.ToString();
        }

        /// <summary>
        /// Counts files under <paramref name="dir"/>, stopping at <see cref="FileCountProbeCap"/>.
        /// Diagnostic only — never walk the whole tree here (#686).
        /// </summary>
        internal static int CountFilesBounded(string dir, SearchOption option)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*.*", option).Take(FileCountProbeCap).Count();
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatProbeCount(int count)
            => count >= FileCountProbeCap ? $"{count}+" : count.ToString();

        #endregion

        #region Crash Recovery

        /// <summary>
        /// Check if the app crashed while audio was ducked and restore volumes. The file probe is
        /// cheap and stays inline; the restore itself is a full render-endpoint sweep, so it goes on
        /// the worker like every other sweep — it used to run on the UI thread inside OnStartup.
        /// </summary>
        private void RecoverFromCrash()
        {
            try
            {
                if (!File.Exists(DuckingRecoveryFile)) return;
                EnqueueDuckWork("crash-recovery", RecoverFromCrashSweep);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not schedule ducking crash recovery: {Error}", ex.Message);
            }
        }

        /// <summary>Worker-thread half of <see cref="RecoverFromCrash"/>.</summary>
        private void RecoverFromCrashSweep()
        {
            try
            {
                if (!File.Exists(DuckingRecoveryFile)) return;

                App.Logger?.Information("Detected ducking recovery file - restoring audio from previous crash");

                var json = File.ReadAllText(DuckingRecoveryFile);
                var savedVolumes = JsonConvert.DeserializeObject<Dictionary<int, float>>(json);

                if (savedVolumes != null && savedVolumes.Count > 0)
                {
                    using var scope = CollectRenderSessions();
                    var sessions = scope.Sessions;

                    int restoredCount = 0;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;

                            if (savedVolumes.TryGetValue(processId, out var originalVolume))
                            {
                                session.SimpleAudioVolume.Volume = originalVolume;
                                restoredCount++;
                            }
                        }
                        catch { /* Session may have ended */ }
                    }

                    App.Logger?.Information("Restored {Count} audio sessions from crash recovery", restoredCount);
                }

                // Delete recovery file after restore
                File.Delete(DuckingRecoveryFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to recover ducking state: {Error}", ex.Message);
                // Try to delete the file anyway to avoid repeated failures
                try { File.Delete(DuckingRecoveryFile); } catch { }
            }
        }

        /// <summary>
        /// Save current ducking state for crash recovery. Snapshots under the lock and writes
        /// outside it — this runs on the sweep worker, where the file I/O must not pin _lockObj.
        /// </summary>
        private void SaveDuckingState()
        {
            try
            {
                Dictionary<int, float> snapshot;
                lock (_lockObj) { snapshot = new Dictionary<int, float>(_originalVolumes); }

                var dir = Path.GetDirectoryName(DuckingRecoveryFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(snapshot);
                File.WriteAllText(DuckingRecoveryFile, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to save ducking state: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Clear crash recovery file (called on successful unduck).
        /// </summary>
        private void ClearDuckingState()
        {
            try
            {
                if (File.Exists(DuckingRecoveryFile))
                    File.Delete(DuckingRecoveryFile);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to clear ducking state: {Error}", ex.Message);
            }
        }

        #endregion

        #region Sound Playback

        /// <summary>
        /// Play a sound effect with volume control
        /// </summary>
        public double PlaySound(string path, int volumePercent)
        {
            try
            {
                StopSound();
                
                if (!File.Exists(path))
                {
                    App.Logger?.Debug("Sound file not found: {Path}", path);
                    return 0;
                }

                _soundFile = new AudioFileReader(path);
                _soundPlayer = CreateWaveOut();
                if (_soundPlayer == null)
                {
                    _soundFile.Dispose();
                    _soundFile = null;
                    return 0;
                }

                // Apply volume curve (gentler, minimum 5%)
                var volume = volumePercent / 100.0f;
                var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume, 1.5));
                _soundFile.Volume = curvedVolume;

                _soundPlayer.Init(_soundFile);
                _soundPlayer.Play();
                
                var duration = _soundFile.TotalTime.TotalSeconds;
                App.Logger?.Debug("Playing sound: {Path}, duration: {Duration}s", Path.GetFileName(path), duration);
                
                return duration;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Could not play sound {Path}: {Error}", path, ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Stop currently playing sound
        /// </summary>
        public void StopSound()
        {
            try
            {
                _soundPlayer?.Stop();
                _soundPlayer?.Dispose();
                _soundFile?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Error stopping sound: {Error}", ex.Message);
            }

            _soundPlayer = null;
            _soundFile = null;
        }

        #endregion

        #region Audio Ducking

        /// <summary>
        /// Current duck generation — capture this when calling Duck() and pass to Unduck() to avoid stale callbacks.
        /// </summary>
        public long DuckGeneration
        {
            get { lock (_lockObj) { return _duckGeneration; } }
        }

        /// <summary>
        /// True while subliminal/flash "whisper" audio is (approximately) still playing. The bark system
        /// consults this so the companion doesn't speak over a whisper. Approximate: based on the clip
        /// duration captured at play time, not a real playback-stopped callback.
        /// </summary>
        public bool IsWhisperAudioPlaying
        {
            get
            {
                var until = System.Threading.Interlocked.Read(ref _whisperBusyUntilTicks);
                return until > 0 && DateTime.UtcNow.Ticks < until;
            }
        }

        /// <summary>
        /// Record that a whisper clip of <paramref name="durationSeconds"/> just started so
        /// <see cref="IsWhisperAudioPlaying"/> reports busy until it finishes (+ a short tail). Called by
        /// the subliminal/flash whisper playback paths. Only ever EXTENDS the busy window — a shorter
        /// concurrent clip can't cut a longer one short. No-op for non-positive/NaN durations.
        /// </summary>
        public void MarkWhisperAudio(double durationSeconds)
        {
            if (double.IsNaN(durationSeconds) || durationSeconds <= 0) return;
            var until = DateTime.UtcNow.AddSeconds(durationSeconds + 0.25).Ticks;
            long prev;
            do { prev = System.Threading.Interlocked.Read(ref _whisperBusyUntilTicks); }
            while (until > prev &&
                   System.Threading.Interlocked.CompareExchange(ref _whisperBusyUntilTicks, until, prev) != prev);
        }

        /// <summary>
        /// Lower the volume of other applications. Returns immediately: only the ref count, the
        /// duck flag and the two timers are touched here — the WASAPI sweep that actually moves the
        /// sliders is queued onto the serialized worker (#750-#753).
        /// </summary>
        /// <param name="strength">0-100 (0 = no ducking, 100 = full mute)</param>
        public void Duck(int strength = 80)
        {
            // Don't duck if master volume is 0% - nothing to play anyway
            if ((App.Settings?.Current?.MasterVolume ?? 100) == 0) return;

            if (_deviceEnumerator == null) return;

            float amount;
            lock (_lockObj)
            {
                _duckCount++;
                if (_isDucked) return; // Already ducked — just bump the ref count

                _duckAmount = Math.Clamp(strength, 0, 100) / 100.0f;
                amount = _duckAmount;

                // "Ducked" is a LOGICAL state decided synchronously, before the sweep has run.
                // Deciding it from the sweep's outcome instead would let an Unduck that arrives
                // while the sweep is still queued see !_isDucked and skip the restore entirely.
                _isDucked = true;

                // Watchdog: force-unduck if ducking exceeds max duration.
                // Catches leaked ref counts from cancelled Task.Delay callbacks,
                // missing Unduck on audio failure, etc. Repeats so a single failed sweep
                // still gets another attempt; it retires itself once nothing is ducked.
                _duckWatchdog?.Dispose();
                _duckWatchdog = new System.Threading.Timer(_ => DuckWatchdogTick(),
                    null, DuckWatchdogMs, DuckWatchdogMs);

                // Rescan for new sessions during the duck window — without this, a Discord
                // notification or Steam ping that creates an audio session AFTER Duck() ran
                // plays at full volume.
                _duckRescanTimer?.Dispose();
                _duckRescanTimer = new System.Threading.Timer(_ => RescanForNewSessions(),
                    null, DuckRescanIntervalMs, DuckRescanIntervalMs);
            }

            // Our layered-audio mixer is in-process, so the session-based ducker (which skips our
            // own PID) can't touch it — duck it cooperatively instead. (#659) In-process gain
            // change, no COM, so it stays inline and our own layers dip on the same frame.
            try { App.LayeredAudio?.ApplyDuck(amount); } catch { }

            EnqueueDuckWork("duck", () => ApplyDuckSweep(amount, strength), trace: true);
        }

        /// <summary>
        /// Worker-thread half of <see cref="Duck"/>: walk every render endpoint, remember each
        /// session's original volume and lower it. Never touches _lockObj across a COM call.
        /// </summary>
        private void ApplyDuckSweep(float amount, int strength)
        {
            var currentProcessId = Environment.ProcessId;
            var excludeWebView2 = App.Settings?.Current?.ExcludeBambiCloudFromDucking ?? true;
            var webViewPids = excludeWebView2 ? RefreshWebView2Pids() : new HashSet<int>();

            int ducked = 0;
            using (var scope = CollectRenderSessions())
            {
                var sessions = scope.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;

                        // Skip our own process
                        if (processId == currentProcessId || processId == 0) continue;

                        // Skip WebView2 processes if setting is enabled (for BambiCloud audio)
                        if (excludeWebView2 && webViewPids.Contains(processId))
                            continue;

                        var currentVolume = session.SimpleAudioVolume.Volume;

                        // Store original volume + process name (for fallback matching at restore time
                        // if PID changes — e.g. user closes and reopens Firefox during the session).
                        lock (_lockObj)
                        {
                            if (!_isDucked) return;  // unducked out from under us — leave the rest alone
                            // Never overwrite a surviving entry: after a restore that threw, the
                            // stored value is the TRUE original and the live one is already ducked.
                            // Recapturing it here is how volumes used to ratchet toward 0%.
                            if (_originalVolumes.ContainsKey(processId)) continue;
                            _originalVolumes[processId] = currentVolume;
                        }
                        var name = TryGetProcessName(processId);
                        lock (_lockObj) { _processNames[processId] = name; }

                        // Calculate ducked volume
                        var newVolume = currentVolume * (1.0f - amount);
                        session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        ducked++;
                    }
                    catch (Exception ex)
                    {
                        // Session may have ended
                        App.Logger?.Debug("Failed to duck audio session: {Error}", ex.Message);
                    }
                }
            }

            // Save state for crash recovery
            SaveDuckingState();

            App.Logger?.Debug("Ducked {Count} audio sessions by {Amount}%", ducked, strength);
        }

        /// <summary>
        /// Refresh the WebView2 PID cache if it has expired. Walks the whole process table, so it
        /// only ever runs on the sweep worker. Returns the current set (never null).
        /// </summary>
        private HashSet<int> RefreshWebView2Pids()
        {
            lock (_lockObj)
            {
                if (DateTime.UtcNow - _webView2PidsCacheTime <= WebView2CacheExpiry)
                    return _webView2Pids;
            }

            try
            {
                var newPids = new HashSet<int>();
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var name = proc.ProcessName.ToLowerInvariant();
                        if (name.Contains("msedgewebview2") || name.Contains("webview2"))
                            newPids.Add(proc.Id);
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
                lock (_lockObj)
                {
                    _webView2Pids = newPids;
                    _webView2PidsCacheTime = DateTime.UtcNow;
                }
                return newPids;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to refresh WebView2 PID cache: {Error}", ex.Message);
                lock (_lockObj) { return _webView2Pids; }
            }
        }

        /// <summary>
        /// Restore the original volume of other applications. Returns immediately — like
        /// <see cref="Duck"/>, only the flags move here and the sweep is queued behind whatever
        /// duck/rescan work is already in flight, so ordering is preserved.
        /// Pass the generation from DuckGeneration captured at Duck() time to prevent stale callbacks
        /// from interfering with newer ducking sessions.
        /// </summary>
        /// <param name="generation">Duck generation to validate against. Pass -1 to skip generation check (legacy callers).</param>
        public void Unduck(long generation = -1)
        {
            bool restore = false;
            lock (_lockObj)
            {
                // If a generation was specified and doesn't match current, this is a stale callback — ignore
                if (generation >= 0 && generation != _duckGeneration)
                {
                    App.Logger?.Debug("Ignoring stale Unduck (gen {Old} vs current {Current})", generation, _duckGeneration);
                    return;
                }

                if (!_isDucked || _deviceEnumerator == null)
                {
                    _duckCount = Math.Max(0, _duckCount - 1);
                    return;
                }

                _duckCount = Math.Max(0, _duckCount - 1);
                if (_duckCount > 0) return; // Other consumers still need ducking

                _isDucked = false;
                _duckWatchdog?.Dispose();
                _duckWatchdog = null;
                // #686: the rescan timer MUST die here. A survivor re-ducks every audio session on
                // the machine every 2.5s forever ("all audio stopped").
                _duckRescanTimer?.Dispose();
                _duckRescanTimer = null;
                restore = true;
            }

            try { App.LayeredAudio?.ReleaseDuck(); } catch { } // restore layered-audio gain (#659)

            if (restore) EnqueueDuckWork("unduck", RestoreDuckedVolumes, trace: true);
        }

        /// <summary>
        /// Restores the volumes captured at Duck() time. Runs ONLY on the sweep worker: it walks
        /// every render endpoint, which is exactly the unbounded COM work that must never sit on the
        /// UI thread (#750-#753 — one trace died inside this call, reached from CloseAll's finally).
        /// The lock is taken only to snapshot the captured volumes and to write the result back.
        /// </summary>
        private void RestoreDuckedVolumes()
        {
            Dictionary<int, float> wanted;
            Dictionary<int, string> names;
            lock (_lockObj)
            {
                wanted = new Dictionary<int, float>(_originalVolumes);
                names = new Dictionary<int, string>(_processNames);
            }

            var restored = new HashSet<int>();
            try
            {
                using var scope = CollectRenderSessions();
                var sessions = scope.Sessions;

                var nameToCurrentPid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;

                        if (wanted.TryGetValue(processId, out var originalVolume))
                        {
                            session.SimpleAudioVolume.Volume = originalVolume;
                            restored.Add(processId);
                        }
                        else
                        {
                            // Build a name-index for fallback restoration of PIDs that no longer exist
                            var name = TryGetProcessName(processId);
                            if (!string.IsNullOrEmpty(name) && !nameToCurrentPid.ContainsKey(name))
                                nameToCurrentPid[name] = processId;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Session may have ended
                        App.Logger?.Debug("Failed to unduck audio session: {Error}", ex.Message);
                    }
                }

                // Fallback: for stored PIDs whose original session is gone, try to find a
                // current session for the same process name (e.g. user restarted Firefox
                // mid-session — new PID, same app).
                foreach (var kv in wanted)
                {
                    if (restored.Contains(kv.Key)) continue;
                    if (!names.TryGetValue(kv.Key, out var name) || string.IsNullOrEmpty(name)) continue;
                    if (!nameToCurrentPid.TryGetValue(name, out var currentPid)) continue;

                    try
                    {
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
                            if ((int)session.GetProcessID == currentPid)
                            {
                                session.SimpleAudioVolume.Volume = kv.Value;
                                restored.Add(kv.Key);
                                break;
                            }
                        }
                    }
                    catch { /* session may have ended between checks */ }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Audio unducking failed, preserving state for retry: {Error}", ex.Message);
                lock (_lockObj)
                {
                    // CRITICAL: Do NOT clear _originalVolumes here. If we do, the next Duck() will
                    // re-read the currently-ducked volumes as "originals", causing volumes to
                    // ratchet toward 0% over repeated cycles. Keep state intact — and pin _isDucked
                    // back on with _duckCount=1 (not 0) so the system can recover: _duckCount=0 +
                    // _isDucked=true would make Duck() silently return and no future Unduck() could
                    // ever restore. Re-arm the watchdog so it retries from here.
                    _isDucked = true;
                    _duckCount = 1;
                    if (!_disposed && _duckWatchdog == null)
                        _duckWatchdog = new System.Threading.Timer(_ => DuckWatchdogTick(),
                            null, DuckWatchdogMs, DuckWatchdogMs);
                }
                // Keep the recovery file so crash recovery can restore if the app exits.
                return;
            }

            int pendingCount;
            lock (_lockObj)
            {
                foreach (var kv in wanted)
                {
                    _originalVolumes.Remove(kv.Key);
                    // Anything we couldn't reach (PID gone, app silent / not playing) goes pending
                    // so the retry timer can re-attempt as the app resumes producing audio.
                    if (restored.Contains(kv.Key)) _processNames.Remove(kv.Key);
                    else _pendingRestores[kv.Key] = kv.Value;
                }

                pendingCount = _pendingRestores.Count;
                if (pendingCount > 0)
                {
                    _restoreRetryDeadline = DateTime.UtcNow + RestoreRetryWindow;
                    _restoreRetryTimer?.Dispose();
                    _restoreRetryTimer = new System.Threading.Timer(_ => RestoreRetryTick(),
                        null, RestoreRetryIntervalMs, RestoreRetryIntervalMs);
                }
                else
                {
                    // All restored — process names no longer needed
                    _processNames.Clear();
                }
            }

            if (pendingCount > 0)
                App.Logger?.Information("[Ducking] {Count} session(s) had no current audio at unduck — will retry restore for up to {Min} min",
                    pendingCount, RestoreRetryWindow.TotalMinutes);

            // Clear crash recovery file
            ClearDuckingState();

            App.Logger?.Debug("Audio unducked ({Restored} restored, {Pending} deferred)",
                restored.Count, pendingCount);
        }

        /// <summary>
        /// Safety net for a duck that outlived <see cref="DuckWatchdogMs"/>. Clears the ducked
        /// state BEFORE attempting restoration — a throwing restore must never be able to leave
        /// _isDucked pinned, which is what kept the rescan timer re-ducking every audio session
        /// on the machine every 2.5s until the app was killed (#686).
        /// </summary>
        private void DuckWatchdogTick()
        {
            bool restore = false;
            lock (_lockObj)
            {
                if (_disposed) return;

                if (!_isDucked)
                {
                    // Nothing pinned — retire until the next Duck() arms a fresh net.
                    _duckWatchdog?.Dispose();
                    _duckWatchdog = null;
                    return;
                }

                App.Logger?.Warning("[Ducking] Watchdog fired after {Ms}ms — force-unducking to prevent stuck volume", DuckWatchdogMs);

                // Hard reset first, restore second.
                _isDucked = false;
                _duckCount = 0;
                _duckGeneration++; // invalidate any in-flight stale Unduck callbacks
                _duckRescanTimer?.Dispose();
                _duckRescanTimer = null;
                restore = true;
            }

            try { App.LayeredAudio?.ReleaseDuck(); } catch { }
            if (restore) EnqueueDuckWork("watchdog-restore", RestoreDuckedVolumes, trace: true);
        }

        /// <summary>
        /// Force-unduck regardless of reference count. Used for panic key / app exit.
        /// Increments the duck generation to invalidate all pending stale Unduck callbacks.
        /// </summary>
        public void ForceUnduck()
        {
            long gen;
            lock (_lockObj)
            {
                _duckGeneration++; // Invalidate all pending stale Unduck callbacks
                _duckCount = 1; // Force next Unduck() to actually restore
                gen = _duckGeneration;
            }
            Unduck(gen);
        }

        private static string TryGetProcessName(int processId)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                return proc.ProcessName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Collect audio sessions across ALL active render endpoints — not just the default
        /// multimedia device. A user who routes their browser / media player to a secondary
        /// output would otherwise never get ducked, since those sessions live on a different
        /// device's session manager (bug #415).
        ///
        /// WORKER-THREAD ONLY. A machine with a dozen endpoints (or one that answers slowly) makes
        /// this take tens of seconds, which is the whole of #750-#753 — never call it from the UI
        /// thread. Uses the worker's own enumerator so the COM calls stay inside its apartment.
        /// </summary>
        private RenderSessionScope CollectRenderSessions()
        {
            var scope = new RenderSessionScope();
            var enumerator = _sweepEnumerator ?? _deviceEnumerator;
            if (enumerator == null) return scope;

            try
            {
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                scope.Own(endpoints);
                for (int d = 0; d < endpoints.Count; d++)
                {
                    try
                    {
                        // The indexer mints a fresh MMDevice wrapper per call — hold it so the
                        // scope can release it instead of stranding it for the finalizer (#686).
                        var device = endpoints[d];
                        scope.Own(device);
                        var manager = device.AudioSessionManager;
                        scope.Own(manager);
                        var sessions = manager.Sessions;
                        scope.Own(sessions);
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            try { scope.Sessions.Add(sessions[i]); } catch { /* session ended mid-enumeration */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("[Ducking] Failed to enumerate sessions for a render endpoint: {Error}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Ducking] Failed to enumerate render endpoints: {Error}", ex.Message);
            }

            return scope;
        }

        /// <summary>
        /// Owns every COM wrapper produced by one <see cref="CollectRenderSessions"/> sweep — the
        /// endpoint collection, each device, its session manager, the session collection, and the
        /// sessions themselves. All of them wrap a native interface that is only released when the
        /// wrapper is disposed or finalized; the duck rescan timer runs this sweep 24x/min, so
        /// leaving them to the finalizer queue leaked native memory and handles (#686).
        ///
        /// The sessions are ONLY valid for the lifetime of the scope — never cache one past Dispose().
        /// </summary>
        internal sealed class RenderSessionScope : IDisposable
        {
            public List<AudioSessionControl> Sessions { get; } = new();

            private readonly List<object> _owned = new();

            /// <summary>Register a COM wrapper for release when the scope ends.</summary>
            internal void Own(object? wrapper)
            {
                if (wrapper != null) _owned.Add(wrapper);
            }

            public void Dispose()
            {
                for (int i = Sessions.Count - 1; i >= 0; i--) SafeDispose(Sessions[i]);
                Sessions.Clear();

                // Reverse acquisition order: sessions -> session collection -> manager -> device
                // -> endpoint collection. A wrapper may already have been disposed by its parent
                // (MMDevice owns its session manager), so every call is individually guarded.
                for (int i = _owned.Count - 1; i >= 0; i--) SafeDispose(_owned[i]);
                _owned.Clear();
            }

            private static void SafeDispose(object? o)
            {
                if (o is IDisposable d)
                {
                    try { d.Dispose(); } catch { /* already released / endpoint vanished */ }
                }
            }
        }

        /// <summary>
        /// Periodically called while ducked to catch new audio sessions (e.g. Discord notification,
        /// Steam ping) that didn't exist when Duck() ran. Without this, those play un-ducked.
        /// Threadpool-timer entry point: it only decides whether to queue a sweep.
        /// </summary>
        private void RescanForNewSessions()
        {
            if (_disposed) return;
            lock (_lockObj)
            {
                if (!_isDucked || _deviceEnumerator == null) return;
            }

            // A sweep is already queued or running. SKIP this cycle rather than queueing behind it:
            // on a machine where one endpoint answers slower than the 2.5s cadence the backlog grew
            // without bound and outlived the duck window entirely (#750-#753).
            if (Volatile.Read(ref _duckWorkDepth) > 0) return;

            EnqueueDuckWork("rescan", RescanSweep);
        }

        /// <summary>Worker-thread half of <see cref="RescanForNewSessions"/>.</summary>
        private void RescanSweep()
        {
            float amount;
            lock (_lockObj)
            {
                if (!_isDucked || _disposed) return;
                amount = _duckAmount;
            }

            var currentProcessId = Environment.ProcessId;
            var excludeWebView2 = App.Settings?.Current?.ExcludeBambiCloudFromDucking ?? true;
            var webViewPids = excludeWebView2 ? RefreshWebView2Pids() : new HashSet<int>();

            int newlyDucked = 0;
            using (var scope = CollectRenderSessions())
            {
                var sessions = scope.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var session = sessions[i];
                        var processId = (int)session.GetProcessID;

                        if (processId == currentProcessId || processId == 0) continue;
                        if (excludeWebView2 && webViewPids.Contains(processId)) continue;

                        var currentVolume = session.SimpleAudioVolume.Volume;
                        // Skip sessions already at 0 — most likely they're sessions we ducked in a
                        // prior generation and the PID got recycled; treating their 0 as "original"
                        // would silence the app forever.
                        if (currentVolume <= 0.001f) continue;

                        lock (_lockObj)
                        {
                            if (!_isDucked) return;                                 // unducked mid-sweep
                            if (_originalVolumes.ContainsKey(processId)) continue;  // already ducked
                            _originalVolumes[processId] = currentVolume;
                        }
                        var name = TryGetProcessName(processId);
                        lock (_lockObj) { _processNames[processId] = name; }

                        var newVolume = currentVolume * (1.0f - amount);
                        session.SimpleAudioVolume.Volume = Math.Max(0.0f, newVolume);
                        newlyDucked++;
                    }
                    catch { /* session may have ended */ }
                }
            }

            if (newlyDucked > 0)
            {
                App.Logger?.Debug("[Ducking] Rescan ducked {Count} new session(s)", newlyDucked);
                SaveDuckingState();
            }
        }

        /// <summary>
        /// Retry restoring volumes for sessions that didn't have an active audio session at Unduck
        /// time. Fires every few seconds for a window after Unduck — when the app starts producing
        /// audio again, its session reappears and we restore the original volume.
        /// </summary>
        private void RestoreRetryTick()
        {
            if (_disposed) return;

            lock (_lockObj)
            {
                if (_pendingRestores.Count == 0)
                {
                    _restoreRetryTimer?.Dispose();
                    _restoreRetryTimer = null;
                    _processNames.Clear();
                    return;
                }

                if (DateTime.UtcNow > _restoreRetryDeadline)
                {
                    App.Logger?.Warning("[Ducking] Restore retry window expired with {Count} unrestored — giving up",
                        _pendingRestores.Count);
                    _pendingRestores.Clear();
                    _processNames.Clear();
                    _restoreRetryTimer?.Dispose();
                    _restoreRetryTimer = null;
                    return;
                }

                if (_deviceEnumerator == null) return;
            }

            // Same skip-don't-queue rule as the rescan: this is a best-effort retry on a 5s
            // cadence, and piling them up behind a slow endpoint helps nobody (#750-#753).
            if (Volatile.Read(ref _duckWorkDepth) > 0) return;

            EnqueueDuckWork("restore-retry", RestoreRetrySweep);
        }

        /// <summary>Worker-thread half of <see cref="RestoreRetryTick"/>.</summary>
        private void RestoreRetrySweep()
        {
            Dictionary<int, float> pending;
            Dictionary<int, string> names;
            lock (_lockObj)
            {
                if (_disposed || _pendingRestores.Count == 0) return;
                pending = new Dictionary<int, float>(_pendingRestores);
                names = new Dictionary<int, string>(_processNames);
            }

            var restored = new List<int>();

            using (var scope = CollectRenderSessions())
            {
                var sessions = scope.Sessions;

                // Build PID-by-name index so we can match a stored PID to a new PID for the same app
                var nameToPid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var pid = (int)sessions[i].GetProcessID;
                        if (pid == 0) continue;
                        var name = TryGetProcessName(pid);
                        if (!string.IsNullOrEmpty(name) && !nameToPid.ContainsKey(name))
                            nameToPid[name] = pid;
                    }
                    catch { }
                }

                foreach (var kv in pending)
                {
                    var storedPid = kv.Key;
                    var originalVolume = kv.Value;
                    int? targetPid = null;

                    // Try direct PID match first
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            if ((int)sessions[i].GetProcessID == storedPid) { targetPid = storedPid; break; }
                        }
                        catch { }
                    }

                    // Fallback: process-name match (handles app-restart with new PID)
                    if (targetPid == null && names.TryGetValue(storedPid, out var name)
                        && !string.IsNullOrEmpty(name) && nameToPid.TryGetValue(name, out var freshPid))
                    {
                        targetPid = freshPid;
                    }

                    if (targetPid == null) continue;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            if ((int)session.GetProcessID == targetPid.Value)
                            {
                                session.SimpleAudioVolume.Volume = originalVolume;
                                restored.Add(storedPid);
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            int remaining;
            lock (_lockObj)
            {
                foreach (var pid in restored)
                {
                    _pendingRestores.Remove(pid);
                    _processNames.Remove(pid);
                }

                remaining = _pendingRestores.Count;
                if (remaining == 0)
                {
                    _processNames.Clear();
                    _restoreRetryTimer?.Dispose();
                    _restoreRetryTimer = null;
                }
            }

            if (restored.Count > 0)
                App.Logger?.Information("[Ducking] Deferred-restore: recovered volume for {Count} session(s); {Remaining} still pending",
                    restored.Count, remaining);
        }

        #region Sweep worker

        /// <summary>
        /// Queue one WASAPI sweep onto the single serialized worker thread and return immediately —
        /// this is what keeps Duck/Unduck off the UI thread's critical path (#750-#753). Items run
        /// in submission order, so a Duck followed straight away by an Unduck still nets out right.
        /// </summary>
        private void EnqueueDuckWork(string name, Action work, bool trace = false)
        {
            if (_disposed) return;
            EnsureDuckWorker();

            Interlocked.Increment(ref _duckWorkDepth);
            _duckWorkIdle.Reset();
            try
            {
                _duckWork.Add(() =>
                {
                    var sw = Stopwatch.StartNew();
                    try { work(); }
                    catch (Exception ex) { App.Logger?.Debug("[Ducking] {Op} sweep failed: {Error}", name, ex.Message); }
                    finally
                    {
                        var ms = sw.ElapsedMilliseconds;
                        // A sweep this slow IS the freeze the reporters saw — it just no longer
                        // happens on their dispatcher. Naming it in the video trace makes the next
                        // report a straight read instead of another blind spot.
                        if (trace || ms >= SlowSweepWarnMs)
                            VideoDiag.Log("DUCK", $"{name} applied after {ms}ms");
                        if (ms >= SlowSweepWarnMs)
                            App.Logger?.Warning("[Ducking] {Op} sweep took {Ms}ms — a render endpoint on this machine is answering slowly", name, ms);
                        if (Interlocked.Decrement(ref _duckWorkDepth) <= 0) _duckWorkIdle.Set();
                    }
                });
            }
            catch (Exception ex)
            {
                // Collection completed (shutdown) — undo the depth bump so the exit flush can finish.
                if (Interlocked.Decrement(ref _duckWorkDepth) <= 0) _duckWorkIdle.Set();
                App.Logger?.Debug("[Ducking] could not queue {Op}: {Error}", name, ex.Message);
            }
        }

        private void EnsureDuckWorker()
        {
            if (Interlocked.Exchange(ref _duckWorkerStarted, 1) == 1) return;
            _duckWorker = new Thread(DuckWorkerLoop)
            {
                IsBackground = true,
                Name = "AudioDuckWorker",
                Priority = ThreadPriority.BelowNormal,
            };
            _duckWorker.Start();
        }

        private void DuckWorkerLoop()
        {
            // Created here on purpose — see the _sweepEnumerator field comment. A COM apartment
            // mismatch would send every sweep back through the dispatcher, defeating the whole point.
            try { _sweepEnumerator = new MMDeviceEnumerator(); }
            catch (Exception ex)
            {
                App.Logger?.Warning("[Ducking] sweep worker could not create its own device enumerator: {Error}", ex.Message);
            }

            try
            {
                foreach (var item in _duckWork.GetConsumingEnumerable())
                {
                    try { item(); } catch { }
                }
            }
            catch { /* collection completed / disposed */ }

            try { _sweepEnumerator?.Dispose(); } catch { }
            _sweepEnumerator = null;
        }

        /// <summary>
        /// Block until every queued sweep has finished, or <paramref name="timeoutMs"/> elapses.
        /// Dispose-only: app exit must not leave other apps pinned at the ducked volume, but a
        /// machine with a wedged endpoint must not hold the exit open either — ducking_recovery.json
        /// is still on disk in that case and the next launch restores from it.
        /// </summary>
        private bool FlushDuckWork(int timeoutMs)
        {
            try { return _duckWorkIdle.Wait(timeoutMs); }
            catch { return false; }
        }

        #endregion

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            // Restore audio levels — force unduck regardless of ref count. This runs BEFORE
            // _disposed goes up: the sweeps bail out when it is set, and an exit that skipped the
            // restore would leave every other app on the machine pinned at the ducked volume until
            // the crash-recovery file is picked up on the NEXT launch.
            bool wasDucked;
            lock (_lockObj) { wasDucked = _isDucked; }
            if (wasDucked) ForceUnduck();

            var flushed = FlushDuckWork(WorkerFlushTimeoutMs);
            if (!flushed)
                App.Logger?.Warning("[Ducking] exit flush timed out after {Ms}ms — a sweep is still in flight; ducking_recovery.json will restore on the next launch",
                    WorkerFlushTimeoutMs);

            _disposed = true;

            StopSound();
            _duckWatchdog?.Dispose();
            _duckRescanTimer?.Dispose();
            _restoreRetryTimer?.Dispose();
            try { _duckWork.CompleteAdding(); } catch { }

            // Only release the enumerators once we know no sweep is mid-COM-call on one. Yanking
            // them out from under a wedged worker turns a clean exit into a native crash; process
            // teardown reclaims them in the timed-out case.
            if (flushed) _deviceEnumerator?.Dispose();
        }

        #endregion
    }
}
