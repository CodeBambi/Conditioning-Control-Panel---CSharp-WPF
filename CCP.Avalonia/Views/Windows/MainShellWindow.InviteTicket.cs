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

        private InvitePanel? InvitesCard => Named<Tabs.ExclusivesTabView>("ExclusivesTab")?.FindControl<InvitePanel>("InvitesHost");

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
            CoreAccount.UnifiedIdentityChanged += OnInviteTicketIdentityChanged;
            // Better than WPF (P01): no wobble ticks while the panel window is hidden.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncInviteWobble(); };
            Closed += (_, _) =>
            {
                CoreAccount.UnifiedIdentityChanged -= OnInviteTicketIdentityChanged;
                _inviteTicketFirst?.Stop(); _inviteTicketRefresh?.Stop(); _inviteTicketWobble?.Stop();
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
            if (_inviteTicketWobble != null) _inviteTicketWobble.Interval = InviteTicketRule.NextWobble(_inviteTicketRng);
            if (!AmbientFxCanvas.Env.AllowTransitions || !IsVisible || Named<Button>("BtnInviteTicket") is not { IsVisible: true } ticket
                || ticket.Content is not Control { RenderTransform: TransformGroup g }
                || g.Children[0] is not ScaleTransform scale || g.Children[1] is not RotateTransform tilt) return;
            const double rest = -8;
            _ = Run(tilt, RotateTransform.AngleProperty, 420, (0.1667, rest - 14), (0.381, rest + 11), (0.595, rest - 8), (0.798, rest + 4), (1, rest));
            _ = Run(scale, ScaleTransform.ScaleXProperty, 280, (0.5, 1.12), (1, 1.0));
            _ = Run(scale, ScaleTransform.ScaleYProperty, 280, (0.5, 1.12), (1, 1.0));

            static Task Run(Animatable target, global::Avalonia.AvaloniaProperty prop, int ms, params (double Cue, double Value)[] frames)
            {
                var a = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Easing = new global::Avalonia.Animation.Easings.QuadraticEaseOut() };
                foreach (var (cue, value) in frames) a.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(prop, value) } });
                return a.RunAsync(target);
            }
        }

        internal void BtnInviteTicket_Click(object? sender, RoutedEventArgs e) => OpenInvitesCard();

        /// <summary>WPF OpenInvitesCard: the Premium tab, a forced read, and the card scrolled into view.
        /// Refused under Lockdown (no veil on this head yet).</summary>
        internal void OpenInvitesCard()
        {
            if (LockdownActive) return;
            ShowTab("exclusives");
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
