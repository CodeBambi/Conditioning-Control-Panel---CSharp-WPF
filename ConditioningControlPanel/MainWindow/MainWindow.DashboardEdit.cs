using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dashboard;
using ConditioningControlPanel.Views.Controls.Dashboard;

namespace ConditioningControlPanel
{
    /// <summary>
    /// EDIT MODE FOR THE NINE MOVABLE CELLS: the pencils, the accepted-edit path and the local
    /// persistence behind it.
    ///
    /// <para>The whole entry point is one small pencil per cell, invisible until the pointer is in
    /// that cell. No global "customize" button, no mode to enter and leave: the wall is either
    /// being looked at or being pointed at, and only the second one needs a verb (plan 6.1,
    /// memory feedback_minimal_ui).</para>
    ///
    /// <para>Two seams Phase F swaps the rolodex in behind, and nothing else:
    /// <see cref="OpenDashboardPicker"/> opens a picker for one slot, and
    /// <see cref="HandleDashboardPick"/> takes the key that came back. Everything downstream of
    /// the second one - the prompt decision, Place, the re-render, the save, the sync nudge - is
    /// shared, so a 3D picker cannot grow its own copy of the layout rules.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- the pencils ---------------------------------------------------------------

        /// <summary>22px of affordance in a ~150px cell: big enough to hit, small enough that the
        /// tile is still the thing you are looking at.</summary>
        private const double PencilSize = 22;

        private const int PencilFadeMs = 140;

        /// <summary>Not quite 1: the pencil sits on top of the art and reads as an overlay rather
        /// than as part of the tile.</summary>
        private const double PencilShownOpacity = 0.94;

        /// <summary>The live pencil per host, rebuilt on every render because the hosts survive a
        /// render and the pencils do not. Keyed by host so the hover handlers - which are attached
        /// to the hosts once and live as long as they do - can find theirs without a closure.</summary>
        private readonly Dictionary<Grid, Button> _dashboardPencilByHost = new();

        /// <summary>The open picker, or null. One at a time by construction: opening closes
        /// whatever was there first.</summary>
        private DashboardPickerPopup? _dashboardPicker;

        /// <summary>One template for all nine. A ControlTemplate is shareable and sealed on first
        /// use, so building it once is both cheaper and the only way to be sure the nine pencils
        /// cannot drift apart.</summary>
        private static ControlTemplate? _pencilTemplate;

