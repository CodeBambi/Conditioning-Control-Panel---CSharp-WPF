using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The header invite ticket: a small ticket glyph between the help button and the profile
    /// bubble, shown only while the signed-in account has an unused invite code this month
    /// (<see cref="InviteTicketRule"/>). Every 20-40 s it wobbles (motion setting permitting); a
    /// click opens the Premium tab on the invites card. Offline, signed out or not a subscriber:
    /// hidden, and nothing here ever throws.
    /// </summary>
    public partial class MainWindow
    {
        private DispatcherTimer? _inviteTicketRefresh;
        private DispatcherTimer? _inviteTicketWobble;
        private bool _inviteTicketReading;
        private readonly Random _inviteTicketRng = new();

        private void InitializeInviteTicket()
        {
            try
            {
                // First read a few seconds in, after sign-in and session restore have settled.
                var first = new DispatcherTimer(DispatcherPriority.Normal) { Interval = InviteTicketRule.StartupDelay };
                first.Tick += (_, _) => { first.Stop(); _ = RefreshInviteTicketAsync(); };
                first.Start();

                _inviteTicketRefresh = new DispatcherTimer(DispatcherPriority.Normal) { Interval = InviteTicketRule.RefreshEvery };
                _inviteTicketRefresh.Tick += (_, _) => _ = RefreshInviteTicketAsync();
                _inviteTicketRefresh.Start();

                _inviteTicketWobble = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = InviteTicketRule.NextWobble(_inviteTicketRng)
                };
                _inviteTicketWobble.Tick += (_, _) => WobbleInviteTicket();

                App.UnifiedIdentityChanged += OnInviteTicketIdentityChanged;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] ticket init failed: {E}", ex.GetType().Name);
            }
        }

        /// <summary>The account changed (sign-in, sign-out, a swap): hide at once, read again shortly.</summary>
        private void OnInviteTicketIdentityChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    ApplyInviteTicket(InviteMine.Unreachable);
                    var again = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
                    again.Tick += (_, _) => { again.Stop(); _ = RefreshInviteTicketAsync(); };
                    again.Start();
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] ticket identity hook failed: {E}", ex.GetType().Name);
            }
        }

        /// <summary>Reads <c>/v2/invites/mine</c> off the UI thread and repaints the ticket. Never throws.</summary>
        private async Task RefreshInviteTicketAsync()
        {
            if (_inviteTicketReading) return;
            try
            {
                var id = BackRoomApi.AppIdentity();
                if (id == null) { ApplyInviteTicket(InviteMine.Unreachable); return; }
                _inviteTicketReading = true;
                var api = new InviteApi(identity: () => id);
                var mine = await Task.Run(() => api.MineAsync());
                ApplyInviteTicket(mine);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] ticket refresh failed: {E}", ex.GetType().Name);
                ApplyInviteTicket(InviteMine.Unreachable);
            }
            finally { _inviteTicketReading = false; }
        }

        /// <summary>Shows or hides the ticket from a read. Also fed by the Premium tab's invites
        /// card, so a code spent there updates the header too.</summary>
        private void ApplyInviteTicket(InviteMine? mine)
        {
            // The Tonight Board's Waiting card reads the same answer (no second request).
            try { Services.Billboard.Providers.WaitingSignals.NoteInvites(mine); } catch { }
            try
            {
                if (BtnInviteTicket == null) return;
                bool show = InviteTicketRule.ShouldShow(mine);
                BtnInviteTicket.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (_inviteTicketWobble == null) return;
                if (show && !_inviteTicketWobble.IsEnabled) _inviteTicketWobble.Start();
                else if (!show) _inviteTicketWobble.Stop();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] ticket paint failed: {E}", ex.GetType().Name);
            }
        }

        /// <summary>A short shake, about 400 ms, then the next gap is picked at random.</summary>
        private void WobbleInviteTicket()
        {
            try
            {
                if (_inviteTicketWobble != null) _inviteTicketWobble.Interval = InviteTicketRule.NextWobble(_inviteTicketRng);
                if (!MotionFx.AllowTransitions || InviteTicketTilt == null || !IsVisible
                    || BtnInviteTicket?.Visibility != Visibility.Visible) return;
                const double rest = -8;
                var shake = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(420),
                    FillBehavior = FillBehavior.Stop
                };
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(rest - 14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(rest + 11, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(rest - 8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(rest + 4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(335))));
                shake.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420))));
                InviteTicketTilt.BeginAnimation(RotateTransform.AngleProperty, shake);

                if (InviteTicketScale != null)
                {
                    var pop = new DoubleAnimation(1.0, 1.12, TimeSpan.FromMilliseconds(140))
                    {
                        AutoReverse = true,
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    InviteTicketScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    InviteTicketScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] ticket wobble failed: {E}", ex.GetType().Name);
            }
        }

        private void BtnInviteTicket_Click(object sender, RoutedEventArgs e) => OpenInvitesCard();

        /// <summary>
        /// Opens Settings · Account &amp; Plans (the retired Premium tab) and brings the invites card into view after a fresh read. The
        /// header ticket and the friends drawer's "Invite them" link both come here.
        /// </summary>
        internal void OpenInvitesCard()
        {
            try
            {
                OpenAppSettingsSection("account");
                var settle = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
                settle.Tick += async (_, _) =>
                {
                    settle.Stop();
                    try
                    {
                        if (_invitePanel != null) await _invitePanel.RefreshAsync(force: true);
                        PlansVaultView?.InvitesHost?.BringIntoView();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("[Invites] bring into view failed: {E}", ex.GetType().Name);
                    }
                };
                settle.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Invites] open invites card failed: {E}", ex.GetType().Name);
            }
        }
    }
}
