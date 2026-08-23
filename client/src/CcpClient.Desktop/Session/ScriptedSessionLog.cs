using System.Globalization;
using System.Text.Json;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Session;

/// <summary>What kind of media a log entry is — upstream's <c>MediaType</c>
/// (<c>Models/SessionLog.cs:7-11</c>), in upstream's own member order.</summary>
public enum ScriptedMediaKind
{
    /// <summary>Upstream <c>Video</c>: one mandatory clip that really started.</summary>
    Video,

    /// <summary>Upstream <c>Image</c>: one image a flash really drew.</summary>
    Image,
}

/// <summary>
/// One thing the session put on screen, and when — upstream's <c>MediaLogEntry</c>
/// (<c>Models/SessionLog.cs:13-36</c>) <b>minus its two content fields</b>.
///
/// <para><b>Upstream records the file.</b> Its entry carries <c>FilePath</c> (<c>:24-25</c>) and
/// <c>DisplayName</c> (<c>:27-28</c>), written into a JSON file that survives the run and read back
/// by a recap row the user can click to reveal the file in Explorer
/// (<c>Windows/SessionCompleteWindow.xaml.cs:173-194</c>).</para>
///
/// <para><b>This port cannot and does not.</b> The rule is older than this slice and was written
/// with it in mind: "No path and no file name: the clips are the user's own media and this record
/// reaches event handlers and, <i>one day, a log</i>"
/// (<see cref="Effects.MandatoryVideoEvent"/>, <c>Effects/MandatoryVideoEffect.cs:9-10</c>), and
/// "a flash is a COUNT everywhere a log or a UI can see it"
/// (<c>Effects/FlashImagesEffect.cs:8-10</c>, <c>:151-155</c>), which is in turn the DTRH
/// manifest's rule for the whole build ("the build logs COUNTS ONLY, never names/paths",
/// <c>Features/Dtrh/DtrhUserMedia.cs:27</c>). It is enforced by construction rather than by
/// discipline: the paths never leave the module — they are handed straight to the surface and the
/// event a log can subscribe to has never carried them — so there is no seam here that could
/// record one. Widening it is a path/logging boundary change and belongs to an owner decision
/// (<c>docs/constitution.md</c>, Boundaries), not to a slice.</para>
///
/// <para>Upstream's absolute <c>Timestamp</c> (<c>:15-16</c>) is not carried either, for a smaller
/// reason: it is <see cref="ScriptedSessionLog.StartedAt"/> plus this offset, and upstream's own
/// recap and history rows only ever render the offset (<c>SessionCompleteWindow.xaml.cs:238-241</c>).
/// </para>
/// </summary>
/// <param name="Kind">Video or image.</param>
/// <param name="SessionTime">How far into the session it happened — upstream's
/// <c>SessionTime</c> (<c>Models/SessionLog.cs:30-35</c>).</param>
public sealed record ScriptedMediaEntry(ScriptedMediaKind Kind, TimeSpan SessionTime);

/// <summary>
/// One finished scripted session, as the recap and the history read it — upstream's
/// <c>SessionLog</c> (<c>Models/SessionLog.cs:38-76</c>) without its <c>xp_earned</c> member, which
/// nothing in this build computes (the same refusal the rack row's <c>+N XP</c> cell already
/// carries, <c>Views/Pages/SessionRackNotices.RowMeta</c>).
/// </summary>
public sealed record ScriptedSessionLog
{
    /// <summary>Upstream <c>session_id</c> (<c>:40-41</c>), and the second half of the file
    /// name.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>Upstream <c>session_name</c> (<c>:43-44</c>).</summary>
    public string SessionName { get; init; } = "";

    /// <summary>Upstream <c>session_icon</c> (<c>:46-47</c>).</summary>
    public string SessionIcon { get; init; } = "";

    /// <summary>Upstream <c>session_difficulty</c> (<c>:49-50</c>).</summary>
    public ScriptedSessionDifficulty Difficulty { get; init; } = ScriptedSessionDifficulty.Easy;

    /// <summary>When it started (upstream <c>started_at</c>, <c>:52-53</c>). The wall clock the run
    /// took at START, so a clock jump leaves this and <see cref="Duration"/> disagreeing exactly as
    /// they do upstream (<c>Services/Session/SessionLogService.cs:57</c>, <c>:86-87</c>).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When it ended (upstream <c>ended_at</c>, <c>:55-56</c>).</summary>
    public DateTimeOffset EndedAt { get; init; }

