using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

// ============================================================================
// GOON GAME — Phase B. The match state machine:
//   Idle -> Lobby -> Consent -> Draft -> Countdown -> Live -> SuddenDeath -> Recap -> Idle
//
// Owns an IGoonTransport (injected; Phase A supplies the real WebRTC/relay one and
// the loopback mock). Owns the SHARED endurance ramp rolled from the draft
// agreement (the intersection of both players' allowed sets) and the combined
// match seed, the scoring/charge economy, the mercy and abandon flows, the
// receiver-side payload gate, and the two-way result handshake.
//
// DRAFT (2026-08-03 redesign): both players toggle what they ALLOW, the pool is
// the intersection, any toggle clears BOTH signatures, and the ramp both sides
// run is one seeded roll over that pool — identical instants, identical
// elements. Bubbles are an always-on baseline underneath it.
//
// It PLANS but never PERFORMS: element cues and admitted payloads leave as events;
// Phase C (GoonPayloadExecutor) does the App.Flash/App.Video/... fan-out.
//
// Safety rails honoured here (plan §Non-negotiable safety rails):
//  * Mercy is available in EVERY phase and always ends the match locally, even if
//    the wire is dead. Pre-Live it degrades to a clean cancel.
//  * Nothing in this file reads or writes PanicKeyEnabled / StrictLockEnabled or
//    any lockdown/tray verb. GG's payload set (GoonPayloadKind) excludes them by
//    construction — there is no code path to a banned verb.
//  * The RECEIVER enforces the economy: burst/gap rate limit, charge cost against
//    the opponent's last self-reported meter, one heavy per match, schedule buffer
//    clamp and text cap — regardless of what the wire claims.
// ============================================================================

namespace ConditioningControlPanel.Services.GoonGame
{
    public sealed class GoonMatchService : IDisposable
    {
        // ---- Phase B local tuning (deliberately NOT added to GoonConsts) ----
        private const int CountdownMs = 5000;               // countdown UI window
        private const int PhaseTimerIntervalMs = 200;       // countdown / handshake poll
        private const int ResultHandshakeTimeoutMs = 10000; // peer never countersigned -> disputed
        private const int DrawWindowMs = 1500;              // both mercy inside this -> Draw
        private const int PayloadScheduleBufferMs = 1500;   // >= GoonConsts.MinScheduleBufferMs
        private const int NoCamCheckIntervalMs = 90000;     // interaction-check cadence (no-cam)
        private const int MaxPayloadDurationMs = 180000;
        private const int EmoteTextMaxChars = 60;
        private const int EmoteIconMaxChars = 8;

        private readonly IGoonTransport _transport;
        private readonly Dispatcher _dispatcher;
        private readonly Func<ulong, IGoonRng> _rngFactory;

        private readonly Stopwatch _liveWatch = new();      // monotonic; survives clock re-sync
        private readonly GoonPayloadRateLimiter _inboundLimiter = new();
        private readonly GoonPayloadRateLimiter _outboundLimiter = new();
        private readonly Dictionary<string, int> _outboundCosts = new();
        private readonly HashSet<GoonElement> _activeElements = new();
        // The agreement: two allowed sets, two signatures, one intersection.
        private List<GoonElement> _localAllowed = new();
        private List<GoonElement> _remoteAllowed = new();
        private List<GoonElement> _sharedPool = new();
        private bool _draftResolved;

        private DispatcherTimer? _liveTimer;    // 1 s, SessionEngine idiom
        private DispatcherTimer? _phaseTimer;   // 200 ms, countdown + handshake deadlines

        private HelloMsg? _remoteHello;
        private bool _helloSent;
        private bool _localConsentConfirmed;
        private bool _remoteConsentConfirmed;
        private bool _localDraftConfirmed;
        private bool _remoteDraftConfirmed;
        private bool _startProposed;

        private ulong? _localSeedContribution;
        private ulong? _remoteSeedContribution;

        private List<GoonElementCue> _ramp = new();
        private int _rampIndex;
        private long _liveDurationMs;
        private long _lastTickSentLocalMs;
        private long _nextInteractionCheckMs;

        private int _payloadSeq;
        private bool _localHeavyUsed;
        private string? _localHeavyPayloadId;
        private bool _remoteHeavyUsed;
        private int _opponentChargesKnown;

        private long? _localMercyMatchMs;
        private long? _remoteMercyMatchMs;

        private bool _ended;
        private bool _finalized;
        private bool _countersignSent;
        private long _resultDeadlineLocalMs;

        private IGoonSuddenDeathRunner? _runner;
        private CancellationTokenSource? _runnerCts;
        private bool _disposed;

        public GoonMatchService(IGoonTransport transport, bool isHost, Func<ulong, IGoonRng>? rngFactory = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            IsHost = isHost;
            // Phase A owns the real xoshiro256** helper; until it is wired the engine falls
            // back to a local splitmix64 so it is runnable/testable standalone.
            _rngFactory = rngFactory ?? (seed => new FallbackRng(seed));
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            _transport.MessageReceived += OnMessageReceived;
            _transport.StateChanged += OnTransportStateChanged;

            ConsentSheet = new ConsentSheetMsg();
            Scoring = new GoonScoring(LocalAttentionMode, 0);
            Opponent = new GoonOpponentState();
        }

        // ------------------------------------------------------------ state

        public bool IsHost { get; }
        public GoonMatchPhase Phase { get; private set; } = GoonMatchPhase.Idle;
        public GoonScoring Scoring { get; }
        public GoonOpponentState Opponent { get; }
        public GoonMatchResult? Result { get; private set; }

        /// <summary>The sheet both sides must confirm byte-identically before drafting.</summary>
        public ConsentSheetMsg ConsentSheet { get; private set; }
        public bool LocalConsentConfirmed => _localConsentConfirmed;
        public bool RemoteConsentConfirmed => _remoteConsentConfirmed;

        /// <summary>What WE allow (canonical, always-on element excluded).</summary>
        public IReadOnlyList<GoonElement> LocalAllowedElements => _localAllowed;
        /// <summary>What THEY allow, as last broadcast.</summary>
        public IReadOnlyList<GoonElement> RemoteAllowedElements => _remoteAllowed;
        /// <summary>The intersection — the pool the ramp is rolled from. Both clients agree on it.</summary>
        public IReadOnlyList<GoonElement> SharedElementPool => GoonDraft.SharedPool(_localAllowed, _remoteAllowed);
        public bool LocalDraftConfirmed => _localDraftConfirmed;
        public bool RemoteDraftConfirmed => _remoteDraftConfirmed;
        /// <summary>True once both signatures landed on one pair of sets with a workable intersection.</summary>
        public bool DraftResolved => _draftResolved;

        // LEGACY aliases: the pre-agreement names. These are now the ALLOWED sets, not three picks.
        public IReadOnlyList<GoonElement> LocalDraft => _localAllowed;
        public IReadOnlyList<GoonElement> RemoteDraft => _remoteAllowed;
        public bool LocalDraftLocked => _localDraftConfirmed;
        public bool RemoteDraftLocked => _remoteDraftConfirmed;

        public ulong MatchSeed { get; private set; }
        public long StartMatchMs { get; private set; }

        /// <summary>Why the lobby was torn down, when it was (protocol/caps mismatch).</summary>
        public string? LobbyFailureReason { get; private set; }

        /// <summary>Set by the UI before hosting/joining. Drives the consent sheet + scoring mode.</summary>
        public GoonAttentionMode LocalAttentionMode { get; set; } = GoonAttentionMode.NoCam;

        /// <summary>Set by Phase E from the haptics mixer; advertised in the hello, never trusted from the peer.</summary>
        public bool LocalToyConnected { get; set; }

        /// <summary>Self-reported closeness dial, 0-3 or null. Bluffable BY DESIGN.</summary>
        public int? LocalCloseness { get; private set; }

        /// <summary>The opponent's lobby hello (identity + capabilities), once it arrives.</summary>
        public HelloMsg? RemoteHello => _remoteHello;

