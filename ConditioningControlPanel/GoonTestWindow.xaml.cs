using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.GoonGame;

// ============================================================================
// GOON GAME — dev play-test cockpit (launched with `--goon-test`).
//
// Two INDEPENDENT player panels in one process, each with its OWN
// GoonGameService (never App.GoonGame, which is the app-level singleton), so a
// full duel can be played against yourself:
//
//   * Server mode  — the real thing: /v2/goon/* signaling + WebRTC data channel,
//                    including the relay fallback (the facade REBUILDS its match
//                    service there, which is why the panels re-subscribe from
//                    CurrentMatchChanged and never cache a match instance).
//   * Loopback mode — GoonLoopbackTransport.CreatePair(): no server, no NAT, same
//                    message types, same clock skew. Built here rather than through
//                    the facade because the facade always constructs a WebRTC
//                    transport; the two match services are wired straight onto the
//                    pair halves and enter the lobby via AdoptLobby().
//
// Touches nothing outside Services\GoonGame\ — no panic, lockdown, tray, or
// session verbs — and applies no premium gating (the server enforces passes).
// ============================================================================

namespace ConditioningControlPanel
{
    public partial class GoonTestWindow : Window
    {
        private readonly GoonTestPanel _a;
        private readonly GoonTestPanel _b;
        private readonly DispatcherTimer _refresh;

        private GoonLoopbackPair? _pair;
        private GoonMatchService? _loopHost;
        private GoonMatchService? _loopGuest;
        private bool _shuttingDown;

        public GoonTestWindow()
        {
            InitializeComponent();

            // Different sim seeds: the two sides must post DIFFERENT round times or every
            // sudden-death round is a draw and the ladder never resolves.
            _a = new GoonTestPanel("Player A", simSeed: 1337);
            _b = new GoonTestPanel("Player B", simSeed: 8675309);

            PanelHostA.Child = _a.Root;
            PanelHostB.Child = _b.Root;

            _a.InviteCodeMinted += (s, code) => _b.SetJoinCode(code);
            _b.InviteCodeMinted += (s, code) => _a.SetJoinCode(code);

            _refresh = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _refresh.Tick += Refresh_Tick;
            _refresh.Start();

            _a.Log("cockpit ready — server mode. Host here, then Join on B (code auto-fills).");
            _b.Log("cockpit ready — server mode.");
            UpdateStatus();
        }

        // ---------------------------------------------------------------- mode

        private bool LoopbackSelected => RadioLoopback?.IsChecked == true;

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;
            if (_a == null || _b == null) return;   // fires once during InitializeComponent

            var loopback = LoopbackSelected;
            CmbLoopProfile.IsEnabled = loopback;
            BtnCreateLoopback.IsEnabled = loopback;
            BtnOutage.IsEnabled = loopback && _pair != null;

            _a.SetLoopbackMode(loopback);
            _b.SetLoopbackMode(loopback);
            _a.Log(loopback ? "mode -> LOOPBACK" : "mode -> SERVER");
            _b.Log(loopback ? "mode -> LOOPBACK" : "mode -> SERVER");
            UpdateStatus();
        }

