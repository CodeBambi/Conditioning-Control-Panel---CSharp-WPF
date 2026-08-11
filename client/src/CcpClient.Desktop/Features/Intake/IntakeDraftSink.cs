using System.Text.Json;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the session-drafting SINK (IntakeHostService.cs:421-427 + UniqueSessionPath
/// :515-528 parity): writes the drafted-session document (marked never-runnable) into
/// &lt;dataDir&gt;/intake/drafted_sessions/ as <c>{name}.session.json</c> with collision
/// suffixes -2…-999, atomic .tmp-swap. The sink is the whole delivery — there is no
/// session engine to register with (the degraded-delivery contract; WPF's
/// RegisterExternallySavedSession + "Run it now" toast are the unfiled session-library row).
/// </summary>
public sealed class IntakeDraftSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _folder;
    private readonly Action<string> _log;

    public IntakeDraftSink(string folder, Action<string> log)
    {
        _folder = folder;
        _log = log;
    }

    /// <summary>Write one draft; returns the file path (the session-drafted reply payload).
    /// Presence+shape logging only — the draft NAME derives from the run's content.</summary>
    public string Write(IntakeDraft.IntakeDraftDocument draft)
    {
        Directory.CreateDirectory(_folder);
        var baseName = SanitizeFileName(draft.Name);
        var path = UniquePath(baseName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(draft, JsonOptions));
        File.Move(tmp, path, overwrite: true);
        _log($"intake: draft sunk ({draft.Difficulty}, runnable:false; collision-suffixed path)"); // presence+shape — never the name/path
        return path;
    }

    /// <summary>Host-built filename (page/run strings never become paths raw): invalid
    /// filename chars → '_'.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrWhiteSpace(cleaned) ? "intake-draft" : cleaned;
    }

    /// <summary>name.session.json, else name-2 … name-999 (:515-528 parity).</summary>
    private string UniquePath(string baseName)
    {
        var candidate = Path.Combine(_folder, baseName + ".session.json");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; i <= 999; i++)
        {
            candidate = Path.Combine(_folder, $"{baseName}-{i}.session.json");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("no free drafted-session filename (999 collisions)");
    }
}
