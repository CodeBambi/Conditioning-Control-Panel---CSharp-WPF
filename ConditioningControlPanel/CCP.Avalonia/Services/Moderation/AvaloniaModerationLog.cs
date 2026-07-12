using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;

namespace ConditioningControlPanel.Avalonia.Services.Moderation;

/// <summary>
/// Cross-platform compliance moderation log for the Avalonia head — a faithful port of the WPF
/// <c>Services/Moderation/ModerationLog.cs</c> CCBill record-retention sink.
/// </summary>
/// <remarks>
/// <para><b>File:</b> <c>{UserDataPath}/logs/moderation.log</c> (the Avalonia head's log directory). The
/// WPF head uses <c>%APPDATA%/ConditioningControlPanel/logs/moderation.log</c>; the port adapts the same
/// <c>{root}/logs/moderation.log</c> semantics to <see cref="IAppEnvironment.UserDataPath"/> (which the
/// port intentionally collapsed from Roaming to Local — see <c>AvaloniaAppEnvironment.cs</c>).</para>
/// <para><b>Line format</b> (pipe-delimited, fixed columns) matches WPF verbatim:
/// <c>{ISO8601 UTC} | {category} | {source} | {session_id_hash} | {model_hint}</c></para>
/// <para><b>Rotation:</b> 10 MB per file, 5 archives (~50 MB ceiling) — same as WPF.</para>
/// <para><b>CRITICAL (WPF contract):</b> no message bodies. No user identifiers beyond the opaque
/// per-launch hash. The file documents moderation activity for CCBill record-retention without becoming
/// a subpoena target for user content. Do NOT extend this to log message text.</para>
/// <para>Existing Serilog logging (the injected <see cref="ILogger{TCategoryName}"/>) is preserved; the
/// compliance file sink is additive.</para>
/// </remarks>
public sealed class AvaloniaModerationLog : IModerationLog
{
    // 10 MB per file, keep 5 archives -> ~50 MB ceiling (WPF Services/Moderation/ModerationLog.cs).
    private const long MaxBytes = 10L * 1024L * 1024L;
    private const int MaxArchives = 5;

    private readonly IAppEnvironment _env;
    private readonly ILogger<AvaloniaModerationLog> _logger;
    private readonly ModerationSession _session;
    private readonly object _writeLock = new();

    // ModerationSession is created internally: this service is registered as a DI singleton, so the
    // per-launch hash is stable for the app lifetime — equivalent to WPF's singleton App.ModerationLog
    // holding one ModerationSession.
    public AvaloniaModerationLog(IAppEnvironment env, ILogger<AvaloniaModerationLog> logger)
    {
        _env = env;
        _logger = logger;
        _session = new ModerationSession();
    }

    private string LogDir => Path.Combine(_env.UserDataPath, "logs");
    private string LogPath => Path.Combine(LogDir, "moderation.log");

    /// <summary>
    /// Records a moderation hit. <paramref name="source"/> is one of <c>input</c>, <c>output</c>, or
    /// <c>edit</c>; <paramref name="modelHint"/> identifies the provider/model. Mirrors WPF
    /// <c>ModerationLog.Record</c>.
    /// </summary>
    public void Record(ProhibitedCategory category, string source, string modelHint)
    {
        // Preserve existing Serilog behavior (the prior stub logged here only).
        _logger.LogInformation(
            "Moderation hit recorded: category={Category}, source={Source}, modelHint={ModelHint}",
            category, source, modelHint);
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();
                // WPF Services/Moderation/ModerationLog.cs:Record — ISO8601 UTC | category | source | hash | model.
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-ddTHH:mm:ssZ} | {1} | {2} | {3} | {4}{5}",
                    DateTime.UtcNow,
                    SanitizeField(category.ToString()),
                    SanitizeField(source),
                    SanitizeField(_session.GetSessionIdHash()),
                    SanitizeField(modelHint),
                    Environment.NewLine);
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Best-effort. A failed log entry must not break the user's chat (WPF parity). The broader
            // event is already captured via the Serilog call above and at the AI-service call sites.
        }
    }

    /// <summary>
    /// Records a PromptValidator flag from a prompt-editor surface. Lives at <c>source=edit</c> and uses
    /// the <c>PromptEditorFlag</c> pseudo-category. Detail format:
    /// <c>surface=&lt;surface&gt;;field=&lt;fieldName&gt;;matches=&lt;count&gt;</c> — count is the regex
    /// hit count (NOT the matched text). Mirrors WPF <c>ModerationLog.RecordEdit</c>.
    /// </summary>
    public void RecordEdit(string fieldName, int count, string source)
    {
        _logger.LogInformation(
            "Moderation edit recorded: field={FieldName}, matches={Count}, source={Source}",
            fieldName, count, source);
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();
                // WPF Services/Moderation/ModerationLog.cs:RecordEdit — PromptEditorFlag | edit + detail.
                var detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "surface={0};field={1};matches={2}",
                    SanitizeField(source),
                    SanitizeField(fieldName),
                    count);
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-ddTHH:mm:ssZ} | PromptEditorFlag | edit | {1} | {2}{3}",
                    DateTime.UtcNow,
                    SanitizeField(_session.GetSessionIdHash()),
                    detail,
                    Environment.NewLine);
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Best-effort, same as Record() (WPF parity).
        }
    }

    /// <summary>
    /// Renames the active file to <c>moderation.log.1</c> (oldest archive bumped first, capped at
    /// <see cref="MaxArchives"/>) when it exceeds <see cref="MaxBytes"/>. Best-effort. Mirrors WPF
    /// <c>ModerationLog.RotateIfNeeded</c>.
    /// </summary>
    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var fi = new FileInfo(LogPath);
            if (fi.Length < MaxBytes) return;

            // Bump archives: moderation.log.4 -> .5, .3 -> .4, ..., .log -> .1 (WPF parity).
            for (int i = MaxArchives; i >= 1; i--)
            {
                var src = i == 1 ? LogPath : LogPath + "." + (i - 1);
                var dst = LogPath + "." + i;
                if (!File.Exists(src)) continue;
                if (File.Exists(dst))
                {
                    try { File.Delete(dst); } catch { /* ignore */ }
                }
                try { File.Move(src, dst); } catch { /* ignore — next call will retry */ }
            }
        }
        catch
        {
            // Rotation is best-effort; keep appending to the current file until the next call succeeds.
        }
    }

    /// <summary>
    /// Defense-in-depth scrubbing of any control char that could break the pipe-delimited format. Fields
    /// are short, predictable values from the caller — this is paranoia, not a real attack surface.
    /// Mirrors WPF <c>ModerationLog.SanitizeField</c>.
    /// </summary>
    private static string SanitizeField(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return new string(s.Select(c =>
            c == '|' ? '/' :
            c == '\r' || c == '\n' || c == '\t' ? ' ' :
            c
        ).ToArray());
    }
}
