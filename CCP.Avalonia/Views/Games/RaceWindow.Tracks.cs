// PORTED from WPF 7.1.5 Services/Chaos/CaucusHostService.cs "track charts" (CHART.md "Host protocol
// additions (PR c6)"): the page asks for a file, the host charts it on a worker and answers with
// progress, one or two charts, a 250 ms clock and an ended note. Nothing about the audio ever leaves
// the machine: the chart carries timestamps and labels.
//   page -> host: track-pick, track-play, track-pause {on}, track-stop, track-cancel
//   host -> page: track-progress {stage, pct, name}, track-chart {chart, partial, authored},
//                 track-clock {t, playing, durationSec}, track-ended, track-error {message}
// The audio plays through the port's LibVLC layer (RaceWindow.TrackPlayer.cs); the analysis is Core
// Services/Race, the same code WPF runs.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Models.Race;
using ConditioningControlPanel.Services.Race;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private IRaceTrackPlayer? _racePlayer;
        /// <summary>Whatever is making the sound right now: the local player, or the audio element the
        /// player is driving in the BambiCloud window. Null with no track loaded.</summary>
        private ITrackClock? _raceClock;
        private DispatcherTimer? _raceTrackClock;
        private CancellationTokenSource? _raceAnalysisCts;
        private string _raceTrackName = "";
        /// <summary>Throttle for track-progress: at most five posts a second, whatever the pass does.</summary>
        private DateTime _raceLastProgressUtc = DateTime.MinValue;
        /// <summary>Bumped per pick so a superseded worker knows to keep quiet.</summary>
        private int _raceAnalysisGen;
        private static readonly AsyncLocal<int?> RaceAnalysisMessageGeneration = new();

        /// <summary>Test seams: the player and the file dialog (null = the player cancelled).</summary>
        internal Func<IRaceTrackPlayer> RaceNewPlayer = () => new RaceTrackPlayer();
        internal Func<Task<string?>>? RacePickFile;

        /// <summary>The analysis in flight, if any; a test's way to wait for the worker.</summary>
        internal Task RaceAnalysis { get; private set; } = Task.CompletedTask;

        /// <summary>Every host to page track message goes through here. The analysis runs on a worker
        /// (Post marshals to the UI thread); a post from a session or a pick that is no longer current
        /// is dropped at both ends of that hop.</summary>
        private void RacePostTrack(object msg)
        {
            int session = _raceSession;
            int? analysis = RaceAnalysisMessageGeneration.Value;
            bool Current() => session == _raceSession && !_raceDisposed && (analysis == null || analysis == _raceAnalysisGen);
            if (!Current()) return;
            if (Dispatcher.UIThread.CheckAccess()) { try { Post(msg); } catch { } }
            else Dispatcher.UIThread.Post(() => { try { if (Current()) Post(msg); } catch { } });
        }

        /// <summary>track-progress, throttled to five posts a second so a fast pass cannot flood the
        /// bridge. A forced post (a stage change, a cancel) always goes.</summary>
        private void RacePostProgress(string stage, double pct, string name, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _raceLastProgressUtc).TotalMilliseconds < 200) return;
            _raceLastProgressUtc = now;
            RacePostTrack(new { type = "track-progress", stage, pct = Math.Clamp(pct, 0, 1), name });
        }

        /// <param name="authored">A person wrote this one: never partial and never replaced.</param>
        private void RacePostChart(TrackChart chart, bool partial, bool authored = false)
            => RacePostTrack(new { type = "track-chart", chart, partial, authored });

        /// <summary>track-pick: the file dialog. A cancelled dialog is not an error, it is a cancelled
        /// progress post.</summary>
        private void RacePickTrack()
        {
            RaceQueue(async () =>
            {
                try
                {
                    string? path = RacePickFile != null ? await RacePickFile() : await RaceShowFileDialog();
                    if (_raceDisposed) return;
                    if (string.IsNullOrEmpty(path)) { RacePostProgress("cancelled", 0, "", force: true); return; }
                    RaceBeginTrack(path);
                }
                catch (Exception ex)
                {
                    Log.Warning("RaceHost.track-pick: {E}", ex.Message);
                    RacePostTrack(new { type = "track-error", message = ex.Message });
                }
            });
        }

        private async Task<string?> RaceShowFileDialog()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load a track",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.wma", "*.flac", "*.ogg" } },
                    FilePickerFileTypes.All,
                },
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        /// <summary>A pick landed: load it into the player right away so track-play can start it,
        /// then chart it on a worker.</summary>
        internal void RaceBeginTrack(string path)
        {
            RaceCancelAnalysis(postCancelled: false);
            _raceTrackName = Path.GetFileName(path);
            _raceLastProgressUtc = DateTime.MinValue;
            RaceLoadTrackFile(path);

            var cts = new CancellationTokenSource();
            _raceAnalysisCts = cts;
            int gen = ++_raceAnalysisGen;
            string name = _raceTrackName;
            var ct = cts.Token;
            RaceAnalysis = Task.Run(() => RaceAnalyzeTrack(path, name, gen, ct, cts));
        }

        private void RaceLoadTrackFile(string path)
        {
            try
            {
                if (_racePlayer == null)
                {
                    _racePlayer = RaceNewPlayer();
                    _racePlayer.Ended += OnRaceTrackEnded;
                }
                _racePlayer.Stop();
                _racePlayer.Load(path);
                _raceClock = new LocalTrackClock(_racePlayer);
                StartRaceTrackClock();
                Log.Information("RaceHost: track loaded {Name} ({Dur:0.0}s)", _raceTrackName, _racePlayer.DurationSec);
            }
            catch (Exception ex)
            {
                Log.Warning("RaceHost: track load failed: {E}", ex.Message);
                RacePostTrack(new { type = "track-error", message = ex.Message });
            }
        }

        /// <summary>Doors (a), (b) and (c) of the lookup: an authored chart the player wrote, an authored
        /// chart we ship, or a generated one already in the cache. True when one of them posted a chart,
        /// in which case NOTHING is analysed. A cached "none" chart is charted again once a Vosk model
        /// has appeared; an authored chart never is.</summary>
        private bool RaceTryChartWithoutAnalysis(string hash, string? cloudId, string name, string? displayName)
        {
            var authored = AuthoredCharts.Find(hash, cloudId, out string door);
            if (authored != null)
            {
                if (string.IsNullOrWhiteSpace(authored.Source.Name) && !string.IsNullOrWhiteSpace(displayName))
                    authored.Source.Name = displayName!;
                Log.Information("RaceHost: chart for {Name} via {Door}", name, door);
                RacePostChart(authored, partial: false, authored: true);
                return true;
            }

            var cached = TrackChartCache.TryLoad(hash);
            if (cached == null) return false;
            if (!AuthoredCharts.IsAuthored(cached) && cached.Analysis?.Words != "vosk-v1" && TrackWordSpotter.ModelAvailable)
                return false;
            Log.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorCache);
            RacePostChart(cached, partial: false, authored: AuthoredCharts.IsAuthored(cached));
            return true;
        }

        /// <summary>The whole analysis, off the UI thread. Every call into the decoder, the analyzer,
        /// the cache and the word spotter sits inside this one try: a file the decoder hates, a missing
        /// Vosk model or a half-written cache entry becomes a track-error, never a crash.</summary>
        /// <param name="displayName">The name to chart under when the file's own is a temp name (a cloud track).</param>
        /// <param name="cloudId">The stable name of the file on the CDN, when the track came from there.</param>
        private void RaceAnalyzeTrack(string path, string name, int gen, CancellationToken ct, CancellationTokenSource cts,
            string? displayName = null, string? cloudId = null)
        {
            var priorGeneration = RaceAnalysisMessageGeneration.Value;
            RaceAnalysisMessageGeneration.Value = gen;
            try
            {
                ct.ThrowIfCancellationRequested();
                RacePostProgress("decode", 0, name, force: true);

                string hash = TrackDecoder.HashFile(path);
                if (RaceTryChartWithoutAnalysis(hash, cloudId, name, displayName)) return;
                Log.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorGenerated);

                var pcm = TrackDecoder.Decode(path, new Progress<double>(v => RacePostProgress("decode", v, name)), ct);
                ct.ThrowIfCancellationRequested();

                var chart = TrackAnalyzer.Energy(pcm, new Progress<double>(v => RacePostProgress("energy", v, name)), ct);
                ct.ThrowIfCancellationRequested();
                chart.Analysis.Partial = true;
                if (displayName != null) chart.Source.Name = displayName;
                RacePostChart(chart, partial: true);
                Log.Information("RaceHost: partial chart for {Name}: {Events} events", name, chart.Events?.Count ?? 0);

                // The word pass is the slow one, which is why the page already has a playable chart.
                // It only LISTENS (Vosk over the file); nothing here ever speaks.
                var lexicon = TrackLexicon.Build();
                var words = TrackWordSpotter.Spot(pcm, lexicon, new Progress<double>(v => RacePostProgress("words", v, name)), ct);
                ct.ThrowIfCancellationRequested();
                TrackChartWords.Apply(chart, words, lexicon);
                chart.Analysis.Partial = false;
                TrackChartCache.Save(chart);
                RacePostChart(chart, partial: false);
                Log.Information("RaceHost: charted {Name}: {Events} events", name, chart.Events?.Count ?? 0);
            }
            catch (OperationCanceledException)
            {
                // A newer pick superseded this one: that pick owns the plate now, so say nothing.
                if (_raceAnalysisGen == gen) RacePostProgress("cancelled", 0, "", force: true);
                Log.Information("RaceHost: track analysis cancelled for {Name}", name);
            }
            catch (Exception ex)
            {
                Log.Warning("RaceHost: track analysis failed for {Name}: {E}", name, ex.Message);
                RacePostTrack(new { type = "track-error", message = ex.Message });
            }
            finally
            {
                RaceAnalysisMessageGeneration.Value = priorGeneration;
                if (ReferenceEquals(_raceAnalysisCts, cts)) _raceAnalysisCts = null;
                try { cts.Dispose(); } catch { }
            }
        }

        /// <summary>Drop an analysis in flight. With nothing running there is no worker to answer, so
        /// an explicit track-cancel is answered here.</summary>
        private void RaceCancelAnalysis(bool postCancelled)
        {
            var cts = _raceAnalysisCts;
            _raceAnalysisCts = null;
            if (cts == null)
            {
                if (postCancelled) RacePostProgress("cancelled", 0, "", force: true);
                return;
            }
            try { cts.Cancel(); }
            catch (Exception ex) { Log.Debug("RaceHost.CancelAnalysis: {E}", ex.Message); }
        }

        /// <summary>track-play: the run started, so a local file starts from its own zero. A cloud
        /// element is already running (that is what started the run) and is left alone.</summary>
        private void RaceTrackPlay()
        {
            if (_raceCloudTrackRefused && _raceClock is CloudTrackClock) return;
            var c = _raceClock;
            if (c == null) return;
            c.Start();
            StartRaceTrackClock();
            PostRaceClock();
        }

        /// <summary>track-pause {on}: the Brake, a host pause and a video pop all land here. On the
        /// cloud source this is the frame that pauses their player.</summary>
        private void RaceTrackPause(bool on)
        {
            if (!on && _raceCloudTrackRefused && _raceClock is CloudTrackClock) return;
            var c = _raceClock;
            if (c == null) return;
            c.SetPaused(on);
            PostRaceClock();
        }

        /// <summary>End of run, exit or teardown: the audio stops, the clock stops and any analysis
        /// still grinding away is dropped. A cloud lap turnover is the one stop that passes straight
        /// through: the next track already owns the clock and its chart is already on the way.</summary>
        private void RaceStopTrack()
        {
            bool swapping = _raceCloudSwapping;
            _raceCloudSwapping = false;
            if (swapping) return;
            StopRaceTrackClock();
            RaceCancelAnalysis(postCancelled: false);
            try { _raceClock?.Stop(); }
            catch (Exception ex) { Log.Debug("RaceHost.StopTrack: {E}", ex.Message); }
        }

        private void OnRaceTrackEnded()
        {
            StopRaceTrackClock();
            RacePostTrack(new { type = "track-ended" });
            Log.Information("RaceHost: track ended");
        }

        /// <summary>The 250 ms clock the page integrates between. UI thread only.</summary>
        private void StartRaceTrackClock()
        {
            if (_raceDisposed) return;
            if (_raceTrackClock == null)
            {
                _raceTrackClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _raceTrackClock.Tick += (_, _) => PostRaceClock();
            }
            _raceTrackClock.Start();
        }

        private void StopRaceTrackClock()
        {
            try { _raceTrackClock?.Stop(); } catch { }
        }

        internal void PostRaceClock()
        {
            var c = _raceClock;
            if (c == null) { StopRaceTrackClock(); return; }
            RacePostTrack(new
            {
                type = "track-clock",
                t = Math.Round(c.PositionSec, 3),
                playing = c.IsPlaying,
                durationSec = Math.Round(c.DurationSec, 3),
            });
        }

        /// <summary>WPF DisposeAll's track half: nothing this window started keeps sounding or
        /// grinding after it closes (panic closes the window, so panic lands here too).</summary>
        private void DisposeRaceTracks()
        {
            ++_raceAnalysisGen;
            _raceCloudSwapping = false;
            RaceStopTrack();
            _raceTrackClock = null;
            _raceClock = null;
            try { _racePlayer?.Dispose(); } catch (Exception ex) { Log.Debug("RaceHost: player dispose: {E}", ex.Message); }
            _racePlayer = null;
        }
    }
}
