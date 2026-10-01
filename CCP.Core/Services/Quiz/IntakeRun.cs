using System;
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

            try
            {
                CoreProgression.AddXP(Xp(run), "Other");
                for (var i = 0; i < MantraCredits(run); i++) CoreProgression.TrackMantraCompleted();
            }
            catch (Exception ex) { Log.Debug("IntakeRun: XP grant failed: {E}", ex.Message); }

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
    }
}
