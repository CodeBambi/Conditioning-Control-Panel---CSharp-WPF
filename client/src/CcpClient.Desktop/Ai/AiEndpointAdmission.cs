namespace CcpClient.Desktop.Ai;

/// <summary>
/// Endpoint admission policy seam (contract §6 rules 2-3; admission §2 rule 5). The policy
/// decides which endpoint CLASSES an operation may reach; enforcement happens BEFORE any
/// socket opens (SP-019 item 7 shape — the send-attempt counter is the proof instrument).
/// Allow-list governance (which remote hosts, if any, are admissible) is owner-pending
/// (admission §9.2 #2); this slice ships the loopback-only placeholder only.
/// </summary>
public interface IAiEndpointAdmissionPolicy
{
    bool IsAdmitted(AiEndpointClass endpointClass);
}

/// <summary>
/// The placeholder policy (owner-pending allow-list): loopback traffic only. First-party
/// cloud, third-party cloud, and remote-host Ollama are all rejected pre-socket — remote
/// Ollama is remote for disclosure purposes (first-attempt rejected assumption
/// "local AI = local-only data").
/// </summary>
public sealed class LoopbackOnlyAdmissionPolicy : IAiEndpointAdmissionPolicy
{
    public static readonly LoopbackOnlyAdmissionPolicy Instance = new();

    private LoopbackOnlyAdmissionPolicy() { }

    public bool IsAdmitted(AiEndpointClass endpointClass) => endpointClass == AiEndpointClass.Loopback;
}
