// PORTED from WPF 7.1.5 Services/Chaos/CaucusHostService.cs "bambicloud (lane D1)". The player signs
// in over there and drives their own playlist. We open a plain browser frame on the site
// (RaceWindow.CloudWindow.cs), watch the audio element their page is already playing, and follow it:
// their pause pauses the race, the Brake pauses them, the next track is the next lap.
//   page -> host: cloud-open {url?, front?}, cloud-start
//   host -> page: cloud-run, plus the track-* frames (clock, chart, progress, ended, error)
// READ-ONLY GUEST. Nothing here calls their API, logs anybody in, or reads anything of theirs beyond
// the audio element's own state and the tab title.
//
// One rule on top of WPF: the desktop's OWN download of the audio (to chart it) is a remote media
// fetch, so it only happens when MediaSource is not "local" and the player gave remote media consent
// (AppSettings.HasRemoteMediaConsent), and never from a sandbox. Without it the browser frame still
// plays (that is the player's own browsing), the run still follows the clock, and a chart that needs
// no download (an authored one, keyed by the track's id) still lands.
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>init.settings.cloud: this host owns the browser window a level opens in.</summary>
        internal const bool RaceCloudAvailable = true;

        /// <summary>The one message the player ever sees when the site is not answering. Plain, and it
        /// points at the path that always works.</summary>
        internal const string RaceCloudDownMessage = "bambicloud is not answering, load a file instead";

        /// <summary>The on-demand browser frame, built the first time the page asks for it.</summary>
        private IRaceCloudWindow? _raceCloud;
        /// <summary>The cloud clock, kept across laps: one window, one audio element, one clock.</summary>
        private CloudTrackClock? _raceCloudClock;
        /// <summary>True between "the next track started over there" and the run-ended that answers our
        /// track-ended. The next lap already owns the clock, so that stop must not take it away.</summary>
        private bool _raceCloudSwapping;
        private bool _raceCloudTrackRefused;
        private readonly CloudTrackRetry _raceRefusedCloudTrack = new();

        /// <summary>Test seams: the browser frame, the consent rule and the client.</summary>
        internal Func<IRaceCloudWindow> RaceNewCloudWindow = () => new RaceCloudWindow();
        internal Func<bool> RaceMayFetchRemote = () =>
        {
            try
            {
                var s = CoreSettings.Current;
                return s != null && !string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase) && s.HasRemoteMediaConsent;
            }
            catch { return false; }
        };

        /// <summary>A CCP_USERDATA_DIR sandbox never talks to a real site, consent or not.</summary>
        internal Func<Uri, bool> RaceNetAllows = uri => ConditioningControlPanel.Services.SandboxNet.Allows(uri);

        /// <summary>One client for the whole process. The desktop talks to the site directly: nothing
        /// of theirs is ever proxied through anything of ours.</summary>
        private static readonly HttpClient RaceCloudHttpShared = new() { Timeout = TimeSpan.FromMinutes(15) };
        internal HttpClient RaceCloudHttp = RaceCloudHttpShared;

        private void RaceRefuseCloudTrack()
        {
            _raceCloudTrackRefused = true;
            _raceCloudSwapping = false;
            RaceStopTrack();
            RaceSetCloudPaused(true);
            if (_raceRun.IsActive) RacePostTrack(new { type = "track-ended" });
            RacePostTrack(new { type = "track-error", message = Loc.Get("race_track_locked") });
        }

        private IRaceCloudWindow EnsureRaceCloud()
        {
            if (_raceCloud == null)
            {
                _raceCloud = RaceNewCloudWindow();
                _raceCloud.Message += OnRaceCloudMessage;
                _raceCloud.Hidden += OnRaceCloudHidden;
            }
            return _raceCloud;
        }

        /// <summary>cloud-open: a level tapped on the menu. Opens the window on that track's page, or
        /// brings it back if the player closed it (closing hides it, so their playlist survives). A
        /// null or off-site url falls back to the site's front door. The purchase is re-checked here,
        /// on the host: a level whose pack is not owned is refused whatever the page asked for.</summary>
        private void RaceOpenCloud(string? url = null, bool background = false)
        {
            if (!RaceCanOpenCloud(url)) { RaceRefuseCloudTrack(); return; }
            RaceQueue(() =>
            {
                try
                {
                    var cloud = EnsureRaceCloud();
                    var landing = RaceCloudWindow.IsSiteUri(url) ? url : null;
                    // Behind the game (owner, 2026-09-25): the level page loads out of the way and the
                    // race keeps the keyboard; the game's play button starts it.
                    if (background) { cloud.ShowInBackground(landing); Activate(); }
                    else cloud.ShowOrFocus(landing);
                }
                catch (Exception ex)
                {
                    Log.Warning("RaceHost.cloud-open: {E}", ex.Message);
                    RacePostTrack(new { type = "track-error", message = RaceCloudDownMessage });
                }
            });
        }

        /// <summary>The window was closed (hidden) with no cloud track in hand: take the menu's
        /// "opening" plate back down so it is not left waiting for something that is not coming.</summary>
        private void OnRaceCloudHidden()
        {
            if (_raceClock is CloudTrackClock) return;
            RacePostProgress("cancelled", 0, "", force: true);
        }

        /// <summary>cloud-start: press play over there for the player, a press of the game's own play
        /// button. The track it starts meets RacingAccess in OnRaceCloudTrack like any other (a refused
        /// source is paused there), and the window is opened behind the game if it does not exist.</summary>
        private void RaceStartCloudTrack()
        {
            RaceQueue(() =>
            {
                try
                {
                    EnsureRaceCloud().RequestStart();
                    Activate();
                }
                catch (Exception ex) { Log.Warning("RaceHost.cloud-start: {E}", ex.Message); }
            });
        }

        /// <summary>Every cloud-* frame the watcher posts, on the UI thread.</summary>
        internal void OnRaceCloudMessage(JObject o)
        {
            if (_raceDisposed) return;
            try
            {
                switch ((string?)o["type"])
                {
                    case "cloud-track":
                        OnRaceCloudTrack(o);
                        break;
                    case "cloud-clock":
                        if (_raceCloudTrackRefused || _raceCloudClock == null || !ReferenceEquals(_raceClock, _raceCloudClock)) break;
                        _raceCloudClock.Update((double?)o["t"] ?? 0, (bool?)o["playing"] ?? false, (double?)o["durationSec"] ?? 0);
                        break;
                    case "cloud-play":
                        OnRaceCloudPlay();
                        break;
                    case "cloud-pause":
                        if (_raceCloudClock == null || !ReferenceEquals(_raceClock, _raceCloudClock)) break;
                        _raceCloudClock.Update(_raceCloudClock.PositionSec, false, _raceCloudClock.DurationSec);
                        PostRaceClock();   // the run reads playing:false and holds where it is
                        break;
                    case "cloud-ended":
                        OnRaceCloudEnded();
                        break;
                    case "cloud-failed":
                        RacePostTrack(new { type = "track-error", message = RaceCloudDownMessage });
                        break;
                }
            }
            catch (Exception ex) { Log.Warning("RaceHost.OnCloudMessage: {E}", ex.Message); }
        }

        /// <summary>A new source started playing over there. It becomes the clock straight away, so the
        /// run follows the voice from the first second; the chart catches up behind it.</summary>
        private void OnRaceCloudTrack(JObject o)
        {
            string src = (string?)o["src"] ?? "";
            string title = ((string?)o["title"] ?? "").Trim();
            double dur = (double?)o["durationSec"] ?? 0;
            if (string.IsNullOrWhiteSpace(src)) return;
            if (!RaceCanOpenCloud(src)) { _raceRefusedCloudTrack.Remember(o); RaceRefuseCloudTrack(); return; }

            // A new track while a run is live is the next lap: end this one the way the file running
            // out does. The run-ended that answers must not take the new clock away, hence the flag.
            bool live = _raceRun.IsActive && _raceClock is CloudTrackClock;
            RaceCancelAnalysis(postCancelled: false);
            try { _racePlayer?.Stop(); } catch (Exception ex) { Log.Debug("RaceHost.cloud stop local: {E}", ex.Message); }

            _raceCloudTrackRefused = false;
            _raceRefusedCloudTrack.Clear();
            _raceTrackName = string.IsNullOrWhiteSpace(title) ? "bambicloud" : title;
            _raceLastProgressUtc = DateTime.MinValue;
            _raceCloudClock ??= new CloudTrackClock(RaceSetCloudPaused);
            _raceCloudClock.Update(0, true, dur);
            _raceClock = _raceCloudClock;
            if (live)
            {
                _raceCloudSwapping = true;
                RacePostTrack(new { type = "track-ended" });
            }
            StartRaceTrackClock();
            PostRaceClock();
            Log.Information("RaceHost: cloud track {Name} ({Dur:0.0}s){Lap}", _raceTrackName, dur, live ? ", next lap" : "");
            RaceBeginCloudChart(src);
        }

        /// <summary>Their player started. If the race is still sitting on the menu, this is what starts
        /// the run: the audio is the clock, so the run begins when the audio does.</summary>
        private void OnRaceCloudPlay()
        {
            // Their watcher announces each source once. After a purchase, the next real Play retries
            // that denied source; the purchase itself never starts audio.
            if (_raceRefusedCloudTrack.TakeIfAllowed(RaceCanOpenCloud) is { } retry) OnRaceCloudTrack(retry);
            if (_raceCloudTrackRefused || !RaceCanLaunch()) { RaceSetCloudPaused(true); return; }
            if (_raceCloudClock == null || !ReferenceEquals(_raceClock, _raceCloudClock)) return;
            _raceCloudClock.Update(_raceCloudClock.PositionSec, true, _raceCloudClock.DurationSec);
            if (!_raceRun.IsActive)
            {
                RacePostTrack(new { type = "cloud-run" });
                // a play pressed over there hands the keyboard back to the race
                RaceQueue(Activate);
            }
            StartRaceTrackClock();
            PostRaceClock();
        }

        /// <summary>Their element ran out. Same ending a local file gets: the page winds the lap up and
        /// the next cloud-track starts the next one.</summary>
        private void OnRaceCloudEnded()
        {
            if (!ReferenceEquals(_raceClock, _raceCloudClock) || _raceCloudClock == null) return;
            _raceCloudClock.Stop();
            StopRaceTrackClock();
            RacePostTrack(new { type = "track-ended" });
            Log.Information("RaceHost: cloud track ended");
        }

        /// <summary>The Brake's half of the bargain: cloud-set-paused into their page.</summary>
        private void RaceSetCloudPaused(bool on)
        {
            if (Dispatcher.UIThread.CheckAccess()) _raceCloud?.PostToPage(new { type = "cloud-set-paused", on });
            else RaceQueue(() => _raceCloud?.PostToPage(new { type = "cloud-set-paused", on }));
        }

        // ---- charting what they are playing ----
        //
        // The chart comes from the audio itself, which means the desktop pulls the file down and runs
        // the SAME analysis a picked file gets: cache hit by hash first (so a track charted once, from
        // anywhere, is instant), then the energy pass, then the word pass. The page runs the plain
        // seeded road until the partial chart lands and swaps in mid-run.
        //
        // The file lives under race/cloud/ for as long as the analysis takes and is deleted after,
        // cancelled or not: audio never stays on this machine, charts hold timestamps and labels.

        /// <summary>Where a cloud track waits while it is being charted. Emptied as it goes.</summary>
        private static string RaceCloudTempRoot => Path.Combine(CorePaths.UserData, "race", "cloud");

        /// <summary>Pull the track down and chart it. Supersedes any analysis already running, the same
        /// way a second file pick does.</summary>
        private void RaceBeginCloudChart(string src)
        {
            RaceSweepCloudTemp();
            var cts = new CancellationTokenSource();
            _raceAnalysisCts = cts;
            int gen = ++_raceAnalysisGen;
            string name = _raceTrackName;
            bool mayFetch;
            try { mayFetch = RaceMayFetchRemote() && RaceNetAllows(new Uri(src)); }
            catch { mayFetch = false; }
            if (mayFetch) RacePostProgress("fetching", 0, name, force: true);
            RaceAnalysis = Task.Run(() => RaceChartCloudTrackAsync(src, name, gen, mayFetch, cts.Token, cts));
        }

        private async Task RaceChartCloudTrackAsync(string src, string name, int gen, bool mayFetch, CancellationToken ct, CancellationTokenSource cts)
        {
            RaceAnalysisMessageGeneration.Value = gen;
            string? temp = null;
            try
            {
                // The doors before the download. (a) and (b) by cloudId need only the url, and all three
                // of (a), (b) and (c) answer off the hash, which is a length and a megabyte. An authored
                // or already charted track therefore costs no download at all.
                string cloudId = AuthoredCharts.CloudIdFrom(src);
                if (RaceTryChartWithoutAnalysis("", cloudId, name, name)) return;

                if (!mayFetch)
                {
                    // No remote media consent (or a sandbox): the desktop asks the site for nothing.
                    // The run keeps following their player on the seeded road; the plate comes down.
                    Log.Information("RaceHost: cloud track {Name} is not charted, remote media is off", name);
                    if (_raceAnalysisGen == gen) RacePostProgress("cancelled", 0, "", force: true);
                    return;
                }

                string? hash = await RaceProbeCloudHashAsync(src, name, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (hash != null && RaceTryChartWithoutAnalysis(hash, cloudId, name, name)) return;

                temp = await RaceDownloadCloudTrackAsync(src, name, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // From here it is the ordinary path, cache and all - the hash is computed off these
                // bytes by TrackDecoder.HashFile, so it matches a local chart of the same file.
                RaceAnalyzeTrack(temp, name, gen, ct, cts, displayName: name, cloudId: cloudId);
            }
            catch (OperationCanceledException)
            {
                if (_raceAnalysisGen == gen) RacePostProgress("cancelled", 0, "", force: true);
                Log.Information("RaceHost: cloud chart cancelled for {Name}", name);
            }
            catch (Exception ex)
            {
                Log.Warning("RaceHost: cloud chart failed for {Name}: {E}", name, ex.Message);
                // Fail loud, and leave the seeded road running: the run is already following the clock.
                if (_raceAnalysisGen == gen) RacePostTrack(new { type = "track-error", message = RaceCloudDownMessage });
            }
            finally
            {
                RaceDeleteCloudTemp(temp);
                if (ReferenceEquals(_raceAnalysisCts, cts)) _raceAnalysisCts = null;
                try { cts.Dispose(); } catch (Exception ex) { Log.Debug("RaceHost.cloud cts: {E}", ex.Message); }
            }
        }

        /// <summary>The CHART.md hash of a track without downloading it: a HEAD for the length and a
        /// Range for the first 1 MiB. Null means "ask the ordinary way": no length, no ranges, a short
        /// read, anything. ONE retry and no more, the same bargain the download makes.</summary>
        private async Task<string?> RaceProbeCloudHashAsync(string src, string name, CancellationToken ct)
        {
            if (!RaceCloudWindow.IsSiteUri(src)) return null;
            try
            {
                long? length = await RaceCloudLengthAsync(src, ct).ConfigureAwait(false);
                if (length is not > 0) return null;

                int want = (int)Math.Min(TrackDecoder.HashHead, length.Value);
                using var req = new HttpRequestMessage(HttpMethod.Get, src);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, want - 1);
                using var resp = await RaceCloudHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                // A 200 here is the CDN ignoring the range and offering the whole file. Walk away: the
                // download path is about to ask for it properly, with progress.
                if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) return null;

                var head = new byte[want];
                using (var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    int filled = 0, got;
                    while (filled < want && (got = await body.ReadAsync(head.AsMemory(filled, want - filled), ct).ConfigureAwait(false)) > 0)
                        filled += got;
                    if (filled != want) return null;
                }

                string hash = TrackDecoder.HashBytes(length.Value, head);
                Log.Information("RaceHost: cloud hash for {Name} off {Bytes} bytes of {Total}: {Hash}", name, want, length.Value, hash);
                return hash;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Information("RaceHost: cloud hash probe failed for {Name} ({E}), downloading instead", name, ex.Message);
                return null;
            }
        }

        /// <summary>The file's length: a HEAD, or the total out of a one-byte range's Content-Range for
        /// a CDN that will not answer HEAD. Null when neither says.</summary>
        private async Task<long?> RaceCloudLengthAsync(string src, CancellationToken ct)
        {
            try
            {
                using var head = new HttpRequestMessage(HttpMethod.Head, src);
                using var resp = await RaceCloudHttp.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength is > 0)
                    return resp.Content.Headers.ContentLength;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { Log.Debug("RaceHost.cloud head: {E}", ex.Message); }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, src);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var resp = await RaceCloudHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                return resp.Content.Headers.ContentRange?.Length;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { Log.Debug("RaceHost.cloud range probe: {E}", ex.Message); }
            return null;
        }

        /// <summary>Stream the audio to a temp file, reporting bytes as track-progress. ONE retry and no
        /// more: a site that answered wrong twice is a site to stop asking.</summary>
        private async Task<string> RaceDownloadCloudTrackAsync(string src, string name, CancellationToken ct)
        {
            // Their own addresses only. A player's page could carry any src at all, and this is the one
            // place a url off that page turns into a request from the desktop.
            if (!RaceCloudWindow.IsSiteUri(src))
                throw new InvalidOperationException("the audio is not an address we may fetch");

            Directory.CreateDirectory(RaceCloudTempRoot);
            string path = Path.Combine(RaceCloudTempRoot, "cloud-" + Guid.NewGuid().ToString("N") + RaceCloudExtension(src));
            Exception? last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var resp = await RaceCloudHttp.GetAsync(src, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;
                    using var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    {
                        var buffer = new byte[1 << 16];
                        long got = 0;
                        int read;
                        while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            got += read;
                            if (total is > 0) RacePostProgress("fetching", (double)got / total.Value, name);
                        }
                        await file.FlushAsync(ct).ConfigureAwait(false);
                        Log.Information("RaceHost: cloud audio down for {Name} ({Bytes} bytes)", name, got);
                    }
                    return path;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { RaceDeleteCloudTemp(path); throw; }
                catch (Exception ex)
                {
                    last = ex;
                    RaceDeleteCloudTemp(path);
                    Log.Information("RaceHost: cloud download attempt {N} failed: {E}", attempt, ex.Message);
                }
            }
            throw last ?? new IOException("the audio would not come down");
        }

        /// <summary>The url's own extension when it is one the decoder knows, else mp3.</summary>
        internal static string RaceCloudExtension(string src)
        {
            try
            {
                var ext = Path.GetExtension(new Uri(src).AbsolutePath).ToLowerInvariant();
                return ext is ".mp3" or ".m4a" or ".wav" or ".ogg" or ".flac" or ".wma" ? ext : ".mp3";
            }
            catch { return ".mp3"; }
        }

        private static void RaceDeleteCloudTemp(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Debug("RaceHost.cloud temp delete: {E}", ex.Message); }
        }

        /// <summary>A crash mid-chart is the one way a download outlives its analysis. Anything left in
        /// the folder from a previous session goes before the next one starts.</summary>
        private static void RaceSweepCloudTemp()
        {
            try
            {
                if (!Directory.Exists(RaceCloudTempRoot)) return;
                var cutoff = DateTime.UtcNow.AddHours(-6);
                foreach (var file in Directory.GetFiles(RaceCloudTempRoot, "cloud-*"))
                {
                    try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                    catch (Exception ex) { Log.Debug("RaceHost.cloud sweep file: {E}", ex.Message); }
                }
            }
            catch (Exception ex) { Log.Debug("RaceHost.cloud sweep: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll's cloud half: the browser frame goes with the race (that is what
        /// stops their audio), and a refused source is forgotten.</summary>
        private void DisposeRaceCloud()
        {
            _raceCloudTrackRefused = false;
            _raceRefusedCloudTrack.Clear();
            _raceCloudSwapping = false;
            try { _raceCloud?.Dispose(); } catch (Exception ex) { Log.Debug("RaceHost: cloud window dispose: {E}", ex.Message); }
            _raceCloud = null;
            _raceCloudClock = null;
        }
    }
}
