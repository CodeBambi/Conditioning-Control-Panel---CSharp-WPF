using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
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
    ///   • on Trainer Card open — ALWAYS, whether or not this account has ever had a
    ///     block; it is the one request that can light a dark key up,
    ///   • every 60s while the card is the visible tab AND the window is in front of
    ///     somebody AND the key is lit (<see cref="EvaluateVatPoll"/>),
    ///   • and immediately after an accepted /v2/user/sync, also key-gated
    ///     (ProfileSyncService.SyncProfileAsync) — the moment today's XP actually
    ///     lands in the server vat.
    /// A 10s floor inside DescentService keeps a burst of those from becoming a
    /// burst of requests, and offline mode stops all three at the service.
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
        private bool _vatOnScreen;

        /// <summary>
        /// The server's daily cap for the last accepted reading — the divisor the
        /// readout needs to turn the DRAWN fill fraction back into an XP amount.
        /// 0 means "no cap known", and the readout falls back to the bare percent it
        /// showed before this number existed rather than inventing a total.
        /// </summary>
        private int _vatCap;

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
                _vatOnScreen = onScreen;
                if (!onScreen)
                {
                    _vatPollTimer?.Stop();
                    OnFaucetVatOffScreen();
                    return;
                }

                // THE TAB-ENTRY ESCAPE VALVE (owner ruling 2026-08-13): entering the
                // Profile tab fresh always shows the TRUE level — no pending hold is
                // carried across visits, so a hold left behind by the last visit is
                // dropped and the glass snapped to the last accepted server fill
                // before the reading below can decide anything.
                if (_vatArmed && _faucetHold.HeldXp > 0)
                {
                    _faucetHold.ClearHeld();
                    DiscordTab?.ProfileVatGlass?.SnapTo(_faucetHold.TruthFill);
                }

                ApplyDescentToVat();                       // draw what we already know

                // THE ONE UNGATED REQUEST. Opening the card always asks, even for an
                // account that has never been seen to carry a block — this is the
                // single call that lets a dark key light up. Everything recurring
                // (the poll below, the post-sync hook in ProfileSyncService) waits
                // for DescentService.HasSeenBlock.
                App.Descent?.RequestRefresh("trainer card open");

                EvaluateVatPoll();
            }
            catch (Exception ex) { App.Logger?.Debug("OnProfileVatVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// THE POLL'S GATE, and it is a NETWORK gate rather than a drawing one.
        ///
        /// Three conditions, all required:
        ///   • the Trainer Card is the tab on screen,
        ///   • the window is actually in front of somebody — mirrored from the
        ///     renderer's own test (<see cref="VatGlassCanvas.WindowIsPresenting"/>)
        ///     so the clock and the requests cannot drift apart. The renderer was
        ///     gated on window state from the start; the timer was not, so a
        ///     minimised app sat there fetching a meter nobody could see, forever.
        ///   • and this account has been seen to have a descent block at all.
        /// Re-evaluated on window state (MainWindow.ProfileFx's existing hooks), on
        /// tab change, and after every block change — the last of which is how the
        /// card-open request promotes a newly-lit key into a polling one.
        /// </summary>
        private void EvaluateVatPoll()
        {
            try
            {
                bool run = _vatOnScreen
                           && App.Descent?.HasSeenBlock == true
                           && VatGlassCanvas.WindowIsPresenting(this);

                if (!run) { _vatPollTimer?.Stop(); return; }

                _vatPollTimer ??= CreateVatPollTimer();
                _vatPollTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("EvaluateVatPoll: {E}", ex.Message); }
        }

        private DispatcherTimer CreateVatPollTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            timer.Tick += (_, _) =>
            {
                try
                {
                    // Belt and braces: the gate is re-tested on the tick as well as
                    // on the events, so no missed Deactivated can leave it running.
                    if (!_vatOnScreen
                        || App.Descent?.HasSeenBlock != true
                        || !VatGlassCanvas.WindowIsPresenting(this))
                    {
                        _vatPollTimer?.Stop();
                        return;
                    }
                    App.Descent?.RequestRefresh("trainer card poll");
                }
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
            try
            {
                ApplyDescentToVat();
                // A block arriving is what arms the poll for a key that was dark when
                // the card opened; a block being withdrawn (or logout) disarms it.
                EvaluateVatPoll();
            }
            catch (Exception ex) { App.Logger?.Debug("OnDescentBlockChanged: {E}", ex.Message); }
        }

        private void OnVatFillPercentChanged(object? sender, int pct)
        {
            try { UpdateVatReadout(); }
            catch (Exception ex) { App.Logger?.Debug("OnVatFillPercentChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// THE READOUT under the jar: banked XP, the level-scaled total that fills it,
        /// and the percent, on one line (owner ask 2026-08-16,
        /// "we need a number for how much XP fills the vat at each level" — the cap is
        /// level-scaled, so the percent alone never says how big today's jar is).
        ///
        /// IT READS THE DRAWN LEVEL, NOT THE SERVER'S. <see cref="VatGlassCanvas.Fill"/>
        /// is the fraction actually on screen, which is the whole point: the desktop
        /// faucet HOLDS earned XP until the user pours it, and a readout sourced from
        /// the block would print XP that the liquid has not risen to yet. Both halves
        /// therefore come off the same number the percent does, and the pour animation
        /// walks them up together.
        ///
        /// The cap is the last accepted server cap (<see cref="_vatCap"/>); with none
        /// known the line degrades to the bare percent rather than guessing a total.
        /// </summary>
        private void UpdateVatReadout()
        {
            var readout = DiscordTab?.ProfileVatReadout;
            if (readout == null) return;

            double fill = DiscordTab?.ProfileVatGlass?.Fill ?? 0;
            // Same rounding as VatGlassCanvas.NotifyPercent, so the two can never
            // disagree about which whole percent is on screen.
            int pct = (int)Math.Round(fill * 100);

            if (_vatCap <= 0)
            {
                readout.Text = pct.ToString(CultureInfo.CurrentCulture) + "%";
                readout.ToolTip = null;
                return;
            }

            int shown = (int)Math.Round(fill * _vatCap);
            int toFill = Math.Max(0, _vatCap - shown);

            readout.Text = Loc.GetF("profile_vat_readout",
                shown.ToString("N0", CultureInfo.CurrentCulture),
                _vatCap.ToString("N0", CultureInfo.CurrentCulture),
                pct);

            readout.ToolTip = toFill > 0
                ? Loc.GetF("profile_vat_readout_tip",
                    shown.ToString("N0", CultureInfo.CurrentCulture),
                    _vatCap.ToString("N0", CultureInfo.CurrentCulture),
                    toFill.ToString("N0", CultureInfo.CurrentCulture))
                : Loc.GetF("profile_vat_readout_tip_full",
                    shown.ToString("N0", CultureInfo.CurrentCulture),
                    _vatCap.ToString("N0", CultureInfo.CurrentCulture));
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

            // BEFORE the fold below, because Snap/Ease can raise FillPercentChanged
            // synchronously and the readout must already know this reading's divisor.
            // Guaranteed > 0 here: the only VatRead that carries no cap is the
            // null-vat one, and both disarm exits above have already taken it.
            _vatCap = read.Cap;

            // THE EXPLAINER, and this is the only line in the app that may fire it. Past this
            // point the jar demonstrably exists for this account - both disarm exits are above -
            // so the card can never describe a portrait that is still a plain 104px avatar.
            //
            // _vatOnScreen as well, because ApplyDescentToVat is also reached from
            // OnDescentBlockChanged: a block landing from the post-sync hook can arm the vat
            // while the user is three tabs away, and a modal about a jar they cannot see is
            // worse than no modal at all. That path leaves the card unspent and the next visit
            // to the Trainer Card - which always re-runs this method - shows it.
            //
            // NOT called from ArmVat: that method is idempotent, so a vat armed off-screen would
            // consume its only chance to introduce itself.
            if (_vatOnScreen) MaybeShowFeatureIntro("descent-vat", "discord");

            // THE HOLD (owner ruling 2026-08-13, desktop surface only): while the
            // Profile tab is on screen, earned XP does not move the liquid — it
            // accumulates in the faucet perched on the lip until the user clicks it.
            // VatFaucetHold folds the coordinator's reading into that hold and
            // answers what the glass should actually do; the shared contract's
            // cases all survive inside it (seed snaps, cap retunes re-scale
            // silently, midnight drains silently, a mid-pour delta extends).
            var step = _faucetHold.Fold(read, holdActive: _vatOnScreen, pouring: glass.IsPouring);
            switch (step.Action)
            {
                case FaucetActionKind.Snap:
                    glass.SnapTo(step.Fill);
                    break;

                case FaucetActionKind.Ease:
                    glass.EaseTo(step.Fill);
                    break;

                case FaucetActionKind.Pour:
                    if (!glass.IsPresenting)
                    {
                        // A POUR NOBODY IS THERE FOR IS NOT REPLAYED — same rule as
                        // ever. The reading still applies, silently.
                        glass.EaseTo(step.Fill);
                        break;
                    }
                    // Only reachable mid-pour (the extension case): the stream keeps
                    // running and simply lasts longer. Nothing restarts.
                    glass.PourTo(step.Fill);
                    App.Logger?.Debug("[Descent] vat pour extended +{Xp} XP -> {Pct:F0}%", read.DeltaXp, step.Fill * 100);
                    break;
            }

            if (read.DeltaXp > 0 && step.Action == FaucetActionKind.None)
                App.Logger?.Debug("[Descent] faucet holding +{Xp} XP ({Held} total)", read.DeltaXp, _faucetHold.HeldXp);

            // A CAP RETUNE MOVES THE TOTAL WITHOUT MOVING THE PERCENT (levelling up is
            // exactly that), and FillPercentChanged only fires on a whole-percent
            // change — so the readout is refreshed unconditionally here as well.
            UpdateVatReadout();
            UpdateFaucetPresentation();
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
            UpdateVatReadout();

            ArmFaucet(glass, jarW, jarH);

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
            _vatCap = 0;

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

            DisarmFaucet();
        }
    }
}
