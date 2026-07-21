namespace CcpSpike.AiProvider;

/// <summary>
/// Central secret registry + redaction (SP-018 pattern). Packet honesty framing (d):
/// provider payloads (prompt/reply text), auth header VALUES, and any token-bearing URL
/// component are never logged — presence + redacted shape only. Every log line passes
/// through <see cref="Scrub"/>; the --audit-logs self-check scans emitted logs for any
/// registered secret value and FAILS on a hit.
/// </summary>
public static class Redact
{
    private static readonly List<(string Name, string Value)> Secrets = new();
    private static readonly object Gate = new();

    public static string NewSecret(string name, int hexChars = 24)
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(hexChars / 2);
        var value = name switch
        {
            "apikey" => "spk_" + Convert.ToHexString(bytes).ToLowerInvariant(),
            _ => "spks_" + Convert.ToHexString(bytes).ToLowerInvariant(),
        };
        lock (Gate) Secrets.Add((name, value));
        return value;
    }

    public static void Register(string name, string value)
    {
        lock (Gate) Secrets.Add((name, value));
    }

    public static string Shape(string name, string value) => $"{name}:present(len={value.Length})";

    /// <summary>Replace every registered secret value in <paramref name="line"/> with its redacted shape.</summary>
    public static string Scrub(string line)
    {
        lock (Gate)
            foreach (var (name, value) in Secrets)
                if (value.Length > 0 && line.Contains(value, StringComparison.Ordinal))
                    line = line.Replace(value, Shape(name, value), StringComparison.Ordinal);
        return line;
    }

    /// <summary>Registry dump (name=value lines) for the --audit-logs self-check. The dump itself is a secret store: gitignored scratch only, never logged, never committed.</summary>
    public static string[] DumpRegistry()
    {
        lock (Gate) return Secrets.Select(s => $"{s.Name}={s.Value}").ToArray();
    }

    /// <summary>
    /// --audit-logs self-check: scan every file under <paramref name="dir"/> for any registered
    /// secret value. Returns the list of (file, secretName) hits; empty = audit green.
    /// </summary>
    public static List<string> Audit(string dir)
    {
        var hits = new List<string>();
        (string Name, string Value)[] secrets;
        lock (Gate) secrets = Secrets.ToArray();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("secrets-", StringComparison.Ordinal)) continue; // the registry itself is never a log
            string text;
            try { text = File.ReadAllText(file); } catch { continue; }
            foreach (var (name, value) in secrets)
                if (value.Length > 0 && text.Contains(value, StringComparison.Ordinal))
                    hits.Add($"{Path.GetFileName(file)}:{name}");
        }
        return hits;
    }
}
