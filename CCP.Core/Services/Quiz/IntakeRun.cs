using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// The head-independent half of the Graded Intake host (WPF Services/Quiz/IntakeHostService.cs):
    /// the per-run latches the page protocol needs, and what a finished run is worth. Each head owns
    /// its window and its carrier; both feed page messages through one of these, so the rule that
    /// the weekly pass is spent only by a parsed <c>quiz-result</c> lives once.
    /// </summary>
    public sealed class IntakeRun
    {
        /// <summary>"Top marks" bar (#870): 90% of the compliance score, same as the classic quiz.</summary>
        public const double TopMarksPercent = 90.0;

        /// <summary>Neutral niche (WPF IntakeNiche.Fallback).</summary>
        public const string FallbackNiche = "default";

        /// <summary>The niche the active mod asks for (WPF IntakeNiche.Resolve): mod id, then manifest
        /// tags, then the legacy SissyHypno reading; otherwise the neutral <see cref="FallbackNiche"/>.</summary>
        public static string ResolveNiche(string? modId, IEnumerable<string>? tags, bool sissyContentMode)
        {
            // BambiSleep ships its own pass card and prompt bank, so it names its own niche here
            // like every other themed built-in. It was the Fallback until 6.9.4 made the fallback
            // neutral, which left "bambi" unreachable from every code path.
            if (modId == BuiltInMods.BambiSleepId) return "bambi";
            if (modId == BuiltInMods.DronificationId) return "drone";
            if (modId == BuiltInMods.SissyHypnoId) return "sissy";
            if (modId == BuiltInMods.LockedId) return "circe";

            // Locked's own tags ("locked"/"chastity") read as circe too.
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag, "bambi", StringComparison.OrdinalIgnoreCase)) return "bambi";
                    if (string.Equals(tag, "drone", StringComparison.OrdinalIgnoreCase)) return "drone";
                    if (string.Equals(tag, "sissy", StringComparison.OrdinalIgnoreCase)) return "sissy";
                    if (string.Equals(tag, "circe", StringComparison.OrdinalIgnoreCase)) return "circe";
                    if (string.Equals(tag, "locked", StringComparison.OrdinalIgnoreCase)) return "circe";
                    if (string.Equals(tag, "chastity", StringComparison.OrdinalIgnoreCase)) return "circe";
                }
            }

            // Only the positive SissyHypno reading counts. ContentMode's other value means
            // "no sissy mod", not "bambi", so everything else lands on the neutral niche.
            if (sissyContentMode) return "sissy";
            return FallbackNiche;
        }

        /// <summary>DisabledAssetPaths as a lookup of root-relative forward-slash paths (WPF
        /// IntakeHostService.BuildDisabledAssetSet, #762/#798/#619).</summary>
        public static HashSet<string> DisabledAssetSet(IEnumerable<string>? disabledPaths) => new(
            (disabledPaths ?? Enumerable.Empty<string>()).Select(p => (p ?? "").Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        public static bool IsAssetActive(HashSet<string> disabled, string root, string fullPath)
        {
            if (disabled.Count == 0) return true;
            string rel;
            try { rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/'); }
            catch { return true; }   // unrelatable path: never silently drop content over a path quirk
            return !disabled.Contains(rel);
        }

        /// <summary>The page's MediaManifest gifs/images (contracts.js): a random sample of up to
        /// <paramref name="take"/> active files of each kind under <c>images/</c>, as escaped paths
        /// relative to <paramref name="assetsRoot"/>. Each head prefixes its own origin for them.</summary>
        public static (string[] Gifs, string[] Images) SampleMedia(
            string assetsRoot, IEnumerable<string>? disabledPaths, int take = 18)
        {
            var gifs = new List<string>();
            var stills = new List<string>();
            var imagesRoot = Path.Combine(assetsRoot, "images");
            var disabled = DisabledAssetSet(disabledPaths);
            if (Directory.Exists(imagesRoot))
            {
                foreach (var file in Directory.EnumerateFiles(imagesRoot, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".gif" && ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
                    if (!IsAssetActive(disabled, assetsRoot, file)) continue;   // unchecked in the Assets tree
                    (ext == ".gif" ? gifs : stills).Add(file);
                }
            }
            string[] Sample(List<string> pool)
            {
                // partial Fisher-Yates: take random items without shuffling the whole list
                for (int i = 0; i < Math.Min(take, pool.Count); i++)
                {
                    int j = Random.Shared.Next(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                return pool.GetRange(0, Math.Min(take, pool.Count)).Select(file =>
                    string.Join('/', Path.GetRelativePath(assetsRoot, file).Replace('\\', '/')
                        .Split('/').Select(Uri.EscapeDataString))).ToArray();
            }
            return (Sample(gifs), Sample(stills));
        }

        /// <summary>Stable per-install fiction id ("Subject #0417"), kept in intake_subject.txt under
        /// <paramref name="userDataDir"/> (WPF IntakeHostService.GetSubjectId).</summary>
        public static string SubjectId(string userDataDir)
        {
            try
            {
                var path = Path.Combine(userDataDir, "intake_subject.txt");
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length is > 0 and <= 8) return existing;
                }
                var id = Random.Shared.Next(1, 10000).ToString("D4");
                Directory.CreateDirectory(userDataDir);
                File.WriteAllText(path, id);
                return id;
            }
            catch { return "0000"; }
        }

        /// <summary>Page silent this long after ready -> recover (WPF StartHeartbeatWatch).</summary>
        public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(20);

        /// <summary>Has this run reported a quiz-result? After it, leaving is a wind-down, not a walk-out.</summary>
        public bool ResultReceived { get; private set; }

        /// <summary>A wind-down is under way (host end-run, page exit, abort).</summary>
        public bool Exiting { get; set; }

        public DateTime LastHeartbeatUtc { get; private set; } = DateTime.UtcNow;

        /// <summary>heartbeat / pong / ready.</summary>
        public void Beat(DateTime? nowUtc = null) => LastHeartbeatUtc = nowUtc ?? DateTime.UtcNow;

        public bool IsHeartbeatSilent(DateTime nowUtc) => !Exiting && nowUtc - LastHeartbeatUtc > HeartbeatTimeout;

        /// <summary>Page messages count only from the document the host loaded: same scheme, authority
        /// and path; query and fragment may differ (WPF ChaosWebViewHost.SameDocument).</summary>
        public static bool SameDocument(Uri source, Uri target) =>
            source.IsAbsoluteUri && target.IsAbsoluteUri
            && source.Scheme == target.Scheme && source.Authority == target.Authority
            && source.AbsolutePath == target.AbsolutePath;

        /// <summary>quiz-result { result } -> the run, latching <see cref="ResultReceived"/>. Null (and no
        /// latch) when the payload does not parse: a garbled message is not a completed intake.</summary>
        public QuizRunResult? AcceptResult(JObject message)
        {
            QuizRunResult? run = null;
            try { run = message["result"]?.ToObject<QuizRunResult>(); }
            catch (Exception ex) { Log.Warning("IntakeRun: bad quiz-result: {E}", ex.Message); }
            if (run != null) ResultReceived = true;
            return run;
        }

        /// <summary>True exactly once for a page-initiated quit (exit / intake-close) before any result:
        /// the caller then raises QuizAbandoned ("held_back"). Latches, since both messages can arrive.</summary>
        public bool TakeWalkOut()
        {
            if (ResultReceived || Exiting) return false;
            ResultReceived = true;
            return true;
        }

        /// <summary>Score, top-marks flag and category the achievement bridge is told about.</summary>
        public static (int Score, bool Perfect, string Category) Grade(QuizRunResult run)
        {
            var pct = run.MaxScore > 0 ? run.TotalScore / run.MaxScore * 100.0 : 0.0;
            var niche = string.IsNullOrWhiteSpace(run.Niche) ? FallbackNiche : run.Niche.Trim().ToLowerInvariant();
            return ((int)Math.Round(run.TotalScore), run.MaxScore > 0 && pct >= TopMarksPercent, niche);
        }

        /// <summary>Affirmed mantras that earn credit, capped at 5 so endless laps cannot farm it.</summary>
        public static int MantraCredits(QuizRunResult run) => Math.Min(run.AffirmedMantras?.Count ?? 0, 5);

        /// <summary>25 base + up to 50 for depth + 5 per credited mantra, capped at 100.</summary>
        public static int Xp(QuizRunResult run) =>
            Math.Min(25 + (int)Math.Round(Math.Clamp(run.PeakDepth, 0, 1) * 50) + MantraCredits(run) * 5, 100);

        /// <summary>
        /// Everything a completed run earns, in WPF's order: achievement signal, XP, mantra credit,
        /// the weekly pass, then the drafted session written into <paramref name="sessionsFolder"/>.
        /// The pass is spent before the draft on purpose: the intake WAS completed even if drafting
        /// fails, and the user must not be charged twice for our error. Returns the session and its
        /// path, or nulls when drafting failed (the caller tells the page either way).
        /// </summary>
        public static (Session? Session, string? Path) Complete(
            QuizRunResult run, string sessionsFolder, Action consumePass,
            Action<int, bool, bool, string>? quizCompleted)
        {
            try
            {
                var (score, perfect, category) = Grade(run);
                quizCompleted?.Invoke(score, true, perfect, category);
            }
            catch (Exception ex) { Log.Debug("IntakeRun: quiz-completed signal failed: {E}", ex.Message); }

            // A failed XP grant skips the mantra credit, as WPF's shared try did.
            if (CoreProgression.AddXP(Xp(run), "Other"))
                for (var i = 0; i < MantraCredits(run); i++) CoreProgression.TrackMantraCompleted();
            else Log.Debug("IntakeRun: XP grant failed; mantra credit skipped");

            try { consumePass(); }
            catch (Exception ex) { Log.Debug("IntakeRun: pass consume failed: {E}", ex.Message); }

            try
            {
                var session = QuizSessionGenerator.GenerateSession(run);
                Directory.CreateDirectory(sessionsFolder);
                var path = UniqueSessionPath(sessionsFolder, SessionFileService.GetExportFileName(session));
                new SessionFileService().ExportSession(session, path);
                session.SourceFilePath = path;
                Log.Information("IntakeRun: drafted session '{Name}' -> {Path}", session.Name, path);
                return (session, path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "IntakeRun: session draft/save failed");
                return (null, null);
            }
        }

        /// <summary>A free <c>.session.json</c> path in <paramref name="folder"/>: collisions are the norm
        /// (same niche, same tier), so suffix -2, -3, ... (bounded at 999) instead of overwriting.</summary>
        public static string UniqueSessionPath(string folder, string fileName)
        {
            const string ext = ".session.json";
            var stem = fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? fileName[..^ext.Length]
                : Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(stem)) stem = "intake-session";

            var candidate = Path.Combine(folder, stem + ext);
            for (var i = 2; i <= 999 && File.Exists(candidate); i++)
                candidate = Path.Combine(folder, $"{stem}-{i}{ext}");
            return candidate;
        }

        /// <summary>8-byte PNG signature - enough to reject arbitrary bytes from the page.</summary>
        public static bool LooksLikePng(byte[] b) =>
            b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

        // ---- remote media (WPF IntakeHostService "remote media (Phase 2, Contract 3)") ----
        //   page -> host { type:'need-remote' }; host -> page { type:'assets-append', images } (when any)
        //   then always { type:'online-status', ok, error, added } so the page clears its in-flight latch.

        public const string RemoteConsumerId = "intake";
        public const int RemoteBatchCap = 24;   // per reply; the page asks again if it wants more

        /// <summary>True when remote media may appear anywhere in the app: <c>HasRemoteMediaConsent</c>,
        /// never the raw consent flag.</summary>
        public static bool RemoteMediaEnabled(AppSettings? s) =>
            s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;

        /// <summary>The intake's own tenant over the app-wide niche selection, stills only.</summary>
        internal static Fyp.Online.FypOnlineCoordinator RemoteCoordinator() =>
            Fyp.Online.FypOnlineCoordinator.For(RemoteConsumerId,
                () => Fyp.Online.FypOnlineCoordinator.ResolveChannels(
                    CoreSettings.Current.FypOnlineNiches, CoreSettings.Current.FypOnlineCustomSubs),
                Fyp.Online.FeedMediaKind.Image);

        /// <summary>Every entry re-checked as a still before it reaches the page, capped at
        /// <see cref="RemoteBatchCap"/>.</summary>
        internal static List<string> RemoteStills(IEnumerable<Fyp.FypAssetManifest.Entry> entries)
        {
            var urls = new List<string>();
            foreach (var e in entries)
            {
                if (!Fyp.Online.RemoteMediaFormats.Validate(e, Fyp.Online.FeedMediaKind.Image, out var reason))
                {
                    Log.Debug("IntakeHost: rejected remote entry {Id}: {Reason}", e.Id, reason);
                    continue;
                }
                urls.Add(e.Url);
                if (urls.Count >= RemoteBatchCap) break;
            }
            return urls;
        }

        private const int MaxSpiralPngBase64Chars = 12 * 1024 * 1024;  // ~9MB decoded ceiling

        /// <summary>
        /// <c>intake-save-image { pngBase64, index }</c> - write one recap spiral as a PNG under
        /// <paramref name="folder"/> (moved from WPF IntakeHostService.OnSaveSpiralImage). Validation is
        /// authoritative here: base64 ceiling, PNG magic, and a filename built entirely here (the page
        /// contributes only a clamped index), so nothing the page sends can steer the write out of the folder.
        /// Error is one of the page-known codes too-big / bad-image / io-failed.
        /// </summary>
        public static (string? Path, string? Error) SaveSpiralImage(JObject o, string folder, DateTime now)
        {
            try
            {
                var b64 = (string?)o["pngBase64"];
                if (string.IsNullOrEmpty(b64) || b64.Length > MaxSpiralPngBase64Chars) return (null, "too-big");

                byte[] bytes;
                try { bytes = Convert.FromBase64String(b64); }
                catch { bytes = Array.Empty<byte>(); }
                if (!LooksLikePng(bytes)) return (null, "bad-image");

                Directory.CreateDirectory(folder);
                var index = Math.Clamp((int?)o["index"] ?? 1, 1, 99);
                var full = Path.Combine(folder, $"intake-spiral-{now:yyyyMMdd-HHmmss}-{index:D2}.png");
                File.WriteAllBytes(full, bytes);
                Log.Information("IntakeHostService: saved recap spiral -> {Path}", full);
                return (full, null);
            }
            catch (Exception ex)
            {
                Log.Warning("IntakeHostService.OnSaveSpiralImage: {E}", ex.Message);
                return (null, "io-failed");
            }
        }
    }
}
