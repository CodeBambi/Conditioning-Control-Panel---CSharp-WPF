using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The host half of the Arcademy bridge for slices 1 to 4 and 6: the BOOT HANDSHAKE, the
/// SET-SETTING ECHO LOOP, the META COMMAND loop, the CLASS PAYOUT and the PANIC LADDER
/// (<see cref="PanicPress"/>, the rung a modal game window must own so two Esc taps cannot reach
/// the application). Upstream keeps them all in
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
    private bool _panicSuspended;                                    // :75 — press 1 froze the page
    private DateTimeOffset _lastPanicPress = DateTimeOffset.MinValue; // :76
    private bool _exiting;                                            // :63

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
    /// The clock the PAYOUT and the PANIC LADDER read, re-read at each <c>class-ended</c> and at
    /// each panic press — upstream reads
    /// <c>DateTime.UtcNow</c> (<c>:1379</c>) and <c>DateTime.Now</c> (<c>:1406</c>) inside the
    /// handler, not at boot. Deliberately NOT <see cref="ArcademyAppFacts.Now"/>, which is the
    /// single boot instant the init projection's two date fields are frozen at (<c>:530</c>): a
    /// class finished after midnight must credit the day it finished on, not the day the window
    /// opened.
    ///
    /// <para><b>The panic ladder reads the same clock</b> even though upstream's press path reads
    /// <c>DateTime.UtcNow</c> (<c>:323</c>) while its payout reads both halves: the ladder only ever
    /// measures an INTERVAL between two presses (<c>:325</c>), and the difference of two
    /// <see cref="DateTimeOffset"/> values is absolute, so the offset cannot move a rung. A second
    /// clock property would be a second thing to keep in step for no observable difference.</para>
    /// </summary>
    public Func<DateTimeOffset> Clock { get; set; } = static () => DateTimeOffset.Now;

    /// <summary>
    /// <b>Does the app's own native state still own the screen</b> — the rung that HOLDS a panic
    /// resume (<c>:359-364</c>: <c>App.Video?.IsPlaying == true || AudioOnlySession</c>). Upstream's
    /// reason is that un-freezing there "would drop a class back on top of a video the user is
    /// supposed to be watching".
    ///
    /// <para>This build has no video service and no audio-only session wired to the Arcademy —
    /// that is native-state suspension, slice 5 of the board row — so the honest default is
    /// <c>false</c>: nothing here can own the screen, so nothing can outrank the user's own resume.
    /// Slice 5 attaches its two predicates here and changes nothing else on this path.</para>
    /// </summary>
    public Func<bool> NativeStateOwnsScreen { get; set; } = static () => false;

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
            case ArcademyProtocol.ArcademyPageMessage.ResumeRequest resumeRequest:
                ResumeRequest(resumeRequest.Reason);
                return;
            case ArcademyProtocol.ArcademyPageMessage.Exit exit:
                // The page's own Esc-HOLD ladder wound itself down (:487-490). Upstream latches
                // _exiting and arms the bounded exit-done wait; the port latches the same flag —
                // so a panic press arriving now does not post a SECOND end-run — and asks its
                // owner for the same bounded wait.
                _exiting = true;                                                    // :488
                _log.Log($"arcademy: page exit ({exit.Reason ?? "no reason"})");     // :489
                RequestClose("page-exit", ArcademyClosePlan.WaitForExitDone);        // :490
                return;
            case ArcademyProtocol.ArcademyPageMessage.ExitDone:
                // The page is finished; the window may go NOW (:492-493, DisposeAll).
                _log.Log("arcademy: exit-done");
                RequestClose("exit-done", ArcademyClosePlan.Immediate);
                return;
        }
    }

    // ============================ the panic ladder (slice 6) ============================

    /// <summary>The app-wide double-press convention (<c>:83-85</c>): two presses inside this
    /// window are a deliberate double-tap, and anything slower re-arms rung 1.</summary>
    public static readonly TimeSpan PanicDoublePressWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Raised when this session has decided it is over: <see cref="ArcademyClosePlan.Immediate"/>
    /// means tear down now, <see cref="ArcademyClosePlan.WaitForExitDone"/> means the page was
    /// asked to wind down and the owner must bound that wait (upstream 1200ms,
    /// <c>ArmExitWatchdog</c>, <c>:1988-1993</c>) so a wedged page cannot outlast the user's own
    /// panic press. The session posts frames; it owns no window and disposes nothing, which is the
    /// same split <see cref="Chaos.ChaosTunnelCore"/> already uses against its service.
    /// </summary>
    public event Action<ArcademyCloseRequest>? CloseRequested;

    /// <summary>
    /// <b>THE PANIC LADDER (<c>HandlePanicPress</c>, <c>:321-340</c>).</b> While the Arcademy is
    /// up, the app-wide panic key belongs to it: <c>MainWindow.HandlePanicKeyPress</c> hands the
    /// press over and RETURNS (<c>MainWindow/MainWindow.xaml.cs:1092-1096</c>), the same hand-off
    /// the descent and the DtRH window get, "because without this rung, two Esc taps with no
    /// session running fell straight through to the 'not running' branch below and EXITED THE
    /// WHOLE APP from inside a mini-game" (<c>:1085-1090</c>; the app ladder's exit is
    /// <c>:1226-1254</c>). Every press that reaches here is CONSUMED, which is what a caller must
    /// honour: whatever ladder sits under this one may not advance on a press this one answered.
    ///
    /// <para><b>Rung 1</b> freezes everything — <c>suspend</c> drops every effect and pauses the
    /// class behind a Resume affordance (<c>:336-339</c>). <b>Rung 2</b>, a second press inside
    /// <see cref="PanicDoublePressWindow"/>, closes the Arcademy gracefully (<c>:328-334</c>). A
    /// SLOWER second press is a fresh rung 1, which is upstream's forgiving reading: "the
    /// emergency stop must not become an accidental exit" (<c>:313-317</c>).</para>
    ///
    /// <para><b>Attendance is safe on either rung</b> (<c>:318-320</c>): the streak is written on
    /// <c>class-ended</c>, and a class abandoned mid-panic simply never ended, so nothing is
    /// graded, paid or credited — the same rule <c>class-left</c> already carries.</para>
    /// </summary>
    public ArcademyPanicRung PanicPress()
    {
        if (_disposed)
        {
            // Upstream's own first line is `if (_host == null) return;` (:322) — with no live
            // Arcademy there is no rung to take, and the app-wide ladder owns the press.
            return new ArcademyPanicRung.NotLive();
        }

        var now = Clock();
        var doubleTap = _panicSuspended && now - _lastPanicPress <= PanicDoublePressWindow;   // :325
        // Set on EVERY press, before the branch (:326): a press always re-times the window.
        _lastPanicPress = now;

        if (doubleTap)
        {
            _log.Log("arcademy: panic press 2 — closing the Arcademy");    // :330
            _panicSuspended = false;                                       // :331
            return new ArcademyPanicRung.Closing(CloseActive("panic"));    // :332
        }

        _panicSuspended = true;                                            // :336
        _log.Log($"arcademy: panic press 1 — suspending{(ClassActive ? " mid-class" : "")} (press again to leave)");
        _post(ArcademyProtocol.BuildSuspend(true, "panic"));               // :339
        return new ArcademyPanicRung.Suspended(ClassActive);
    }

    /// <summary>
    /// Graceful close (<c>CloseActive</c>, <c>:246-263</c>), also the app-exit and panic path, and
    /// idempotent. A page that is UP and not already winding down is ASKED to wind down
    /// (<c>end-run</c>, then its <c>exit-done</c>); anything else — a page that never booted, or a
    /// second close after one is already in flight — goes immediately, because waiting on an
    /// <c>exit-done</c> that can never arrive is how a panic press leaves someone stuck.
    /// </summary>
    /// <param name="reason">Transcript vocabulary for WHY this session is closing
    /// (<c>"panic"</c>, <c>"host"</c>). It is not the <c>end-run</c> frame's reason: that is the
    /// literal <c>"host"</c> on every path upstream (<c>:254</c>).</param>
    public ArcademyClosePlan CloseActive(string reason)
    {
        // `_host.IsReady` upstream (:250) — the page said `ready` and got its init. The port's
        // one-init-per-boot latch IS that state.
        if (_initPosted && !_exiting)
        {
            _exiting = true;                                               // :252
            _post(ArcademyProtocol.BuildEndRun());                         // :254
            return RequestClose(reason, ArcademyClosePlan.WaitForExitDone);
        }

        return RequestClose(reason, ArcademyClosePlan.Immediate);          // :259
    }

    /// <summary>
    /// The page asking to come back from a PANIC suspend (<c>OnResumeRequest</c>,
    /// <c>:346-370</c>) — a REQUEST rather than a page-side resume, because "the host stays the
    /// only thing that may un-freeze a class" (<c>:342-345</c>). Three refusals, each silent to
    /// the page and named in the transcript: a reason that is not <c>"panic"</c> (<c>:349-352</c>),
    /// no outstanding panic suspend (<c>:354-357</c>), and native state that still owns the screen
    /// (<c>:359-364</c>, <see cref="NativeStateOwnsScreen"/>).
    /// </summary>
    private void ResumeRequest(string? reason)
    {
        // A missing reason reads as "panic" (:348) — the page's own default.
        var asked = (reason ?? "panic").Trim();
        if (!string.Equals(asked, "panic", StringComparison.Ordinal))
        {
            _log.Log($"arcademy: resume-request for '{asked}' refused — only panic resumes on request");   // :351
            return;
        }

        if (!_panicSuspended)
        {
            _log.Log("arcademy: resume-request with no panic suspend outstanding — ignored");              // :356
            return;
        }

        if (NativeStateOwnsScreen())
        {
            _log.Log("arcademy: resume-request held — a video / audio-only session still owns the screen"); // :362
            return;
        }

        _panicSuspended = false;                                           // :366
        _lastPanicPress = DateTimeOffset.MinValue;                         // :367 — re-arms at rung 1
        _log.Log("arcademy: panic resume granted");                        // :368
        _post(ArcademyProtocol.BuildSuspend(false, "panic"));              // :369
    }

    private ArcademyClosePlan RequestClose(string reason, ArcademyClosePlan plan)
    {
        _log.Log($"arcademy: close requested ({reason}, {(plan == ArcademyClosePlan.Immediate ? "immediate" : "bounded wait for exit-done")})");

        // PER-HANDLER isolation, as the payout seam already does — and here the reason is the
        // whole point of the slice: this runs on the panic path, so a subscriber that throws must
        // not throw back through the key press. A press that faulted would be a press the app
        // ladder underneath is then free to treat as its own.
        var request = new ArcademyCloseRequest(reason, plan);
        foreach (Action<ArcademyCloseRequest> handler in CloseRequested?.GetInvocationList() ?? [])
        {
            try
            {
                handler(request);
            }
            catch (Exception ex)
            {
                _log.Log($"arcademy: close handler failed, isolated ({ex.GetType().Name})");
            }
        }

        return plan;
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

/// <summary>What the session's owner must do about a close (upstream's two branches of
/// <c>CloseActive</c>, <c>ArcademyHostService.cs:250-260</c>).</summary>
public enum ArcademyClosePlan
{
    /// <summary>Tear down NOW: the page never booted, or it is already winding down and this is a
    /// second close on top of the first (<c>:259</c>).</summary>
    Immediate,

    /// <summary><c>end-run</c> has been posted; close on the page's <c>exit-done</c> or on the
    /// owner's own bounded wait — upstream's is 1200ms (<c>ArmExitWatchdog</c>,
    /// <c>:1988-1993</c>), and it exists because a wedged page must not outlast the press that
    /// asked to leave.</summary>
    WaitForExitDone,
}

/// <summary>One close decision, carried to whoever owns the window.</summary>
/// <param name="Reason">Transcript vocabulary: <c>"panic"</c>, <c>"page-exit"</c>,
/// <c>"exit-done"</c>, or a host's own word.</param>
/// <param name="Plan">Immediate teardown, or the bounded wind-down wait.</param>
public sealed record ArcademyCloseRequest(string Reason, ArcademyClosePlan Plan);

/// <summary>
/// Which rung of the panic ladder one press took (<c>HandlePanicPress</c>, <c>:321-340</c>).
/// Upstream returns void and the rung is only visible in its log; the port types it so the press
/// can be seen to have been ANSWERED — a caller that cannot tell whether the Arcademy took the
/// press is a caller that will eventually let it fall through to the ladder underneath.
/// </summary>
public abstract record ArcademyPanicRung
{
    private ArcademyPanicRung() { }

    /// <summary>Rung 1: every effect dropped, the class frozen, and one more press inside
    /// <see cref="ArcademySession.PanicDoublePressWindow"/> leaves (<c>:336-339</c>).</summary>
    /// <param name="MidClass">The freeze landed inside a class rather than on the shell
    /// (<c>:337-338</c>). Attendance is unaffected either way: nothing ended.</param>
    public sealed record Suspended(bool MidClass) : ArcademyPanicRung;

    /// <summary>Rung 2: the Arcademy is closing (<c>:328-334</c>).</summary>
    /// <param name="Plan">How the close proceeds — see <see cref="ArcademyClosePlan"/>.</param>
    public sealed record Closing(ArcademyClosePlan Plan) : ArcademyPanicRung;

    /// <summary>There is no live Arcademy to take the press (<c>:322</c>), so this one is NOT
    /// consumed and the app-wide ladder owns it.</summary>
    public sealed record NotLive : ArcademyPanicRung;
}
