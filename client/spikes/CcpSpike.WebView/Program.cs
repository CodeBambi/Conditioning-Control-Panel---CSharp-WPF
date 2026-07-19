using Avalonia;

namespace CcpSpike.WebView;

public sealed record SpikeConfig
{
    public required string Page { get; init; }          // index | spike | probe
    public required string PayloadRoot { get; init; }   // READ-ONLY dtrh tree
    public required string OverlayRoot { get; init; }   // tracked overlay dir
    public required string ScratchDir { get; init; }    // spike-local writable scratch
    public required string LogPath { get; init; }
    public int AutoQuitSeconds { get; init; }           // 0 = stay open
    public bool PopulateManifest { get; init; }         // --manifest: payload media in manifest
    public bool BlockMediaAfterArm { get; init; }       // --block-media: fault-injection case 2
    public string? SpikeVideoPath { get; init; }        // --spike-video: media-root-relative override
    public string? SpikeImagePath { get; init; }        // --spike-image: media-root-relative override
    public long StartedTicks { get; init; }             // Stopwatch.GetTimestamp() at Main entry
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var config = Parse(args) with { StartedTicks = started };

        var log = new SpikeLog(config.LogPath);
        try
        {
            log.Log($"spike: page={config.Page} payload={config.PayloadRoot}");
            log.Log($"spike: overlay={config.OverlayRoot} scratch={config.ScratchDir}");
            var server = new LoopbackServer(
                config.PayloadRoot,
                config.OverlayRoot,
                Path.Combine(config.PayloadRoot, "assets"),
                log);
            server.Start();

            return AppBuilder
                .Configure(() => new App(config, server, log))
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            log.Log("spike: process exiting");
            log.Dispose();
        }
    }

    private static SpikeConfig Parse(string[] args)
    {
        string Get(string name, string? fallback = null)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            if (fallback is not null) return fallback;
            throw new ArgumentException($"missing required arg {name}");
        }

        bool Flag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        string? Opt(string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        var payload = Get("--payload");
        var spikeDir = FindUp("CcpSpike.WebView.csproj", AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("spike project dir not found above " + AppContext.BaseDirectory);
        var scratch = Path.Combine(spikeDir, "scratch");
        Directory.CreateDirectory(scratch);

        return new SpikeConfig
        {
            Page = Get("--page", "index"),
            PayloadRoot = payload,
            OverlayRoot = Path.Combine(spikeDir, "overlay"),
            ScratchDir = scratch,
            LogPath = Path.Combine(scratch, $"spike-{DateTime.Now:yyyyMMdd-HHmmss}.log"),
            AutoQuitSeconds = int.Parse(Get("--auto-quit", "0")),
            PopulateManifest = Flag("--manifest"),
            BlockMediaAfterArm = Flag("--block-media"),
            SpikeVideoPath = Opt("--spike-video"),
            SpikeImagePath = Opt("--spike-image"),
        };
    }

    private static string? FindUp(string marker, string startDir)
    {
        var d = new DirectoryInfo(startDir);
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, marker))) return d.FullName;
            d = d.Parent;
        }

        return null;
    }
}