        private async void BtnCreateLoopback_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Dispatcher.HasShutdownStarted) return;

            try
            {
                BtnCreateLoopback.IsEnabled = false;
                await TearDownLoopbackAsync().ConfigureAwait(true);

                var options = CmbLoopProfile.SelectedIndex switch
                {
                    1 => GoonLoopbackOptions.Relay(),
                    2 => GoonLoopbackOptions.Instant(),
                    _ => GoonLoopbackOptions.P2P(),
                };

                var pair = GoonLoopbackTransport.CreatePair(options);
                _pair = pair;

                // Real Phase A RNG on both halves, exactly like the facade does.
                _loopHost = new GoonMatchService(pair.Host, isHost: true, seed => new GoonRng(seed))
                {
                    SuddenDeathRunner = _a.CreateRunner(),
                };
                _loopGuest = new GoonMatchService(pair.Guest, isHost: false, seed => new GoonRng(seed))
                {
                    SuddenDeathRunner = _b.CreateRunner(),
                };

                _a.AttachLoopbackMatch(_loopHost);
                _b.AttachLoopbackMatch(_loopGuest);

                // ORDER MATTERS. AdoptLobby BEFORE ConnectAsync: the hello is sent from the
                // transport's connected state change, and a hello that lands while the peer is
                // still Idle is dropped (HandleHello only advances a match that is in Lobby),
                // which would strand both sides forever.
                _loopHost.AdoptLobby();
                _loopGuest.AdoptLobby();

                _a.Log($"loopback pair created ({options.LatencyMs}ms +{options.JitterMs}ms jitter, guest skew {options.GuestClockSkewMs}ms)");
                _b.Log("loopback pair created — connecting + syncing clocks");

                var ok = await pair.ConnectAsync().ConfigureAwait(true);
                if (Dispatcher.HasShutdownStarted) return;

                _a.Log(ok ? "loopback clocks SYNCED" : "loopback clock sync FAILED (scheduling will stall)");
                _b.Log(ok ? "loopback clocks SYNCED" : "loopback clock sync FAILED (scheduling will stall)");
                BtnOutage.IsEnabled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[GGTest] loopback pair creation failed");
                _a.Log("loopback creation THREW: " + ex.Message);
            }
            finally
            {
                if (!Dispatcher.HasShutdownStarted) BtnCreateLoopback.IsEnabled = LoopbackSelected;
                UpdateStatus();
            }
        }

        private void BtnOutage_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;
            try
            {
                _pair?.SimulateOutage(20000);
                _a.Log("loopback: 20s outage simulated (expect Wobbly at 15s, Dead/abandon at 60s)");
                _b.Log("loopback: 20s outage simulated");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] outage threw"); }
        }

        private async void BtnTearDown_Click(object sender, RoutedEventArgs e)
        {
            await TearDownAllAsync().ConfigureAwait(true);
        }

        // ------------------------------------------------------------ teardown

        private async Task TearDownAllAsync()
        {
            try { await _a.LeaveAsync().ConfigureAwait(true); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] panel A leave threw"); }
            try { await _b.LeaveAsync().ConfigureAwait(true); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] panel B leave threw"); }
            await TearDownLoopbackAsync().ConfigureAwait(true);
            UpdateStatus();
        }

        private async Task TearDownLoopbackAsync()
        {
            var pair = _pair;
            var host = _loopHost;
            var guest = _loopGuest;
            _pair = null;
            _loopHost = null;
            _loopGuest = null;

            try { host?.Dispose(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loop host dispose threw"); }
            try { guest?.Dispose(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loop guest dispose threw"); }

            if (pair != null)
            {
                try { await pair.Host.CloseAsync().ConfigureAwait(true); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loop host close threw"); }
                try { await pair.Guest.CloseAsync().ConfigureAwait(true); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loop guest close threw"); }
                try { pair.Dispose(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] loop pair dispose threw"); }
            }

            if (!Dispatcher.HasShutdownStarted) BtnOutage.IsEnabled = false;
        }

        // -------------------------------------------------------------- render

        private void Refresh_Tick(object? sender, EventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Dispatcher.HasShutdownStarted || _shuttingDown) return;
            try
            {
                _a.RefreshLive();
                _b.RefreshLive();
                UpdateStatus();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] refresh tick threw"); }
        }

        private void UpdateStatus()
        {
            if (Application.Current?.Dispatcher == null || Dispatcher.HasShutdownStarted) return;
            var mode = LoopbackSelected ? (_pair != null ? "loopback (pair up)" : "loopback (no pair)") : "server";
            TxtStatus.Text = $"mode={mode}   ||   {_a.ShortStatus}   ||   {_b.ShortStatus}";
        }

        // ------------------------------------------------------------ lifetime

        protected override void OnClosed(EventArgs e)
        {
            _shuttingDown = true;
            try { _refresh.Stop(); _refresh.Tick -= Refresh_Tick; }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] refresh stop threw"); }

            try { _ = ShutdownAsync(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] shutdown threw"); }

            base.OnClosed(e);
        }

        private async Task ShutdownAsync()
        {
            try { await TearDownAllAsync().ConfigureAwait(true); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] teardown on close threw"); }
            try { _a.Dispose(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] panel A dispose threw"); }
            try { _b.Dispose(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] panel B dispose threw"); }
        }
    }
}
