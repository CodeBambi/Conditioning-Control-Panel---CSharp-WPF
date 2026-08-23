namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The narrow seam between <see cref="ButtplugHapticSink"/> and the Buttplug client library.
///
/// <para><b>Why a seam exists here and not on the Lovense route.</b> On Lovense this port writes the
/// wire itself, so the shape of each request is our risk and the sink is proved against a real HTTP
/// server. Here the wire is the library's — BSD-3, maintained, and not ours to re-verify. What this
/// port owns is which calls are made, in what order, and what happens when they fail. This interface
/// is exactly that surface and nothing else.</para>
///
/// <para><b>It is deliberately thin to the point of being boring.</b> Every implementation is pure
/// delegation with no branching of its own, because logic placed here would be logic the sink's own
/// facts cannot see. The real implementation is proved end to end against a server speaking the
/// captured Buttplug v4 protocol, so the seam is covered from both sides rather than trusted.</para>
/// </summary>
public interface IButtplugSession : IDisposable
{
    /// <summary>Whether a server connection currently holds.</summary>
    bool Connected { get; }

    /// <summary>Connect to the server. Throws if it cannot be reached; the sink turns that into a
    /// typed refusal rather than letting it escape.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>The devices the server currently knows, keyed as the sink addresses them. These are
    /// the exact strings handed back to <see cref="SetVibrateAsync"/>.</summary>
    Task<IReadOnlyList<string>> DeviceKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Drive one device's vibrate features to <paramref name="level"/> (0..1, PASSED THROUGH —
    /// Buttplug maps a percentage onto each feature's own step range).
    /// </summary>
    /// <returns>How many vibrate features were driven. Zero means the device advertises none, which
    /// is a different fact from a refusal and is reported as one.</returns>
    Task<int> SetVibrateAsync(string deviceKey, double level, CancellationToken cancellationToken);

    /// <summary>Stop every device the server knows. Returns how many were stopped.</summary>
    Task<int> StopAllAsync();
}