        /// <summary>What this client advertises (Windows = everything).</summary>
        public GoonCaps LocalCaps { get; } = GoonCapabilities.Local();

        /// <summary>The peer's advertisement, once the hello lands. Null until then.</summary>
        public GoonCaps? RemoteCaps => _remoteHello?.Caps;

        /// <summary>
        /// The draft pool actually offered to the player: the INTERSECTION of both clients'
        /// SupportedElements. Falls back to our own pool until the peer's hello arrives.
        /// </summary>
        public IReadOnlyList<GoonElement> AvailableDraftPool { get; private set; } = GoonDraft.PoolV1;

        /// <summary>Payload kinds the PEER can actually run — the only ones we may send.</summary>
        public IReadOnlyList<GoonPayloadKind> AvailablePayloadKinds { get; private set; } =
            Enum.GetValues<GoonPayloadKind>();

        /// <summary>Sudden-death round kinds both clients advertised (ReactionDuel always included).</summary>
        public IReadOnlyList<GoonRoundKind> AllowedRoundKinds { get; private set; } =
            new[] { GoonCapabilities.UniversalRound };

        /// <summary>Phase D plugs its round harness in here before the Live phase expires.</summary>
        public IGoonSuddenDeathRunner? SuddenDeathRunner
        {
            get => _runner;
            set => _runner = value;
        }

        public TimeSpan LiveElapsed => _liveWatch.Elapsed;
        public TimeSpan LiveRemaining
        {
            get
            {
                if (_liveDurationMs <= 0) return TimeSpan.Zero;
                var remaining = _liveDurationMs - _liveWatch.ElapsedMilliseconds;
                return remaining <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(remaining);
            }
        }

        // ----------------------------------------------------------- events

        public event EventHandler<GoonMatchPhase>? PhaseChanged;

        /// <summary>Phase C subscribes: begin this element locally. DurationMs 0 = sustained until Stop.</summary>
        public event EventHandler<GoonElementCueEventArgs>? ElementStartRequested;
        /// <summary>Sustained-element ramp update (same element, new intensity).</summary>
        public event EventHandler<GoonElementCueEventArgs>? ElementIntensityChanged;
        /// <summary>Phase C subscribes: stop this element locally.</summary>
        public event EventHandler<GoonElementCueEventArgs>? ElementStopRequested;

        /// <summary>An inbound payload that PASSED the receiver-side gate. Phase C resolves + executes.</summary>
        public event EventHandler<GoonInboundPayloadEventArgs>? PayloadAccepted;
        /// <summary>An inbound payload the gate refused (receipt already sent).</summary>
        public event EventHandler<PayloadReceiptMsg>? PayloadRejected;
        /// <summary>Receipt for one of OUR payloads (drives refunds + the recap log).</summary>
        public event EventHandler<PayloadReceiptMsg>? PayloadReceiptReceived;

        public event EventHandler? ConsentChanged;
        public event EventHandler? DraftChanged;
        public event EventHandler? OpponentStateChanged;
        public event EventHandler<GoonConnectionHealth>? ConnectionHealthChanged;
        public event EventHandler<EmoteMsg>? EmoteReceived;

        /// <summary>No-cam mode only: Phase E should prompt an interaction check, then call ReportInteractionCheck.</summary>
        public event EventHandler? InteractionCheckDue;

        /// <summary>Lobby could not proceed (protocol mismatch / empty capability intersection). Arg = reason.</summary>
        public event EventHandler<string>? LobbyFailed;

        /// <summary>Raised once, on entering Recap, with this client's view of the outcome.</summary>
        public event EventHandler<GoonMatchResult>? MatchEnded;
        /// <summary>Raised once the peer countersigned (or the handshake timed out -> disputed).</summary>
        public event EventHandler<GoonMatchResult>? ResultFinalized;

        // ------------------------------------------------------- lobby entry

        /// <summary>Host path. Returns the invite code to show the user, or null on failure.</summary>
        public async Task<string?> CreateInviteAsync(CancellationToken ct)
        {
            if (Phase != GoonMatchPhase.Idle) return null;
            SetPhase(GoonMatchPhase.Lobby);
            StartPhaseTimer();
            try
            {
                var code = await _transport.CreateInviteAsync(ct).ConfigureAwait(true);
                if (string.IsNullOrEmpty(code))
                {
                    App.Logger?.Warning("[GG] invite creation returned no code");
                    ResetToIdle();
                }
                return code;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] invite creation failed");
                ResetToIdle();
                return null;
            }
        }

