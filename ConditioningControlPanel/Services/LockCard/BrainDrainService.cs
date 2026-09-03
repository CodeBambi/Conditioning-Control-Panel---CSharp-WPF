using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public class BrainDrainService : IDisposable
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _cts;
        
        private bool _isRunning;
        private double _intensity = 50; // 50% default intensity
        
        private string[]? _audioFiles;
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        /// <summary>Bumped by every trigger and by StopCurrentAudio. A build that finishes after a
        /// newer one started (or after a stop) is thrown away instead of published over the live
        /// player.</summary>
        private long _playGeneration;

        /// <summary>
        /// Guards the player fields and the generation. Held ONLY for cheap work: the arbitration
        /// check, the field swap and <c>WaveOutEvent.Play</c> (which just queues the playback
        /// thread). Never held across a build, a Stop or a Dispose, so a background build can never
        /// block a stop and a stop can never block the UI thread.
        /// </summary>
        private readonly object _audioLock = new();

        // ---------------------------------------------------------------------------------
        //  RANDOM ONSET (#general 2026-08-31: "i wish it kicked on randomly not as soon as
        //  i press start"). Standalone start ONLY - sessions and the autonomy trigger own
        //  their own timing and never come through here.
        // ---------------------------------------------------------------------------------
        private readonly DispatcherTimer _onsetTimer;
        private bool _onsetPending;
        private DateTime _onsetDueUtc;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// True while a random onset is armed: Brain Drain is enabled and the user has pressed
        /// Start, but BOTH halves are deliberately withheld until the wait runs out. OverlayService
        /// reads this so the screen blur waits too - delaying only the audio would give the game
        /// away the instant the blur landed.
        /// </summary>
        public bool OnsetPending => _onsetPending;

        /// <summary>Whole minutes still to wait, for the panel's armed readout. 0 when nothing is armed.</summary>
        public int OnsetMinutesRemaining => _onsetPending
            ? Math.Max(1, (int)Math.Ceiling((_onsetDueUtc - DateTime.UtcNow).TotalMinutes))
            : 0;

        /// <summary>Raised on the UI thread when the armed state changes, so the panel can repaint.</summary>
        public event EventHandler? OnsetStateChanged;

        public int AudioFileCount => _audioFiles?.Length ?? 0;

        /// <summary>
        /// THE brain-drain clip folder: <c>&lt;EffectiveAssetsPath&gt;\braindrain</c>, alongside
        /// <c>images</c> / <c>videos</c> and everything else a user drops in.
        ///
        /// <para>It used to be the INSTALL directory instead (see
        /// <see cref="LegacyAudioFolderPath"/>) and that was the single biggest support cost the
        /// feature has ever had: five-plus users in two nights could not find it, the community
        /// moderator included, and an install folder is the one place that does NOT survive a
        /// reinstall or a portable copy - so people who did find it lost their clips again. The
        /// assets folder is where users already look, it is backed by
        /// <c>AppSettings.CustomAssetsPath</c>, and nothing in the installer touches it.</para>
        ///
        /// <para>The legacy folder is still SCANNED (see <see cref="LoadAudioFiles"/>); it is just
        /// no longer where the UI sends people. Nothing migrates, moves or deletes user files.</para>
        /// </summary>
        public static string AudioFolderPath
        {
            get
            {
                try { return Path.Combine(App.EffectiveAssetsPath, "braindrain"); }
                catch { return LegacyAudioFolderPath; }   // pre-settings startup / test host
            }
        }

        /// <summary>
        /// The ORIGINAL clip folder under the install directory
        /// (<c>%LOCALAPPDATA%\Programs\Conditioning Control Panel\Resources\sounds\braindrain</c>).
        /// Still scanned so the users who did find it keep their clips working; never advertised as
        /// the place to put new ones, never written to, never emptied, and deliberately NOT added to
        /// installer-content-deletions (that would delete their files on the next update).
        /// </summary>
        public static string LegacyAudioFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "braindrain");

        /// <summary>Supported clip extensions. One list, used by both folder scans.</summary>
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg" };

        /// <summary>Create the primary (assets) clip folder if it does not exist yet and hand back
        /// its path. Used by the feature card's "Open folder" button so Explorer never opens onto
        /// nothing, and by the first scan so the folder EXISTS to be found.</summary>
        public static string EnsureAudioFolder()
        {
            var path = AudioFolderPath;
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BrainDrain: could not create the audio folder"); }
            return path;
        }

        /// <summary>How many clips live in the LEGACY install folder. The feature card shows a
        /// "your old clips still work, but new ones go here" note only when this is non-zero, so a
        /// clean install never sees a line about a folder it has no reason to care about.</summary>
        public static int LegacyAudioFileCount()
        {
            try { return EnumerateClips(LegacyAudioFolderPath).Count(); }
            catch { return 0; }
        }

        /// <summary>Clip files directly inside <paramref name="folder"/>, or nothing when the folder
        /// is absent/unreadable. Never throws - a missing legacy folder is the normal case.</summary>
        private static IEnumerable<string> EnumerateClips(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Array.Empty<string>();
            try
            {
                return Directory.GetFiles(folder, "*.*")
                    .Where(f => AudioExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BrainDrain: could not read clip folder {Path}", folder);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Fires when a brain drain audio effect is triggered
        /// </summary>
        public event EventHandler? BrainDrainTriggered;
        
        public double Intensity
        {
            get => _intensity;
            set => _intensity = Math.Clamp(value, 1, 100);
        }
        
        public BrainDrainService()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;

            // Built here, on the UI thread, for the same reason _timer is: a DispatcherTimer
            // binds to whichever dispatcher created it.
            _onsetTimer = new DispatcherTimer();
            _onsetTimer.Tick += OnsetTimer_Tick;

            LoadAudioFiles();
        }

        private void UpdateTimerInterval()
        {
            // High refresh mode: 500ms interval for smoother effect
            // Normal mode: 5s interval for lower CPU usage
            var interval = App.Settings.Current.BrainDrainHighRefresh
                ? TimeSpan.FromMilliseconds(500)
                : TimeSpan.FromSeconds(5);

            _timer.Interval = interval;
        }
        
        /// <summary>
        /// Build the clip pool from BOTH folders: the assets folder (primary, and created here on
        /// the first scan so users can actually find it) and the legacy install folder.
        ///
        /// <para>De-duped by FILE NAME with the assets copy winning, so a user who copied their
        /// clips out of the install folder into assets - the obvious thing to do once the UI points
        /// there - gets each track once, not twice. Nothing is moved or deleted either way; the
        /// legacy folder is read-only as far as this service is concerned.</para>
        /// </summary>
        private void LoadAudioFiles()
        {
            try
            {
                var assetsFolder = EnsureAudioFolder();
                var legacyFolder = LegacyAudioFolderPath;

                App.Logger?.Information("BrainDrain: Looking for audio files in {Path} (+ legacy {Legacy})",
                    assetsFolder, legacyFolder);

                // Assets first so its entries claim their file names; the legacy folder then only
                // contributes names the assets folder does not already have.
                var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in EnumerateClips(assetsFolder))
                    byName[Path.GetFileName(file)] = file;

                int legacyAdded = 0;
                foreach (var file in EnumerateClips(legacyFolder))
                {
                    var name = Path.GetFileName(file);
                    if (byName.ContainsKey(name)) continue;
                    byName[name] = file;
                    legacyAdded++;
                }

                _audioFiles = byName.Values.ToArray();

                if (_audioFiles.Length == 0)
                {
                    App.Logger?.Warning("BrainDrain: No .mp3/.wav/.ogg files found in {Path} or {Legacy}",
                        assetsFolder, legacyFolder);
                }
                else
                {
                    App.Logger?.Information("BrainDrain: Loaded {Count} audio files ({Legacy} from the legacy install folder)",
                        _audioFiles.Length, legacyAdded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrainDrain: Failed to load audio files");
                _audioFiles = Array.Empty<string>();
            }
        }
        
        public void ReloadAudioFiles()
        {
            LoadAudioFiles();
        }
        
        public void Start()
        {
            // A random onset is armed and still counting down: the wait IS the feature. The tick
            // clears the flag and calls back in here when it is done.
            if (_onsetPending) return;

            if (!App.Settings.Current.BrainDrainEnabled)
            {
                App.Logger?.Debug("BrainDrain: Not enabled in settings");
                return;
            }

            if (_isRunning) return;

            // Re-scan the clip folder on every session start. The folder is a plain drop target
            // that lives outside the app (see AudioFolderPath) and NOTHING used to call
            // ReloadAudioFiles at all, so a clip added after launch only ever worked following a
            // full restart - several users hit that in one evening. One Directory.GetFiles per
            // Start is free next to what Start already does.
            ReloadAudioFiles();

            UpdateSettings();
            UpdateTimerInterval();
            _isRunning = true;
            _cts = new CancellationTokenSource();

            _timer.Start();

            // Update Discord presence
            App.DiscordRpc?.SetBrainDrainActivity();

            var mode = App.Settings.Current.BrainDrainHighRefresh ? "High Refresh (500ms)" : "Normal (5s)";
            App.Logger?.Information("BrainDrain started at intensity {Intensity}%, mode: {Mode}", _intensity, mode);
        }

        public void Stop()
        {
            // Cancel an armed-but-not-yet-fired onset FIRST: while it is pending _isRunning is
            // still false, so the "no session" early return further down would skip it and leave
            // a timer ticking after the feature was stopped.
            CancelRandomOnset();

            var wasRunning = _isRunning;
            _isRunning = false;

            // Audio teardown FIRST, inline, and UNCONDITIONAL. App.KillAllAudio can reach us from
            // the panic fallback thread, and the DispatcherTimer below has UI-thread affinity -
            // stopping it first would throw there and leave the audio playing. NAudio has no thread
            // affinity.
            // Unlike MindWipe there is no TriggerOnce here (PlayAudioNow is private and only the
            // _isRunning-gated Timer_Tick reaches it) and PlayAudio's publish IS gated on
            // _isRunning, so nothing can currently start while stopped - the teardown is ungated
            // anyway so panic's "always reaches the audio" guarantee is structural rather than a
            // consequence of that gate staying put.
            StopCurrentAudio();

            // Session/timer/state teardown only matters if there was a session.
            if (!wasRunning) return;

            _cts?.Cancel();

            DispatcherHelper.RunOnUI(() => { try { _timer.Stop(); } catch { } });

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            App.Logger?.Information("BrainDrain stopped");
        }
        
        public void UpdateSettings()
        {
            Intensity = App.Settings.Current.BrainDrainIntensity;
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning) return;
            if (_audioFiles == null || _audioFiles.Length == 0) return;

            var probability = _intensity / 100.0 / (60.0 / _timer.Interval.TotalSeconds);
            
            if (_random.NextDouble() < probability)
            {
                PlayAudioNow();
            }
        }
        
        private void PlayAudioNow()
        {
            if (_audioFiles == null || _audioFiles.Length == 0) return;

            // #983: a winning roll used to DISPLACE the pair still playing - a long track
            // audibly "rebooted" partway through, and a single-file folder restarted itself.
            // A clip now always plays to its end; a roll that lands mid-clip is skipped, not
            // queued. (Triggers inside the ~0.5s off-thread build window can still displace -
            // _waveOut is only published after arbitration - which is harmless at clip scale.)
            lock (_audioLock)
            {
                if (_waveOut != null) return;
            }

            try
            {
                var audioFile = _audioFiles[_random.Next(_audioFiles.Length)];
                PlayAudio(audioFile);

                // Fire event for avatar/UI notification
                BrainDrainTriggered?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrainDrain: Failed to play audio");
            }
        }
        
        private void PlayAudio(string filePath)
        {
            if (App.Audio?.IsOutputSuppressed == true) return; // endpoint down — stay quiet, don't spin

            var volume = CurrentMasterVolume();
            long generation;
            lock (_audioLock)
            {
                generation = ++_playGeneration;
            }

            // Building an AudioFileReader (mp3 index) and opening a WaveOutEvent are half-second
            // blocking calls. Run at this feature's cadence on the UI thread they read as the app
            // hitching every few seconds (#890), so the whole build happens off-thread; the pair is
            // only started once it has won the arbitration below.
            Task.Run(() =>
            {
                AudioFileReader? reader = null;
                WaveOutEvent? waveOut = null;
                try
                {
                    reader = new AudioFileReader(filePath) { Volume = volume };
                    waveOut = new WaveOutEvent();
                    App.Audio?.ApplyPreferredDevice(waveOut);
                    waveOut.Init(reader);

                    // Capture THIS pair rather than reading the fields: by the time the clip ends
                    // they can already point at a newer player, and disposing that one killed
                    // live audio.
                    var endedReader = reader;
                    var endedWaveOut = waveOut;
                    waveOut.PlaybackStopped += (_, _) =>
                    {
                        lock (_audioLock)
                        {
                            if (ReferenceEquals(_waveOut, endedWaveOut))
                            {
                                _waveOut = null;
                                _audioReader = null;
                            }
                        }
                        DisposePair(endedWaveOut, endedReader);
                    };
                }
                catch (Exception ex)
                {
                    DisposePair(waveOut, reader);
                    App.Logger?.Error(ex, "BrainDrain: Error playing audio file {Path}", filePath);
                    App.Audio?.NoteOutputFailure("braindrain", ex.Message);
                    return;
                }

                WaveOutEvent? displacedOut = null;
                AudioFileReader? displacedReader = null;
                var started = false;

                lock (_audioLock)
                {
                    // The stop decision is arbitrated HERE, on this thread. NAudio has no UI
                    // affinity, and deferring it to the dispatcher meant a wedged UI thread (the
                    // panic case) could never stop an in-flight pair, while a shutdown dropped it
                    // still playing and undisposed.
                    if (_isRunning && generation == _playGeneration)
                    {
                        try
                        {
                            waveOut!.Play();   // only queues the playback thread; cheap under the lock
                            displacedOut = _waveOut;
                            displacedReader = _audioReader;
                            _waveOut = waveOut;
                            _audioReader = reader;
                            // Master volume may have been dragged during the build, and nothing
                            // else re-reads it for this clip's whole duration.
                            try { reader!.Volume = CurrentMasterVolume(); } catch { }
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "BrainDrain: Error starting audio file {Path}", filePath);
                        }
                    }
                }

                DisposePair(displacedOut, displacedReader);
                if (started) App.Audio?.NoteOutputSuccess();
                else DisposePair(waveOut, reader);
            });
        }

        private static float CurrentMasterVolume()
        {
            try { return Math.Clamp(App.Settings.Current.MasterVolume, 0, 100) / 100f; }
            catch { return 1f; }
        }

        /// <summary>
        /// Stops and disposes a pair. Never called with <see cref="_audioLock"/> held: waveOutReset
        /// and waveOutClose take NAudio's own locks and can be raced by the playback thread, so this
        /// service's lock deliberately stays out of them.
        /// </summary>
        private static void DisposePair(WaveOutEvent? waveOut, AudioFileReader? reader)
        {
            try { waveOut?.Stop(); } catch { }
            try { waveOut?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
        }

        private void StopCurrentAudio()
        {
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                // Bumping the generation is what makes a stop stick: Stop() alone relied on
                // !_isRunning, so a Stop->Start while a build was in flight let the stopped
                // session's clip publish over the new one.
                _playGeneration++;
                // Fields cleared FIRST: PlaybackStopped may already have disposed this pair, and a
                // throw out of Stop() used to leave the stale references in place.
                waveOut = _waveOut;
                reader = _audioReader;
                _waveOut = null;
                _audioReader = null;
            }
            DisposePair(waveOut, reader);
        }

        /// <summary>
        /// Update volume on currently playing audio (for live master volume changes).
        /// </summary>
        public void UpdateMasterVolume(int volume)
        {
            try
            {
                if (_audioReader != null)
                {
                    _audioReader.Volume = Math.Clamp(volume, 0, 100) / 100.0f;
                }
            }
            catch { }
        }

        /// <summary>
        /// Arm the random onset if the user configured one, and report whether Brain Drain is now
        /// WAITING (so the caller knows not to start it yet).
        ///
        /// <para>Called from <c>MainWindow.StartEngine</c> BEFORE OverlayService starts, because
        /// the blur half asks <see cref="OnsetPending"/> at its own start. Idempotent: a second
        /// call while one is already armed keeps the first wait.</para>
        /// </summary>
        public bool ArmRandomOnset()
        {
            if (_onsetPending) return true;
            if (_isRunning) return false;

            var s = App.Settings?.Current;
            if (s == null || !s.BrainDrainEnabled) return false;

            var max = s.BrainDrainRandomStartMaxMinutes;
            if (max < 1) return false;

            var minutes = _random.Next(1, max + 1);
            _onsetPending = true;
            _onsetDueUtc = DateTime.UtcNow.AddMinutes(minutes);

            DispatcherHelper.RunOnUI(() =>
            {
                if (System.Windows.Application.Current?.Dispatcher == null) return;
                try
                {
                    _onsetTimer.Stop();
                    _onsetTimer.Interval = TimeSpan.FromMinutes(minutes);
                    _onsetTimer.Start();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "BrainDrain: could not arm random onset"); }
            });

            App.Logger?.Information("BrainDrain: random onset armed, waiting {Minutes} min", minutes);
            RaiseOnsetStateChanged();
            return true;
        }

        /// <summary>
        /// Drop a pending onset without starting anything. Called by <see cref="Stop"/> (so the
        /// panic key and a plain Stop both kill it) and by SessionEngine, which owns Brain Drain's
        /// timing for the length of a session.
        /// </summary>
        public void CancelRandomOnset()
        {
            if (!_onsetPending) return;
            _onsetPending = false;

            DispatcherHelper.RunOnUI(() =>
            {
                if (System.Windows.Application.Current?.Dispatcher == null) return;
                try { _onsetTimer.Stop(); } catch { }
            });

            App.Logger?.Information("BrainDrain: pending random onset cancelled");
            RaiseOnsetStateChanged();
        }

        private void OnsetTimer_Tick(object? sender, EventArgs e)
        {
            if (System.Windows.Application.Current?.Dispatcher == null) return;
            try { _onsetTimer.Stop(); } catch { }
            if (!_onsetPending) return;

            _onsetPending = false;
            RaiseOnsetStateChanged();

            App.Logger?.Information("BrainDrain: random onset elapsed, kicking in now");
            try { Start(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BrainDrain: onset start failed"); }

            // The blur half needs no nudge from here: OverlayService's 500ms reconciler re-reads
            // OnsetPending on its next tick and raises the overlay itself.
        }

        private void RaiseOnsetStateChanged()
        {
            var handler = OnsetStateChanged;
            if (handler == null) return;
            DispatcherHelper.RunOnUI(() =>
            {
                if (System.Windows.Application.Current?.Dispatcher == null) return;
                try { handler(this, EventArgs.Empty); } catch { }
            });
        }

        public void Dispose()
        {
            Stop();
            StopCurrentAudio();
        }
    }
}