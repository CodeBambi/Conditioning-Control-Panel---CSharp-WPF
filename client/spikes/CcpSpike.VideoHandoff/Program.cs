namespace CcpSpike.VideoHandoff;

/// <summary>
/// SP-018 spike host. Modes:
///   (default)        decode-level matrix run (lab + native decode probes, M1-M8)
///   --audit-logs DIR sensitive-logging self-check over DIR (FAILS on any secret value)
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var scratch = Path.Combine(AppContext.BaseDirectory, "scratch");
        Directory.CreateDirectory(scratch);

        if (args.Length >= 2 && args[0] == "--audit-logs")
        {
            // Re-register the run's secrets from the gitignored registry, then scan every file.
            var secretsFile = Path.Combine(args[1], "secrets-registry.txt");
            if (!File.Exists(secretsFile))
            {
                Console.WriteLine("audit: no secrets registry in dir — cannot audit");
                return 2;
            }
            foreach (var line in File.ReadAllLines(secretsFile))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) Redact.Register(parts[0], parts[1]);
            }
            var hits = Redact.Audit(args[1]);
            foreach (var h in hits) Console.WriteLine($"audit HIT: {h}");
            Console.WriteLine(hits.Count == 0 ? "audit GREEN: zero secret values in logs" : $"audit FAILED: {hits.Count} hits");
            return hits.Count == 0 ? 0 : 1;
        }

        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other";
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss");
        SpikeLog.Open(Path.Combine(scratch, $"run-{platform}-{runId}.jsonl"));
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

        using var lab = new Lab(fixtures);
        SpikeLog.Line("main", $"spike start platform={platform} lab=http://127.0.0.1:{lab.Port}");
        try
        {
            // Diagnostic isolation (finding-hunt): --selftest-file parses the fixture from disk,
            // bypassing the lab/HTTP path entirely.
            if (args.Length >= 1 && args[0] == "--selftest-file")
            {
                var probe = new Probe();
                var report = await probe.RunFileAsync(Path.Combine(fixtures, "clip.mp4"));
                SpikeLog.Line("main", $"selftest-file outcome={report.Outcome} vtrack={report.VideoTrack} dur={report.DurationMs} end={report.EndReached}");
                PersistSecrets(scratch);
                Probe.HardExit(report.Outcome == ProbeOutcome.Success ? 0 : 1);
                return 2; // unreachable
            }

            var rows = await Matrix.RunDecodeLevelAsync(lab);
            var failed = rows.Count(r => !r.Pass);
            SpikeLog.Line("main", $"matrix done rows={rows.Count} failed={failed}");
            Console.WriteLine($"MATRIX: {rows.Count - failed}/{rows.Count} pass");
            // HardExit bypasses finally — persist the audit registry first (idempotent with finally).
            PersistSecrets(scratch);
            Probe.HardExit(failed == 0 ? 0 : 1);
            return 2; // unreachable
        }
        finally
        {
            // Secret registry for --audit-logs: gitignored scratch only, never a log, never committed.
            PersistSecrets(scratch);
        }
    }

    /// <summary>Append this run's secrets to the registry (accumulates across runs so --audit-logs covers every emitted log).</summary>
    private static void PersistSecrets(string scratch)
    {
        var path = Path.Combine(scratch, "secrets-registry.txt");
        var existing = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        File.WriteAllLines(path, existing.Concat(Redact.DumpRegistry()).Distinct().ToArray());
    }
}