        /// <summary>Joiner path.</summary>
        public async Task<bool> JoinAsync(string inviteCode, CancellationToken ct)
        {
            if (Phase != GoonMatchPhase.Idle) return false;
            SetPhase(GoonMatchPhase.Lobby);
            StartPhaseTimer();
            try
            {
                var ok = await _transport.JoinAsync(inviteCode, ct).ConfigureAwait(true);
                if (!ok) ResetToIdle();
                return ok;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] join failed");
                ResetToIdle();
                return false;
            }
        }

        /// <summary>
        /// Enters the Lobby over a transport that is already signaling or connected — the
        /// relay-fallback path, where <see cref="GoonRelayTransport.AdoptRoom"/> re-uses the room
        /// (and the burned weekly pass) the failed P2P attempt minted, so neither
        /// <see cref="CreateInviteAsync"/> nor <see cref="JoinAsync"/> may be called again.
        /// </summary>
        public bool AdoptLobby()
        {
            if (Phase != GoonMatchPhase.Idle) return false;
            SetPhase(GoonMatchPhase.Lobby);
            StartPhaseTimer();
            // If the transport connected before this service subscribed, StateChanged already
            // fired without us and nothing else will trigger the hello.
            if (_transport.State is GoonTransportState.ConnectedP2P or GoonTransportState.ConnectedRelay)
                SendHelloOnce();
            return true;
        }

        // ----------------------------------------------------------- consent

        /// <summary>
        /// Publishes a new consent sheet. ANY change clears both confirmations — the two
        /// sides may only advance on a byte-identical sheet.
        /// </summary>
        public void ProposeConsent(int liveDurationSec, double toyCap, int payloadMinGapMs)
        {
            if (Phase is not (GoonMatchPhase.Lobby or GoonMatchPhase.Consent)) return;

            ConsentSheet = new ConsentSheetMsg
            {
                LiveDurationSec = Math.Clamp(liveDurationSec, 60, 3600),
                // The sheet can only LOWER a receiver's own cap; the haptics mixer applies
                // MasterCap/GlobalIntensity unconditionally on top of this (Phase C).
                ToyCap = Math.Clamp(toyCap, 0.0, 1.0),
                PayloadMinGapMs = Math.Max(GoonConsts.PayloadMinGapMs, payloadMinGapMs),
                Confirmed = false,
            };
            _localConsentConfirmed = false;
            _remoteConsentConfirmed = false;
            Send(ConsentSheet);
            RaiseSafe(ConsentChanged);
        }

        /// <summary>Confirms the CURRENT sheet. Both confirmations on one fingerprint -> Draft.</summary>
        public void ConfirmConsent()
        {
            if (Phase != GoonMatchPhase.Consent) return;
            _localConsentConfirmed = true;
            Send(CloneSheet(ConsentSheet, confirmed: true));
            RaiseSafe(ConsentChanged);
            TryEnterDraft();
        }

        /// <summary>Backing out of the sheet. Either player may do this; it never ends the lobby by itself.</summary>
        public void WithdrawConsent()
        {
            if (Phase != GoonMatchPhase.Consent) return;
            _localConsentConfirmed = false;
            Send(CloneSheet(ConsentSheet, confirmed: false));
            RaiseSafe(ConsentChanged);
        }

        // ------------------------------------------------------------- draft
        //
        // The draft is a mutual agreement, not two loadouts: each side publishes the elements it
        // ALLOWS and the effective pool is the intersection. Changing a toggle clears BOTH
        // signatures — the same rule the consent sheet lives by, for the same reason. Two
        // signatures on one pair of sets resolve the draft.

        /// <summary>Publishes the local allowed set. ANY change clears BOTH confirmations.</summary>
        public bool SetAllowedElements(IReadOnlyList<GoonElement>? allowed, out string error)
        {
            error = "";
            if (Phase != GoonMatchPhase.Draft) { error = "not drafting"; return false; }

            var next = SanitizeAllowed(allowed);
            if (!GoonDraft.IsValidAllowed(next, out error)) return false;

            _localAllowed = next;
            _localDraftConfirmed = false;
            _remoteDraftConfirmed = false;
            _draftResolved = false;
            SendDraftState();
            RaiseSafe(DraftChanged);
            return true;
        }

        /// <summary>
        /// Flips one element on or off. A toggle that would leave fewer than
        /// <see cref="GoonDraft.MinAllowedElements"/> on is REFUSED and nothing changes.
        /// </summary>
        public bool ToggleAllowedElement(GoonElement element, out string error)
        {
            error = "";
            if (Phase != GoonMatchPhase.Draft) { error = "not drafting"; return false; }
            var next = _localAllowed.Contains(element)
                ? _localAllowed.Where(e => e != element).ToList()
                : _localAllowed.Concat(new[] { element }).ToList();
            return SetAllowedElements(next, out error);
        }

        /// <summary>Signs the CURRENT pair of sets. Both signatures + a workable intersection -> resolved.</summary>
        public bool ConfirmDraft(out string error)
        {
            error = "";
            if (Phase != GoonMatchPhase.Draft) { error = "not drafting"; return false; }
            if (!GoonDraft.IsValidAllowed(_localAllowed, out error)) return false;

            var pool = GoonDraft.SharedPool(_localAllowed, _remoteAllowed);
            if (!GoonDraft.IsValidSharedPool(pool, out error)) return false;   // shown inline; nothing signed

            _localDraftConfirmed = true;
            SendDraftState();
            Scoring.Configure(LocalAttentionMode, GoonDraft.MatchRiskTier(pool));
            RaiseSafe(DraftChanged);
            TryResolveDraft();
            return true;
        }

        /// <summary>Backing out of a signature without touching the toggles.</summary>
        public void WithdrawDraft()
        {
            if (Phase != GoonMatchPhase.Draft || !_localDraftConfirmed) return;
            _localDraftConfirmed = false;
            _draftResolved = false;
            SendDraftState();
            RaiseSafe(DraftChanged);
        }

        // LEGACY names. SetDraft(list) is SetAllowedElements(list); LockDraft() is ConfirmDraft().
        public bool SetDraft(IReadOnlyList<GoonElement> picks, out string error) => SetAllowedElements(picks, out error);
        public bool LockDraft(out string error) => ConfirmDraft(out error);

        /// <summary>Anything the caps intersection cannot mirror is dropped rather than trusted.</summary>
        private List<GoonElement> SanitizeAllowed(IEnumerable<GoonElement>? allowed) =>
            GoonDraft.NormalizeAllowed((allowed ?? Enumerable.Empty<GoonElement>()).Where(AvailableDraftPool.Contains));

        private void SendDraftState()
        {
            Send(new DraftMsg
            {
                Elements = _localAllowed.ToList(),      // legacy mirror
                Locked = _localDraftConfirmed,          // legacy mirror
                Allowed = _localAllowed.ToList(),
                Confirmed = _localDraftConfirmed,
            });
        }

        private void TryResolveDraft()
        {
            if (Phase != GoonMatchPhase.Draft) return;
            if (!_localDraftConfirmed || !_remoteDraftConfirmed) return;

            var pool = GoonDraft.SharedPool(_localAllowed, _remoteAllowed);
            if (!GoonDraft.IsValidSharedPool(pool, out _)) return;   // cannot happen; never guess

            _sharedPool = pool;
            _draftResolved = true;
            Scoring.Configure(LocalAttentionMode, GoonDraft.MatchRiskTier(pool));
            App.Logger?.Information("[GG] draft agreed: pool {Pool} (+ always-on {AlwaysOn})",
                string.Join("+", pool), GoonDraft.AlwaysOnElement);
        }

        // -------------------------------------------------- live-phase input

        /// <summary>Phase E feeds the gaze-derived attention percentage (0..100). Cam mode only.</summary>
        public void ReportAttention(double pct) => Scoring.ReportAttention(pct);

        /// <summary>Phase E reports the outcome of an interaction check prompt. No-cam mode only.</summary>
        public void ReportInteractionCheck(bool passed)
        {
            Scoring.ReportInteractionCheck(passed);
            if (!passed) App.Logger?.Information("[GG] interaction check failed ({Count} total)", Scoring.FailedChecks);
        }

        public void SetCloseness(int? closeness)
        {
            LocalCloseness = closeness.HasValue ? Math.Clamp(closeness.Value, 0, 3) : null;
        }

        public void SendEmote(string text, string icon)
        {
            if (_ended || Phase == GoonMatchPhase.Idle) return;
            Send(new EmoteMsg
            {
                Text = Truncate(SanitizeText(text), EmoteTextMaxChars),
                Icon = Truncate(SanitizeText(icon), EmoteIconMaxChars),
            });
        }

        // ---------------------------------------------------------- payloads

        /// <summary>
        /// Fires an offensive payload at the opponent. Sender-side mirror of the receiver's
        /// gate (charges, rate limit, one heavy per match) so a well-behaved client is never
        /// rejected; the receiver re-checks everything anyway.
        /// </summary>
        public bool TryFirePayload(GoonPayloadRequest request, out string error)
        {
            error = "";
            if (request == null) { error = "no request"; return false; }
            if (Phase is not (GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath)) { error = "match is not live"; return false; }
            if (request.Kind == GoonPayloadKind.BrainDrain && _localHeavyUsed) { error = "heavy already used this match"; return false; }
            if (!AvailablePayloadKinds.Contains(request.Kind)) { error = $"opponent's client cannot run {request.Kind}"; return false; }

            int cost = GoonConsts.CostOf(request.Kind);
            if (cost <= 0 || cost == int.MaxValue) { error = "unknown payload"; return false; }
            if (Scoring.Charges < cost) { error = $"needs {cost} charge(s)"; return false; }

            long nowLocal = Environment.TickCount64;
            if (!_outboundLimiter.TryAdmit(nowLocal))
            {
                error = $"rate limited ({_outboundLimiter.MsUntilNextToken(nowLocal) / 1000}s)";
                return false;
            }
            if (!Scoring.TrySpend(request.Kind, out cost)) { error = $"needs {cost} charge(s)"; return false; }

            var id = $"p{++_payloadSeq}{(IsHost ? "h" : "g")}";
            var msg = new PayloadMsg
            {
                Id = id,
                Kind = request.Kind,
                FireAtMatchMs = ClockNow() + Math.Max(GoonConsts.MinScheduleBufferMs, PayloadScheduleBufferMs),
                DurationMs = Math.Clamp(request.DurationMs, 1000, MaxPayloadDurationMs),
                Tags = request.Tags,
                Text = Truncate(SanitizeText(request.Text), GoonConsts.OpponentTextMaxChars),
                Voice = request.Voice,
                Pattern = request.Pattern,
                Intensity = Math.Clamp(request.Intensity, 0.0, 1.0),
            };

            if (request.Kind == GoonPayloadKind.BrainDrain)
            {
                _localHeavyUsed = true;
                _localHeavyPayloadId = id;
            }
            _outboundCosts[id] = cost;
            Send(msg);
            App.Logger?.Information("[GG] payload out {Id} {Kind} cost {Cost}", id, request.Kind, cost);
            return true;
        }

        /// <summary>
        /// Credits charges earned outside the payload/round economy (the bubble economy is the
        /// first consumer). Integer count >= 1; the total is clamped to
        /// <see cref="GoonConsts.ChargeCap"/> exactly like every other earning, and the next state
        /// tick reports the new meter. No-ops outside the Live phase.
        ///
        /// MIRROR: Resources/web/goon/core/match.js creditCharges(count, reason).
        /// </summary>
        /// <returns>true when the request was accepted (credited, cap permitting).</returns>
        public bool CreditCharges(int count, string reason)
        {
            if (_disposed || _ended) return false;
            if (Phase != GoonMatchPhase.Live) return false;
            if (count < 1) return false;

            int before = Scoring.Charges;
            for (int i = 0; i < count; i++) Scoring.AwardEventWon();   // caps at GoonConsts.ChargeCap
            App.Logger?.Information("[GG] charges +{Count} ({Reason}) -> {Now} (was {Before})",
                count, string.IsNullOrEmpty(reason) ? "unspecified" : reason, Scoring.Charges, before);
            return true;
        }

        /// <summary>
        /// Phase C calls this when an accepted inbound payload finishes.
        /// endured = the receiver took it all the way -> +1 charge (plan §charge economy).
        /// </summary>
        public void NotifyInboundPayloadFinished(string payloadId, bool endured)
        {
            if (string.IsNullOrEmpty(payloadId)) return;
            Send(new PayloadReceiptMsg
            {
                Id = payloadId,
                Status = endured ? GoonReceiptStatus.Survived : GoonReceiptStatus.Completed,
            });
            if (endured) Scoring.AwardPayloadEndured();
        }

        // ------------------------------------------------------------- mercy

        /// <summary>
        /// The dignified concede. Available in EVERY phase — the Esc ladder maps straight here
        /// (Phase E does the key hooking; this method never touches panic/lockdown settings).
        /// Pre-Live it degrades to a clean cancel that never reaches the ledger.
        /// </summary>
        public void DeclareMercy()
        {
            if (_ended || Phase == GoonMatchPhase.Idle) return;

            long atMs = ClockNow();
            _localMercyMatchMs = atMs;
            Send(new MercyMsg { AtMatchMs = atMs });

            bool preLive = Phase is GoonMatchPhase.Lobby or GoonMatchPhase.Consent
                                 or GoonMatchPhase.Draft or GoonMatchPhase.Countdown;
            bool draw = _remoteMercyMatchMs.HasValue && Math.Abs(atMs - _remoteMercyMatchMs.Value) <= DrawWindowMs;

            App.Logger?.Information("[GG] local mercy at {At} (preLive={PreLive}, draw={Draw})", atMs, preLive, draw);
            EndMatch(draw ? GoonEndReason.Draw : GoonEndReason.Mercy, localLost: !draw, countsForLedger: !preLive);
        }

        /// <summary>
        /// Clean lobby failure: incompatible protocol version or an empty capability
        /// intersection. Tears the lobby down with a reason instead of letting the two
        /// clients desync mid-match.
        /// </summary>
        private void FailLobby(string reason)
        {
            App.Logger?.Warning("[GG] lobby failed: {Reason}", reason);
            LobbyFailureReason = reason;

            var handler = LobbyFailed;
            if (handler != null && !_dispatcher.HasShutdownStarted)
            {
                try { handler(this, reason); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GG] LobbyFailed handler threw"); }
            }

            // No dedicated teardown verb in the protocol: a pre-live mercy is the clean exit
            // both clients already understand, and it never reaches the ledger.
            Send(new MercyMsg { AtMatchMs = ClockNow() });
            _ended = true;
            StopTimers();
            StopPhaseTimer();
            SetPhase(GoonMatchPhase.Idle);
        }

        /// <summary>Aborts a lobby/consent/draft locally without the mercy semantics (UI "cancel").</summary>
        public void CancelMatch(string reason)
        {
            if (_ended)
            {
                ResetToIdle();
                return;
            }
            App.Logger?.Information("[GG] match cancelled: {Reason}", reason);
            if (Phase is GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath)
            {
                DeclareMercy();
                return;
            }
            Send(new MercyMsg { AtMatchMs = ClockNow() });
            EndMatch(GoonEndReason.Mercy, localLost: true, countsForLedger: false);
        }

        // ------------------------------------------------------ message pump

        private void OnMessageReceived(object? sender, GoonMessage message)
        {
            if (_disposed || message == null) return;
            if (_dispatcher.HasShutdownStarted) return;

            try
            {
                switch (message)
                {
                    case HelloMsg hello: HandleHello(hello); break;
                    case ConsentSheetMsg consent: HandleConsent(consent); break;
                    case DraftMsg draft: HandleDraft(draft); break;
                    case MatchStartMsg start: HandleMatchStart(start); break;
                    case StateTickMsg tick: HandleTick(tick); break;
                    case PayloadMsg payload: HandleInboundPayload(payload); break;
                    case PayloadReceiptMsg receipt: HandleReceipt(receipt); break;
                    case MercyMsg mercy: HandleRemoteMercy(mercy); break;
                    case EmoteMsg emote: HandleEmote(emote); break;
                    case ResultMsg result: HandleRemoteResult(result); break;
                    case RoundScheduleMsg:
                    case RoundResultMsg:
                        _runner?.HandleMessage(message);
                        break;
                    default:
                        // Clock ping/pong belongs to the transport; anything else is a newer protocol.
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] message handling failed for {Type}", message.Type);
            }
        }

        private void OnTransportStateChanged(object? sender, GoonTransportState state)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            App.Logger?.Information("[GG] transport state {State}", state);

            if (state is GoonTransportState.ConnectedP2P or GoonTransportState.ConnectedRelay)
            {
                SendHelloOnce();
            }
        }

        private void SendHelloOnce()
        {
            if (_helloSent) return;
            _helloSent = true;
            Send(new HelloMsg
            {
                DisplayName = App.Settings?.Current?.UserDisplayName ?? "Player",
                AttentionMode = LocalAttentionMode,
                ToyConnected = LocalToyConnected,
                AppVersion = UpdateService.AppVersion,
                Caps = LocalCaps,
            });
        }

        private void HandleHello(HelloMsg hello)
        {
            _remoteHello = hello;
            Opponent.DisplayName = Truncate(SanitizeText(hello.DisplayName), 32);
            Opponent.AppVersion = Truncate(SanitizeText(hello.AppVersion), 16);
            Opponent.AttentionMode = hello.AttentionMode;
            Opponent.ToyConnected = hello.ToyConnected;
            Opponent.Platform = Truncate(SanitizeText(hello.Caps?.Platform), 16);
            RaiseSafe(OpponentStateChanged);

            SendHelloOnce();

            // (4) Protocol compatibility — fail the lobby cleanly instead of desyncing later.
            var caps = hello.Caps;
            if (caps != null && caps.MinProtocolVersion > GoonConsts.ProtocolVersion)
            {
                FailLobby($"opponent requires protocol v{caps.MinProtocolVersion}, this client speaks v{GoonConsts.ProtocolVersion} - update required");
                return;
            }
            if (hello.Version < LocalCaps.MinProtocolVersion)
            {
                FailLobby($"opponent speaks protocol v{hello.Version}, this client needs at least v{LocalCaps.MinProtocolVersion}");
                return;
            }

            // (1) and (3): every cross-client set is an intersection of the two advertisements.
            AvailableDraftPool = GoonCapabilities.Intersect(LocalCaps.SupportedElements, caps?.SupportedElements);
            AvailablePayloadKinds = GoonCapabilities.Intersect(LocalCaps.SupportedPayloads, caps?.SupportedPayloads);

            var rounds = GoonCapabilities.Intersect(LocalCaps.SupportedRounds, caps?.SupportedRounds);
            if (!rounds.Contains(GoonCapabilities.UniversalRound)) rounds.Add(GoonCapabilities.UniversalRound);
            AllowedRoundKinds = rounds;

            // The always-on element does not count: the agreement needs MinAllowedElements
            // TOGGLEABLE elements both clients can run, or there is nothing to roll a ramp from.
            var rollable = GoonDraft.NormalizeAllowed(AvailableDraftPool);
            if (rollable.Count < GoonDraft.MinAllowedElements)
            {
                FailLobby($"only {rollable.Count} shared draft element(s) - need {GoonDraft.MinAllowedElements}");
                return;
            }

            App.Logger?.Information("[GG] caps: peer {Platform} v{Version}, pool {Pool}, payloads {Payloads}, rounds {Rounds}",
                Opponent.Platform, hello.Version, AvailableDraftPool.Count, AvailablePayloadKinds.Count, AllowedRoundKinds.Count);

            if (Phase == GoonMatchPhase.Lobby)
            {
                SetPhase(GoonMatchPhase.Consent);
                // The host authors the opening proposal; the guest may counter-propose.
                if (IsHost) ProposeConsent(ConsentSheet.LiveDurationSec, ConsentSheet.ToyCap, ConsentSheet.PayloadMinGapMs);
            }
        }

        private void HandleConsent(ConsentSheetMsg sheet)
        {
            if (Phase == GoonMatchPhase.Lobby) SetPhase(GoonMatchPhase.Consent);
            if (Phase != GoonMatchPhase.Consent) return;

            if (!SameSheet(sheet, ConsentSheet))
            {
                // A different sheet is a counter-proposal: adopt it and clear BOTH confirms so
                // nobody can be advanced onto terms they never saw.
                ConsentSheet = CloneSheet(sheet, confirmed: false);
                _localConsentConfirmed = false;
                _remoteConsentConfirmed = sheet.Confirmed;
                RaiseSafe(ConsentChanged);
                return;
            }

            _remoteConsentConfirmed = sheet.Confirmed;
            RaiseSafe(ConsentChanged);
            TryEnterDraft();
        }

        private void TryEnterDraft()
        {
            if (Phase != GoonMatchPhase.Consent) return;
            if (!_localConsentConfirmed || !_remoteConsentConfirmed) return;

            _liveDurationMs = (long)ConsentSheet.LiveDurationSec * 1000L;
            _inboundLimiter.Reset();
            _outboundLimiter.Reset();

            // Everything the pairing can run is allowed by default, on both sides — the agreement
            // screen is about switching things OFF. The peer's set is assumed to be the same
            // default until their first draft frame says otherwise, so the intersection is never
            // briefly empty.
            _localAllowed = GoonDraft.DefaultAllowed(AvailableDraftPool);
            _remoteAllowed = GoonDraft.DefaultAllowed(AvailableDraftPool);
            _localDraftConfirmed = false;
            _remoteDraftConfirmed = false;
            _draftResolved = false;
            _sharedPool = new List<GoonElement>();

            SetPhase(GoonMatchPhase.Draft);
            SendDraftState();
        }

        private void HandleDraft(DraftMsg draft)
        {
            if (Phase != GoonMatchPhase.Draft) return;

            // Prefer the v2 fields; fall back to the v1 pair so a capture/older frame still parses.
            var rawAllowed = (draft.Allowed != null && draft.Allowed.Count > 0) ? draft.Allowed : draft.Elements;
            bool rawConfirmed = draft.Confirmed ?? draft.Locked;

            var incoming = SanitizeAllowed(rawAllowed);
            bool changed = !incoming.SequenceEqual(_remoteAllowed);

            _remoteAllowed = incoming;
            _remoteDraftConfirmed = rawConfirmed;

            if (changed)
            {
                // They moved a toggle: BOTH signatures die, exactly as on the consent sheet.
                // Re-publishing our (unchanged) set tells them our signature dropped — and because
                // the set is unchanged, their handler sees changed == false and this terminates.
                _localDraftConfirmed = false;
                _draftResolved = false;
                SendDraftState();
            }

            RaiseSafe(DraftChanged);
            TryResolveDraft();
        }

        // -------------------------------------------------------- countdown

        private void TryProposeStart()
        {
            if (_startProposed || !IsHost) return;
            if (Phase != GoonMatchPhase.Draft) return;
            if (!_draftResolved) return;
            if (!_transport.Clock.IsSynced) return;   // fire-at-timestamp needs a synced clock

            _startProposed = true;
            _localSeedContribution ??= RandomUlong();
            StartMatchMs = _transport.Clock.NowMatchMs + Math.Max(GoonConsts.MinScheduleBufferMs, CountdownMs);
            Send(new MatchStartMsg { StartMatchMs = StartMatchMs, SeedContribution = _localSeedContribution.Value });
            SetPhase(GoonMatchPhase.Countdown);
        }

        private void HandleMatchStart(MatchStartMsg start)
        {
            if (Phase is not (GoonMatchPhase.Draft or GoonMatchPhase.Countdown)) return;

            _remoteSeedContribution = start.SeedContribution;

            if (!IsHost)
            {
                // Guest adopts the host's instant and answers with its own contribution.
                StartMatchMs = start.StartMatchMs;
                if (_localSeedContribution == null)
                {
                    _localSeedContribution = RandomUlong();
                    Send(new MatchStartMsg { StartMatchMs = StartMatchMs, SeedContribution = _localSeedContribution.Value });
                }
                SetPhase(GoonMatchPhase.Countdown);
            }

            if (_localSeedContribution.HasValue && _remoteSeedContribution.HasValue)
                MatchSeed = _localSeedContribution.Value ^ _remoteSeedContribution.Value;
        }

        // ------------------------------------------------------------- live

        private void EnterLive()
        {
            if (Phase != GoonMatchPhase.Countdown) return;

            if (MatchSeed == 0 && _localSeedContribution.HasValue)
            {
                // Peer contribution never arrived — degrade to our own rather than stall the match.
                MatchSeed = _localSeedContribution.Value;
                App.Logger?.Warning("[GG] starting Live without a peer seed contribution");
            }

            // The pool is the agreement's intersection, and the ramp is rolled from it — one
            // schedule, both players, no exchange: BuildRamp is a pure function of its arguments.
            var pool = (_draftResolved && _sharedPool.Count > 0)
                ? _sharedPool
                : GoonDraft.SharedPool(_localAllowed, _remoteAllowed);
            _sharedPool = pool;

            Scoring.Reset();
            Scoring.Configure(LocalAttentionMode, GoonDraft.MatchRiskTier(pool));
            _ramp = GoonDraft.BuildRamp(pool, MatchSeed, ConsentSheet.LiveDurationSec, _rngFactory);
            _rampIndex = 0;
            _liveDurationMs = (long)ConsentSheet.LiveDurationSec * 1000L;
            _activeElements.Clear();
            _liveWatch.Restart();
            _lastTickSentLocalMs = Environment.TickCount64;
            Opponent.LastTickLocalMs = Environment.TickCount64;
            _nextInteractionCheckMs = NoCamCheckIntervalMs;

            _liveTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _liveTimer.Tick += LiveTimer_Tick;
            _liveTimer.Start();

            App.Logger?.Information("[GG] Live phase: seed {Seed:X16}, {Sec}s, pool {Pool} (+always-on bubbles), risk {Risk}, {Cues} cues",
                MatchSeed, ConsentSheet.LiveDurationSec, string.Join("+", pool), Scoring.RiskTier, _ramp.Count);

            SetPhase(GoonMatchPhase.Live);
        }

        private void LiveTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed || _ended) return;
            if (Phase is not (GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath)) return;

            try
            {
                long elapsed = _liveWatch.ElapsedMilliseconds;

                if (Phase == GoonMatchPhase.Live)
                {
                    PumpRamp(elapsed);
                    Scoring.Tick(1.0);
                    PumpInteractionChecks(elapsed);

                    if (elapsed >= _liveDurationMs)
                    {
                        EnterSuddenDeath();
                        return;
                    }
                }

                MaybeSendStateTick();
                CheckOpponentFreshness();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] live tick failed");
            }
        }

        private void PumpRamp(long elapsedMs)
        {
            while (_rampIndex < _ramp.Count && _ramp[_rampIndex].OffsetMs <= elapsedMs)
            {
                var cue = _ramp[_rampIndex++];
                var args = new GoonElementCueEventArgs
                {
                    Element = cue.Element,
                    Intensity = cue.Intensity,
                    DurationMs = cue.DurationMs,
                    ElapsedMs = elapsedMs,
                };

                switch (cue.Action)
                {
                    case GoonCueAction.Start:
                        if (_activeElements.Add(cue.Element)) RaiseCue(ElementStartRequested, args);
                        else RaiseCue(ElementIntensityChanged, args);
                        break;
                    case GoonCueAction.Intensity:
                        if (_activeElements.Contains(cue.Element)) RaiseCue(ElementIntensityChanged, args);
                        break;
                    case GoonCueAction.Stop:
                        if (_activeElements.Remove(cue.Element)) RaiseCue(ElementStopRequested, args);
                        break;
                }
            }
        }

        private void PumpInteractionChecks(long elapsedMs)
        {
            if (LocalAttentionMode != GoonAttentionMode.NoCam) return;
            if (elapsedMs < _nextInteractionCheckMs) return;
            _nextInteractionCheckMs = elapsedMs + NoCamCheckIntervalMs;
            RaiseSafe(InteractionCheckDue);
        }

        private void MaybeSendStateTick()
        {
            long now = Environment.TickCount64;
            if (now - _lastTickSentLocalMs < GoonConsts.TickIntervalMs) return;
            _lastTickSentLocalMs = now;

            Send(new StateTickMsg
            {
                AtMatchMs = ClockNow(),
                Score = Scoring.Score,
                AttentionPct = (int)Math.Round(Scoring.AttentionPct),
                AttentionMode = LocalAttentionMode,
                ActiveEffects = _activeElements.Select(x => x.ToString()).ToList(),
                ToyActive = _activeElements.Contains(GoonElement.ToyPatterns),
                Closeness = LocalCloseness,
                Charges = Scoring.Charges,
            });
        }

        private void CheckOpponentFreshness()
        {
            long age = Environment.TickCount64 - Opponent.LastTickLocalMs;
            var health = age >= GoonConsts.TickDeadMs ? GoonConnectionHealth.Dead
                       : age >= GoonConsts.TickStaleMs ? GoonConnectionHealth.Wobbly
                       : GoonConnectionHealth.Fresh;

            if (health != Opponent.Health)
            {
                Opponent.Health = health;
                App.Logger?.Information("[GG] opponent connection {Health} (age {Age}ms)", health, age);
                RaiseHealth(health);
            }

            if (health == GoonConnectionHealth.Dead && !_ended)
            {
                // Recorded as an abandon, NEVER auto-declared a mercy (plan §state tick).
                EndMatch(GoonEndReason.Abandon, localLost: false, countsForLedger: true);
            }
        }

        private void HandleTick(StateTickMsg tick)
        {
            Opponent.Score = tick.Score;
            Opponent.AttentionPct = Math.Clamp(tick.AttentionPct, 0, 100);
            Opponent.AttentionMode = tick.AttentionMode;
            Opponent.ActiveEffects = tick.ActiveEffects ?? new List<string>();
            Opponent.ToyActive = tick.ToyActive;
            Opponent.Closeness = tick.Closeness.HasValue ? Math.Clamp(tick.Closeness.Value, 0, 3) : null;
            Opponent.Charges = Math.Clamp(tick.Charges, 0, GoonConsts.ChargeCap);
            Opponent.LastTickLocalMs = Environment.TickCount64;
            Opponent.HasSeenTick = true;

            // The opponent's self-reported meter is the ceiling we hold them to when their
            // next payload lands. Ticks may only ever RAISE it back toward what they claim.
            _opponentChargesKnown = Opponent.Charges;

            if (Opponent.Health != GoonConnectionHealth.Fresh)
            {
                Opponent.Health = GoonConnectionHealth.Fresh;
                RaiseHealth(GoonConnectionHealth.Fresh);
            }
            RaiseSafe(OpponentStateChanged);
        }

        // ----------------------------------------------- inbound payload gate

        private void HandleInboundPayload(PayloadMsg payload)
        {
            if (string.IsNullOrEmpty(payload.Id)) payload.Id = $"anon{++_payloadSeq}";

            if (Phase is not (GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath))
            {
                RejectPayload(payload, GoonReceiptStatus.RejectedFiltered, "not live");
                return;
            }

            int cost = GoonConsts.CostOf(payload.Kind);
            if (cost <= 0 || cost == int.MaxValue)
            {
                RejectPayload(payload, GoonReceiptStatus.RejectedFiltered, "unknown kind");
                return;
            }
            // Belt-and-braces: a kind we never advertised is not runnable here, whatever the wire says.
            if (!LocalCaps.SupportedPayloads.Contains(payload.Kind))
            {
                RejectPayload(payload, GoonReceiptStatus.RejectedFiltered, "kind not in our advertised caps");
                return;
            }
            if (payload.Kind == GoonPayloadKind.BrainDrain && _remoteHeavyUsed)
            {
                RejectPayload(payload, GoonReceiptStatus.RejectedFiltered, "heavy already used");
                return;
            }

            long nowLocal = Environment.TickCount64;
            if (!_inboundLimiter.TryAdmit(nowLocal))
            {
                RejectPayload(payload, GoonReceiptStatus.RejectedRate, "rate limit");
                return;
            }
            if (_opponentChargesKnown < cost)
            {
                // Economy violation. No dedicated receipt status exists in the contract, so it
                // rides rejected_rate and is logged distinctly (see report / open questions).
                RejectPayload(payload, GoonReceiptStatus.RejectedRate, $"charge economy: claimed {_opponentChargesKnown} < cost {cost}");
                return;
            }

            _opponentChargesKnown -= cost;
            if (payload.Kind == GoonPayloadKind.BrainDrain) _remoteHeavyUsed = true;

            // Defensive normalisation. Phase C still owns full receiver-side resolution
            // (tags -> own library, tag stripping, level gates, mixer caps).
            payload.Text = Truncate(SanitizeText(payload.Text), GoonConsts.OpponentTextMaxChars);
            payload.Intensity = Math.Clamp(payload.Intensity, 0.0, 1.0);
            // ONE clamp band for every kind (1 s .. MaxPayloadDurationMs) — there is deliberately no
            // per-kind table, so a new sustained kind (Spiral, 2026-08-03) inherits BrainDrain's band
            // by construction. Add a per-kind switch here only if a kind ever needs a NARROWER band,
            // and mirror it in core/match.js at the same time.
            payload.DurationMs = Math.Clamp(payload.DurationMs, 1000, MaxPayloadDurationMs);

            long earliestMatchMs = ClockNow() + GoonConsts.MinScheduleBufferMs;
            if (payload.FireAtMatchMs < earliestMatchMs) payload.FireAtMatchMs = earliestMatchMs;

            Send(new PayloadReceiptMsg { Id = payload.Id, Status = GoonReceiptStatus.Accepted });

            long fireAtLocal;
            try { fireAtLocal = _transport.Clock.MatchMsToLocalMs(payload.FireAtMatchMs); }
            catch { fireAtLocal = Environment.TickCount64 + GoonConsts.MinScheduleBufferMs; }

            App.Logger?.Information("[GG] payload in {Id} {Kind} accepted, fires in {Ms}ms",
                payload.Id, payload.Kind, fireAtLocal - Environment.TickCount64);

            var handler = PayloadAccepted;
            if (handler == null) return;
            try { handler(this, new GoonInboundPayloadEventArgs { Payload = payload, FireAtLocalMs = fireAtLocal }); }
            catch (Exception ex) { App.Logger?.Error(ex, "[GG] PayloadAccepted handler threw"); }
        }

        private void RejectPayload(PayloadMsg payload, string status, string reason)
        {
            App.Logger?.Information("[GG] payload in {Id} {Kind} rejected: {Reason}", payload.Id, payload.Kind, reason);
            var receipt = new PayloadReceiptMsg { Id = payload.Id, Status = status };
            Send(receipt);
            var handler = PayloadRejected;
            if (handler == null) return;
            try { handler(this, receipt); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] PayloadRejected handler threw"); }
        }

        private void HandleReceipt(PayloadReceiptMsg receipt)
        {
            if (!string.IsNullOrEmpty(receipt.Id) &&
                receipt.Status != null && receipt.Status.StartsWith("rejected", StringComparison.OrdinalIgnoreCase) &&
                _outboundCosts.TryGetValue(receipt.Id, out int cost))
            {
                _outboundCosts.Remove(receipt.Id);
                Scoring.Refund(cost);
                // A rejected heavy was never delivered — give the once-per-match slot back.
                if (_localHeavyPayloadId == receipt.Id)
                {
                    _localHeavyPayloadId = null;
                    _localHeavyUsed = false;
                }
                App.Logger?.Information("[GG] payload {Id} rejected ({Status}) - {Cost} charge(s) refunded",
                    receipt.Id, receipt.Status, cost);
            }
            else if (!string.IsNullOrEmpty(receipt.Id) &&
                     (receipt.Status == GoonReceiptStatus.Completed || receipt.Status == GoonReceiptStatus.Survived))
            {
                _outboundCosts.Remove(receipt.Id);
            }

            var handler = PayloadReceiptReceived;
            if (handler == null) return;
            try { handler(this, receipt); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] PayloadReceiptReceived handler threw"); }
        }

        private void HandleEmote(EmoteMsg emote)
        {
            emote.Text = Truncate(SanitizeText(emote.Text), EmoteTextMaxChars);
            emote.Icon = Truncate(SanitizeText(emote.Icon), EmoteIconMaxChars);
            var handler = EmoteReceived;
            if (handler == null) return;
            try { handler(this, emote); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] EmoteReceived handler threw"); }
        }

        private void HandleRemoteMercy(MercyMsg mercy)
        {
            if (_ended) return;
            _remoteMercyMatchMs = mercy.AtMatchMs;
            if (Phase == GoonMatchPhase.SuddenDeath) _runner?.HandleMessage(mercy);

            bool preLive = Phase is GoonMatchPhase.Lobby or GoonMatchPhase.Consent
                                 or GoonMatchPhase.Draft or GoonMatchPhase.Countdown;
            bool draw = _localMercyMatchMs.HasValue && Math.Abs(mercy.AtMatchMs - _localMercyMatchMs.Value) <= DrawWindowMs;

            App.Logger?.Information("[GG] opponent mercy at {At} (draw={Draw})", mercy.AtMatchMs, draw);
            EndMatch(draw ? GoonEndReason.Draw : GoonEndReason.Mercy, localLost: false, countsForLedger: !preLive);
        }

        // ------------------------------------------------------ sudden death

        private void EnterSuddenDeath()
        {
            if (Phase != GoonMatchPhase.Live) return;

            StopAllElements();
            SetPhase(GoonMatchPhase.SuddenDeath);

            var runner = _runner;
            if (runner == null)
            {
                // Phase D not attached: degrade to the score comparison rather than hanging.
                App.Logger?.Warning("[GG] no sudden-death runner attached — settling on score");
                int mine = Scoring.Score, theirs = Opponent.Score;
                if (mine == theirs) EndMatch(GoonEndReason.Draw, localLost: false, countsForLedger: true);
                else EndMatch(GoonEndReason.SuddenDeathLoss, localLost: mine < theirs, countsForLedger: true);
                return;
            }

            runner.RoundWon += OnRoundWon;
            runner.RoundLost += OnRoundLost;
            runner.NetLossReached += OnNetLossReached;

            var ctx = new GoonSuddenDeathContext
            {
                Transport = _transport,
                Clock = _transport.Clock,
                RngFactory = _rngFactory,
                MatchSeed = MatchSeed,
                IsHost = IsHost,
                LocalMode = LocalAttentionMode,
                RemoteMode = Opponent.AttentionMode,
                NetLossThreshold = GoonConsts.SuddenDeathNetLoss,
                // Caps intersection: never schedule a round the peer's client cannot render.
                AllowedRoundKinds = AllowedRoundKinds,
            };

            _runnerCts = new CancellationTokenSource();
            _ = RunSuddenDeathAsync(runner, ctx, _runnerCts.Token);
        }

        private async Task RunSuddenDeathAsync(IGoonSuddenDeathRunner runner, GoonSuddenDeathContext ctx, CancellationToken ct)
        {
            try
            {
                await runner.StartAsync(ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Mercy / dispose during sudden death — expected.
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] sudden-death runner failed");
                if (Application.Current?.Dispatcher == null) return;
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (_disposed || _ended) return;
                            EndMatch(GoonEndReason.Draw, localLost: false, countsForLedger: true);
                        }
                        catch (Exception inner) { App.Logger?.Error(inner, "[GG] sudden-death failure handling threw"); }
                    });
                }
                catch (Exception dispatchEx) { App.Logger?.Warning(dispatchEx, "[GG] could not dispatch sudden-death failure"); }
            }
        }

        private void OnRoundWon(object? sender, int roundNo)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            App.Logger?.Information("[GG] sudden-death round {Round} won", roundNo);
            Scoring.AwardEventWon();
        }

        private void OnRoundLost(object? sender, int roundNo)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            App.Logger?.Information("[GG] sudden-death round {Round} lost", roundNo);
        }

        private void OnNetLossReached(object? sender, bool localLost)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            EndMatch(GoonEndReason.SuddenDeathLoss, localLost, countsForLedger: true);
        }

        private void DetachRunner()
        {
            var runner = _runner;
            if (runner == null) return;

            runner.RoundWon -= OnRoundWon;
            runner.RoundLost -= OnRoundLost;
            runner.NetLossReached -= OnNetLossReached;
            _runner = null;

            try { _runnerCts?.Cancel(); } catch { /* already gone */ }
            try { _runnerCts?.Dispose(); } catch { /* already gone */ }
            _runnerCts = null;
            _ = StopRunnerAsync(runner);
        }

        private static async Task StopRunnerAsync(IGoonSuddenDeathRunner runner)
        {
            try { await runner.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] sudden-death StopAsync threw"); }
        }

        // ------------------------------------------------- end + result sign

        private void EndMatch(GoonEndReason reason, bool localLost, bool countsForLedger)
        {
            if (_ended) return;
            _ended = true;

            StopTimers();
            StopAllElements();
            DetachRunner();

            long survivedMs = _liveWatch.ElapsedMilliseconds;
            _liveWatch.Stop();

            bool? winnerIsHost = reason == GoonEndReason.Draw ? null : (localLost ? !IsHost : IsHost);

            var result = new GoonMatchResult
            {
                EndReason = reason,
                WinnerIsHost = winnerIsHost,
                LocalIsHost = IsHost,
                HostScore = IsHost ? Scoring.Score : Opponent.Score,
                GuestScore = IsHost ? Opponent.Score : Scoring.Score,
                SurvivedMs = survivedMs,
                CountsForLedger = countsForLedger,
            };
            Result = result;

            SetPhase(GoonMatchPhase.Recap);

            Send(new ResultMsg
            {
                EndReason = reason,
                WinnerIsHost = winnerIsHost,
                HostScore = result.HostScore,
                GuestScore = result.GuestScore,
                SurvivedMs = survivedMs,
                Agree = false,
            });

            _resultDeadlineLocalMs = Environment.TickCount64 + ResultHandshakeTimeoutMs;
            App.Logger?.Information("[GG] match ended: {Reason}, localLost={Lost}, survived {Sec}s, {H}-{G}",
                reason, localLost, survivedMs / 1000, result.HostScore, result.GuestScore);

            var handler = MatchEnded;
            if (handler == null) return;
            try { handler(this, result); }
            catch (Exception ex) { App.Logger?.Error(ex, "[GG] MatchEnded handler threw"); }
        }

        private void HandleRemoteResult(ResultMsg remote)
        {
            if (!_ended)
            {
                // Their end signal reached us before ours fired (lost MercyMsg, or a
                // sudden-death exit resolved on their side first). Adopt their outcome.
                bool localLost = remote.WinnerIsHost.HasValue && remote.WinnerIsHost.Value != IsHost;
                EndMatch(remote.EndReason, localLost, countsForLedger: true);
            }
            var result = Result;
            if (result == null) return;

            // Each side's own score is authoritative for its own half.
            if (IsHost) result.GuestScore = remote.GuestScore;
            else result.HostScore = remote.HostScore;

            if (remote.Agree)
            {
                result.Agreed = true;
                result.Disputed = false;
                FinalizeResult();
                return;
            }

            bool sameReason = remote.EndReason == result.EndReason;
            bool sameWinner = remote.WinnerIsHost == result.WinnerIsHost;

            if (sameReason && sameWinner)
            {
                if (!_countersignSent)
                {
                    _countersignSent = true;
                    Send(new ResultMsg
                    {
                        EndReason = result.EndReason,
                        WinnerIsHost = result.WinnerIsHost,
                        HostScore = result.HostScore,
                        GuestScore = result.GuestScore,
                        SurvivedMs = result.SurvivedMs,
                        Agree = true,
                    });
                }
                result.Agreed = true;
                result.Disputed = false;
            }
            else
            {
                // Disputed: both claims are recorded; the ledger stores the pair and the
                // uncontested cosmetics (survival time, payload counts) are still granted.
                result.Disputed = true;
                result.RemoteClaim = remote;
                App.Logger?.Warning("[GG] result DISPUTED - local {LocalReason}/{LocalWinner} vs remote {RemoteReason}/{RemoteWinner}",
                    result.EndReason, result.WinnerIsHost, remote.EndReason, remote.WinnerIsHost);
            }

            FinalizeResult();
        }

        private void FinalizeResult()
        {
            var result = Result;
            if (_finalized || result == null) return;
            _finalized = true;
            StopPhaseTimer();

            var handler = ResultFinalized;
            if (handler == null) return;
            try { handler(this, result); }
            catch (Exception ex) { App.Logger?.Error(ex, "[GG] ResultFinalized handler threw"); }
        }

        // ------------------------------------------------------ phase plumbing

        private void SetPhase(GoonMatchPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            App.Logger?.Information("[GG] phase -> {Phase}", phase);

            var handler = PhaseChanged;
            if (handler == null) return;
            try { handler(this, phase); }
            catch (Exception ex) { App.Logger?.Error(ex, "[GG] PhaseChanged handler threw"); }
        }

        private void StartPhaseTimer()
        {
            if (_phaseTimer != null) return;
            _phaseTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(PhaseTimerIntervalMs),
            };
            _phaseTimer.Tick += PhaseTimer_Tick;
            _phaseTimer.Start();
        }

        private void PhaseTimer_Tick(object? sender, EventArgs e)
        {
            if (_disposed) return;
            try
            {
                switch (Phase)
                {
                    case GoonMatchPhase.Draft:
                        TryProposeStart();
                        break;

                    case GoonMatchPhase.Countdown:
                        if (StartMatchMs > 0 && ClockNow() >= StartMatchMs) EnterLive();
                        break;

                    case GoonMatchPhase.Recap:
                        if (!_finalized && Environment.TickCount64 >= _resultDeadlineLocalMs)
                        {
                            if (Result != null && !Result.Agreed)
                            {
                                Result.Disputed = true;
                                App.Logger?.Warning("[GG] result handshake timed out — recorded as disputed");
                            }
                            FinalizeResult();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GG] phase tick failed");
            }
        }

        private void StopPhaseTimer()
        {
            if (_phaseTimer == null) return;
            _phaseTimer.Stop();
            _phaseTimer.Tick -= PhaseTimer_Tick;
            _phaseTimer = null;
        }

        private void StopTimers()
        {
            if (_liveTimer != null)
            {
                _liveTimer.Stop();
                _liveTimer.Tick -= LiveTimer_Tick;
                _liveTimer = null;
            }
            // The phase timer keeps running through Recap for the handshake deadline.
        }

        /// <summary>Stops every element the ramp started. Phase C's executor does the real teardown.</summary>
        private void StopAllElements()
        {
            if (_activeElements.Count == 0) return;
            long elapsed = _liveWatch.ElapsedMilliseconds;
            foreach (var element in _activeElements.ToList())
            {
                RaiseCue(ElementStopRequested, new GoonElementCueEventArgs
                {
                    Element = element,
                    Intensity = 0,
                    DurationMs = 0,
                    ElapsedMs = elapsed,
                });
            }
            _activeElements.Clear();
        }

        private void ResetToIdle()
        {
            StopTimers();
            StopPhaseTimer();
            SetPhase(GoonMatchPhase.Idle);
        }

        // ------------------------------------------------------------ helpers

        private long ClockNow()
        {
            try { return _transport.Clock?.NowMatchMs ?? 0; }
            catch { return 0; }
        }

        private void Send(GoonMessage message)
        {
            if (_disposed || message == null) return;
            _ = SendSafeAsync(_transport, message);
        }

        private static async Task SendSafeAsync(IGoonTransport transport, GoonMessage message)
        {
            try
            {
                await transport.SendAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[GG] send failed for {Type}", message.Type);
            }
        }

        private void RaiseCue(EventHandler<GoonElementCueEventArgs>? handler, GoonElementCueEventArgs args)
        {
            if (handler == null) return;
            if (_dispatcher.HasShutdownStarted) return;
            try { handler(this, args); }
            catch (Exception ex) { App.Logger?.Error(ex, "[GG] element cue handler threw for {Element}", args.Element); }
        }

        private void RaiseSafe(EventHandler? handler)
        {
            if (handler == null) return;
            if (_dispatcher.HasShutdownStarted) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] handler threw"); }
        }

        private void RaiseHealth(GoonConnectionHealth health)
        {
            var handler = ConnectionHealthChanged;
            if (handler == null || _dispatcher.HasShutdownStarted) return;
            try { handler(this, health); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] ConnectionHealthChanged handler threw"); }
        }

        private static ConsentSheetMsg CloneSheet(ConsentSheetMsg sheet, bool confirmed) => new()
        {
            LiveDurationSec = sheet.LiveDurationSec,
            ToyCap = sheet.ToyCap,
            PayloadMinGapMs = sheet.PayloadMinGapMs,
            Confirmed = confirmed,
        };

        /// <summary>Sheet identity ignores the Confirmed flag — the TERMS must match, not the signature.</summary>
        private static bool SameSheet(ConsentSheetMsg a, ConsentSheetMsg b) =>
            a.LiveDurationSec == b.LiveDurationSec &&
            Math.Abs(a.ToyCap - b.ToyCap) < 0.0001 &&
            a.PayloadMinGapMs == b.PayloadMinGapMs;

        /// <summary>Opponent-supplied text: strip markup/control chars (Phase C caps + strips again).</summary>
        private static string SanitizeText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c == '<' || c == '>') continue;
                if (char.IsControl(c) && c != ' ') continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static ulong RandomUlong()
        {
            Span<byte> bytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _transport.MessageReceived -= OnMessageReceived;
                _transport.StateChanged -= OnTransportStateChanged;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GG] transport unsubscribe failed"); }

            StopTimers();
            StopPhaseTimer();
            DetachRunner();
            _liveWatch.Stop();
        }

        /// <summary>
        /// splitmix64 stand-in so the engine is runnable before Phase A's GoonRng lands.
        /// Pass a real factory into the constructor at integration time.
        /// </summary>
        private sealed class FallbackRng : IGoonRng
        {
            private ulong _state;
            public FallbackRng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

            public ulong NextULong()
            {
                unchecked
                {
                    _state += 0x9E3779B97F4A7C15UL;
                    ulong z = _state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive) return minInclusive;
                ulong range = (ulong)(maxExclusive - minInclusive);
                return minInclusive + (int)(NextULong() % range);
            }

            public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
