using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Companion</b> tab pass — what is left of it after the
    /// "Her Room" redesign.
    ///
    /// <para><b>What moved, and why this file shrank.</b> Both effects this partial owned belonged
    /// to controls the redesign superseded:</para>
    /// <list type="bullet">
    ///   <item>the hero disc's 1.00↔1.015 breathe drove <c>CompanionHeroAvatarRing</c>. The Companion
    ///   Card owns that loop now (<c>CompanionHeroCard.StartAmbientLoop</c>, the tab's ONE permitted
    ///   Forever storyboard), and <c>CompanionRoomView</c> parks and resumes it off the tab's own
    ///   visibility — including the asleep state, which the old clock had no way to know about;</item>
    ///   <item>the connect sheen swept <c>CompanionAiBrainCard</c>, which the Engine Room replaced.
    ///   Z7 is deliberately unglamorous — flat, thin gray border, no glow — so a sheen sweeping
    ///   across it would have been the one piece of decoration the design specifically rules out.
    ///   The connection result now lands in the drawer's status line, in words.</item>
    /// </list>
    ///
    /// <para>The visibility hook stays: <c>ShowTab</c>'s choreography still routes through it, and
    /// it is where a future Companion-tab effect would attach.</para>
    /// </summary>
    public partial class MainWindow
    {
        private bool _companionFxInitialized;

        /// <summary>Called from CompanionTabView's IsVisibleChanged - the tab's own file, so
        /// ShowTab keeps working exactly as it did.</summary>
        internal void OnCompanionTabVisibilityChanged(bool visible)
        {
            try
            {
                if (visible && !IsIncomingTab("companion")) return;  // the outgoing tab's fade-out re-show

                // Leaving the tab is when the two drawers' open/closed state is written. Per-toggle
                // would mean a settings save on every click of a control whose whole point is that
                // it is opened rarely.
                if (!visible) PersistCompanionDrawerStates();

                InitializeCompanionFx();
            }
            catch (Exception ex) { App.Logger?.Debug("OnCompanionTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeCompanionFx()
        {
            if (_companionFxInitialized) return;
            _companionFxInitialized = true;
            try
            {
                if (App.Mods != null) App.Mods.ModChanged += OnCompanionFxModChanged;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeCompanionFx failed"); }
        }

        /// <summary>
        /// A mod switch changes her name, her portrait, her flavor line and the mod chip, so the
        /// page re-reads. (It used to drop the sheen adorner instead — the sheen is gone; the
        /// re-read is what actually mattered and was previously done nowhere.)
        /// </summary>
        private void OnCompanionFxModChanged(object? sender, ModPackage mod)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(() => OnCompanionFxModChanged(sender, mod)));
                    return;
                }
                CompanionRoom?.Sync();
            }
            catch (Exception ex) { App.Logger?.Debug("OnCompanionFxModChanged: {E}", ex.Message); }
        }
    }
}