    /// <summary>How long it ran — the run's own reconciled elapsed, past the clock-jump guard,
    /// which is the number upstream hands <c>EndSession</c> (<c>SessionLogService.cs:87</c>,
    /// <c>Services/Session/SessionEngine.cs:423</c>).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when it reached its duration, false when it was stopped early. Upstream
    /// <c>completed</c> (<c>:61-62</c>), and the one field the recap's whole headline turns
    /// on.</summary>
    public bool Completed { get; init; }

    /// <summary>Everything the session put on screen, in the order it happened — upstream
    /// <c>media</c> (<c>:67-68</c>).</summary>
    public IReadOnlyList<ScriptedMediaEntry> Media { get; init; } = [];

    /// <summary>Videos in <see cref="Media"/>, the left half of upstream's count cell
    /// (<c>Windows/SessionCompleteWindow.xaml.cs:122-124</c>).</summary>
    public int VideoCount => Media.Count(m => m.Kind == ScriptedMediaKind.Video);

    /// <summary>Images in <see cref="Media"/>, the right half of the same cell — upstream derives
    /// it by subtraction (<c>:123</c>).</summary>
    public int ImageCount => Media.Count - VideoCount;
}

/// <summary>
/// The post-session media log — upstream's <c>Services/Session/SessionLogService.cs</c>: it
/// "captures every video played and image flashed during a session, persists the result to disk
/// (capped at MaxRetainedLogs files), and raises LogReady when a session ends so the post-session
/// dialog can render it" (<c>:10-13</c>).
///
/// <para><b>What "during a session" means here.</b> Upstream subscribes to the flash and video
/// services at <c>BeginSession</c> and releases them at <c>EndSession</c>, and every handler ALSO
/// checks <c>_activeLog == null</c> (<c>:150-171</c>, <c>:182</c>, <c>:214</c>). The port keeps the
/// second guard and drops the first: <see cref="ScriptedSessionRun.Running"/> is the same predicate
/// read from the one object that knows it, and the composition root gates every record on it
/// (<see cref="SessionParticipant"/>). A subscription that is never taken cannot be leaked, which
/// is the failure upstream's <c>_isSubscribed</c> flag exists to prevent. Media outside a scripted
/// session is not logged either way — upstream only ever calls <c>BeginSession</c> from the
/// scripted engine's own start (<c>Services/Session/SessionEngine.cs:266</c>).</para>
///
/// <para><b>This class owns no clock.</b> Every time it is handed is the run's own reconciled
/// elapsed or the run's own wall readings, so nothing here can disagree with the countdown the user
/// was watching, and nothing here reads <c>DateTime.Now</c>. Upstream stamps its offsets from a
/// second, unguarded wall clock (<c>:183-184</c>, <c>:218-219</c>), so a mid-session clock jump
/// moves upstream's media offsets and does not move the port's — a divergence in the user's
/// favour, recorded rather than smoothed.</para>
/// </summary>
public sealed class ScriptedSessionLogStore
{
    /// <summary>Upstream's retention cap (<c>Services/Session/SessionLogService.cs:20</c>). It is
    /// behaviour, not configuration: it bounds both the prune (<c>:260-268</c>) and the read
    /// (<c>:125</c>).</summary>
    public const int MaxRetainedLogs = 20;

    /// <summary>The folder name beside the user's documents, upstream's own
    /// (<c>:37</c>).</summary>
    public const string FolderName = "session_logs";

    /// <summary>
    /// Upstream's <c>PersistenceMinDuration</c> (<c>:24</c>) and the comment above it: "Sessions
    /// shorter than this with no media are not persisted - prevents accidental starts/stops from
    /// cluttering the log folder" (<c>:22-23</c>).
    /// </summary>
    public static readonly TimeSpan PersistenceMinDuration = TimeSpan.FromSeconds(30);

    private readonly ILogSink _log;
    private readonly List<ScriptedMediaEntry> _pending = [];
    private readonly object _gate = new();

