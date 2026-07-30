using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Available Subjects</b> tab pass.
    ///
    /// One focal loop, per FX plan section 4:
    ///   • hero  - a very sparse <see cref="AmbientFxCanvas"/> fog drift behind the roster. Two
    ///     puffs instead of the default three and a low intensity: this is a list you read, and
    ///     the air behind it must never compete with a name.
    ///   • micro - the roster cards stagger in on arrival, and the Connect buttons squish on press.
    ///     Both are interaction-clock: nothing here ticks while the tab sits still.
    ///
    /// On the plan's "NeonPurple tint": the tint is taken from <see cref="FxTheme"/> (the active
    /// mod's mist colour), NOT from a literal purple. The tab's own chrome - its heading, card
    /// borders and CTA - already carries the NeonPurple identity through theme brushes; hard-coding
    /// a hex into the ambient layer is exactly the "theme from a name" the plan's rule 4 rules out,
    /// and it would be the one element on the tab that never re-tints when the mod changes.
    /// AmbientFxCanvas has no per-instance colour override, so a purple-tinted canvas would mean
    /// editing shared PR-0 infrastructure.
    ///
    /// House rules: the canvas self-gates on tier + reduced-motion + window focus and is registered
    /// with RegisterTabFx so it parks on every tab switch; every callback wrapped.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Sparse on purpose - see the class remarks.</summary>
        private const int SubjectsFogPuffs = 2;
        private const double SubjectsFogIntensity = 0.40;

        // ---- state -----------------------------------------------------------------

        private bool _subjectsFxInitialized;

        // ============================== lifecycle ==============================

        internal void OnAvailableSubjectsTabVisibilityChanged(bool visible)
        {
            try
            {
                if (!visible || !IsIncomingTab("availablesubjects")) return;  // outgoing tab's fade-out re-show
                InitializeSubjectsFx();
                StaggerSubjectCards(allowRetry: true);
            }
            catch (Exception ex) { App.Logger?.Debug("OnAvailableSubjectsTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeSubjectsFx()
        {
            if (_subjectsFxInitialized) return;
            _subjectsFxInitialized = true;
            try
            {
                var canvas = AvailableSubjectsTab?.SubjectsAmbientFx;
                if (canvas == null) return;
                canvas.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.FogDrift,
                    FogPuffs = SubjectsFogPuffs,
                    Intensity = SubjectsFogIntensity,
                });
                // ShowTab parks it on the way out and resumes it on the way in, for free.
                RegisterTabFx("availablesubjects", canvas);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeSubjectsFx failed"); }
        }

        // ============================== entrance stagger ==============================

        /// <summary>
        /// Staggers the roster cards in. Only on a tab show: the service polls in the background
        /// and re-pushes its collection, and re-running the stagger on every poll would make the
        /// roster twitch every few seconds while somebody is reading it.
        /// </summary>
        /// <param name="allowRetry">
        /// ShowTab makes the tab visible BEFORE it binds the roster (EnsureAvailableSubjectsBound
        /// runs a few lines later), so on the very first open this handler sees an empty list. One
        /// re-post at Normal priority lands after ShowTab has returned and catches it. Normal, not
        /// Loaded: Loaded-priority work is starved in this window and would simply never run.
        /// </param>
        private void StaggerSubjectCards(bool allowRetry = false)
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var list = AvailableSubjectsTab?.AvailableSubjectsList;
                if (list == null) return;
                if (list.Items.Count == 0)
                {
                    if (!allowRetry) return;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (AvailableSubjectsTab?.IsVisible == true) StaggerSubjectCards();
                        }
                        catch (Exception ex) { App.Logger?.Debug("StaggerSubjectCards retry: {E}", ex.Message); }
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                    return;
                }

                // Containers are generated during layout, so force one rather than betting on a
                // dispatcher priority (DispatcherPriority.Loaded is starved in this window).
                try { list.UpdateLayout(); }
                catch (Exception ex) { App.Logger?.Debug("Subjects FX layout: {E}", ex.Message); }

                // The CONTAINER, deliberately, not the card Border inside it: the card binds its
                // Opacity to CardOpacity (taken subjects render dimmed), and an opacity animation
                // holds its end value forever, which would silently un-dim every taken card for
                // the rest of the session.
                var cards = new List<FrameworkElement>();
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement card)
                        cards.Add(card);
                }
                if (cards.Count == 0) return;
                foreach (var card in cards) EnsureCardTransforms(card);
                MotionFx.StaggerIn(cards);
            }
            catch (Exception ex) { App.Logger?.Debug("StaggerSubjectCards: {E}", ex.Message); }
        }

        // ============================== connect press ==============================

        /// <summary>Press squish on a roster card's Connect button. Interaction clock, so it
        /// survives Reduced and only snaps at Off.</summary>
        internal void OnSubjectConnectPress(FrameworkElement? button, bool down)
        {
            try { MotionFx.PressSquish(button, down); }
            catch (Exception ex) { App.Logger?.Debug("OnSubjectConnectPress: {E}", ex.Message); }
        }
    }
}
