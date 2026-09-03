using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

// ============================================================================
// GOON GAME — dev play-test cockpit (launched with `--goon-test`).
//
// PORTED from ConditioningControlPanel/GoonTestWindow.xaml.cs.
//
// The WPF original drove two INDEPENDENT player panels, each with its own
// GoonGameService, over either the real /v2/goon signaling + WebRTC transport or
// GoonLoopbackTransport.CreatePair(). None of that is available here:
// GoonTestPanel, GoonGameService, GoonMatchService, GoonLoopbackTransport and
// GoonLoopbackOptions all still live in the WPF head (only GoonContracts,
// GoonDraft, GoonMatchTypes, GoonRng, GoonScoring and GoonWire made it to
// CCP.Core). So this layer ports the COCKPIT CHROME — mode bar, enablement
// rules, status strip, panel hosts — and stubs every verb that would touch a
// service. The ordering comment that mattered most on the original (AdoptLobby
// BEFORE ConnectAsync, or both sides strand in Idle) is preserved verbatim on
// the stub so it is not lost when the wiring comes back.
// ============================================================================

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class GoonTestWindow : Window
    {
        private readonly RadioButton _radioLoopback;
        private readonly ComboBox _cmbLoopProfile;
        private readonly Button _btnCreateLoopback, _btnOutage, _btnTearDown;
        private readonly TextBlock _txtStatus;

        public GoonTestWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _radioLoopback = this.FindControl<RadioButton>("RadioLoopback")!;
            _cmbLoopProfile = this.FindControl<ComboBox>("CmbLoopProfile")!;
            _btnCreateLoopback = this.FindControl<Button>("BtnCreateLoopback")!;
            _btnOutage = this.FindControl<Button>("BtnOutage")!;
            _btnTearDown = this.FindControl<Button>("BtnTearDown")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;

            // ponytail: needs GoonTestPanel (+ GoonGameService/GoonMatchService), wired when they
            // move to Core. The WPF original built two panels with different sim seeds — 1337 and
            // 8675309 — because two sides posting the SAME round time make every sudden-death
            // round a draw and the ladder never resolves. Keep the two seeds different when this
            // comes back.
            this.FindControl<Border>("PanelHostA")!.Child = PanelPlaceholder("Player A", 1337);
            this.FindControl<Border>("PanelHostB")!.Child = PanelPlaceholder("Player B", 8675309);

            // Handlers live here rather than in markup, per the porting convention.
            // WPF's Checked= becomes IsCheckedChanged; it fires for both halves of the group, and
            // Mode_Changed is idempotent, so that is harmless.
            this.FindControl<RadioButton>("RadioServer")!.IsCheckedChanged += (_, _) => Mode_Changed();
            _radioLoopback.IsCheckedChanged += (_, _) => Mode_Changed();
            _btnCreateLoopback.Click += (_, _) => BtnCreateLoopback_Click();
            _btnOutage.Click += (_, _) => BtnOutage_Click();
            _btnTearDown.Click += (_, _) => BtnTearDown_Click();

            // ponytail: the WPF 1 s DispatcherTimer polled _a.RefreshLive()/_b.RefreshLive(). With
            // the panels stubbed there is nothing live to poll, so the timer is dropped rather
            // than left spinning; restore it with the panels.
            UpdateStatus();
        }

        // ---------------------------------------------------------------- mode

        private bool LoopbackSelected => _radioLoopback.IsChecked == true;

        private void Mode_Changed()
        {
            var loopback = LoopbackSelected;
            _cmbLoopProfile.IsEnabled = loopback;
            _btnCreateLoopback.IsEnabled = loopback;
            // WPF: `loopback && _pair != null`. There is never a pair without the transport.
            _btnOutage.IsEnabled = false;
            UpdateStatus();
        }

        private void BtnCreateLoopback_Click()
        {
            // ponytail: needs GoonLoopbackTransport.CreatePair + GoonMatchService, wired when they
            // move to Core. The profile index maps 1 -> Relay(), 2 -> Instant(), default -> P2P().
            //
            // ORDER MATTERS when this comes back. AdoptLobby BEFORE ConnectAsync: the hello is sent
            // from the transport's connected state change, and a hello that lands while the peer is
            // still Idle is dropped (HandleHello only advances a match that is in Lobby), which
            // would strand both sides forever.
            Stub("Create Loopback Pair");
        }

        private void BtnOutage_Click()
        {
            // ponytail: needs GoonLoopbackPair.SimulateOutage(20000), wired when it moves to Core.
            // Expect Wobbly at 15 s and Dead/abandon at 60 s once it is back.
            Stub("Simulate 20s Outage");
        }

        private void BtnTearDown_Click()
        {
            // ponytail: needs GoonTestPanel.LeaveAsync + the loopback dispose chain, wired when
            // they move to Core.
            Stub("Tear Down Both");
        }

        // -------------------------------------------------------------- render

        private void UpdateStatus()
        {
            var mode = LoopbackSelected ? "loopback (no pair)" : "server";
            _txtStatus.Text = $"mode={mode}   ||   Player A: not wired   ||   Player B: not wired";
        }

        private void Stub(string verb)
        {
            _txtStatus.Text = $"{verb}: not wired on this head — GoonGame transports are still in the WPF project.";
        }

        /// <summary>
        /// Stands in for a GoonTestPanel until that control is ported. Placeholder only: it names
        /// the side and the sim seed the real panel would carry, so the two hosts are visibly
        /// distinct in the render proof rather than two identical empty panes.
        /// </summary>
        private static Control PanelPlaceholder(string side, int simSeed)
        {
            return new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = side,
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#FFFF69B4")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"sim seed {simSeed}",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.Parse("#FF9A9ABF")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "GoonTestPanel is not ported yet.\nIt owns GoonGameService and the WebRTC / loopback\ntransports, which are still in the WPF project.",
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center,
                        Foreground = new SolidColorBrush(Color.Parse("#FFEDEDF5")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new global::Avalonia.Thickness(0, 10, 0, 0),
                    },
                },
            };
        }
    }
}
