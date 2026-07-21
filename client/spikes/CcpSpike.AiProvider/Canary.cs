using CcpClient.Desktop.Ai;

namespace CcpSpike.AiProvider;

/// <summary>
/// The canary executor (spike-side ONLY — never product code). Records every command it is
/// handed: kind + shape tokens, never payload text. Zero-execution is PROVEN by rejected
/// payloads producing no AiExecutionPlan (type-enforced — the plan is null) so the canary
/// is never invoked; valid envelopes must produce plans whose commands the canary records
/// EXACTLY (the falsifiable pair).
/// </summary>
public sealed class CanaryExecutor
{
    private readonly List<AiCommandKind> _invocations = new();
    private readonly object _gate = new();

    public int Calls { get; private set; }

    public IReadOnlyList<AiCommandKind> Invocations { get { lock (_gate) return _invocations.ToArray(); } }

    public void Execute(AiExecutionPlan plan)
    {
        lock (_gate)
        {
            Calls++;
            foreach (var cmd in plan.Commands)
            {
                _invocations.Add(cmd.Kind);
                SpikeLog.Line("canary", $"FIRED kind={cmd.Kind}");
            }
        }
    }

    public void Reset()
    {
        lock (_gate) { _invocations.Clear(); Calls = 0; }
    }
}
