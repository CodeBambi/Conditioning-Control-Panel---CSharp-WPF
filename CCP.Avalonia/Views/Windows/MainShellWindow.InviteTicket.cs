// PORTED from ConditioningControlPanel/MainWindow/MainWindow.InviteTicket.cs (main e2d4e35ef): the header
// invite ticket. Shown only while this account holds an unused invite code this month (Core
// InviteTicketRule); wobbles now and then when motion allows; a click opens the invites card.
using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Services.Invites;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private DispatcherTimer? _inviteTicketFirst, _inviteTicketRefresh, _inviteTicketWobble;
        private bool _inviteTicketReading, _inviteTicketShown;
        private readonly Random _inviteTicketRng = new();

        /// <summary>The ticket's server read (tests swap the wire).</summary>
        internal Func<IInviteApi> InviteTicketApi { get; set; } = () => new InviteApi();

        /// <summary>WPF 7.1.5 PlansVaultView.InvitesHost: the invites card lives on Settings &gt; Account &amp;
        /// Plans (the Premium page copy keeps its panel hidden). Falls back to the Premium copy.</summary>
        private InvitePanel? InvitesCard =>
            Named<Tabs.AppSettingsTabView>("AppSettingsTab")?.FindControl<Views.Controls.AppSettings.AccountSettingsSection>("SectionAccount")
                ?.Plans.FindControl<InvitePanel>("InvitesHost")
            ?? PremiumInvitesCard;

        private InvitePanel? PremiumInvitesCard => Named<Tabs.ExclusivesTabView>("ExclusivesTab")?.FindControl<InvitePanel>("InvitesHost");

        /// <summary>WPF InitializeInviteTicket: one delayed read, then every 30 min and on account change;
        /// every read the invites card makes repaints the ticket too.</summary>
        private void InitializeInviteTicket()
        {
            _inviteTicketFirst = new DispatcherTimer { Interval = InviteTicketRule.StartupDelay };
            _inviteTicketFirst.Tick += (_, _) => { _inviteTicketFirst?.Stop(); _ = RefreshInviteTicketAsync(); };
            _inviteTicketFirst.Start();
            _inviteTicketRefresh = new DispatcherTimer { Interval = InviteTicketRule.RefreshEvery };
            _inviteTicketRefresh.Tick += (_, _) => _ = RefreshInviteTicketAsync();
            _inviteTicketRefresh.Start();
            _inviteTicketWobble = new DispatcherTimer { Interval = InviteTicketRule.NextWobble(_inviteTicketRng) };
            _inviteTicketWobble.Tick += (_, _) => WobbleInviteTicket();
            if (InvitesCard is { } card) card.Read += ApplyInviteTicket;
            if (PremiumInvitesCard is { } premium && !ReferenceEquals(premium, InvitesCard)) premium.Read += ApplyInviteTicket;
            CoreAccount.UnifiedIdentityChanged += OnInviteTicketIdentityChanged;
            // Better than WPF (P01): no wobble ticks while the panel window is hidden.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncInviteWobble(); };
            Closed += (_, _) =>
            {
                CoreAccount.UnifiedIdentityChanged -= OnInviteTicketIdentityChanged;
                _inviteTicketFirst?.Stop(); _inviteTicketRefresh?.Stop(); _inviteTicketWobble?.Stop();
                InvitePanel.CancelExpiry(); // no end-of-week one-shot outlives the shell
            };
        }

        /// <summary>A different account: hide at once, read again once the new identity has settled.</summary>
        private void OnInviteTicketIdentityChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            ApplyInviteTicket(InviteMine.Unreachable);
            DispatcherTimer.RunOnce(() => _ = RefreshInviteTicketAsync(), TimeSpan.FromSeconds(3));
        });

        internal async Task RefreshInviteTicketAsync()
        {
            if (_inviteTicketReading) return;
            try
            {
                if (InviteApi.DefaultIdentity() == null) { ApplyInviteTicket(InviteMine.Unreachable); return; }
                _inviteTicketReading = true;
                var api = InviteTicketApi();
                ApplyInviteTicket(await Task.Run(() => api.MineAsync()));
            }
            catch (Exception ex)
            {
                Log.Debug("[Invites] ticket refresh failed: {E}", ex.GetType().Name);
                ApplyInviteTicket(InviteMine.Unreachable);
            }
            finally { _inviteTicketReading = false; }
        }

        internal void ApplyInviteTicket(InviteMine? mine)
        {
            if (Named<Button>("BtnInviteTicket") is not { } ticket) return;
            ticket.IsVisible = _inviteTicketShown = InviteTicketRule.ShouldShow(mine);
            SyncInviteWobble();
        }

        /// <summary>The wobble runs only while the ticket is up AND the window is shown.</summary>
        private void SyncInviteWobble()
        {
            if (_inviteTicketWobble == null) return;
            bool run = _inviteTicketShown && IsVisible;
            if (run && !_inviteTicketWobble.IsEnabled) _inviteTicketWobble.Start();
            else if (!run) _inviteTicketWobble.Stop();
        }

        internal bool InviteWobbleRunning => _inviteTicketWobble?.IsEnabled == true;

        /// <summary>WPF WobbleInviteTicket: a 420 ms shake about the -8 degree rest and a small pop.</summary>
        private void WobbleInviteTicket()
        {
          try
          {
            if (_inviteTicketWobble != null) _inviteTicketWobble.Interval = InviteTicketRule.NextWobble(_inviteTicketRng);
            if (!AmbientFxCanvas.Env.AllowTransitions || !IsVisible || Named<Button>("BtnInviteTicket") is not { IsVisible: true } ticket
                || ticket.Content is not Control { RenderTransform: TransformGroup g }
                || g.Children[0] is not ScaleTransform scale || g.Children[1] is not RotateTransform tilt) return;
            const double rest = -8;
            // Animation.RunAsync on a Transform throws (TransformAnimator casts to Visual) and this runs
            // from a timer tick, so the keys go through TransformTween and the tick is guarded.
            var ease = new global::Avalonia.Animation.Easings.QuadraticEaseOut();
            _inviteTiltRun?.Stop();
            _inviteTiltRun = Helpers.TransformTween.Run(tilt, TimeSpan.FromMilliseconds(420),
                new (double, global::Avalonia.AvaloniaProperty, double)[]
                {
                    (0, RotateTransform.AngleProperty, rest), (0.1667, RotateTransform.AngleProperty, rest - 14),
                    (0.381, RotateTransform.AngleProperty, rest + 11), (0.595, RotateTransform.AngleProperty, rest - 8),
                    (0.798, RotateTransform.AngleProperty, rest + 4), (1, RotateTransform.AngleProperty, rest),
                }, ease);
            _invitePopRun?.Stop();
            _invitePopRun = Helpers.TransformTween.Run(scale, TimeSpan.FromMilliseconds(280),
                new (double, global::Avalonia.AvaloniaProperty, double)[]
                {
                    (0, ScaleTransform.ScaleXProperty, 1.0), (0.5, ScaleTransform.ScaleXProperty, 1.12), (1, ScaleTransform.ScaleXProperty, 1.0),
                    (0, ScaleTransform.ScaleYProperty, 1.0), (0.5, ScaleTransform.ScaleYProperty, 1.12), (1, ScaleTransform.ScaleYProperty, 1.0),
                }, ease);
          }
          catch (Exception ex) { Log.Debug("WobbleInviteTicket: {E}", ex.Message); }
        }

        private DispatcherTimer? _inviteTiltRun, _invitePopRun;
        /// <summary>Test seam: the wobble's two runs (tilt, pop), null until the first wobble.</summary>
        internal (DispatcherTimer? Tilt, DispatcherTimer? Pop) InviteWobbleRuns => (_inviteTiltRun, _invitePopRun);
        internal void WobbleInviteTicketForTest() => WobbleInviteTicket();

        internal void BtnInviteTicket_Click(object? sender, RoutedEventArgs e) => OpenInvitesCard();

        /// <summary>WPF 7.1.5 OpenInvitesCard: Settings &gt; Account &amp; Plans, a forced read, and the card
        /// scrolled into view. Refused under Lockdown (no veil on this head yet).</summary>
        internal void OpenInvitesCard()
        {
            if (LockdownActive) return;
            OpenAppSettingsSection("account");
            DispatcherTimer.RunOnce(async () =>
            {
                try
                {
                    if (InvitesCard is not { } card) return;
                    await card.RefreshAsync(force: true);
                    card.BringIntoView();
                }
                catch (Exception ex) { Log.Debug("[Invites] open invites card failed: {E}", ex.GetType().Name); }
            }, TimeSpan.FromMilliseconds(300));
        }
    }
}
