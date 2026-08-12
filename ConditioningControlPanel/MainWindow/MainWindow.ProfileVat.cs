using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE VAT on the Trainer Card — the desktop's first Descent surface.
    ///
    /// WHAT THIS FILE OWNS: when to ask the server, what to do with the answer, and
    /// the one geometry swap that turns the 104px hero avatar into a portrait
    /// floating inside a glass jar. The drawing is <see cref="VatGlassCanvas"/>, the
    /// wire contract is <see cref="DescentReader"/>, and the pour/silent decision is
    /// <see cref="VatFillCoordinator"/> — none of the three knows about the others.
    ///
    /// THE TRI-STATE LAW IS ENFORCED HERE, and it is the feature's safety property:
    /// no `descent` key (every account outside the server's rollout dial), or a
    /// malformed one, and the vat DOES NOT EXIST — the jar stays Collapsed, the
    /// avatar stays 104px at its original margin, and the Trainer Card measures and
    /// renders exactly as it did before this file was written. There is no desktop
    /// demo dataset and nothing here ever derives a fill from local XP: a vat filled
    /// from XP the server has not accepted is a number nobody agreed to.
    ///
    /// CADENCE (shared with mobile and web):
    ///   • on Trainer Card open,
    ///   • every 60s while the card is the visible tab,
    ///   • and immediately after an accepted /v2/user/sync — the moment today's XP
    ///     actually lands in the server vat (ProfileSyncService.SyncProfileAsync).
    /// A 10s floor inside DescentService keeps a burst of those from becoming a
    /// burst of requests.
    /// </summary>
    public partial class MainWindow
    {
        // ---- geometry ---------------------------------------------------------
        // The jar's height is the only free number; everything else is the locked
        // mockup's ratios (VatGlassCanvas). 218 is chosen so the armed bay —
        // jar + readout + the hero grid's 16px top/bottom margins — still fits
        // inside ProfileHeroCard's 275px MinHeight and never grows the card.

        private const double VatJarHeight = 218;

        /// <summary>The avatar size the hero uses when the vat is dark. Do not change.</summary>
        private const double VatDarkAvatarSize = 104;

        // ---- state ------------------------------------------------------------

        private readonly VatFillCoordinator _vatCoordinator = new();
        private DispatcherTimer? _vatPollTimer;
        private bool _vatWired;
        private bool _vatArmed;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Called from <see cref="OnProfileTabVisibilityChanged"/> with "the Trainer
        /// Card is the tab the user is actually on". False parks the poll — the vat
        /// costs nothing while you are somewhere else.
        /// </summary>
        private void OnProfileVatVisibilityChanged(bool onScreen)
        {
            try
            {
                WireProfileVat();
                if (!onScreen)
                {
                    _vatPollTimer?.Stop();
                    return;
                }

                ApplyDescentToVat();                       // draw what we already know
                App.Descent?.RequestRefresh("trainer card open");

                _vatPollTimer ??= CreateVatPollTimer();
                _vatPollTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("OnProfileVatVisibilityChanged: {E}", ex.Message); }
        }

        private DispatcherTimer CreateVatPollTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            timer.Tick += (_, _) =>
            {
                try { App.Descent?.RequestRefresh("trainer card poll"); }
                catch (Exception ex) { App.Logger?.Debug("Vat poll: {E}", ex.Message); }
            };
            return timer;
        }

        private void WireProfileVat()
        {
            if (_vatWired) return;
            _vatWired = true;
            try
            {
                if (App.Descent != null) App.Descent.BlockChanged += OnDescentBlockChanged;

                var glass = DiscordTab?.ProfileVatGlass;
                if (glass != null) glass.FillPercentChanged += OnVatFillPercentChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("WireProfileVat: {E}", ex.Message); }
        }

        private void OnDescentBlockChanged(object? sender, EventArgs e)
        {
            // DescentService already marshalled to the UI thread and already checked
            // the dispatcher; this is a plain handler by the time it gets here.
            try { ApplyDescentToVat(); }
            catch (Exception ex) { App.Logger?.Debug("OnDescentBlockChanged: {E}", ex.Message); }
        }

        private void OnVatFillPercentChanged(object? sender, int pct)
        {
            try
            {
                var readout = DiscordTab?.ProfileVatReadout;
                if (readout != null) readout.Text = pct + "%";
            }
            catch (Exception ex) { App.Logger?.Debug("OnVatFillPercentChanged: {E}", ex.Message); }
        }

        // ============================== the apply ==============================

        /// <summary>
        /// Fold the current block into the visual. Every exit here is deliberate:
        /// a missing block DISARMS rather than freezing the last vat on screen,
        /// because a dial that narrows mid-session must take the meter with it.
        /// </summary>
        private void ApplyDescentToVat()
        {
            var glass = DiscordTab?.ProfileVatGlass;
            if (glass == null) return;

            var read = _vatCoordinator.Apply(App.Descent?.Current);
            if (read.Kind == VatReadKind.Ignored && !_vatArmed) { DisarmVat(); return; }
            if (App.Descent?.Current?.Vat is null) { DisarmVat(); return; }

            ArmVat(glass);
            glass.SetLip(read.Lip);

            switch (read.Kind)
            {
                case VatReadKind.Seed:
                    glass.Seed(read.Fill);
                    break;
                case VatReadKind.Pour:
                    // The faucet swings in. A pour landing while one is already
                    // running EXTENDS it inside the canvas; nothing restarts here.
                    glass.PourTo(read.Fill);
                    App.Logger?.Debug("[Descent] vat pour +{Xp} XP -> {Pct:F0}%", read.DeltaXp, read.Fill * 100);
                    break;
                case VatReadKind.Silent:
                    glass.EaseTo(read.Fill);
                    break;
            }
        }

        /// <summary>
        /// Turn the hero avatar into the jar's portrait. Idempotent, and the ONLY
        /// place the avatar is resized — every number is derived from the locked
        /// ratios on <see cref="VatGlassCanvas"/> so the portrait cannot drift out of
        /// the glass if the jar size is ever retuned.
        /// </summary>
        private void ArmVat(VatGlassCanvas glass)
        {
            if (_vatArmed) return;
            _vatArmed = true;

            double jarH = VatJarHeight;
            double jarW = jarH / VatGlassCanvas.JarAspect;
            double portrait = Math.Round(jarW * VatGlassCanvas.PortraitDiameterRatio);
            double left = Math.Round((jarW - portrait) / 2);
            double top = Math.Round(jarH * VatGlassCanvas.PortraitCenterYRatio - 2 - portrait / 2);

            glass.Width = jarW;
            glass.Height = jarH;
            glass.Visibility = Visibility.Visible;

            var avatar = DiscordTab?.ProfileHeroAvatar;
            if (avatar != null)
            {
                // AdornedAvatar scales its decoration off AvatarSize, so the wardrobe
                // follows the portrait down without a second number to keep in step.
                avatar.AvatarSize = portrait;
                avatar.Margin = new Thickness(left, top, 0, 0);
            }

            var readout = DiscordTab?.ProfileVatReadout;
            if (readout != null)
            {
                readout.Width = jarW;
                readout.Visibility = Visibility.Visible;
            }

            App.Logger?.Information("[Descent] vat armed on the Trainer Card");
        }

        /// <summary>
        /// Put the hero back exactly as it ships without the Descent: 104px avatar at
        /// its original 6px margin, no jar, no readout, and a bay that measures to the
        /// avatar alone.
        /// </summary>
        private void DisarmVat()
        {
            if (!_vatArmed)
            {
                // Still make sure the collapsed state holds on first pass — the XAML
                // ships Collapsed, so this is a no-op in the normal case.
                var g = DiscordTab?.ProfileVatGlass;
                if (g != null) g.Visibility = Visibility.Collapsed;
                return;
            }
            _vatArmed = false;

            var glass = DiscordTab?.ProfileVatGlass;
            if (glass != null) glass.Visibility = Visibility.Collapsed;

            var avatar = DiscordTab?.ProfileHeroAvatar;
            if (avatar != null)
            {
                avatar.AvatarSize = VatDarkAvatarSize;
                avatar.Margin = new Thickness(6, 6, 0, 0);
            }

            var readout = DiscordTab?.ProfileVatReadout;
            if (readout != null) readout.Visibility = Visibility.Collapsed;
        }
    }
}
