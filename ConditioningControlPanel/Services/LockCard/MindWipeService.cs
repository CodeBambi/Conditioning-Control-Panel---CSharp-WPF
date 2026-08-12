using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using NAudio.Wave;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for playing mind wipe audio effects at random intervals.
    /// Unlockable at level 75. Does NOT duck other audio.
    /// </summary>
    public class MindWipeService : IDisposable
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _cts;
        
        private bool _isRunning;
        private double _frequencyPerHour = 6; // Default 6 per hour
        private double _volume = 0.5; // 50% default volume
        
        private string[]? _audioFiles;
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        /// <summary>Bumped by every one-shot trigger and by StopCurrentAudio. A build that finishes
        /// after a newer trigger (or after a stop) is thrown away instead of published.</summary>
        private long _playGeneration;

        /// <summary>
        /// Guards every player field in this service. Held ONLY for cheap work: state checks,
        /// generation reads/bumps, field swaps and <c>WaveOutEvent.Play</c> (which just queues the
        /// playback thread). Never held across a build, a Stop or a Dispose, so a background build
        /// can never block a stop and a stop can never block the UI thread.
        /// </summary>
        private readonly object _audioLock = new();

        // Session mode
        private bool _sessionMode;
        private int _sessionBaseFrequency;
        private DateTime _sessionStartTime;
        
        // Loop mode with crossfade
        private bool _loopMode;
        private string? _loopFilePath;
        private DateTime _loopStartTime;
        private bool _cleanSlateAchieved;
        
        // Crossfade support - two players for seamless looping
        private const double CROSSFADE_OVERLAP_SECONDS = 0.12;
        private WaveOutEvent? _loopWaveOutA;
        private WaveOutEvent? _loopWaveOutB;
        private AudioFileReader? _loopReaderA;
        private AudioFileReader? _loopReaderB;
        private bool _usePlayerA = true; // Alternate between A and B
        /// <summary>Identifies the current loop session; bumped by StopLoop (and so by every
        /// StartLoop, which stops first). A player that finishes building - or is still parked -
        /// after a stop is discarded instead of played.</summary>
        private long _loopGeneration;
        private DispatcherTimer? _crossfadeTimer;
        private TimeSpan _loopDuration;
        /// <summary>The next clip's pair, already opened and Init'd, waiting for its crossfade tick.</summary>
        private PreparedLoopPlayer? _pendingLoopPlayer;

        /// <summary>A fully built pair (device open, reader indexed) that has not been started yet.</summary>
        private sealed class PreparedLoopPlayer
        {
            public PreparedLoopPlayer(AudioFileReader reader, WaveOutEvent waveOut, bool isA, long generation)
            {
                Reader = reader;
                WaveOut = waveOut;
                IsA = isA;
                Generation = generation;
            }

            public readonly AudioFileReader Reader;
            public readonly WaveOutEvent WaveOut;
            public readonly bool IsA;
            public readonly long Generation;
        }

        public bool IsRunning => _isRunning;
        public bool IsLooping => _loopMode && (_loopWaveOutA?.PlaybackState == PlaybackState.Playing ||
                                                _loopWaveOutB?.PlaybackState == PlaybackState.Playing);
        public int AudioFileCount => _audioFiles?.Length ?? 0;

        /// <summary>
        /// Fires when a mind wipe audio effect is triggered
        /// </summary>
        public event EventHandler? MindWipeTriggered;
        
        public double FrequencyPerHour
        {
            get => _frequencyPerHour;
            set => _frequencyPerHour = Math.Clamp(value, 1, 180);
        }
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                // Update live if playing
                if (_audioReader != null)
                {
                    try { _audioReader.Volume = (float)_volume; } catch { }
                }
                // Update loop players
                if (_loopReaderA != null)
                {
                    try { _loopReaderA.Volume = (float)_volume; } catch { }
                }
                if (_loopReaderB != null)
                {
                    try { _loopReaderB.Volume = (float)_volume; } catch { }
                }
            }
        }
        
        public MindWipeService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10) // Check every 10 seconds for better high-frequency support
            };
            _timer.Tick += Timer_Tick;
            
            LoadAudioFiles();
        }
        
        private void LoadAudioFiles()
        {
            try
            {
                // User-chosen custom clip wins over the built-in folder (a short ~2s clip is recommended).
                var customPath = App.Settings?.Current?.MindWipeAudioPath;
                if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    _audioFiles = new[] { customPath };
                    App.Logger?.Information("MindWipe: Using custom audio file {Path}", customPath);
                    return;
                }

                var audioFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "mindwipe");

                App.Logger?.Information("MindWipe: Looking for audio files in {Path}", audioFolderPath);
                
                if (!Directory.Exists(audioFolderPath))
                {
                    // Create the directory so user knows where to put files
                    Directory.CreateDirectory(audioFolderPath);
                    App.Logger?.Warning("MindWipe: Created empty folder at {Path} - add audio files here!", audioFolderPath);
                    _audioFiles = Array.Empty<string>();
                    return;
                }
                
                _audioFiles = Directory.GetFiles(audioFolderPath, "*.*")
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                
                if (_audioFiles.Length == 0)
                {
                    App.Logger?.Warning("MindWipe: No .mp3/.wav/.ogg files found in {Path}", audioFolderPath);
                }
                else
                {
                    App.Logger?.Information("MindWipe: Loaded {Count} audio files: {Files}", 
                        _audioFiles.Length, 
                        string.Join(", ", _audioFiles.Select(Path.GetFileName)));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to load audio files");
                _audioFiles = Array.Empty<string>();
            }
        }
        
        /// <summary>
        /// Reload audio files from disk (call after adding new files)
        /// </summary>
        public void ReloadAudioFiles()
        {
            LoadAudioFiles();
        }
        
        public void Start(double frequencyPerHour, double volume)
        {
            if (_isRunning)
            {
                App.Logger?.Debug("MindWipe: Already running, updating settings");
                UpdateSettings(frequencyPerHour, volume);
                return;
            }
            
            _frequencyPerHour = frequencyPerHour;
            _volume = volume;
            _sessionMode = false;
            _isRunning = true;
            _cts = new CancellationTokenSource();
            
            _timer.Start();

            // Update Discord presence
            App.DiscordRpc?.SetMindWipeActivity();

            App.Logger?.Information("MindWipe: Started (frequency: {Freq}/hour, volume: {Vol}%, files: {Count})",
                frequencyPerHour, volume * 100, _audioFiles?.Length ?? 0);
        }

        /// <summary>
        /// Start in session mode with escalating frequency
        /// </summary>
        public void StartSession(int baseFrequencyMultiplier)
        {
            if (_isRunning) return;

            _sessionMode = true;
            _sessionBaseFrequency = baseFrequencyMultiplier;
            _sessionStartTime = DateTime.Now;
            _isRunning = true;
            _cts = new CancellationTokenSource();

            _timer.Start();

            // Update Discord presence
            App.DiscordRpc?.SetMindWipeActivity();

            App.Logger?.Information("MindWipe: Started in session mode (base multiplier: {Base})",
                baseFrequencyMultiplier);
        }

        public void Stop()
        {
            var wasRunning = _isRunning;
            _isRunning = false;

            // Audio teardown FIRST, inline, and UNCONDITIONAL. App.KillAllAudio can reach us from
            // the panic fallback thread, and the DispatcherTimer below has UI-thread affinity -
            // stopping it first would throw there and leave the audio playing, which is the one
            // thing panic must never do. NAudio itself has no thread affinity.
            // Ungated on purpose: TriggerOnce plays a one-shot with the service STOPPED (six call
            // sites do), and PlayAudio's publish check is deliberately ungated to match - so an
            // early return here left panic with no way to reach a clip that was still being built.
            StopCurrentAudio();
            StopLoop();

            // Session/timer/state teardown only matters if there was a session.
            if (!wasRunning) return;

            _cts?.Cancel();

            DispatcherHelper.RunOnUI(() => { try { _timer.Stop(); } catch { } });

            // Update Discord presence back to idle
            App.DiscordRpc?.SetIdleActivity();

            App.Logger?.Information("MindWipe: Stopped");
        }
        
        public void UpdateSettings(double frequencyPerHour, double volume)
        {
            _frequencyPerHour = frequencyPerHour;
            _volume = volume;
            // Update live volume if playing. Try/catch like the Volume setter above: PlaybackStopped
            // nulls _audioReader from the playback thread, so the field can go away (or the reader
            // can already be disposed) between the test and the assignment.
            if (_audioReader != null)
            {
                try { _audioReader.Volume = (float)_volume; } catch { }
            }
            // Update loop players
            if (_loopReaderA != null)
            {
                try { _loopReaderA.Volume = (float)_volume; } catch { }
            }
            if (_loopReaderB != null)
            {
                try { _loopReaderB.Volume = (float)_volume; } catch { }
            }
        }
        
        /// <summary>
        /// Start looping a random audio file continuously in the background with crossfade
        /// </summary>
        public void StartLoop(double volume)
        {
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files available for loop");
                return;
            }
            
            // Stop any existing playback
            StopLoop();
            
            lock (_audioLock)
            {
                _loopMode = true;
                _volume = volume;
                _loopFilePath = _audioFiles[_random.Next(_audioFiles.Length)];
                _usePlayerA = true;
            }
            _loopStartTime = DateTime.Now;
            _cleanSlateAchieved = false;

            // Get audio duration for crossfade timing
            try
            {
                using var tempReader = new AudioFileReader(_loopFilePath);
                _loopDuration = tempReader.TotalTime;
                App.Logger?.Information("MindWipe: Loop file duration: {Duration:F2}s", _loopDuration.TotalSeconds);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to get audio duration, using fallback");
                _loopDuration = TimeSpan.FromSeconds(30); // Fallback
            }
            
            // Start first player (nothing to wait for, so it plays as soon as it is built) - which
            // then immediately prepares the pair the first crossfade tick will need.
            PrepareNextLoopPlayer(playImmediately: true);

            // Setup crossfade timer - triggers slightly before track ends to start next player
            var crossfadeInterval = _loopDuration - TimeSpan.FromSeconds(CROSSFADE_OVERLAP_SECONDS);
            if (crossfadeInterval.TotalMilliseconds < 100)
            {
                crossfadeInterval = TimeSpan.FromMilliseconds(100);
            }
            
            _crossfadeTimer = new DispatcherTimer
            {
                Interval = crossfadeInterval
            };
            _crossfadeTimer.Tick += CrossfadeTimer_Tick;
            _crossfadeTimer.Start();
            
            App.Logger?.Information("MindWipe: Loop started with {File} at {Vol}% volume (crossfade: {Overlap}s)", 
                Path.GetFileName(_loopFilePath), volume * 100, CROSSFADE_OVERLAP_SECONDS);
        }
        
        private void CrossfadeTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning || !_loopMode || string.IsNullOrEmpty(_loopFilePath)) return;
            
            // Check for Clean Slate achievement (60 seconds of continuous loop)
            if (!_cleanSlateAchieved)
            {
                var elapsed = (DateTime.Now - _loopStartTime).TotalSeconds;
                if (elapsed >= 60)
                {
                    _cleanSlateAchieved = true;
                    App.Achievements?.TrackMindWipeDuration(elapsed);
                    App.Logger?.Information("MindWipe: Clean Slate achievement triggered at {Elapsed:F0}s", elapsed);
                }
            }
            
            // Play the pair prepared a whole clip ago. The tick fires only 120ms before the outgoing
            // clip ends, so BUILDING here (~half a second) is an audible gap every single cycle.
            StartPreparedLoopPlayer();
        }

        /// <summary>
        /// Starts the pair parked by the previous cycle, then immediately prepares the next one so
        /// it has a full clip length of lead time.
        /// </summary>
        private void StartPreparedLoopPlayer()
        {
            PreparedLoopPlayer? prepared;
            PreparedLoopPlayer? discarded = null;
            WaveOutEvent? displacedOut = null;
            AudioFileReader? displacedReader = null;
            var started = false;

            lock (_audioLock)
            {
                prepared = _pendingLoopPlayer;
                _pendingLoopPlayer = null;

                if (prepared != null)
                {
                    if (!_isRunning || !_loopMode || prepared.Generation != _loopGeneration)
                    {
                        // A stop beat this tick — a parked pair from a dead loop session must never
                        // be heard.
                        discarded = prepared;
                    }
                    else
                    {
                        try
                        {
                            // Play() only queues the playback thread, so it is cheap enough to hold
                            // the lock across — and holding it means no stop can slip in between
                            // starting the pair and publishing it where a stop can find it.
                            prepared.WaveOut.Play();
                            (displacedOut, displacedReader) = PublishLoopPlayerLocked(prepared);
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "MindWipe: Error starting prepared loop player");
                            discarded = prepared;
                        }
                    }
                }
            }

            if (discarded != null) DisposePair(discarded.WaveOut, discarded.Reader);
            DisposePair(displacedOut, displacedReader);

            if (started)
            {
                App.Audio?.NoteOutputSuccess();
                App.Logger?.Debug("MindWipe: Started player {Slot}", prepared!.IsA ? "A" : "B");
                // Retire the OTHER player once the overlap has elapsed.
                SchedulePlayerCleanup(!prepared.IsA);
                PrepareNextLoopPlayer(playImmediately: false);
                return;
            }

            // Nothing usable was parked (first cycle after a failed build, or the build is still
            // running). Build-and-play so the loop repairs itself instead of going silent.
            PrepareNextLoopPlayer(playImmediately: true);
        }

        /// <summary>
        /// Builds the next A/B pair off-thread and either parks it for the next crossfade tick or,
        /// for the first clip and the self-repair path, starts it as soon as it is ready.
        /// Building an AudioFileReader (mp3 index) and opening a WaveOutEvent are half-second
        /// blocking calls, and the crossfade re-does both every clip length — on the UI thread that
        /// is the app hitching every ~3 seconds for as long as the loop runs (#890).
        /// </summary>
        private void PrepareNextLoopPlayer(bool playImmediately)
        {
            // Endpoint is down — the crossfade loop would otherwise re-open a device every clip
            // length forever, blocking inside waveOutOpen each time (#778/#779). Checked before the
            // A/B turn is taken, so a skipped cycle can't desync the alternation.
            if (App.Audio?.IsOutputSuppressed == true) return;

            bool isA;
            long generation;
            string path;
            float volume;

            lock (_audioLock)
            {
                if (!_isRunning || !_loopMode || string.IsNullOrEmpty(_loopFilePath)) return;

                isA = _usePlayerA;
                // Alternate immediately: the build below is asynchronous, but the A/B turn-taking
                // has to stay in step with the crossfade timer that will start it.
                _usePlayerA = !_usePlayerA;
                generation = _loopGeneration;
                path = _loopFilePath!;
                volume = (float)_volume;
            }

            Task.Run(() =>
            {
                AudioFileReader? reader = null;
                WaveOutEvent? waveOut = null;
                try
                {
                    reader = new AudioFileReader(path) { Volume = volume };
                    waveOut = new WaveOutEvent();
                    App.Audio?.ApplyPreferredDevice(waveOut);
                    waveOut.Init(reader);
                }
                catch (Exception ex)
                {
                    DisposePair(waveOut, reader);
                    App.Logger?.Error(ex, "MindWipe: Error building loop player");
                    App.Audio?.NoteOutputFailure("mindwipe-loop", ex.Message);
                    return;
                }

                var prepared = new PreparedLoopPlayer(reader!, waveOut!, isA, generation);
                PreparedLoopPlayer? discarded = null;
                WaveOutEvent? displacedOut = null;
                AudioFileReader? displacedReader = null;
                var started = false;

                lock (_audioLock)
                {
                    // The stop decision is arbitrated HERE, on this thread. NAudio has no UI
                    // affinity, and deferring it to the dispatcher meant a wedged UI thread (the
                    // panic case) could never stop an in-flight pair, while a shutdown dropped it
                    // still playing and undisposed.
                    if (!_isRunning || !_loopMode || generation != _loopGeneration)
                    {
                        discarded = prepared;
                    }
                    else if (playImmediately)
                    {
                        try
                        {
                            prepared.WaveOut.Play();
                            (displacedOut, displacedReader) = PublishLoopPlayerLocked(prepared);
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "MindWipe: Error starting loop player");
                            discarded = prepared;
                        }
                    }
                    else
                    {
                        discarded = _pendingLoopPlayer;   // only one build is ever in flight
                        _pendingLoopPlayer = prepared;
                    }
                }

                if (discarded != null) DisposePair(discarded.WaveOut, discarded.Reader);
                DisposePair(displacedOut, displacedReader);

                if (started)
                {
                    App.Audio?.NoteOutputSuccess();
                    SchedulePlayerCleanup(!isA);
                    // A whole clip of lead time for the next one, so its tick only has to Play().
                    PrepareNextLoopPlayer(playImmediately: false);
                }
            });
        }

        /// <summary>
        /// Swaps a freshly started pair into its A/B slot and returns whatever it displaced (for the
        /// caller to dispose outside the lock). Caller must hold <see cref="_audioLock"/>.
        /// </summary>
        private (WaveOutEvent?, AudioFileReader?) PublishLoopPlayerLocked(PreparedLoopPlayer prepared)
        {
            WaveOutEvent? oldOut;
            AudioFileReader? oldReader;

            if (prepared.IsA)
            {
                oldOut = _loopWaveOutA;
                oldReader = _loopReaderA;
                _loopWaveOutA = prepared.WaveOut;
                _loopReaderA = prepared.Reader;
            }
            else
            {
                oldOut = _loopWaveOutB;
                oldReader = _loopReaderB;
                _loopWaveOutB = prepared.WaveOut;
                _loopReaderB = prepared.Reader;
            }

            // The volume slider may have moved while this player was being built.
            try { prepared.Reader.Volume = (float)_volume; } catch { }
            return (oldOut, oldReader);
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

        private void SchedulePlayerCleanup(bool cleanupA)
        {
            // Capture the pair that occupies the slot RIGHT NOW, not just which slot it is: a
            // StopLoop+StartLoop inside the delay below (clip change, stop-all-then-start) publishes
            // a brand new player into the same slot, and retiring the slot blind killed that one
            // instead - the loop then ran silent for up to a whole clip length.
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                waveOut = cleanupA ? _loopWaveOutA : _loopWaveOutB;
                reader = cleanupA ? _loopReaderA : _loopReaderB;
            }
            if (waveOut == null && reader == null) return;

            // Wait a bit longer than the overlap to ensure smooth transition, then cleanup old
            // player. No dispatcher hop: NAudio has no UI affinity, and this retirement must still
            // happen when the UI thread is wedged or already shutting down.
            Task.Delay(TimeSpan.FromSeconds(CROSSFADE_OVERLAP_SECONDS + 0.1)).ContinueWith(_ =>
            {
                try { RetireLoopPlayer(cleanupA, waveOut, reader); }
                catch { }
            });
        }

        /// <summary>
        /// Retires the pair captured when the cleanup was scheduled. The slot is cleared only if it
        /// still holds that exact pair, so a newer player published into it in the meantime survives.
        /// The captured pair is then disposed unconditionally and OUTSIDE the lock - double-disposing
        /// an already-retired pair is harmless (NAudio's Stop no-ops when stopped, and DisposePair
        /// swallows everything).
        /// </summary>
        private void RetireLoopPlayer(bool isA, WaveOutEvent? waveOut, AudioFileReader? reader)
        {
            lock (_audioLock)
            {
                if (isA)
                {
                    if (ReferenceEquals(_loopWaveOutA, waveOut)) _loopWaveOutA = null;
                    if (ReferenceEquals(_loopReaderA, reader)) _loopReaderA = null;
                }
                else
                {
                    if (ReferenceEquals(_loopWaveOutB, waveOut)) _loopWaveOutB = null;
                    if (ReferenceEquals(_loopReaderB, reader)) _loopReaderB = null;
                }
            }
            DisposePair(waveOut, reader);
        }

        private void DisposePlayerA()
        {
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                waveOut = _loopWaveOutA;
                reader = _loopReaderA;
                _loopWaveOutA = null;
                _loopReaderA = null;
            }
            DisposePair(waveOut, reader);
        }

        private void DisposePlayerB()
        {
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                waveOut = _loopWaveOutB;
                reader = _loopReaderB;
                _loopWaveOutB = null;
                _loopReaderB = null;
            }
            DisposePair(waveOut, reader);
        }

        /// <summary>
        /// Stop the looping audio
        /// </summary>
        public void StopLoop()
        {
            PreparedLoopPlayer? pending;
            lock (_audioLock)
            {
                _loopMode = false;
                _loopFilePath = null;
                _loopGeneration++;   // any player still being built, or parked, is now stale
                pending = _pendingLoopPlayer;
                _pendingLoopPlayer = null;
            }

            var timer = _crossfadeTimer;
            _crossfadeTimer = null;
            if (timer != null)
            {
                // DispatcherTimer has UI-thread affinity; everything else here does not, so a stop
                // arriving off-thread still kills the audio. A tick that drains later finds
                // _loopMode false and does nothing.
                DispatcherHelper.RunOnUI(() =>
                {
                    try
                    {
                        timer.Tick -= CrossfadeTimer_Tick;
                        timer.Stop();
                    }
                    catch { }
                });
            }

            if (pending != null) DisposePair(pending.WaveOut, pending.Reader);
            DisposePlayerA();
            DisposePlayerB();

            App.Logger?.Information("MindWipe: Loop stopped");
        }
        
        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Don't trigger random sounds if loop mode is active
            if (_loopMode) return;
            
            if (!_isRunning)
            {
                App.Logger?.Warning("MindWipe: Timer ticked but not running");
                return;
            }
            
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files loaded");
                return;
            }
            
            // Calculate probability of triggering in this 30-second window
            double probability;
            
            if (_sessionMode)
            {
                // Escalating frequency in session mode
                var elapsed = DateTime.Now - _sessionStartTime;
                var fiveMinBlocks = (int)(elapsed.TotalMinutes / 5);
                var playsThisBlock = _sessionBaseFrequency + fiveMinBlocks;
                
                // Cap at reasonable maximum (15 plays per 5 min block)
                playsThisBlock = Math.Min(playsThisBlock, 15);
                
                // 5 minutes = 30 ten-second windows
                probability = playsThisBlock / 30.0;
                
                App.Logger?.Debug("MindWipe: Session mode - Block {Block}, plays: {Plays}, prob: {Prob:P0}", 
                    fiveMinBlocks, playsThisBlock, probability);
            }
            else
            {
                // Normal mode: frequency per hour
                // 360 ten-second windows per hour
                // At 180/hour, probability = 0.5 = 50% chance per interval
                probability = _frequencyPerHour / 360.0;
                
                App.Logger?.Debug("MindWipe: Normal mode - Freq: {Freq}/h, prob: {Prob:P0}", 
                    _frequencyPerHour, probability);
            }
            
            // Generate random and check (probability > 1.0 means always trigger)
            var roll = _random.NextDouble();
            if (roll < probability)
            {
                App.Logger?.Information("MindWipe: Triggering audio (roll: {Roll:F2} < prob: {Prob:F2})", roll, probability);
                PlayAudioNow();
            }
        }
        
        private void PlayAudioNow()
        {
            if (_audioFiles == null || _audioFiles.Length == 0) return;

            try
            {
                var audioFile = _audioFiles[_random.Next(_audioFiles.Length)];
                PlayAudio(audioFile);
                App.Logger?.Debug("MindWipe: Playing {File} at volume {Vol}%",
                    Path.GetFileName(audioFile), _volume * 100);

                // Fire event for avatar/UI notification
                MindWipeTriggered?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MindWipe: Failed to play audio");
            }
        }
        
        private void PlayAudio(string filePath)
        {
            if (App.Audio?.IsOutputSuppressed == true) return; // endpoint down — stay quiet, don't spin

            float volume;
            long generation;
            lock (_audioLock)
            {
                volume = (float)_volume;
                generation = ++_playGeneration;
            }

            // Same off-thread build as the loop path: AudioFileReader (mp3 index) and waveOutOpen
            // are half-second blocking calls, and this fires from a DispatcherTimer tick up to 180
            // times an hour (#890).
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
                    // they can already point at a newer player, and disposing that one killed live
                    // audio.
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
                    App.Logger?.Error(ex, "MindWipe: Error playing audio file {Path}", filePath);
                    App.Audio?.NoteOutputFailure("mindwipe", ex.Message);
                    return;
                }

                WaveOutEvent? displacedOut = null;
                AudioFileReader? displacedReader = null;
                var started = false;

                lock (_audioLock)
                {
                    // Arbitrated on this thread, not on the dispatcher: a wedged or shut-down UI
                    // thread must still be able to stop this pair (App.KillAllAudio on panic).
                    // Deliberately NOT gated on _isRunning — TriggerOnce plays a test clip with the
                    // service stopped; the generation is what a stop bumps.
                    if (generation == _playGeneration)
                    {
                        try
                        {
                            waveOut!.Play();   // only queues the playback thread; cheap under the lock
                            displacedOut = _waveOut;
                            displacedReader = _audioReader;
                            _waveOut = waveOut;
                            _audioReader = reader;
                            // The volume slider may have moved while this player was being built.
                            try { reader!.Volume = (float)_volume; } catch { }
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "MindWipe: Error starting audio file {Path}", filePath);
                        }
                    }
                }

                DisposePair(displacedOut, displacedReader);
                if (started) App.Audio?.NoteOutputSuccess();
                else DisposePair(waveOut, reader);
            });
        }

        private void StopCurrentAudio()
        {
            WaveOutEvent? waveOut;
            AudioFileReader? reader;
            lock (_audioLock)
            {
                // Bumping the generation is what makes a stop stick: a build still in flight finds
                // itself stale and disposes instead of publishing over the top of the stop.
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
        /// Trigger a single mind wipe sound immediately (for testing)
        /// </summary>
        public void TriggerOnce()
        {
            if (_audioFiles == null || _audioFiles.Length == 0)
            {
                App.Logger?.Warning("MindWipe: No audio files available in assets/mindwipe/");
                System.Windows.MessageBox.Show(
                    Loc.Get("mindwipe_no_audio_files"),
                    Loc.Get("mindwipe_title"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            // Use settings volume for test
            _volume = App.Settings.Current.MindWipeVolume / 100.0;
            PlayAudioNow();
        }
        
        /// <summary>
        /// Get current session frequency (for UI display)
        /// </summary>
        public int GetCurrentSessionFrequency()
        {
            if (!_sessionMode) return (int)_frequencyPerHour;
            
            var elapsed = DateTime.Now - _sessionStartTime;
            var fiveMinBlocks = (int)(elapsed.TotalMinutes / 5);
            return Math.Min(_sessionBaseFrequency + fiveMinBlocks, 30);
        }
        
        public void Dispose()
        {
            Stop();
            StopCurrentAudio();
            StopLoop();
            _timer.Tick -= Timer_Tick;
        }
    }
}
