namespace CcpSpike.AiProvider;

/// <summary>
/// Step-2 smoke: proves the lab + client + redaction/audit core end to end before the
/// evidence matrices build on it. Cases: (1) Ok round-trip (typed Completed, plan with
/// exactly the lab's command); (2) non-loopback policy rejection with SendAttempts==0;
/// (3) bounded timeout (typed Timeout, no hang); (4) canary fires only via a plan.
/// </summary>
public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var failures = new List<string>();
        void Check(bool ok, string name)
        {
            SpikeLog.Line("selftest", $"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures.Add(name);
        }

        using var lab = new AiLab();
        var apiKey = Redact.NewSecret("apikey");

        // (1) Ok round-trip.
        var client = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromSeconds(5));
        var gen = client.AdvanceGeneration();
        lab.Inject(LabMode.Ok);
        var r1 = await client.RequestAsync("selftest-prompt-1", gen, CancellationToken.None);
        Check(r1.Kind == SpikeOutcomeKind.Completed, "ok-roundtrip typed Completed");
        Check(r1.Reply is CcpClient.Desktop.Ai.AiReply.Generated, "ok-roundtrip typed Generated reply");
        var canary = new CanaryExecutor();
        // The plan comes only from the validated envelope; the canary consumes it.
        var result = CcpClient.Desktop.Ai.AiEnvelopeValidator.Validate(lab.OkBody(), CcpClient.Desktop.Ai.AiEnvelopePolicy.PermitAll);
        Check(result.Accepted && result.Plan is not null, "ok-roundtrip plan exists");
        if (result.Plan is not null) canary.Execute(result.Plan);
        Check(canary.Invocations.Count == 1 && canary.Invocations[0] == CcpClient.Desktop.Ai.AiCommandKind.Bubbles,
            "canary recorded exactly the plan's command");
        Check(lab.HitCount == 1 && lab.Records[0].AuthShape is not null, "lab saw exactly 1 hit with auth present");

        // (2) Remote-host policy rejection — no socket (SendAttempts stays 0), instant, typed.
        var remote = new SpikeAiClient(new Uri("http://192.0.2.1:11434/"), apiKey, TimeSpan.FromSeconds(5));
        var rgen = remote.AdvanceGeneration();
        var sw = Environment.TickCount64;
        var r2 = await remote.RequestAsync("selftest-prompt-2", rgen, CancellationToken.None);
        var elapsed = Environment.TickCount64 - sw;
        Check(r2.Kind == SpikeOutcomeKind.PolicyRejected, "policy rejection typed outcome");
        Check(remote.SendAttempts == 0, "policy rejection: zero send attempts (no socket)");
        Check(elapsed < 1000, $"policy rejection instant ({elapsed}ms < 1000ms, no connect timeout)");

        // (3) Bounded timeout — typed, no hang.
        var tclient = new SpikeAiClient(lab.Endpoint, apiKey, TimeSpan.FromMilliseconds(800));
        var tgen = tclient.AdvanceGeneration();
        lab.Inject(LabMode.Timeout);
        var t0 = Environment.TickCount64;
        var r3 = await tclient.RequestAsync("selftest-prompt-3", tgen, CancellationToken.None);
        var tElapsed = Environment.TickCount64 - t0;
        Check(r3.Kind == SpikeOutcomeKind.Timeout, "timeout typed outcome");
        Check(tElapsed < 5000, $"timeout bounded ({tElapsed}ms < 5000ms, no hang)");

        SpikeLog.Line("selftest", failures.Count == 0 ? "SELFTEST GREEN" : $"SELFTEST FAILED: {failures.Count}");
        Console.WriteLine(failures.Count == 0 ? "SELFTEST: all pass" : $"SELFTEST: {failures.Count} FAILURES");
        return failures.Count == 0 ? 0 : 1;
    }
}
