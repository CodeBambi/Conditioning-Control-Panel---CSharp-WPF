using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The host half of the Arcademy bridge for slices 1 and 2: the BOOT HANDSHAKE and the
/// SET-SETTING ECHO LOOP. Upstream keeps both in <c>ArcademyHostService</c>'s static state
/// (<c>OnPageReady</c> <c>:388-404</c>, <c>OnPageMessage</c> <c>:444-498</c>, <c>OnSetSetting</c>
/// <c>:1164-1188</c>, <c>OnSettingsCurrentReplaced</c> <c>:1777-1794</c>); this port keeps it in
/// one instance per session so the two halves can be exercised without a browser.
///
/// <para><b>The handshake's observable shape, in order</b> (<c>:396-401</c>): on <c>ready</c>,
/// EXACTLY ONE <c>init</c> per boot, then <c>fullscreen</c> carrying the REAL window state. The
/// once-per-boot guard sits AHEAD of the fullscreen post upstream, so a second <c>ready</c>
/// produces NOTHING — not a second init and not a second fullscreen. Ported as it stands.</para>
///
/// <para><b>What upstream does at this point that this build does not:</b> <c>SeedNativeState</c>
/// (<c>:415-439</c>) pushes a <c>suspend</c> when a mandatory video is already playing or
/// AudioOnlySession flipped during boot. That is native-state suspension — slice 5 of the board
/// row — and it is ABSENT rather than stubbed, because a <c>suspend</c> this host never sends is
/// safer than one it sends without the video and browser-media services that make it true.</para>
///
/// <para><b>There is no echo-suppression flag, and that is a considered absence.</b> Upstream
/// raises <c>_suppressSettingEcho</c> around its write (<c>:1173-1182</c>) because
/// <c>AppSettings.PropertyChanged</c> would otherwise fire a SECOND <c>setting</c> frame for the
/// same write (<c>OnSettingChangedInApp</c>, <c>:1849</c>). This document raises no change
/// notifications at all, so the reply is the only echo and there is nothing to suppress. The
/// app→page direction that upstream's watch also serves is
/// <see cref="RepushProjected"/>, wired to the store's own replacement signal.</para>
/// </summary>
public sealed class ArcademySession : IDisposable
{
    private readonly PersistenceStore<ArcademySettingsDocument> _store;
    private readonly Action<object> _post;
    private readonly ILogSink _log;
    private bool _initPosted;
    private bool _disposed;

    /// <param name="store">The Arcademy settings document store.</param>
    /// <param name="facts">The app-wide values the projection reads (see <see cref="ArcademyAppFacts"/>).</param>
    /// <param name="post">Host→page sink. A frame object; the caller serializes with
    /// <see cref="ArcademyProtocol.SerializeForPage"/> and puts it on whatever transport its window
    /// uses (the goon host's <c>SendToPage</c> shape).</param>
    /// <param name="log">Diagnostics. Never receives a setting VALUE, only its key.</param>
    public ArcademySession(
        PersistenceStore<ArcademySettingsDocument> store,
        ArcademyAppFacts facts,
        Action<object> post,
        ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(post);
        _store = store;
        _post = post;
        _log = log;
        Facts = facts;
        // The settings instance can be REPLACED underneath us (a restore, a reset). Upstream
        // follows the same signal (:1777-1794) and re-pushes the whole projection, because the
        // page's model only ever moves on an echo.
        _store.SettingsReplaced += RepushProjected;
    }

    /// <summary>The app-wide values in force for this session. Three set-setting keys write here
    /// rather than into the Arcademy document (see <see cref="ArcademySettingsEcho"/>).</summary>
    public ArcademyAppFacts Facts { get; private set; }

    /// <summary>Raised when a page write moved an APP-WIDE value. This build has no app-wide store
    /// to persist into; the hook is where one attaches.</summary>
    public event Action<ArcademyAppFacts>? AppFactsChanged;

    /// <summary>The REAL window state the <c>fullscreen</c> frames carry (<c>:400</c>, <c>:515</c>:
    /// always the actual state, never the requested one). With no window in slices 1-2 the honest
    /// answer is <c>false</c>; the window that arrives with slice 8 supplies its own.</summary>
    public Func<bool> FullscreenState { get; set; } = static () => false;

    /// <summary>True once the one <c>init</c> of this boot has gone out.</summary>
    public bool InitPosted => _initPosted;

    /// <summary>The page reported <c>boot-error</c> this session, or a parse said the page is
    /// unusable (<c>BootFailedThisSession</c>, <c>:93-95</c>). Entry points can read this to stop
    /// sending someone back through a door that has already failed on this machine.</summary>
    public bool BootFailed { get; private set; }

    /// <summary>The page says it is up (<c>OnPageReady</c>, <c>:388</c>). Idempotent by contract:
    /// exactly one init per boot, and nothing at all on a repeat.</summary>
    public void Ready()
    {
        if (_initPosted)
        {
            return;                                                        // :396
        }

        _initPosted = true;
        _post(ArcademyProtocol.BuildInit(_store.Current, Facts));          // :399
        _post(ArcademyProtocol.BuildFullscreen(FullscreenState()));        // :400
        _log.Log($"arcademy: sent init (protocol {ArcademyProtocol.Version})");
    }

