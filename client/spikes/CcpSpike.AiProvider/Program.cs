namespace CcpSpike.AiProvider;

/// <summary>
/// SP-019 spike host. Modes:
///   --selftest        Step-2 smoke: lab round-trip, policy rejection, timeout, audit
///   --fuzz            Step-3 strict-envelope fuzz matrix (zero-execution proof)
///   --matrix          Step-4 provider-behavior matrix (cancellation/timeout/429/5xx/refusal/malformed)
///   (default)         --fuzz then --matrix
///   --audit-logs DIR  sensitive-logging self-check over DIR (FAILS on any secret value)
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var scratch = Path.Combine(AppContext.BaseDirectory, "scratch");
        Directory.CreateDirectory(scratch);

        if (args.Length >= 2 && args[0] == "--audit-logs")
        {
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
        SpikeLog.Line("main", $"spike start platform={platform} mode={(args.Length > 0 ? args[0] : "default")}");

        try
        {
            var code = args.Length >= 1 && args[0] == "--selftest"
                ? await SelfTest.RunAsync()
                : args.Length >= 1 && args[0] == "--fuzz"
                    ? Fuzz.Run()
                    : args.Length >= 1 && args[0] == "--matrix"
                        ? await Matrix.RunAsync()
                        : Combine(Fuzz.Run(), await Matrix.RunAsync());
            SpikeLog.Line("main", $"spike done code={code}");
            return code;
        }
        finally
        {
            PersistSecrets(scratch);
        }
    }

    private static int Combine(int a, int b) => a == 0 && b == 0 ? 0 : 1;

    /// <summary>Append this run's secrets to the registry (accumulates across runs so --audit-logs covers every emitted log).</summary>
    private static void PersistSecrets(string scratch)
    {
        var path = Path.Combine(scratch, "secrets-registry.txt");
        var existing = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        File.WriteAllLines(path, existing.Concat(Redact.DumpRegistry()).Distinct().ToArray());
    }
}
