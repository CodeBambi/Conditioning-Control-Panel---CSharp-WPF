namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The b3 protocol router (SP-025): maps the b3-owned page messages onto the real native
/// effects (<see cref="DtrhNativeEffects"/>) — kept UI-free so unit tests drive it with
/// recording fakes, never the real backends. The host window owns all window behavior
/// (payload-state mirror, video window, focus reclaim); this class owns ONLY the
/// message → effects routing.
/// </summary>
public sealed class DtrhFxRouter
{
    private readonly DtrhNativeEffects _fx;
    private readonly Action<string> _log;

    public DtrhFxRouter(DtrhNativeEffects fx, Action<string> log)
    {
        _fx = fx;
        _log = log;
    }

    /// <summary>Dispatch one b3-owned (Handled-classified) message to the real effect.
    /// Presence+shape logging only (§4.8 sensitive-logging ban).</summary>
    public void Handle(DtrhProtocol.DtrhPageMessage message)
    {
        switch (message)
        {
            case DtrhProtocol.DtrhPageMessage.Sfx sfx:
                _fx.PlaySfx(sfx.Name, sfx.Scale);
                return;
            case DtrhProtocol.DtrhPageMessage.FirePayload payload:
                _fx.FirePayload(payload.Kind, payload.Strength, payload.DurationMult);
                return;
            case DtrhProtocol.DtrhPageMessage.FreezeState freeze:
                _fx.SetWorldFrozen(freeze.On);
                return;
            case DtrhProtocol.DtrhPageMessage.VnSpeaking vn:
                _fx.SetVnSpeaking(vn.On);
                return;
            default:
                _log($"dtrh-fx: router received non-b3 message '{message.GetType().Name}' — ignored");
                return;
        }
    }

    /// <summary>Run-boundary freeze/duck hygiene (WPF run-started :252/:259, run-ended
    /// :513): the stale-freeze/stale-duck cleanup rides the run boundary. b3 invoked it
    /// from the Deferred branch; b4's REAL run-started/run-ended handlers invoke it FIRST
    /// (same entry, WPF order preserved). Returns true when the hygiene ran.</summary>
    public bool TryRunBoundaryHygiene(DtrhProtocol.DtrhPageMessage message)
    {
        if (message is not (DtrhProtocol.DtrhPageMessage.RunStarted or DtrhProtocol.DtrhPageMessage.RunEnded))
        {
            return false;
        }

        _fx.NotifyRunBoundary();
        return true;
    }
}
