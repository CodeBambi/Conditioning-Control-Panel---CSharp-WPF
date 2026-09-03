// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileVat.cs (420 lines) - THE VAT
// on the Trainer Card, sorted member by member.
//
// WHAT IS REAL HERE: the geometry swap that turns the 104px hero avatar into a portrait floating
// inside the glass jar, its exact undo, and the readout under it. Every number is derived from
// the locked ratios on CCP.Avalonia/Controls/VatGlassCanvas (a full port, ratios and all), so the
// portrait cannot drift out of the glass if the jar height is ever retuned.
//
// THE TRI-STATE LAW IS INTACT, and it is the feature's safety property: no `descent` block and the
// vat DOES NOT EXIST - the jar stays hidden, the avatar stays 104px at its original 6px margin,
// and the card measures exactly as it did before this file was written. Nothing here derives a
// fill from local XP; DisarmVat is what a missing block produces, and it is the state the XAML
// ships in.
//
// Controls are reached with ProfilePage?.FindControl<T>(name) (ProfilePage is in
// MainShellWindow.ProfileCard.cs). DiscordTabView loads with AvaloniaXamlLoader.Load, so its
// generated x:Name fields are permanently null - `page.ProfileVatGlass` would compile and be a
// silent no-op forever.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   ApplyDescentToVat      - App.Descent (ConditioningControlPanel/Services/Descent/
//   OnDescentBlockChanged    DescentService.cs), the wire half of the feature. Services.Descent's
//   WireProfileVat           VatFillCoordinator and DescentReader ARE in Core
//   EvaluateVatPoll          (CCP.Core/Services/Descent/), so the fold and the read parsing are
//   CreateVatPollTimer       already portable - only the thing that ASKS the server is not, and a
//                            vat filled from anything else is a number nobody agreed to.
//   the faucet               - _faucetHold, ArmFaucet, DisarmFaucet, OnFaucetVatOffScreen,
//                            UpdateFaucetPresentation and PositionVatTickGlyphs are all in
//                            MainShellWindow.ProfileFaucet.cs, still a wholesale stub (1,146 WPF
//                            lines). ArmVat and DisarmVat below therefore arm and disarm the JAR
//                            only; the tap that holds earned XP comes with that file.
//   MaybeShowFeatureIntro  - CCP.Avalonia/Views/Windows/FeatureIntroPopup exists, but the spend
//                            ledger it reads is on the shell's stub side; and the explainer may
//                            only fire from ApplyDescentToVat, which is blocked above.
//   OnProfileVatVisibilityChanged - its caller (OnProfileTabVisibilityChanged,
//                            MainShellWindow.ProfileFx.cs) is 100% head-side, and its own body is
//                            the poll plus the faucet snap. Blocked at both ends.
//
// NO CALLER YET: on WPF every entry into this file is OnProfileVatVisibilityChanged or
// OnDescentBlockChanged, both named above. The four restored members are called by whoever
// restores MainShellWindow.ProfileFaucet.cs / the Descent seam.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ---- geometry ----------------------------------------------------------------
        // The jar's height is the only free number; everything else is the locked mockup's
        // ratios. 218 is chosen so the armed bay - jar + readout + the hero grid's 16px margins -
        // still fits inside ProfileHeroCard's MinHeight and never grows the card.

        private const double VatJarHeight = 218;

        /// <summary>The avatar size the hero uses when the vat is dark. Do not change.</summary>
        private const double VatDarkAvatarSize = 104;

        private bool _vatArmed;

        /// <summary>
        /// The server's daily cap for the last accepted reading - the divisor the readout needs to
        /// turn the DRAWN fill fraction back into an XP amount. 0 means "no cap known", and the
        /// readout falls back to the bare percent rather than inventing a total.
        /// </summary>
        private int _vatCap;

        /// <summary>The glass, or null before the Profile tab has been realized.</summary>
        private VatGlassCanvas? VatGlass => ProfilePage?.FindControl<VatGlassCanvas>("ProfileVatGlass");

        /// <summary>Repaint the readout when the drawn percent changes. Wired by WireProfileVat on
        /// WPF; that method needs App.Descent and is not restored here.</summary>
        private void OnVatFillPercentChanged(object? sender, int pct)
        {
            try { UpdateVatReadout(); }
            catch (Exception ex) { Log.Debug("OnVatFillPercentChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// THE READOUT under the jar: banked XP, the level-scaled total that fills it, and the
        /// percent, on one line - the cap is level-scaled, so the percent alone never says how big
        /// today's jar is.
        ///
        /// <para>IT READS THE DRAWN LEVEL, NOT THE SERVER'S. <c>VatGlassCanvas.Fill</c> is the
        /// fraction actually on screen, which is the whole point: the desktop faucet HOLDS earned
        /// XP until the user pours it, and a readout sourced from the block would print XP the
        /// liquid has not risen to yet.</para>
        ///
        /// <para>ProfileVatReadout carries no {loc:Str}, so writing .Text is safe here; the
        /// formatted strings come from Loc.GetF with the WPF keys and argument order.</para>
        /// </summary>
        private void UpdateVatReadout()
        {
            var readout = ProfilePage?.FindControl<TextBlock>("ProfileVatReadout");
            if (readout == null) return;

            double fill = VatGlass?.Fill ?? 0;
            // Same rounding as VatGlassCanvas.NotifyPercent, so the two can never disagree about
            // which whole percent is on screen.
            int pct = (int)Math.Round(fill * 100);

            if (_vatCap <= 0)
            {
                readout.Text = pct.ToString(CultureInfo.CurrentCulture) + "%";
                ToolTip.SetTip(readout, null);
                return;
            }

            int shown = (int)Math.Round(fill * _vatCap);
            int toFill = Math.Max(0, _vatCap - shown);

            readout.Text = Loc.GetF("profile_vat_readout",
                shown.ToString("N0", CultureInfo.CurrentCulture),
                _vatCap.ToString("N0", CultureInfo.CurrentCulture),
                pct);

            ToolTip.SetTip(readout, toFill > 0
                ? Loc.GetF("profile_vat_readout_tip",
                    shown.ToString("N0", CultureInfo.CurrentCulture),
                    _vatCap.ToString("N0", CultureInfo.CurrentCulture),
                    toFill.ToString("N0", CultureInfo.CurrentCulture))
                : Loc.GetF("profile_vat_readout_tip_full",
                    shown.ToString("N0", CultureInfo.CurrentCulture),
                    _vatCap.ToString("N0", CultureInfo.CurrentCulture)));
        }

        /// <summary>
        /// Turn the hero avatar into the jar's portrait. Idempotent, and the ONLY place the avatar
        /// is resized. <paramref name="cap"/> is the accepted server cap for this reading, which
        /// the readout needs before anything can raise FillPercentChanged.
        /// </summary>
        private void ArmVat(VatGlassCanvas glass, int cap)
        {
            _vatCap = cap;
            if (_vatArmed) { UpdateVatReadout(); return; }
            _vatArmed = true;

            double jarH = VatJarHeight;
            double jarW = jarH / VatGlassCanvas.JarAspect;
            double portrait = Math.Round(jarW * VatGlassCanvas.PortraitDiameterRatio);
            double left = Math.Round((jarW - portrait) / 2);
            double top = Math.Round(jarH * VatGlassCanvas.PortraitCenterYRatio - 2 - portrait / 2);

            glass.Width = jarW;
            glass.Height = jarH;
            glass.IsVisible = true;

            var avatar = ProfilePage?.FindControl<Controls.AdornedAvatar>("ProfileHeroAvatar");
            if (avatar != null)
            {
                // AdornedAvatar scales its decoration off AvatarSize, so the wardrobe follows the
                // portrait down without a second number to keep in step.
                avatar.AvatarSize = portrait;
                avatar.Margin = new Thickness(left, top, 0, 0);
            }

            var readout = ProfilePage?.FindControl<TextBlock>("ProfileVatReadout");
            if (readout != null)
            {
                readout.Width = jarW;
                readout.IsVisible = true;
            }
            UpdateVatReadout();

            // ponytail: ArmFaucet(glass, jarW, jarH) belongs here - see the header.
            Log.Information("[Descent] vat armed on the Trainer Card");
        }

        /// <summary>
        /// Put the hero back exactly as it ships without the Descent: 104px avatar at its original
        /// 6px margin, no jar, no readout, and a bay that measures to the avatar alone.
        /// </summary>
        private void DisarmVat()
        {
            var glass = VatGlass;
            if (!_vatArmed)
            {
                // Still make sure the hidden state holds on first pass - the XAML ships it hidden,
                // so this is a no-op in the normal case.
                if (glass != null) glass.IsVisible = false;
                return;
            }
            _vatArmed = false;
            _vatCap = 0;

            if (glass != null) glass.IsVisible = false;

            var avatar = ProfilePage?.FindControl<Controls.AdornedAvatar>("ProfileHeroAvatar");
            if (avatar != null)
            {
                avatar.AvatarSize = VatDarkAvatarSize;
                avatar.Margin = new Thickness(6, 6, 0, 0);
            }

            var readout = ProfilePage?.FindControl<TextBlock>("ProfileVatReadout");
            if (readout != null) readout.IsVisible = false;

            // ponytail: DisarmFaucet() belongs here - see the header.
        }
    }
}