    /// <summary>Route one page→host frame. Never throws; every outcome is typed and logged
    /// (<c>OnPageMessage</c>, <c>:444</c>).</summary>
    public void Handle(string json)
    {
        switch (ArcademyProtocol.ParsePageMessage(json))
        {
            case ArcademyProtocol.ArcademyPageParseResult.Parsed parsed:
                Dispatch(parsed.Message);
                return;
            case ArcademyProtocol.ArcademyPageParseResult.LaterSlice later:
                // Real vocabulary this build does not own yet. Named, never acted on.
                _log.Log($"arcademy: '{later.Type}' belongs to a later slice — acknowledged, not acted on");
                return;
            case ArcademyProtocol.ArcademyPageParseResult.ForwardVersion forward:
                _log.Log($"arcademy: '{forward.Type}' declares protocol {forward.Protocol} (this host speaks {ArcademyProtocol.Version}) — ignored");
                return;
            case ArcademyProtocol.ArcademyPageParseResult.UnknownType unknown:
                _log.Log($"arcademy: unhandled message '{unknown.Type}'");     // :496
                return;
            case ArcademyProtocol.ArcademyPageParseResult.Malformed malformed:
                _log.Log($"arcademy: malformed page frame ({malformed.Reason})");
                return;
        }
    }

    private void Dispatch(ArcademyProtocol.ArcademyPageMessage message)
    {
        switch (message)
        {
            case ArcademyProtocol.ArcademyPageMessage.Ready:
                Ready();
                return;
            case ArcademyProtocol.ArcademyPageMessage.Log log:
                _log.Log($"arcademy page log: {log.Msg}");
                return;
            case ArcademyProtocol.ArcademyPageMessage.Heartbeat:
            case ArcademyProtocol.ArcademyPageMessage.Pong:
                // Liveness. The watchdog that consumes it is not slices 1-2, so this is a sign of
                // life nothing yet reads — recorded rather than pretended.
                return;
            case ArcademyProtocol.ArcademyPageMessage.BootError bootError:
                BootFailed = true;
                _log.Log($"arcademy: boot-error from page ({(bootError.Msg is { Length: > 0 } m ? m : "no detail")})");
                return;
            case ArcademyProtocol.ArcademyPageMessage.FullscreenRequest:
                // C# owns the borderless toggle (:504-509) and the page reads only the ECHOED
                // state, so an unanswered request is a dead key. With no window to toggle, the
                // real state is what goes back.
                _post(ArcademyProtocol.BuildFullscreen(FullscreenState()));    // :515
                return;
            case ArcademyProtocol.ArcademyPageMessage.SetSetting setSetting:
                SetSetting(setSetting.Key, setSetting.Value);
                return;
            case ArcademyProtocol.ArcademyPageMessage.Exit exit:
                _log.Log($"arcademy: page exit ({exit.Reason ?? "no reason"})");
                return;
            case ArcademyProtocol.ArcademyPageMessage.ExitDone:
                _log.Log("arcademy: exit-done");
                return;
        }
    }

    /// <summary>One settings write: validate, clamp, persist, echo the POST-CLAMP value
    /// (<c>OnSetSetting</c>, <c>:1164-1188</c>).</summary>
    public void SetSetting(string key, System.Text.Json.JsonElement? value)
    {
        if (!ArcademySettingsEcho.IsWritableKey(key))
        {
            return;                                                        // :1166 — not answered at all
        }

        var trimmed = key.Trim();
        ArcademySettingsEcho.ArcademyWriteResult? result = null;
        try
        {
            // Under the store's own mutation gate: the write and the dirty mark are one step.
            _store.Mutate(document => result = ArcademySettingsEcho.Apply(document, Facts, trimmed, value));
        }
        catch (Exception ex)
        {
            _log.Log($"arcademy: set-setting '{trimmed}' failed ({ex.GetType().Name})");   // :1178-1181
            return;
        }

        if (result is null)
        {
            return;
        }

        _ = _store.Save();                                                 // :1184

        if (result.AppFactsChanged)
        {
            Facts = result.Facts;
            AppFactsChanged?.Invoke(Facts);
        }

        _post(ArcademyProtocol.BuildSetting(trimmed, result.Echo));        // :1187
    }

    /// <summary>Re-echo every key the init projection carries (<c>RepushProjectedSettings</c>,
    /// <c>:1798-1806</c>). The restored values may differ from what the page is painting, and the
    /// page's model only ever moves on an echo, so the whole projection goes out at once.</summary>
    public void RepushProjected()
    {
        foreach (var (key, value) in ArcademySettingsEcho.Projected(_store.Current, Facts))
        {
            _post(ArcademyProtocol.BuildSetting(key, value));
        }

        _log.Log("arcademy: re-echoed the projected settings after a settings-instance swap");
    }

    /// <summary>Idempotent teardown: stop following the store (lifecycle discipline).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.SettingsReplaced -= RepushProjected;
    }
}
