namespace CcpSpike.WebView;

/// <summary>Thread-safe spike evidence log: timestamped lines to one file + Debug.</summary>
public sealed class SpikeLog : IDisposable
{
    private readonly StreamWriter _w;
    private readonly object _gate = new();

    public SpikeLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _w = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    public void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        lock (_gate)
        {
            _w.WriteLine(line);
            System.Diagnostics.Debug.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate) { _w.Dispose(); }
    }
}