    /// <param name="dataDirectory">The user's data directory — the one the session's documents
    /// already live in, so the logs land beside them and inside whatever root the composition root
    /// resolved.</param>
    /// <param name="log">Diagnostics. Content-free: counts and outcomes, never a name.</param>
    public ScriptedSessionLogStore(string dataDirectory, ILogSink log)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        Folder = Path.Combine(dataDirectory, FolderName);
    }

    /// <summary>
    /// Raised once per session that ends, however it ended, and AFTER the file has been written —
    /// upstream's <c>LogReady</c> (<c>:31</c>, raised at <c>:101</c>).
    ///
    /// <para><b>It fires even when nothing was persisted</b> (<c>:95-101</c>: the persist is inside
    /// an <c>if</c>, the raise is outside it), which is the behaviour that keeps a 20-second abort's
    /// recap on screen while its file is deliberately not written.</para>
    /// </summary>
    public event Action<ScriptedSessionLog>? LogReady;

    /// <summary>Where the logs are (upstream's <c>LogsFolder</c>, <c>:33</c>). Created at the first
    /// write rather than at construction (upstream creates it eagerly, <c>:38</c>) — a folder that
    /// appears the day a session is really logged is the same user outcome with one less boot-time
    /// write.</summary>
    public string Folder { get; }

    /// <summary>
    /// Upstream's persist rule, whole and by itself (<c>:93-94</c>):
    /// <c>log.Media.Count &gt; 0 || duration &gt;= PersistenceMinDuration</c>.
    ///
    /// <para><b>It is BOTH conditions and it is an OR</b>, which is the part a sentence about it
    /// tends to lose: a run is skipped only when it produced NO media AND ran for less than 30
    /// seconds. A five-second run that flashed one image IS persisted; a silent run of exactly 30
    /// seconds IS persisted, because the comparison is <c>&gt;=</c>.</para>
    /// </summary>
    public static bool ShouldPersist(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Media.Count > 0 || log.Duration >= PersistenceMinDuration;
    }

    /// <summary>
    /// The file a log is written to — upstream's name, verbatim (<c>:242</c>):
    /// <c>{StartedAt:yyyyMMdd_HHmmss}_{SanitizeId(SessionId)}.json</c>.
    ///
    /// <para>The leading timestamp is what makes an ORDINARY STRING SORT chronological, which is
    /// the whole mechanism behind both the newest-first read (<c>:122-123</c>) and the
    /// oldest-first eviction (<c>:262-268</c>). Two runs of the same session ending in the same
    /// second collide onto one name and the second overwrites the first — upstream's behaviour
    /// exactly, and unreachable for a real session, whose duration is authored in whole
    /// minutes.</para>
    ///
    /// <para>The stamp is UTC where upstream's is local, following the port's own clock
    /// (<c>Session/ScriptedClock.cs</c>, slice 1's recorded divergence). It sorts identically; the
    /// history row converts back to local before showing a user a time.</para>
    /// </summary>
    public static string FileNameFor(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var stamp = log.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{stamp}_{SanitizeId(log.SessionId)}.json";
    }

    /// <summary>
    /// One flash that came due, as this log records it: <paramref name="count"/> image entries at
    /// the same offset — upstream's own loop over the paths the flash drew
    /// (<c>:185-196</c>, one <c>MediaLogEntry</c> per path).
    ///
    /// <para>A draw that produced nothing records nothing, which is upstream's
    /// <c>if (paths == null || paths.Count == 0) return;</c> (<c>:178</c>) and the port's
    /// <see cref="Effects.FlashEvent.PoolWasEmpty"/> arriving at the same answer from the same
    /// fact.</para>
    /// </summary>
    public void RecordImages(int count, TimeSpan at)
    {
        if (count <= 0)
        {
            return;
        }

        lock (_gate)
        {
            for (var i = 0; i < count; i++)
            {
                _pending.Add(new ScriptedMediaEntry(ScriptedMediaKind.Image, at));
            }
        }
    }

    /// <summary>One clip that really started — upstream's <c>OnVideoStarted</c>
    /// (<c>:205-230</c>). Upstream drops a firing with no path (<c>:210</c>); the port's module
    /// raises <c>Fired</c> only for a clip it really put up
    /// (<c>Effects/MandatoryVideoEffect.cs:245-269</c>), so the same firings are recorded.</summary>
    public void RecordVideo(TimeSpan at)
    {
        lock (_gate)
        {
            _pending.Add(new ScriptedMediaEntry(ScriptedMediaKind.Video, at));
        }
    }

    /// <summary>
    /// Finalize the log for a session that has ended — upstream's <c>EndSession</c>
    /// (<c>:76-103</c>), in upstream's order: take the media and close the log, persist it when the
    /// rule says so, prune, then raise <see cref="LogReady"/>.
    ///
    /// <para>Called for COMPLETION AND ABORT alike, because upstream is
    /// (<c>Services/Session/SessionEngine.cs:420-424</c>: "Aborted sessions still get a log … so the
    /// post-session dialog shows what played even when the user cut things short").</para>
    /// </summary>
    /// <returns>The log, whether or not it was written to disk.</returns>
    public ScriptedSessionLog Complete(ScriptedSessionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        ScriptedMediaEntry[] media;
        lock (_gate)
        {
            media = [.. _pending];
            _pending.Clear();
        }

        var log = new ScriptedSessionLog
        {
            SessionId = outcome.Session.Id,
            SessionName = outcome.Session.Name,
            SessionIcon = outcome.Session.Icon,
            Difficulty = outcome.Session.Difficulty,
            StartedAt = outcome.StartedAt,
            EndedAt = outcome.EndedAt,
            Duration = outcome.Elapsed,
            Completed = outcome.Completed,
            Media = media,
        };

        if (ShouldPersist(log))
        {
            if (Persist(log))
            {
                Prune();
            }
        }
        else
        {
            _log.Log(
                "session log: not persisted — no media and under "
                + $"{PersistenceMinDuration.TotalSeconds:0} seconds (SessionLogService.cs:93-94)");
        }

        LogReady?.Invoke(log);
        return log;
    }

    /// <summary>
    /// The retained logs, newest first — upstream's <c>LoadRecentLogs</c> (<c>:105-139</c>),
    /// including its cap on the READ (<c>:125</c>) and its refusal to throw on a corrupt file
    /// (<c>:133-136</c>). An absent folder is an empty list (<c>:112</c>).
    ///
    /// <para>The cap is applied to the read as well as to the prune, so a folder that somehow holds
    /// more than <see cref="MaxRetainedLogs"/> — a copy dropped in by hand, a prune that could not
    /// delete — still shows the user twenty.</para>
    /// </summary>
    public IReadOnlyList<ScriptedSessionLog> LoadRecent() =>
        [.. Files().Take(MaxRetainedLogs).Select(Read).OfType<ScriptedSessionLog>()];

    /// <summary>Upstream's <c>SanitizeId</c>, verbatim (<c>:276-286</c>): an empty id becomes
    /// <c>session</c> and every character the platform forbids in a file name becomes an
    /// underscore. It is the reason a session id can never write outside
    /// <see cref="Folder"/>.</summary>
    private static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "session";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    /// <summary>Every log file, newest name first — the sort both upstream reads run
    /// (<c>:122-123</c>, <c>:262-263</c>).</summary>
    private string[] Files()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return [];
            }

            var files = Directory.GetFiles(Folder, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            Array.Reverse(files);
            return files;
        }
        catch (IOException ex)
        {
            _log.Log($"session log: could not enumerate the log folder ({ex.GetType().Name})");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Log($"session log: could not enumerate the log folder ({ex.GetType().Name})");
            return [];
        }
    }

    private ScriptedSessionLog? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ScriptedSessionLog>(
                File.ReadAllText(path), ScriptedSession.JsonOptions);
        }
        catch (JsonException)
        {
            // Upstream skips a corrupt file rather than throwing (:133-136): one bad file must not
            // take the whole history window with it.
            _log.Log("session log: a log file would not parse and was skipped");
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Upstream's <c>TryPersist</c> (<c>:238-252</c>): write, and warn rather than throw
    /// when the write fails.</summary>
    private bool Persist(ScriptedSessionLog log)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(
                Path.Combine(Folder, FileNameFor(log)),
                JsonSerializer.Serialize(log, ScriptedSession.JsonOptions));
            _log.Log($"session log: persisted a run with {log.Media.Count} media entries");
            return true;
        }
        catch (IOException ex)
        {
            _log.Log($"session log: could not write the log ({ex.GetType().Name})");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Log($"session log: could not write the log ({ex.GetType().Name})");
            return false;
        }
    }

    /// <summary>
    /// Upstream's <c>TryPrune</c> (<c>:254-274</c>), and the half of the cap that is easy to get
    /// backwards: the new log is ALREADY WRITTEN by the time this runs (<c>:97-98</c> — persist,
    /// then prune), so the twenty-first run is never the one refused. The files are sorted by name,
    /// reversed so the newest is first, and everything from index
    /// <see cref="MaxRetainedLogs"/> onward is deleted — which is the OLDEST, and only the oldest.
    /// </summary>
    private void Prune()
    {
        var files = Files();
        if (files.Length <= MaxRetainedLogs)
        {
            return;
        }

        for (var i = MaxRetainedLogs; i < files.Length; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (IOException ex)
            {
                _log.Log($"session log: could not evict an old log ({ex.GetType().Name})");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Log($"session log: could not evict an old log ({ex.GetType().Name})");
            }
        }
    }
}