        /// <summary>
        /// Puts a pencil in every slot host. Called at the end of every render: the cards are new
        /// objects and were added to hosts that were cleared, so the previous pencils went with
        /// them. The hover handlers are re-subscribed defensively (a -= before every +=) because
        /// the HOSTS are not new, and a render that added a second handler to each would fade the
        /// pencil twice per hover for the rest of the session.
        /// </summary>
        private void BuildDashboardPencils(Grid[] hosts)
        {
            _dashboardPencilByHost.Clear();
            if (hosts == null) return;

            try
            {
                for (int i = 0; i < hosts.Length; i++)
                {
                    var host = hosts[i];
                    if (host == null) continue;
                    int slot = i;

                    // An EMPTY cell has no card in it, so with a null Background it reports no
                    // hover at all and its pencil could never be reached - which is precisely the
                    // cell that most needs one. Transparent is hit-testable and paints nothing.
                    host.Background = Brushes.Transparent;

                    var pencil = new Button
                    {
                        Width = PencilSize,
                        Height = PencilSize,
                        Opacity = 0,
                        Focusable = false,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        // Clear of the card's own top-right "?" (8px inside a 6px-margined card),
                        // so the two affordances never sit on each other.
                        Margin = new Thickness(0, 1, 1, 0),
                        Template = _pencilTemplate ??= BuildPencilTemplate(),
                        ToolTip = Loc.Get("dash_pencil_tip"),
                    };
                    Panel.SetZIndex(pencil, 20);
                    pencil.Click += (_, _) => OpenDashboardPicker(slot);

                    host.Children.Add(pencil);
                    _dashboardPencilByHost[host] = pencil;

                    host.MouseEnter -= OnDashboardHostMouseEnter;
                    host.MouseLeave -= OnDashboardHostMouseLeave;
                    host.MouseEnter += OnDashboardHostMouseEnter;
                    host.MouseLeave += OnDashboardHostMouseLeave;
                }

                ApplyDashboardEditLock(IsSessionFeatureLockActive);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BuildDashboardPencils failed"); }
        }

        /// <summary>
        /// The pencil's face: a round, dark, hairline-bordered chip with a pencil glyph in it.
        /// Built in code rather than authored in the theme because the hosts are empty cells by
        /// contract (DashboardSlotMapTests pins that), so everything inside one is the renderer's.
        /// </summary>
        private static ControlTemplate BuildPencilTemplate()
        {
            var chip = new FrameworkElementFactory(typeof(Border));
            chip.SetValue(Border.BackgroundProperty, Frozen("#D9120A1E"));
            chip.SetValue(Border.BorderBrushProperty, Frozen("#66FFFFFF"));
            chip.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(PencilSize / 2));

            // EmojiTextBlock, not TextBlock: WPF cannot draw a COLR/CPAL colour-emoji font, and
            // this is the app-wide route that turns one into an inline image instead.
            var glyph = new FrameworkElementFactory(typeof(EmojiTextBlock));
            glyph.SetValue(TextBlock.TextProperty, "✏️");
            glyph.SetValue(TextBlock.FontSizeProperty, 10.0);
            glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chip.AppendChild(glyph);

            return new ControlTemplate(typeof(Button)) { VisualTree = chip };

            static Brush Frozen(string hex)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
        }

        private void OnDashboardHostMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
            => FadeDashboardPencil(sender as Grid, true);

        private void OnDashboardHostMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
            => FadeDashboardPencil(sender as Grid, false);

