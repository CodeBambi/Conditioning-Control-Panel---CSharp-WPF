namespace CcpSpike.AiProvider;

/// <summary>JSONL observation log (SP-018 pattern). EVERY line passes through Redact.Scrub.</summary>
public static class SpikeLog
{
    private static string? _path;
    private static readonly object Gate = new();
    private static long _seq;

    public static void Open(string path)
    {
        lock (Gate) _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public static string? CurrentPath { get { lock (Gate) return _path; } }

    public static void Line(string source, string message)
    {
        var seq = Interlocked.Increment(ref _seq);
        var line = $"{{\"seq\":{seq},\"t\":\"{DateTimeOffset.UtcNow:O}\",\"src\":\"{source}\",\"msg\":\"{Escape(Redact.Scrub(message))}\"}}";
        lock (Gate)
        {
            Console.WriteLine(line);
            if (_path is not null) File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
