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
                        // An invisible pencil is not a target: see DashboardPickerRule
                        // .PencilHitTestable. Turned on by the fade-in and off again by the
                        // fade-out, so the corner belongs to the card whenever nothing is drawn
                        // there - which is what gives that corner its right-click back.
                        IsHitTestVisible = false,
                        Focusable = false,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        // BOTTOM-RIGHT. Both top corners are already claimed INSIDE the card, at
                        // an 8px inset on a 6px-margined, 1px-bordered border - so 15px in from
                        // the host's own corner: the "?" (BtnHelp) top-right, the tier badge
                        // (TierBadgeHost) top-left. A 22px chip at a 1px inset reaches 23px in, so
                        // it overlaps either by about 8x8px, and over BtnHelp it also wins the
                        // hit-test and the "?" stops opening. The bottom-right corner is clear:
                        // the only thing under it is the lockband, a 15px scrim that is
                        // IsHitTestVisible=False and carries its padlock dead centre, and the
                        // title above it is lifted by the band's own height while it is up.
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 1, 1),
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

                // Hit-testing follows the paint, in both directions and immediately: on the way in
                // so the pencil is clickable from the first frame of the fade rather than 140ms
                // later, and on the way out so the corner hands its clicks - and its right-click -
                // straight back to the tile instead of at the end of the fade.
                pencil.IsHitTestVisible = DashboardPickerRule.PencilHitTestable(IsSessionFeatureLockActive, show);

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

                // Either way the pencil goes back to being invisible AND untouchable until the
                // pointer next enters the cell: unlocking shows the pencils again but does not
                // fade any of them in, and a shown-but-transparent Button that still answered the
                // hit-test would be exactly the bug PencilHitTestable exists to close.
                pencil.IsHitTestVisible = DashboardPickerRule.PencilHitTestable(locked, pointerInCell: false);

                if (show) continue;
                pencil.BeginAnimation(UIElement.OpacityProperty, null);
                pencil.Opacity = 0;
            }

            if (show) return;
            CloseDashboardPicker();
            // Both pickers answer to the lock. The rolodex is a native HWND, so one left up over a
            // session's ribbon would be a rectangle the ribbon cannot paint through.
            if (_rolodex != null) CloseRolodex();
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

            // PHASE F. The rolodex first; the flat shelf when it cannot run. One session-wide
            // give-up flag decides that (RolodexAvailability), so a machine with no WebView2
            // runtime probes once and opens the shelf nine times out of nine after it.
            if (TryOpenRolodexPicker(slot)) return;
            OpenFlatDashboardPicker(slot);
        }

        /// <summary>
        /// The flat shelf: four ring groups of art tiles laid over the wall. Still the picker on
        /// every machine the rolodex will not run on, and still the one a keyboard can drive, so it
        /// is never deleted and never allowed to rot.
        /// </summary>
        private void OpenFlatDashboardPicker(int slot)
        {
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
            catch (Exception ex) { App.Logger?.Warning(ex, "OpenFlatDashboardPicker failed for slot {Slot}", slot); }
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
                // The rolodex raised this pick and its HWND is already gone (nothing may be drawn
                // over one, which is why it goes first), so there is no chrome left to ask in.
                // Bring the flat shelf up on the same slot and ask there rather than defaulting
                // the question away: Split is a real answer, and the 3D picker must not be the one
                // that cannot reach it.
                OpenFlatDashboardPicker(slot);
                picker = _dashboardPicker;
            }

            if (picker == null)
            {
                // No shelf either. A plain replace is what the question would have defaulted to.
                CommitDashboardPick(slot, key, split: false);
                return;
            }

            picker.Ask(offerSplit, split => CommitDashboardPick(slot, key, split));
        }

        // ---- the accepted edit ---------------------------------------------------------

        /// <summary>
        /// The one place the layout changes. Mutate, SAVE, then re-render.
        ///
        /// <para>Save before paint, not after. <c>RenderDashboardSlots</c> swallows its own
        /// exceptions, so a render never proved anything about the mutation; what it can do is
        /// repaint the wall around an edit the settings never took, which is a phantom the user
        /// loses on the next launch with no idea why. So the write is attempted first, and a
        /// write that cannot land rolls the layout back to the wire it had and paints nothing.</para>
        /// </summary>
        internal void CommitDashboardPick(int slot, string key, bool split)
        {
            try
            {
                // Taken BEFORE Place, which mutates in place and has no undo of its own.
                var before = DashboardLayoutRule.ToWire(CurrentLayout);

                var outcome = DashboardLayoutRule.Place(CurrentLayout, slot, key, split);
                if (outcome is PlaceOutcome.RefusedUnknownKey or PlaceOutcome.RefusedNotSplittable)
                {
                    App.Logger?.Debug("[Dashboard] Pick refused for slot {Slot}: {Outcome}", slot, outcome);
                    return;
                }

                if (!DashboardPickerRule.ShouldCommit(outcome))
                {
                    // Unchanged: the tile the user picked is the tile that was already there.
                    // Nothing rendered, nothing written, and above all nothing marked Touched -
                    // that latch is permanent and the cloud's fill-if-empty adopt reads it. The
                    // pencil has still been answered, so the picker closes.
                    App.Logger?.Debug("[Dashboard] Slot {Slot} already held {Key}; not an edit", slot, key);
                    CloseDashboardPicker();
                    return;
                }

                if (!SaveDashboardLayout(DashboardPickerRule.Commit(CurrentLayout)))
                {
                    _dashboardLayout = DashboardLayoutRule.FromWire(before);
                    App.Logger?.Warning("[Dashboard] Slot {Slot} edit rolled back: settings unavailable", slot);
                    CloseDashboardPicker();
                    return;
                }

                RenderDashboardSlots(CurrentLayout);
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
                // Settings first, the same order an accepted pick uses: a reset the app cannot
                // remember must not repaint the wall out from under the layout it will load back.
                if (!SaveDashboardLayout(DashboardPickerRule.Reset()))
                {
                    App.Logger?.Warning("[Dashboard] Reset not written; the wall is left as it was");
                    CloseDashboardPicker();
                    return;
                }

                RenderDashboardSlots(DashboardLayout.Default());
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
        ///
        /// <para>Answers whether the edit was actually recorded. False means there was nowhere to
        /// put it - no settings object yet, or the write threw - and the caller is the one that
        /// has to decide what to do about a layout it has already mutated.</para>
        /// </summary>
        private static bool SaveDashboardLayout((string Wire, bool Touched) state)
        {
            try
            {
                var settings = App.Settings;
                var current = settings?.Current;
                if (settings == null || current == null) return false;

                current.DashboardLayoutWire = state.Wire;
                current.DashboardLayoutTouched = state.Touched;
                settings.Save();
                App.ProfileSync?.NudgeSyncSoon("dashboard-layout");
                return true;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SaveDashboardLayout failed"); return false; }
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