        /// <summary>
        /// The reveal. BeginAnimation on the element, never a Storyboard: this namescope does not
        /// support them (SettingsTabView.xaml:1038-1039), which is why every animation on this
        /// wall is code-driven. Reduced motion cuts instead of fading, the same shape as
        /// <c>FeatureCard.ApplyHover</c>'s rim-light.
        /// </summary>
        private void FadeDashboardPencil(Grid? host, bool show)
        {
            try
            {
                if (host == null || !_dashboardPencilByHost.TryGetValue(host, out var pencil)) return;

                // A hidden pencil is hidden: a session is running and this is not the moment.
                if (pencil.Visibility != Visibility.Visible) return;

                double to = show ? PencilShownOpacity : 0;
                if (!MotionFx.AllowTransitions)
                {
                    pencil.BeginAnimation(UIElement.OpacityProperty, null);
                    pencil.Opacity = to;
                    return;
                }

                pencil.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(PencilFadeMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    });
            }
            catch (Exception ex) { App.Logger?.Debug("FadeDashboardPencil: {E}", ex.Message); }
        }

        /// <summary>
        /// Hides the pencils (and shuts any open picker) while a session owns the dose. Hidden,
        /// not disabled: the ribbon over the mosaic is already explaining the situation, and a
        /// greyed pencil on every one of nine tiles would say it nine more times.
        ///
        /// <para>Called from <c>RefreshSessionFeatureLock</c>, so it rides the one derive-never-latch
        /// path the whole lock uses - including the per-second heartbeat, which is what closes a
        /// picker left open when a session starts from somewhere else.</para>
        /// </summary>
        private void ApplyDashboardEditLock(bool locked)
        {
            var show = DashboardPickerRule.ShowPencil(locked);

            foreach (var pencil in _dashboardPencilByHost.Values)
            {
                pencil.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) continue;
                pencil.BeginAnimation(UIElement.OpacityProperty, null);
                pencil.Opacity = 0;
            }

            if (!show) CloseDashboardPicker();
        }

        // ---- the two seams -------------------------------------------------------------

        /// <summary>
        /// PHASE F SEAM 1. Opens a picker for one slot. Everything about WHICH picker that is
        /// lives here and nowhere else, so swapping the flat one for the rolodex is a change to
        /// this method's body and to nothing downstream of it.
        ///
        /// <para>The picker is an overlay in the grid's own tree rather than a Popup. A Popup
        /// gets its own top-level HWND, which does not inherit the root Viewbox's scale, so the
        /// panel would line up with the wall at exactly one window size. It is also the rect the
        /// rolodex has to occupy, which is the other half of why it is here and not floating.</para>
        /// </summary>
        internal void OpenDashboardPicker(int slot)
        {
            if (RefuseActionIfSessionLocked("dashboard:edit")) return;
            if (slot < 0 || slot >= DashboardLayout.SlotCount) return;

            try
            {
                var grid = SettingsTab?.VelvetFeatureGrid;
                if (grid == null) return;

                // Built fresh every time rather than hidden and shown: entitlement, mod art and
                // mod names can all have moved, and a stale shelf promises a tile the wall will
                // not paint.
                CloseDashboardPicker();

                var picker = new DashboardPickerPopup { Margin = new Thickness(6) };
                Grid.SetRow(picker, 0);
                Grid.SetRowSpan(picker, 4);
                Grid.SetColumn(picker, 0);
                Grid.SetColumnSpan(picker, 4);
                // Over the program lock ribbon's 30, which is the highest thing on this grid.
                Panel.SetZIndex(picker, 40);

                picker.Picked += key => HandleDashboardPick(picker.Slot, key);
                picker.ResetRequested += ResetDashboardLayout;
                picker.CloseRequested += CloseDashboardPicker;

                grid.Children.Add(picker);
                _dashboardPicker = picker;
                picker.ShowFor(slot);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "OpenDashboardPicker failed for slot {Slot}", slot); }
        }

        /// <summary>Closes whatever picker is open. A no-op when none is.</summary>
        internal void CloseDashboardPicker()
        {
            try
            {
                var picker = _dashboardPicker;
                _dashboardPicker = null;
                if (picker == null) return;
                (picker.Parent as Panel)?.Children.Remove(picker);
            }
            catch (Exception ex) { App.Logger?.Debug("CloseDashboardPicker: {E}", ex.Message); }
        }

        /// <summary>
        /// PHASE F SEAM 2. Takes a picked key for a slot and runs the one accepted-edit path:
        /// decide what to ask, ask it if anything needs asking, then commit. A picker that reaches
        /// straight for <see cref="DashboardLayoutRule.Place"/> instead of this is a picker with
        /// its own idea of what a split is.
        /// </summary>
        internal void HandleDashboardPick(int slot, string key)
        {
            if (RefuseActionIfSessionLocked("dashboard:edit")) return;

            var prompt = DashboardPickerRule.Decide(CurrentLayout, slot, key);
            if (prompt is PickPrompt.Place or PickPrompt.Move)
            {
                CommitDashboardPick(slot, key, split: false);
                return;
            }

            AskDashboardPickChoice(slot, key, offerSplit: prompt == PickPrompt.AskReplaceOrSplit);
        }

        /// <summary>
        /// Raises the Replace / Split / Cancel ask, and commits whatever comes back. Cancel
        /// answers nothing at all, which is correct: never mind is not an edit.
        /// </summary>
        private void AskDashboardPickChoice(int slot, string key, bool offerSplit)
        {
            var picker = _dashboardPicker;
            if (picker == null)
            {
                // No picker on screen means no room to ask - a host that raised a pick from its
                // own chrome. A plain replace is what the question would have defaulted to.
                CommitDashboardPick(slot, key, split: false);
                return;
            }

            picker.Ask(offerSplit, split => CommitDashboardPick(slot, key, split));
        }

        // ---- the accepted edit ---------------------------------------------------------

        /// <summary>
        /// The one place the layout changes. Mutate, re-render, save, nudge - in that order,
        /// because the render is what proves the mutation was legal and the save must not write a
        /// layout the wall refused to paint.
        /// </summary>
        internal void CommitDashboardPick(int slot, string key, bool split)
        {
            try
            {
                var outcome = DashboardLayoutRule.Place(CurrentLayout, slot, key, split);
                if (outcome is PlaceOutcome.RefusedUnknownKey or PlaceOutcome.RefusedNotSplittable)
                {
                    App.Logger?.Debug("[Dashboard] Pick refused for slot {Slot}: {Outcome}", slot, outcome);
                    return;
                }

                RenderDashboardSlots(CurrentLayout);
                SaveDashboardLayout(DashboardPickerRule.Commit(CurrentLayout));
                App.Logger?.Information("[Dashboard] Slot {Slot} changed ({Outcome})", slot, outcome);

                // One ask per pencil. The pencil was for THIS cell, and it has been answered.
                CloseDashboardPicker();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "CommitDashboardPick failed for slot {Slot}", slot); }
        }

        /// <summary>
        /// Back to the shipped wall. Still marked touched afterwards: touched records that the
        /// user has had an opinion, not that their wall differs from the default, and the
        /// fill-if-empty cloud adopt reads it - so clearing it here would let another machine's
        /// layout land on someone who just asked for this one.
        /// </summary>
        internal void ResetDashboardLayout()
        {
            if (RefuseActionIfSessionLocked("dashboard:edit")) return;
            try
            {
                RenderDashboardSlots(DashboardLayout.Default());
                SaveDashboardLayout(DashboardPickerRule.Reset());
                CloseDashboardPicker();
                App.Logger?.Information("[Dashboard] Layout reset to default");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "ResetDashboardLayout failed"); }
        }

        // ---- persistence ---------------------------------------------------------------

        /// <summary>
        /// Writes the wire to settings and asks for a sync soon. <c>Save()</c> debounces 500ms and
        /// <c>NudgeSyncSoon</c> coalesces, so nine edits in a row cost one write and one sync.
        /// Phase D is what makes that sync carry the field; until then the string still reaches the
        /// cloud inside the settings backup, which is why it is not on the excluded list.
        /// </summary>
        private static void SaveDashboardLayout((string Wire, bool Touched) state)
        {
            try
            {
                var current = App.Settings?.Current;
                if (current == null) return;

                current.DashboardLayoutWire = state.Wire;
                current.DashboardLayoutTouched = state.Touched;
                App.Settings?.Save();
                App.ProfileSync?.NudgeSyncSoon("dashboard-layout");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SaveDashboardLayout failed"); }
        }

        /// <summary>
        /// On every show of the Home tab: if the settings wire no longer describes the wall on
        /// screen, the wall is stale and the settings win. Nothing in THIS phase can make that
        /// true - an edit writes both ends - but Phase D's cloud adopt rewrites the setting from a
        /// background response, and a restore-from-cloud rewrites the whole settings object. Both
        /// land here rather than needing to know the renderer exists.
        /// </summary>
        internal void SyncDashboardLayoutFromSettings()
        {
            try
            {
                EnsureDashboardSlotsRendered();

                var wire = App.Settings?.Current?.DashboardLayoutWire;
                if (string.IsNullOrWhiteSpace(wire)) return;   // never edited: the wall IS the default

                // Both sides normalized through the same parser, so casing, spacing and a key the
                // catalog has since dropped do not read as a change.
                var wanted = DashboardLayoutRule.FromWire(wire);
                if (string.Equals(DashboardLayoutRule.ToWire(wanted),
                                  DashboardLayoutRule.ToWire(CurrentLayout), StringComparison.OrdinalIgnoreCase))
                    return;

                App.Logger?.Information("[Dashboard] Re-rendering the wall from settings");
                RenderDashboardSlots(wanted);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SyncDashboardLayoutFromSettings failed"); }
        }
    }
}
