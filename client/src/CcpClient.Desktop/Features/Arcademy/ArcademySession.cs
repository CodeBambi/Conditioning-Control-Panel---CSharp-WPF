using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The host half of the Arcademy bridge for slices 1 to 4: the BOOT HANDSHAKE, the SET-SETTING
/// ECHO LOOP, the META COMMAND loop and the CLASS PAYOUT. Upstream keeps them all in
/// <c>ArcademyHostService</c>'s static state (<c>OnPageReady</c> <c>:388-404</c>,
/// <c>OnPageMessage</c> <c>:444-498</c>, <c>OnSetSetting</c> <c>:1164-1188</c>,
/// <c>OnClassEnded</c> <c>:1354-1428</c>, <c>OnSettingsCurrentReplaced</c> <c>:1777-1794</c>);
/// this port keeps it in one instance per session so the halves can be exercised without a
/// browser.
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
    private readonly ArcademyMetaStore? _meta;
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
    /// <param name="meta">The meta store (slice 3). Optional the way upstream's is nullable
    /// (<c>_meta?.…</c> at every call site, <c>:464</c>, <c>:568</c>, <c>:1386</c>, <c>:1407</c>):
    /// without one, a <c>meta-command</c> is answered with nothing, <c>init.meta</c> is the empty
    /// object, and a class-ended payout credits nobody and reports zeros.</param>
    public ArcademySession(
        PersistenceStore<ArcademySettingsDocument> store,
        ArcademyAppFacts facts,
        Action<object> post,
        ILogSink log,
        ArcademyMetaStore? meta = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(post);
        _store = store;
        _meta = meta;
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

    /// <summary>
    /// <b>THE XP SEAM (slice 4).</b> Raised with the payout a finished class computed, AFTER the
    /// page has been answered. Upstream's equivalent line is
    /// <c>App.Progression?.AddXP(xp, XPSource.Other)</c> (<c>:1394-1400</c>) — the same
    /// hosted-experience route Intake and the descent take. This build has no XP store, level or
    /// rank of any kind, so <see cref="ArcademyClassPayout.ArcademyPayout.Xp"/> is computed and
    /// lands nowhere; a subscriber here is the whole of what an XP economy would need to wire, and
    /// nothing else in this file would change. Upstream's own failure posture is ported too: it
    /// wraps <c>AddXP</c> in a try/catch "because a payout must not take the report card down with
    /// it" — a throwing handler here is isolated and logged for the same reason.
    /// </summary>
    public event Action<ArcademyClassPayout.ArcademyPayout>? PayoutComputed;

    /// <summary>
    /// The clock the PAYOUT reads, re-read at each <c>class-ended</c> — upstream reads
    /// <c>DateTime.UtcNow</c> (<c>:1379</c>) and <c>DateTime.Now</c> (<c>:1406</c>) inside the
    /// handler, not at boot. Deliberately NOT <see cref="ArcademyAppFacts.Now"/>, which is the
    /// single boot instant the init projection's two date fields are frozen at (<c>:530</c>): a
    /// class finished after midnight must credit the day it finished on, not the day the window
    /// opened.
    /// </summary>
    public Func<DateTimeOffset> Clock { get; set; } = static () => DateTimeOffset.Now;

    /// <summary>Between <c>class-started</c> and <c>class-left</c>/<c>class-ended</c>
    /// (<c>_classActive</c>, <c>:73</c>). Upstream's consumer is the heartbeat watchdog's
    /// mid-class limit (12s vs 20s), which is not in slices 1-4 — so this is a bracket nothing yet
    /// reads, recorded rather than pretended.</summary>
    public bool ClassActive { get; private set; }

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
        _post(ArcademyProtocol.BuildInit(_store.Current, Facts, _meta?.Snapshot()));   // :399, :568
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
            case ArcademyProtocol.ArcademyPageMessage.MetaCommand metaCommand:
                // The store answers with the POST-write value; a command it could not use at all
                // (missing/oversized key, unknown op) is answered with silence, as upstream
                // (upstream ArcademyMetaStore.cs:124-128, :142-145).
                if (_meta?.Handle(metaCommand.Op, metaCommand.Key, metaCommand.Value) is { } reply)
                {
                    _post(ArcademyProtocol.BuildMeta(reply.Key, reply.Value));      // :147
                }

                return;
            case ArcademyProtocol.ArcademyPageMessage.ClassStarted classStarted:
                ClassActive = true;                                                 // :467
                _log.Log($"arcademy: class started ({classStarted.GameKey}, tier {classStarted.GradeTier})");
                return;
            case ArcademyProtocol.ArcademyPageMessage.ClassEnded classEnded:
                ClassEnd(classEnded.Fields);
                return;
            case ArcademyProtocol.ArcademyPageMessage.ClassLeft classLeft:
                // Leaving a class with Esc ENDS no class: nothing is graded, paid or credited
                // (:474-480).
                ClassActive = false;
                _log.Log($"arcademy: class left ({classLeft.GameKey})");
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

    /// <summary>
    /// One finished class (<c>OnClassEnded</c>, <c>:1354-1428</c>): compute the payout, push the
    /// authoritative blob, answer the page, then offer the payout to the XP seam.
    ///
    /// <para><b>The order is upstream's and it is observable.</b> The whole-blob <c>meta</c> push
    /// goes out BEFORE <c>payout-result</c> (<c>:1408</c> then <c>:1410</c>); the page folds the
    /// payout frame's numbers over its cache afterwards, so the streak chip is right the instant a
    /// class ends rather than one frame later (<c>arcademy/core/store.js:236-252</c>).</para>
    /// </summary>
    public void ClassEnd(System.Text.Json.JsonElement fields)
    {
        ClassActive = false;                                                   // :1356

        ArcademyClassPayout.ArcademyPayout payout;
        try
        {
            payout = ArcademyClassPayout.Compute(fields, _meta, Clock());
        }
        catch (Exception ex)
        {
            // Upstream wraps the whole handler (:1427). Nothing in Compute is expected to throw;
            // if it ever does, the page must not be left with a class that never ended.
            _log.Log($"arcademy: class-ended failed ({ex.GetType().Name})");
            return;
        }

        if (_meta is { } meta)
        {
            _post(ArcademyProtocol.BuildMetaSnapshot(meta.Rev, meta.Snapshot()));   // :1408
        }

        _post(ArcademyProtocol.BuildPayoutResult(payout));                          // :1410

        // PER-HANDLER isolation, the shape PersistenceStore.Replace already uses for
        // SettingsReplaced: upstream wraps its single AddXP call (:1396-1399) because "a payout must
        // not take the report card down with it", and with an event there is more than one call to
        // protect — one falling over must not cost the next subscriber its payout.
        foreach (Action<ArcademyClassPayout.ArcademyPayout> handler in PayoutComputed?.GetInvocationList() ?? [])
        {
            try
            {
                handler(payout);
            }
            catch (Exception ex)
            {
                _log.Log($"arcademy: payout handler failed, isolated ({ex.GetType().Name})");
            }
        }

        _log.Log(
            $"arcademy: class complete ({payout.GameKey}, tier {payout.GradeTier}, grade {payout.Grade}) = "
            + $"{payout.Xp:0} XP{(payout.Retake ? $" (retake — already paid for {payout.XpLedgerUtcDay})" : "")}, "
            + $"streak {payout.Streak}, {payout.ClassesToday}/{ArcademyMetaStore.ClassesPerDay} today "
            + $"— computed only ({ArcademyClassPayout.NoXpStoreReason})");          // :1422-1425
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
