namespace CcpClient.Desktop.Ai;

/// <summary>
/// The diagnostics sink for AI operations (contract §12). The pipeline emits exactly one
/// <see cref="AiDiagnosticRecord"/> per operation. The record's schema is content-free BY
/// CONSTRUCTION (enums, stable codes, counts, durations, generation IDs — the SP-016
/// structural proof in AiOperationContractTests covers the schema); sinks must never be
/// handed prompts, completions, user text, window titles, keywords, memory contents,
/// secrets, or raw envelopes.
/// </summary>
public interface IAiDiagnosticsSink
{
    void Emit(AiDiagnosticRecord record);
}

/// <summary>In-memory sink (tests + the composition root's wiring point). Thread-safe.</summary>
public sealed class CollectingAiDiagnosticsSink : IAiDiagnosticsSink
{
    private readonly object _gate = new();
    private readonly List<AiDiagnosticRecord> _records = [];

    public IReadOnlyList<AiDiagnosticRecord> Records
    {
        get { lock (_gate) { return _records.ToArray(); } }
    }

    public void Emit(AiDiagnosticRecord record)
    {
        lock (_gate) { _records.Add(record); }
    }
}
