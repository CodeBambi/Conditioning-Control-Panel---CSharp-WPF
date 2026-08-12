using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Profile</b> tab pass (the profile viewer + community
    /// settings surface, tab key "discord").
    ///
    /// No hero loop is added: the tab already has one, and it is the best animated element in the
    /// app - the rotating OG border on the profile card. It is left EXACTLY as it shipped, storyboard
    /// and all. A second loop next to it would be two heroes on one tab.
    ///
    /// Micro only:
    ///   • entrance stagger on the profile column's cards when the tab is opened;
    ///   • a focus glow on the search box - the border brightens to the mod's FX glow colour over
    ///     150ms and fades back on blur.
    ///
    /// House rules: BeginAnimation only, colour from <see cref="FxTheme"/>, no new resource keys
    /// (the focus brush is built in code and owned by this file), every callback wrapped.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        private const int ProfileSearchGlowMs = 150;

        /// <summary>Peak alpha of the focus border. Enough to read as "this is where you are
        /// typing" without turning the search box into a second CTA.</summary>
        private const byte ProfileSearchGlowAlpha = 0xC0;

        // ---- state -----------------------------------------------------------------

        private bool _profileFxInitialized;

        /// <summary>Whether the OG border's storyboard is currently turning.</summary>
        private bool _ogBorderLoopRunning;

        /// <summary>The search box's own border brush. Built once and mutated by animation, so the
        /// focus glow never allocates and never touches an app-level resource.</summary>
        private SolidColorBrush? _profileSearchBrush;

        // ============================== lifecycle ==============================

        internal void OnProfileTabVisibilityChanged(bool visible)
        {
            try
            {
                // The OG border is the tab's hero loop and it is gated whether or not the tab is
                // the incoming one — parking it is exactly what a hide should do.
                InitializeProfileFx();
                ApplyOgBorderLoop();
                // THE VAT (MainWindow.ProfileVat.cs). "On screen" means the Trainer
                // Card is the tab being arrived at — a hide, and the outgoing tab's
                // fade-out re-show, both park the 60s poll.
                OnProfileVatVisibilityChanged(visible && IsIncomingTab("discord"));
                if (!visible) return;
                // The rail's footer reads settings, not checkboxes, so it is right even before the
                // Privacy & Sharing dialog has ever been opened. Refreshed on every show because a
                // toggle can be flipped from Settings or the Goon tab in between.
                UpdateProfileSharingSummary();
                // Me-first (redesign Phase 1): the tab opens on your own card, not a placeholder.
                EnsureProfileMeFirst();
                if (!IsIncomingTab("discord")) return;  // the outgoing tab's fade-out re-show
                StaggerProfileCards();
            }
            catch (Exception ex) { App.Logger?.Debug("OnProfileTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeProfileFx()
        {
            if (_profileFxInitialized) return;
            _profileFxInitialized = true;
            try
            {
                var search = DiscordTab?.TxtProfileSearch;
                if (search != null)
                {
                    search.GotFocus += ProfileSearch_GotFocus;
                    search.LostFocus += ProfileSearch_LostFocus;
                }
                EnsureProfileSearchBrush();

                // PR-5 coherence pass: the OG border was the one pre-existing ambient loop the
                // overhaul never retrofitted. Its own hooks, so the shared funnels stay untouched.
                Activated += OnProfileFxWindowStateish;
                Deactivated += OnProfileFxWindowStateish;
                StateChanged += OnProfileFxWindowStateish;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeProfileFx failed"); }
        }

        private void OnProfileFxWindowStateish(object? sender, EventArgs e) => ApplyOgBorderLoop();

        // ============================== OG border loop ==============================

        /// <summary>
        /// Gates the rotating OG border - a 3s forever storyboard on a gradient's RelativeTransform
        /// that shipped ungated: it kept turning with the Profile tab hidden, with the window
        /// minimised, and at MotionLevel.Off. The art is untouched (it is the best animated element
        /// in the app); all that changes is when its clock is allowed to run, and that it now draws
        /// at the shared ambient frame rate instead of the full compositor rate.
        ///
        /// Stop() returns the gradient to Angle 0, which is a perfectly good static resting state,
        /// so the degraded mode is "a still gold border" rather than "a border frozen mid-turn".
        /// </summary>
        internal void ApplyOgBorderLoop()
        {
            try
            {
                // Reachable from the profile-render paths, which can run before the tab has ever
                // been shown - so the window hooks are installed here rather than assumed.
                InitializeProfileFx();
                var container = DiscordTab?.OgBorderContainer;
                if (container == null) return;
                if (container.Resources["OgBorderAnimation"] is not Storyboard storyboard) return;

                bool wanted = container.Visibility == Visibility.Visible
                              && MotionFx.AllowAmbientLoops
                              && IsActive
                              && WindowState != WindowState.Minimized
                              && DiscordTab?.IsVisible == true;

                if (!wanted)
                {
                    if (!_ogBorderLoopRunning) return;
                    _ogBorderLoopRunning = false;
                    storyboard.Stop(container);
                    return;
                }

                // Begin() restarts from Angle 0, so only ever call it on the stopped -> running
                // edge. Without this, every Activated event would visibly snap the border back.
                if (_ogBorderLoopRunning) return;
                if (!storyboard.IsFrozen) Timeline.SetDesiredFrameRate(storyboard, AmbientFrameRate);
                storyboard.Begin(container, true);
                _ogBorderLoopRunning = true;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyOgBorderLoop: {E}", ex.Message); }
        }

        // ============================== entrance stagger ==============================

        /// <summary>
        /// Staggers the profile column's cards. PR-1's tab choreography stages the tab as a whole;
        /// this adds the layer inside it. Only the visible children - the profile card and the
        /// "no profile selected" placeholder swap, and animating a collapsed one would burn a
        /// stagger slot on nothing.
        /// </summary>
        private void StaggerProfileCards()
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var stack = DiscordTab?.ProfileColumnStack;
                if (stack == null) return;
                var cards = stack.Children.OfType<FrameworkElement>()
                                 .Where(c => c.Visibility == Visibility.Visible)
                                 .ToList();
                if (cards.Count == 0) return;
                foreach (var card in cards) EnsureCardTransforms(card);
                MotionFx.StaggerIn(cards);
            }
            catch (Exception ex) { App.Logger?.Debug("StaggerProfileCards: {E}", ex.Message); }
        }

        // ============================== search focus glow ==============================

        private void EnsureProfileSearchBrush()
        {
            try
            {
                var box = DiscordTab?.ProfileSearchBox;
                if (box == null) return;
                if (_profileSearchBrush != null) { box.BorderBrush = _profileSearchBrush; return; }

                var tint = FxTheme.GlowColor;
                _profileSearchBrush = new SolidColorBrush(Color.FromArgb(0, tint.R, tint.G, tint.B));
                box.BorderBrush = _profileSearchBrush;
            }
            catch (Exception ex) { App.Logger?.Debug("EnsureProfileSearchBrush: {E}", ex.Message); }
        }

        private void ProfileSearch_GotFocus(object sender, RoutedEventArgs e) => ApplyProfileSearchGlow(true);

        private void ProfileSearch_LostFocus(object sender, RoutedEventArgs e) => ApplyProfileSearchGlow(false);

        /// <summary>
        /// Brightens (or fades) the search box's border. The colour is re-read from
        /// <see cref="FxTheme"/> on the way in, so a mod switch re-tints it on the next focus
        /// without anything having to listen for ModChanged.
        /// </summary>
        private void ApplyProfileSearchGlow(bool on)
        {
            try
            {
                EnsureProfileSearchBrush();
                var brush = _profileSearchBrush;
                if (brush == null || brush.IsFrozen) return;

                var tint = FxTheme.GlowColor;
                var to = on
                    ? Color.FromArgb(ProfileSearchGlowAlpha, tint.R, tint.G, tint.B)
                    : Color.FromArgb(0, tint.R, tint.G, tint.B);

                if (!MotionFx.AllowTransitions)
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    brush.Color = to;
                    return;
                }
                var anim = new ColorAnimation(to, TimeSpan.FromMilliseconds(ProfileSearchGlowMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileSearchGlow: {E}", ex.Message); }
        }
    }
}
