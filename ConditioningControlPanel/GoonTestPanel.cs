using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services.GoonGame;

// ============================================================================
// GOON GAME — dev play-test cockpit, one player panel (used twice by
// GoonTestWindow). Everything is built in code so the exact same layout backs
// Player A and Player B with zero XAML duplication.
//
// The panel talks ONLY to GoonGameService / GoonMatchService. It never touches
// panic, lockdown, tray, sessions or any other app service, and it does no
// premium gating (the server enforces passes at /v2/goon/invite).
//
// The facade REBUILDS its match service on relay fallback, so this panel always
// re-subscribes from CurrentMatchChanged and never caches the first CurrentMatch.
// ============================================================================

namespace ConditioningControlPanel
{
    internal sealed class GoonTestPanel : IDisposable
    {
        private const int LogCap = 900;
        private const int TestPayloadDurationMs = 8000;

        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xBF));
        private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x5B));
        private static readonly Brush Hot = new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x1F));
        private static readonly Brush Idle = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x5F));

        private readonly string _name;
        private readonly int _simSeed;

        // --- connection row
        private readonly TextBlock _txtTitle = new();
        private readonly Button _btnHost = new();
        private readonly Button _btnJoin = new();
        private readonly Button _btnLeave = new();
        private readonly TextBox _txtCode = new();
        private readonly TextBlock _txtCodeBig = new();
        private readonly TextBlock _txtState = new();
        private readonly ComboBox _cmbAttention = new();
        private readonly CheckBox _chkToy = new();
        private readonly CheckBox _chkSimRounds = new();

        // --- consent row
        private readonly ComboBox _cmbDuration = new();
        private readonly Button _btnPropose = new();
        private readonly Button _btnConfirm = new();
        private readonly Button _btnWithdraw = new();
        private readonly TextBlock _txtConsent = new();

        // --- draft row
        private readonly WrapPanel _draftPanel = new();
        private readonly List<CheckBox> _draftBoxes = new();
        private readonly Button _btnAutoDraft = new();
        private readonly Button _btnLockDraft = new();
        private readonly TextBlock _txtDraft = new();

        // --- live row
        private readonly TextBlock _txtScore = new();
        private readonly Slider _sldAttention = new();
        private readonly TextBlock _txtAttention = new();
        private readonly Button _btnCheckOk = new();
        private readonly Button _btnCheckFail = new();
        private readonly Button _btnFlash = new();
        private readonly Button _btnSub = new();
        private readonly Button _btnBubbles = new();
        private readonly Button _btnCharge = new();
        private readonly Button _btnEmote = new();
        private readonly Button _btnMercy = new();
        private readonly CheckBox _chkAutoEndure = new();
        private readonly Button _btnEndure = new();
        private readonly Button _btnFlinch = new();
        private readonly TextBlock _txtInbound = new();

        // --- log
        private readonly ListBox _log = new();

        private readonly List<DispatcherTimer> _payloadTimers = new();
        private readonly List<string> _pendingInbound = new();

        private GoonMatchService? _match;
        private bool _loopbackMode;
        private bool _ownsLoopbackMatch;
        private bool _disposed;

        public GoonTestPanel(string name, int simSeed)
        {
            _name = name;
            _simSeed = simSeed;
            Facade = new GoonGameService { RunnerFactory = CreateRunner };
            Facade.CurrentMatchChanged += Facade_CurrentMatchChanged;
            Facade.ConnectFailed += Facade_ConnectFailed;
            Root = BuildUi();
            RefreshEnabled();
        }

        /// <summary>The panel's OWN facade — never App.GoonGame (that is the app-level singleton).</summary>
        public GoonGameService Facade { get; }

        public FrameworkElement Root { get; }

        /// <summary>Last ConnectFailed reason, surfaced by the window's status strip.</summary>
        public string? LastConnectFailure { get; private set; }

        /// <summary>Raised when this panel mints an invite code (the window auto-fills the other panel).</summary>
        public event EventHandler<string>? InviteCodeMinted;

        public GoonAttentionMode SelectedAttentionMode =>
            _cmbAttention.SelectedIndex == 1 ? GoonAttentionMode.Cam : GoonAttentionMode.NoCam;

        public bool ToyConnected => _chkToy.IsChecked == true;

        public GoonMatchService? Match => _match;

        public string ShortStatus
        {
            get
            {
                var phase = _match?.Phase.ToString() ?? "-";
                var fail = string.IsNullOrEmpty(LastConnectFailure) ? "" : $"  ConnectFailed={LastConnectFailure}";
                return $"{_name}: {phase}{fail}";
            }
        }

        // ------------------------------------------------------------------ UI

        private FrameworkElement BuildUi()
        {
            var root = new DockPanel { LastChildFill = true };

            var rows = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(rows, Dock.Top);
            root.Children.Add(rows);

            rows.Children.Add(BuildConnectionRow());
            rows.Children.Add(BuildConsentRow());
            rows.Children.Add(BuildDraftRow());
            rows.Children.Add(BuildLiveRow());

            _log.SelectionMode = SelectionMode.Extended;
            ScrollViewer.SetHorizontalScrollBarVisibility(_log, ScrollBarVisibility.Auto);
            root.Children.Add(new Border
            {
                Margin = new Thickness(0, 4, 0, 0),
                Child = _log,
            });

            return root;
        }

        private static Border Section(string header, UIElement body)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.Bold,
                Foreground = Accent,
                Margin = new Thickness(0, 0, 0, 2),
            });
            stack.Children.Add(body);
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x3C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 4, 6, 6),
                Margin = new Thickness(0, 0, 0, 3),
                Child = stack,
            };
        }

        private UIElement BuildConnectionRow()
        {
            _txtTitle.Text = _name;
            _txtTitle.FontSize = 16;
            _txtTitle.FontWeight = FontWeights.Bold;

            _btnHost.Content = "Host";
            _btnHost.Click += BtnHost_Click;
            _btnJoin.Content = "Join";
            _btnJoin.Click += BtnJoin_Click;
            _btnLeave.Content = "Leave";
            _btnLeave.Click += BtnLeave_Click;

            _txtCode.Width = 110;
            _txtCode.ToolTip = "Invite code to redeem";

            _txtCodeBig.Text = "------";
            _txtCodeBig.FontSize = 22;
            _txtCodeBig.FontWeight = FontWeights.Bold;
            _txtCodeBig.Foreground = Accent;
            _txtCodeBig.Margin = new Thickness(10, 0, 0, 0);

            _cmbAttention.Items.Add("NoCam");
            _cmbAttention.Items.Add("Cam");
            _cmbAttention.SelectedIndex = 0;
            _cmbAttention.ToolTip = "LocalAttentionMode — must be set BEFORE Host/Join (it rides the hello)";

            _chkToy.Content = "Toy";
            _chkToy.ToolTip = "LocalToyConnected (advertised in the hello)";

            _chkSimRounds.Content = "Sim SD inputs";
            _chkSimRounds.IsChecked = true;
            _chkSimRounds.ToolTip = "Drive the sudden-death rounds with simulated input so the ladder actually resolves";

            _txtState.Text = "phase Idle";
            _txtState.Foreground = Muted;
            _txtState.TextWrapping = TextWrapping.Wrap;

            var line1 = new WrapPanel();
            line1.Children.Add(_txtTitle);
            line1.Children.Add(new TextBlock { Text = "  mode:", Foreground = Muted });
            line1.Children.Add(_cmbAttention);
            line1.Children.Add(_chkToy);
            line1.Children.Add(_chkSimRounds);

            var line2 = new WrapPanel();
            line2.Children.Add(_btnHost);
            line2.Children.Add(_btnJoin);
            line2.Children.Add(_txtCode);
            line2.Children.Add(_btnLeave);
            line2.Children.Add(_txtCodeBig);

            var stack = new StackPanel();
            stack.Children.Add(line1);
            stack.Children.Add(line2);
            stack.Children.Add(_txtState);
            return Section("1 — CONNECTION", stack);
        }

        private UIElement BuildConsentRow()
        {
            _cmbDuration.Items.Add("60 s");
            _cmbDuration.Items.Add("180 s");
            _cmbDuration.Items.Add("720 s");
            _cmbDuration.SelectedIndex = 0;
            _cmbDuration.SelectionChanged += Duration_SelectionChanged;

            _btnPropose.Content = "Propose";
            _btnPropose.Click += BtnPropose_Click;
            _btnConfirm.Content = "Confirm Consent";
            _btnConfirm.Click += BtnConfirm_Click;
            _btnWithdraw.Content = "Withdraw";
            _btnWithdraw.Click += BtnWithdraw_Click;

            _txtConsent.Text = "no sheet";
            _txtConsent.Foreground = Muted;
            _txtConsent.TextWrapping = TextWrapping.Wrap;

            var line = new WrapPanel();
            line.Children.Add(new TextBlock { Text = "duration:" });
            line.Children.Add(_cmbDuration);
            line.Children.Add(_btnPropose);
            line.Children.Add(_btnConfirm);
            line.Children.Add(_btnWithdraw);

            var stack = new StackPanel();
            stack.Children.Add(line);
            stack.Children.Add(_txtConsent);
            return Section("2 — CONSENT", stack);
        }

        private UIElement BuildDraftRow()
        {
            foreach (GoonElement element in Enum.GetValues<GoonElement>())
            {
                var box = new CheckBox { Content = element.ToString(), Tag = element, MinWidth = 110 };
                box.Checked += Draft_Changed;
                box.Unchecked += Draft_Changed;
                _draftBoxes.Add(box);
                _draftPanel.Children.Add(box);
            }

            _btnAutoDraft.Content = $"Auto-pick {GoonDraft.PicksPerPlayer}";
            _btnAutoDraft.Click += BtnAutoDraft_Click;
            _btnLockDraft.Content = "Lock Draft";
            _btnLockDraft.Click += BtnLockDraft_Click;

            _txtDraft.Text = "not drafting";
            _txtDraft.Foreground = Muted;
            _txtDraft.TextWrapping = TextWrapping.Wrap;

            var buttons = new WrapPanel();
            buttons.Children.Add(_btnAutoDraft);
            buttons.Children.Add(_btnLockDraft);

            var stack = new StackPanel();
            stack.Children.Add(_draftPanel);
            stack.Children.Add(buttons);
            stack.Children.Add(_txtDraft);
            return Section($"3 — DRAFT (pick {GoonDraft.PicksPerPlayer})", stack);
        }

        private UIElement BuildLiveRow()
        {
            _txtScore.Text = "score - / -";
            _txtScore.FontSize = 18;
            _txtScore.FontWeight = FontWeights.Bold;

            _sldAttention.Minimum = 0;
            _sldAttention.Maximum = 100;
            _sldAttention.Value = 90;
            _sldAttention.Width = 160;
            _sldAttention.ValueChanged += Attention_ValueChanged;
            _txtAttention.Text = "90%";
            _txtAttention.Width = 42;

            _btnCheckOk.Content = "Interaction Check OK";
            _btnCheckOk.Click += BtnCheckOk_Click;
            _btnCheckFail.Content = "Check FAIL";
            _btnCheckFail.Click += BtnCheckFail_Click;

            _btnFlash.Content = "Fire FlashBurst (1)";
            _btnFlash.Click += (s, e) => FirePayload(GoonPayloadKind.FlashBurst);
            _btnSub.Content = "Fire SubliminalStorm (1)";
            _btnSub.Click += (s, e) => FirePayload(GoonPayloadKind.SubliminalStorm);
            _btnBubbles.Content = "Fire BubbleSwarm (1)";
            _btnBubbles.Click += (s, e) => FirePayload(GoonPayloadKind.BubbleSwarm);

            _btnCharge.Content = "+1 charge (dev)";
            _btnCharge.ToolTip = "Charges only trickle every 90 s clean — this grants one so payloads are testable in a 60 s match";
            _btnCharge.Click += BtnCharge_Click;

            _btnEmote.Content = "Emote 💦";
            _btnEmote.Click += BtnEmote_Click;

            _btnMercy.Content = "MERCY";
            _btnMercy.FontWeight = FontWeights.Bold;
            _btnMercy.Background = Danger;
            _btnMercy.MinWidth = 90;
            _btnMercy.Click += BtnMercy_Click;

            _chkAutoEndure.Content = "auto-endure";
            _chkAutoEndure.IsChecked = true;
            _chkAutoEndure.Checked += (s, e) => RefreshEnabled();
            _chkAutoEndure.Unchecked += (s, e) => RefreshEnabled();

            _btnEndure.Content = "Endure";
            _btnEndure.Click += (s, e) => FinishInbound(endured: true);
            _btnFlinch.Content = "Flinch";
            _btnFlinch.Click += (s, e) => FinishInbound(endured: false);

            _txtInbound.Text = "no inbound payload";
            _txtInbound.Foreground = Muted;

            var line1 = new WrapPanel();
            line1.Children.Add(_txtScore);

            var line2 = new WrapPanel();
            line2.Children.Add(new TextBlock { Text = "attention:" });
            line2.Children.Add(_sldAttention);
            line2.Children.Add(_txtAttention);
            line2.Children.Add(_btnCheckOk);
            line2.Children.Add(_btnCheckFail);

            var line3 = new WrapPanel();
            line3.Children.Add(_btnFlash);
            line3.Children.Add(_btnSub);
            line3.Children.Add(_btnBubbles);
            line3.Children.Add(_btnCharge);

            var line4 = new WrapPanel();
            line4.Children.Add(_chkAutoEndure);
            line4.Children.Add(_btnEndure);
            line4.Children.Add(_btnFlinch);
            line4.Children.Add(_txtInbound);

            var line5 = new WrapPanel();
            line5.Children.Add(_btnEmote);
            line5.Children.Add(_btnMercy);

            var stack = new StackPanel();
            stack.Children.Add(line1);
            stack.Children.Add(line2);
            stack.Children.Add(line3);
            stack.Children.Add(line4);
            stack.Children.Add(line5);
            return Section("4 — LIVE", stack);
        }

        // --------------------------------------------------------------- modes

        /// <summary>Server mode uses the facade; loopback mode is driven entirely by the window.</summary>
        public void SetLoopbackMode(bool loopback)
        {
            _loopbackMode = loopback;
            RefreshEnabled();
        }

        /// <summary>Window hands the panel one half of a loopback pair (already subscribed, not yet in Lobby).</summary>
        public void AttachLoopbackMatch(GoonMatchService match)
        {
            DetachMatch();
            _ownsLoopbackMatch = true;
            AttachMatch(match);
        }

        public void SetJoinCode(string code)
        {
            if (!IsUiUsable()) return;
            _txtCode.Text = code ?? "";
        }

        /// <summary>Builds the sudden-death runner for this panel (facade RunnerFactory + loopback matches).</summary>
        public IGoonSuddenDeathRunner CreateRunner()
        {
            var simulate = _chkSimRounds.IsChecked == true;
            GoonSuddenDeathRunner runner;
            if (simulate)
            {
                var driver = new GoonTestSimRoundDriver(_simSeed, Log);
                runner = new GoonSuddenDeathRunner(driver, driver.Inputs);
                Log("sudden-death runner: SIMULATED inputs (rounds will resolve)");
            }
            else
            {
                runner = new GoonSuddenDeathRunner();
                Log("sudden-death runner: HEADLESS null feeds (every round draws — ladder never ends)");
            }

            runner.RoundResolved += (s, e) => Log(
                $"ROUND {e.RoundNo} {e.Kind} d{e.Difficulty} -> {e.Verdict} (net {e.NetScore}) " +
                $"local[done={e.Local.Completed} ms={e.Local.ElapsedMs} react={e.Local.ReactionMs} prog={e.Local.Progress}] " +
                $"peer[done={e.Peer.Completed} ms={e.Peer.ElapsedMs} react={e.Peer.ReactionMs} prog={e.Peer.Progress}]");
            runner.Aborted += (s, reason) => Log($"SUDDEN DEATH ABORTED: {reason}");
            return runner;
        }

        // ---------------------------------------------------------- connection

        private async void BtnHost_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsUiUsable()) return;
            try
            {
                Log("HostAsync...");
                _btnHost.IsEnabled = false;
                var code = await Facade.HostAsync().ConfigureAwait(true);
                if (!IsUiUsable()) return;
                if (string.IsNullOrEmpty(code))
                {
                    Log("HostAsync returned NO CODE (see ConnectFailed / status strip)");
                }
                else
                {
                    _txtCodeBig.Text = code;
                    Log($"invite code = {code}");
                    try { InviteCodeMinted?.Invoke(this, code!); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] invite relay threw"); }
                }
            }
            catch (Exception ex)
            {
                Log("HostAsync THREW: " + ex.Message);
                App.Logger?.Error(ex, "[GGTest] host failed");
            }
            finally { RefreshEnabled(); }
        }

        private async void BtnJoin_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsUiUsable()) return;
            var code = (_txtCode.Text ?? "").Trim();
            if (code.Length == 0) { Log("no invite code typed"); return; }
            try
            {
                Log($"JoinAsync({code})...");
                _btnJoin.IsEnabled = false;
                var ok = await Facade.JoinAsync(code).ConfigureAwait(true);
                if (!IsUiUsable()) return;
                Log(ok ? "join accepted" : "join FAILED (see ConnectFailed / status strip)");
                if (ok) _txtCodeBig.Text = code;
            }
            catch (Exception ex)
            {
                Log("JoinAsync THREW: " + ex.Message);
                App.Logger?.Error(ex, "[GGTest] join failed");
            }
            finally { RefreshEnabled(); }
        }

        private async void BtnLeave_Click(object? sender, RoutedEventArgs e)
        {
            await LeaveAsync().ConfigureAwait(true);
        }

        public async Task LeaveAsync()
        {
            try
            {
                if (_ownsLoopbackMatch)
                {
                    var match = _match;
                    Log("loopback: CancelMatch(left)");
                    try { match?.CancelMatch("left"); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loopback cancel threw"); }
                    DetachMatch();
                    match?.Dispose();
                }
                else
                {
                    Log("LeaveAsync...");
                    await Facade.LeaveAsync().ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[GGTest] leave threw");
            }
            finally
            {
                ClearPayloadTimers();
                if (IsUiUsable()) { _txtCodeBig.Text = "------"; RefreshEnabled(); }
            }
        }

        private void Facade_CurrentMatchChanged(object? sender, EventArgs e)
        {
            if (!IsUiUsable()) return;
            if (_ownsLoopbackMatch) return;   // loopback panels ignore the (idle) facade

            var next = Facade.CurrentMatch;
            if (ReferenceEquals(next, _match)) return;

            DetachMatch();
            if (next != null)
            {
                Log("CurrentMatchChanged -> new match service (re-subscribing)");
                AttachMatch(next);
            }
            else
            {
                Log("CurrentMatchChanged -> match torn down");
                RefreshEnabled();
            }
        }

        private void Facade_ConnectFailed(object? sender, string reason)
        {
            if (!IsUiUsable()) return;
            LastConnectFailure = reason;
            // The server burns a weekly free pass for non-premium accounts; if it ever surfaces a
            // pass-related code (no_pass / pass_burned) it lands here verbatim.
            Log("CONNECT FAILED: " + reason);
            RefreshEnabled();
        }

        // ------------------------------------------------- match subscription

        private void AttachMatch(GoonMatchService match)
        {
            _match = match;
            match.LocalAttentionMode = SelectedAttentionMode;
            match.LocalToyConnected = ToyConnected;

            match.PhaseChanged += Match_PhaseChanged;
            match.ConsentChanged += Match_ConsentChanged;
            match.DraftChanged += Match_DraftChanged;
            match.OpponentStateChanged += Match_OpponentStateChanged;
            match.ConnectionHealthChanged += Match_ConnectionHealthChanged;
            match.EmoteReceived += Match_EmoteReceived;
            match.InteractionCheckDue += Match_InteractionCheckDue;
            match.LobbyFailed += Match_LobbyFailed;
            match.MatchEnded += Match_MatchEnded;
            match.ResultFinalized += Match_ResultFinalized;
            match.PayloadAccepted += Match_PayloadAccepted;
            match.PayloadRejected += Match_PayloadRejected;
            match.PayloadReceiptReceived += Match_PayloadReceipt;
            match.ElementStartRequested += Match_ElementStart;
            match.ElementIntensityChanged += Match_ElementIntensity;
            match.ElementStopRequested += Match_ElementStop;

            RefreshEnabled();
            RefreshLive();
        }

        private void DetachMatch()
        {
            var match = _match;
            _match = null;
            _ownsLoopbackMatch = false;
            if (match == null) return;

            match.PhaseChanged -= Match_PhaseChanged;
            match.ConsentChanged -= Match_ConsentChanged;
            match.DraftChanged -= Match_DraftChanged;
            match.OpponentStateChanged -= Match_OpponentStateChanged;
            match.ConnectionHealthChanged -= Match_ConnectionHealthChanged;
            match.EmoteReceived -= Match_EmoteReceived;
            match.InteractionCheckDue -= Match_InteractionCheckDue;
            match.LobbyFailed -= Match_LobbyFailed;
            match.MatchEnded -= Match_MatchEnded;
            match.ResultFinalized -= Match_ResultFinalized;
            match.PayloadAccepted -= Match_PayloadAccepted;
            match.PayloadRejected -= Match_PayloadRejected;
            match.PayloadReceiptReceived -= Match_PayloadReceipt;
            match.ElementStartRequested -= Match_ElementStart;
            match.ElementIntensityChanged -= Match_ElementIntensity;
            match.ElementStopRequested -= Match_ElementStop;
        }

        // ------------------------------------------------------ match handlers

        private void Match_PhaseChanged(object? sender, GoonMatchPhase phase)
        {
            if (!IsUiUsable()) return;
            Log($"PHASE -> {phase}");

            if (phase == GoonMatchPhase.Draft) AutoPickIfEmpty();
            RefreshEnabled();
            RefreshLive();
        }

        private void Match_ConsentChanged(object? sender, EventArgs e)
        {
            if (!IsUiUsable()) return;
            var match = _match;
            if (match == null) return;

            var sheet = match.ConsentSheet;
            _txtConsent.Text =
                $"live={sheet.LiveDurationSec}s toyCap={sheet.ToyCap:0.00} gap={sheet.PayloadMinGapMs}ms   " +
                $"local={(match.LocalConsentConfirmed ? "CONFIRMED" : "-")}  peer={(match.RemoteConsentConfirmed ? "CONFIRMED" : "-")}";
            Log($"CONSENT sheet live={sheet.LiveDurationSec}s cap={sheet.ToyCap:0.00} gap={sheet.PayloadMinGapMs} " +
                $"local={match.LocalConsentConfirmed} peer={match.RemoteConsentConfirmed}");

            // The host auto-proposes its default 720 s sheet the instant the hello lands; re-propose
            // the (short) duration this panel actually selected so tests do not run for 12 minutes.
            if (match.IsHost && match.Phase == GoonMatchPhase.Consent &&
                sheet.LiveDurationSec != SelectedDurationSec && !match.LocalConsentConfirmed)
            {
                Log($"host re-proposing {SelectedDurationSec}s (was {sheet.LiveDurationSec}s)");
                match.ProposeConsent(SelectedDurationSec, sheet.ToyCap, sheet.PayloadMinGapMs);
            }

            RefreshEnabled();
        }

        private void Match_DraftChanged(object? sender, EventArgs e)
        {
            if (!IsUiUsable()) return;
            var match = _match;
            if (match == null) return;

            _txtDraft.Text =
                $"local [{string.Join(", ", match.LocalDraft)}] {(match.LocalDraftLocked ? "LOCKED" : "")}   " +
                $"peer [{string.Join(", ", match.RemoteDraft)}] {(match.RemoteDraftLocked ? "LOCKED" : "")}   " +
                $"pool={match.AvailableDraftPool.Count}";
            Log($"DRAFT local=[{string.Join(",", match.LocalDraft)}] locked={match.LocalDraftLocked} " +
                $"peer=[{string.Join(",", match.RemoteDraft)}] locked={match.RemoteDraftLocked}");
            RefreshEnabled();
        }

        private void Match_OpponentStateChanged(object? sender, EventArgs e)
        {
            if (!IsUiUsable()) return;
            RefreshLive();
        }

        private void Match_ConnectionHealthChanged(object? sender, GoonConnectionHealth health)
        {
            if (!IsUiUsable()) return;
            Log("CONNECTION HEALTH -> " + health);
        }

        private void Match_EmoteReceived(object? sender, EmoteMsg emote)
        {
            if (!IsUiUsable()) return;
            Log($"EMOTE in: {emote.Icon} {emote.Text}");
        }

        private void Match_InteractionCheckDue(object? sender, EventArgs e)
        {
            if (!IsUiUsable()) return;
            Log("INTERACTION CHECK DUE");
            _btnCheckOk.Background = Hot;
            _btnCheckOk.FontWeight = FontWeights.Bold;
        }

        private void Match_LobbyFailed(object? sender, string reason)
        {
            if (!IsUiUsable()) return;
            Log("LOBBY FAILED: " + reason);
            RefreshEnabled();
        }

        private void Match_MatchEnded(object? sender, GoonMatchResult result)
        {
            if (!IsUiUsable()) return;
            Log($"MATCH ENDED reason={result.EndReason} winnerIsHost={result.WinnerIsHost?.ToString() ?? "draw"} " +
                $"localWon={result.LocalWon} host={result.HostScore} guest={result.GuestScore} " +
                $"survived={result.SurvivedMs / 1000}s ledger={result.CountsForLedger}");
            ClearPayloadTimers();
            RefreshEnabled();
        }

        private void Match_ResultFinalized(object? sender, GoonMatchResult result)
        {
            if (!IsUiUsable()) return;
            Log($"RESULT FINALIZED agreed={result.Agreed} disputed={result.Disputed} " +
                $"reason={result.EndReason} local={result.LocalScore} remote={result.RemoteScore}" +
                (result.RemoteClaim != null
                    ? $" remoteClaim[reason={result.RemoteClaim.EndReason} winnerIsHost={result.RemoteClaim.WinnerIsHost}]"
                    : ""));
        }

        private void Match_PayloadAccepted(object? sender, GoonInboundPayloadEventArgs e)
        {
            if (!IsUiUsable()) return;
            var p = e.Payload;
            var inMs = e.FireAtLocalMs - Environment.TickCount64;
            Log($"PAYLOAD IN accepted id={p.Id} kind={p.Kind} fires in {inMs}ms dur={p.DurationMs}ms " +
                $"intensity={p.Intensity:0.00} text='{p.Text}'");

            _pendingInbound.Add(p.Id);
            _txtInbound.Text = $"inbound {p.Id} ({p.Kind}) — {(_chkAutoEndure.IsChecked == true ? "auto-endure armed" : "awaiting Endure/Flinch")}";

            if (_chkAutoEndure.IsChecked == true)
            {
                var wait = Math.Max(500, (int)Math.Min(int.MaxValue, inMs)) + p.DurationMs;
                ScheduleOnce(wait, () =>
                {
                    if (!_pendingInbound.Contains(p.Id)) return;
                    Log($"auto-endure: NotifyInboundPayloadFinished({p.Id}, endured: true)");
                    NotifyFinished(p.Id, true);
                });
            }
            RefreshEnabled();
        }

        private void Match_PayloadRejected(object? sender, PayloadReceiptMsg receipt)
        {
            if (!IsUiUsable()) return;
            Log($"PAYLOAD IN rejected id={receipt.Id} status={receipt.Status}");
        }

        private void Match_PayloadReceipt(object? sender, PayloadReceiptMsg receipt)
        {
            if (!IsUiUsable()) return;
            Log($"RECEIPT for our payload id={receipt.Id} status={receipt.Status}");
        }

        private void Match_ElementStart(object? sender, GoonElementCueEventArgs e) =>
            LogCue("ELEMENT START", e);

        private void Match_ElementIntensity(object? sender, GoonElementCueEventArgs e) =>
            LogCue("ELEMENT INTENSITY", e);

        private void Match_ElementStop(object? sender, GoonElementCueEventArgs e) =>
            LogCue("ELEMENT STOP", e);

        private void LogCue(string what, GoonElementCueEventArgs e)
        {
            if (!IsUiUsable()) return;
            Log($"{what} {e.Element} intensity={e.Intensity:0.00} dur={e.DurationMs}ms at +{e.ElapsedMs / 1000}s");
        }

        // ------------------------------------------------------- consent input

        private int SelectedDurationSec => _cmbDuration.SelectedIndex switch
        {
            1 => 180,
            2 => 720,
            _ => 60,
        };

        private void Duration_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var match = _match;
            if (match == null || match.Phase != GoonMatchPhase.Consent) return;
            match.ProposeConsent(SelectedDurationSec, match.ConsentSheet.ToyCap, match.ConsentSheet.PayloadMinGapMs);
        }

        private void BtnPropose_Click(object? sender, RoutedEventArgs e)
        {
            var match = _match;
            if (match == null) return;
            Log($"ProposeConsent({SelectedDurationSec}s)");
            match.ProposeConsent(SelectedDurationSec, match.ConsentSheet.ToyCap, match.ConsentSheet.PayloadMinGapMs);
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            Log("ConfirmConsent()");
            _match?.ConfirmConsent();
        }

        private void BtnWithdraw_Click(object? sender, RoutedEventArgs e)
        {
            Log("WithdrawConsent()");
            _match?.WithdrawConsent();
        }

        // --------------------------------------------------------- draft input

        private List<GoonElement> SelectedDraft() => _draftBoxes
            .Where(b => b.IsChecked == true && b.Tag is GoonElement)
            .Select(b => (GoonElement)b.Tag)
            .ToList();

        private void Draft_Changed(object? sender, RoutedEventArgs e)
        {
            var match = _match;
            if (match == null || match.Phase != GoonMatchPhase.Draft) return;

            var picks = SelectedDraft();
            if (picks.Count != GoonDraft.PicksPerPlayer)
            {
                _txtDraft.Text = $"{picks.Count}/{GoonDraft.PicksPerPlayer} picked";
                return;
            }
            if (!match.SetDraft(picks, out var error))
            {
                _txtDraft.Text = "SetDraft error: " + error;
                Log("SetDraft REJECTED: " + error);
            }
        }

        private void AutoPickIfEmpty()
        {
            if (SelectedDraft().Count > 0) return;
            BtnAutoDraft_Click(null, new RoutedEventArgs());
        }

        private void BtnAutoDraft_Click(object? sender, RoutedEventArgs e)
        {
            var match = _match;
            var pool = match?.AvailableDraftPool ?? GoonDraft.PoolV1;
            var wanted = pool.Take(GoonDraft.PicksPerPlayer).ToHashSet();
            foreach (var box in _draftBoxes)
            {
                if (box.Tag is not GoonElement element) continue;
                box.IsChecked = wanted.Contains(element);
            }
        }

        private void BtnLockDraft_Click(object? sender, RoutedEventArgs e)
        {
            var match = _match;
            if (match == null) return;

            // SetDraft first: LockDraft validates whatever was last broadcast.
            var picks = SelectedDraft();
            if (picks.Count == GoonDraft.PicksPerPlayer && !match.SetDraft(picks, out var setError))
            {
                _txtDraft.Text = "SetDraft error: " + setError;
                Log("SetDraft REJECTED: " + setError);
                return;
            }
            if (!match.LockDraft(out var error))
            {
                _txtDraft.Text = "LockDraft error: " + error;
                Log("LockDraft REJECTED: " + error);
                return;
            }
            Log("draft LOCKED");
        }

        // ---------------------------------------------------------- live input

        private void Attention_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsUiUsable()) return;
            _txtAttention.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture) + "%";
            _match?.ReportAttention(e.NewValue);
        }

        private void BtnCheckOk_Click(object? sender, RoutedEventArgs e)
        {
            Log("ReportInteractionCheck(passed: true)");
            _match?.ReportInteractionCheck(true);
            _btnCheckOk.Background = Idle;
            _btnCheckOk.FontWeight = FontWeights.Normal;
        }

        private void BtnCheckFail_Click(object? sender, RoutedEventArgs e)
        {
            Log("ReportInteractionCheck(passed: false)");
            _match?.ReportInteractionCheck(false);
            _btnCheckOk.Background = Idle;
            _btnCheckOk.FontWeight = FontWeights.Normal;
        }

        private void FirePayload(GoonPayloadKind kind)
        {
            var match = _match;
            if (match == null) return;

            var request = new GoonPayloadRequest
            {
                Kind = kind,
                DurationMs = TestPayloadDurationMs,
                Tags = new List<string> { "test" },
                Text = kind == GoonPayloadKind.SubliminalStorm ? "cockpit test line" : null,
                Voice = false,
                Pattern = null,
                Intensity = 0.5,
            };

            if (!match.TryFirePayload(request, out var error))
            {
                Log($"TryFirePayload({kind}) REJECTED locally: {error}");
                return;
            }
            Log($"PAYLOAD OUT {kind} dur={TestPayloadDurationMs}ms (charges left {match.Scoring.Charges})");
            RefreshLive();
        }

        private void BtnCharge_Click(object? sender, RoutedEventArgs e)
        {
            var match = _match;
            if (match == null) return;
            // The only public way to add a charge without waiting 90 s of clean time. The receiver
            // gates on the LAST TICK it saw, so wait ~3 s (one StateTick) before firing.
            match.Scoring.AwardPayloadEndured();
            Log($"dev: +1 charge (now {match.Scoring.Charges}) — wait ~3s for the state tick before firing");
            RefreshLive();
        }

        private void BtnEmote_Click(object? sender, RoutedEventArgs e)
        {
            Log("SendEmote(\"soaked\", \"💦\")");
            _match?.SendEmote("soaked", "💦");
        }

        private void BtnMercy_Click(object? sender, RoutedEventArgs e)
        {
            Log("DeclareMercy()");
            _match?.DeclareMercy();
        }

        private void FinishInbound(bool endured)
        {
            if (_pendingInbound.Count == 0) { Log("no pending inbound payload"); return; }
            var id = _pendingInbound[0];
            Log($"NotifyInboundPayloadFinished({id}, endured: {endured})");
            NotifyFinished(id, endured);
        }

        private void NotifyFinished(string id, bool endured)
        {
            _pendingInbound.Remove(id);
            try { _match?.NotifyInboundPayloadFinished(id, endured); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] NotifyInboundPayloadFinished threw"); }
            if (IsUiUsable())
            {
                _txtInbound.Text = _pendingInbound.Count == 0
                    ? "no inbound payload"
                    : $"inbound {_pendingInbound[0]} pending";
                RefreshEnabled();
                RefreshLive();
            }
        }

        // -------------------------------------------------------------- render

        /// <summary>Called by the window's 1 s timer and by every state event.</summary>
        public void RefreshLive()
        {
            if (!IsUiUsable()) return;
            var match = _match;
            if (match == null)
            {
                _txtScore.Text = "score - / -";
                _txtState.Text = _loopbackMode ? "loopback: no match" : "phase Idle";
                return;
            }

            var opponent = match.Opponent;
            _txtScore.Text =
                $"YOU {match.Scoring.Score}  (ch {match.Scoring.Charges}, att {(int)match.Scoring.AttentionPct}%)" +
                $"   |   OPP {opponent.Score}  (ch {opponent.Charges}, att {opponent.AttentionPct}%)";

            _txtState.Text =
                $"phase {match.Phase}  host={match.IsHost}  health={opponent.Health}  " +
                $"live {(int)match.LiveElapsed.TotalSeconds}s/{match.ConsentSheet.LiveDurationSec}s  " +
                $"opp='{opponent.DisplayName}' {opponent.Platform} {opponent.AppVersion} mode={opponent.AttentionMode}  " +
                $"seed={match.MatchSeed:X16}";

            // Cam mode is graded on a live attention stream, so keep feeding the slider value.
            if (match.Phase is GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath &&
                match.LocalAttentionMode == GoonAttentionMode.Cam)
            {
                match.ReportAttention(_sldAttention.Value);
            }
        }

        private void RefreshEnabled()
        {
            if (!IsUiUsable()) return;
            var match = _match;
            var phase = match?.Phase ?? GoonMatchPhase.Idle;
            bool hasMatch = match != null;
            bool serverMode = !_loopbackMode;

            _btnHost.IsEnabled = serverMode && !hasMatch;
            _btnJoin.IsEnabled = serverMode && !hasMatch;
            _txtCode.IsEnabled = serverMode && !hasMatch;
            _btnLeave.IsEnabled = hasMatch;
            _cmbAttention.IsEnabled = !hasMatch;
            _chkToy.IsEnabled = !hasMatch;
            _chkSimRounds.IsEnabled = !hasMatch;

            bool consent = phase == GoonMatchPhase.Consent;
            _btnPropose.IsEnabled = consent || phase == GoonMatchPhase.Lobby;
            _btnConfirm.IsEnabled = consent && match?.LocalConsentConfirmed != true;
            _btnWithdraw.IsEnabled = consent;

            bool draft = phase == GoonMatchPhase.Draft;
            _draftPanel.IsEnabled = draft && match?.LocalDraftLocked != true;
            _btnAutoDraft.IsEnabled = draft && match?.LocalDraftLocked != true;
            _btnLockDraft.IsEnabled = draft && match?.LocalDraftLocked != true;

            bool live = phase is GoonMatchPhase.Live or GoonMatchPhase.SuddenDeath;
            _btnFlash.IsEnabled = live;
            _btnSub.IsEnabled = live;
            _btnBubbles.IsEnabled = live;
            _btnCharge.IsEnabled = hasMatch;
            _btnCheckOk.IsEnabled = live;
            _btnCheckFail.IsEnabled = live;
            _btnEmote.IsEnabled = hasMatch;
            _btnMercy.IsEnabled = hasMatch;

            bool manualEndure = _chkAutoEndure.IsChecked != true && _pendingInbound.Count > 0;
            _btnEndure.IsEnabled = manualEndure;
            _btnFlinch.IsEnabled = manualEndure;
        }

        // ----------------------------------------------------------------- log

        public void Log(string line)
        {
            if (!IsUiUsable()) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            _log.Items.Add($"[{stamp}] {line}");
            while (_log.Items.Count > LogCap) _log.Items.RemoveAt(0);
            try { _log.ScrollIntoView(_log.Items[_log.Items.Count - 1]); }
            catch { /* virtualization race — cosmetic only */ }
            App.Logger?.Information("[GGTest/{Panel}] {Line}", _name, line);
        }

        // ------------------------------------------------------------ plumbing

        private void ScheduleOnce(int delayMs, Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)),
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _payloadTimers.Remove(timer);
                if (_disposed) return;
                try { action(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] scheduled action threw"); }
            };
            _payloadTimers.Add(timer);
            timer.Start();
        }

        private void ClearPayloadTimers()
        {
            foreach (var timer in _payloadTimers.ToList())
            {
                try { timer.Stop(); } catch { /* already dead */ }
            }
            _payloadTimers.Clear();
            _pendingInbound.Clear();
        }

        private bool IsUiUsable()
        {
            if (_disposed) return false;
            var dispatcher = Application.Current?.Dispatcher;
            return dispatcher != null && !dispatcher.HasShutdownStarted;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ClearPayloadTimers();
            try
            {
                Facade.CurrentMatchChanged -= Facade_CurrentMatchChanged;
                Facade.ConnectFailed -= Facade_ConnectFailed;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] facade unsubscribe threw"); }

            var ownsMatch = _ownsLoopbackMatch;
            var match = _match;
            try { DetachMatch(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] detach threw"); }
            if (ownsMatch)
            {
                try { match?.Dispose(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loopback match dispose threw"); }
            }

            try { Facade.Dispose(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] facade dispose threw"); }
        }
    }
}
