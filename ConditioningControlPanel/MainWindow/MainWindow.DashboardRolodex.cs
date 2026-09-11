using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Dashboard;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dashboard;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE ROLODEX OVER THE WALL: the 3D picker swapped in behind the Phase C seams, the cloud
    /// adopt's re-render, and the first-run "pick three" tour.
    ///
    /// <para>Nothing here knows a layout rule. <c>OpenDashboardPicker</c> chooses a picker and
    /// <c>HandleDashboardPick</c> takes the answer; both live in
    /// <c>MainWindow.DashboardEdit.cs</c> and both are unchanged in what they mean. What this file
    /// adds is the one thing a native HWND makes different from a WPF overlay: the wall underneath
    /// has to be PARKED, because a browser rectangle cannot be drawn over, faded or clipped, and
    /// the question that follows a pick has to wait until it is gone.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- state ---------------------------------------------------------------------

        private RolodexEmbedView? _rolodex;

        /// <summary>The slot the open rolodex is picking for. Meaningless in tour mode.</summary>
        private int _rolodexSlot;

        /// <summary>True while the open view is running the first-run tour rather than one edit.</summary>
        private bool _rolodexTour;

        /// <summary>True inside <see cref="TryOpenRolodexPicker"/>. The runtime probe is
        /// synchronous, so a machine with no WebView2 reports its failure while the opener is still
        /// on the stack - and the opener, not the failure handler, is the one that must fall back,
        /// or the flat picker opens twice.</summary>
        private bool _rolodexOpening;

        /// <summary>True while the wall under the rolodex is parked. Read by
        /// <c>ApplyDashboardFxLoops</c>, which is otherwise re-run by half a dozen callers that
        /// would happily restart the fog behind a browser.</summary>
        private bool _dashboardRolodexParked;

        /// <summary>What the program lock ribbon was doing before we took the grid.</summary>
        private Visibility _ribbonBeforeRolodex = Visibility.Collapsed;

        /// <summary>What the mosaic canvas was doing before we took the grid. It is Visible almost
        /// always and Collapsed on the machines that matter (Motion off, a low-end GPU), which is
        /// exactly why restoring it to a hard-coded Visible was wrong.</summary>
        private Visibility _mosaicBeforeRolodex = Visibility.Visible;

        /// <summary>The static event's handler, kept so the window can let go of it on the way
        /// out. A static event holding an instance delegate pins the window for the life of the
        /// process.</summary>
        private Action? _dashboardAdoptHandler;

        /// <summary>Latched once the first-run tour has been offered this launch, so walking back
        /// to Home does not offer it again while the first offer is still sitting in the Inbox.</summary>
        private bool _dashboardTourOffered;

        // ---- the swap ------------------------------------------------------------------

        /// <summary>
        /// Try to open the 3D picker for one slot. False means "not this session" - no runtime, a
        /// browser that would not start, or no wall to cover - and the caller opens the flat picker
        /// instead. The flat picker is never deleted and never becomes a fallback in name only: it
        /// is the keyboard path and the no-GPU path both.
        /// </summary>
        private bool TryOpenRolodexPicker(int slot)
        {
            if (RolodexAvailability.GivenUp) return false;

            var grid = SettingsTab?.VelvetFeatureGrid;
            if (grid == null) return false;

            try
            {
                CloseDashboardPicker();
                CloseRolodex();

                _rolodexOpening = true;
                try
                {
                    if (!ShowRolodex(grid, slot, tour: false)) return false;
                }
                finally { _rolodexOpening = false; }

                // The probe failed, or this open was aborted, while we were still on the stack:
                // the view is already gone, so say so and let the caller reach for the flat
                // picker. Asked of the VIEW rather than of the give-up flag, because an abort
                // closes this open without latching anything.
                return _rolodex != null;
            }
            catch (Exception ex)
            {
                _rolodexOpening = false;
                App.Logger?.Warning(ex, "[Rolodex] open failed for slot {Slot}", slot);
                CloseRolodex();
                return false;
            }
        }

        /// <summary>
        /// Build the view, park the wall, add it to the grid and post its one message. The rect is
        /// the flat picker's rect exactly - rows 0-3, columns 0-3 of <c>VelvetFeatureGrid</c>, over
        /// the lock ribbon's ZIndex 30 - because both pickers answer the same question about the
        /// same nine cells and neither is allowed to be somewhere else on screen.
        /// </summary>
        private bool ShowRolodex(Grid grid, int slot, bool tour)
        {
            var view = new RolodexEmbedView();
            Grid.SetRow(view, 0);
            Grid.SetRowSpan(view, 4);
            Grid.SetColumn(view, 0);
            Grid.SetColumnSpan(view, 4);
            Panel.SetZIndex(view, 40);

            view.Picked += OnRolodexPicked;
            view.TourDone += OnRolodexTourDone;
            view.CloseRequested += OnRolodexCloseRequested;
            view.InitFailed += OnRolodexInitFailed;
            view.OpenAborted += OnRolodexOpenAborted;

            _rolodex = view;
            _rolodexSlot = slot;
            _rolodexTour = tour;

            ParkWallForRolodex();
            grid.Children.Add(view);

            // Must be in the visual tree of a shown window before the browser is built
            // (BrowserVideoSurface.InitAsync:82 says the same thing out loud).
            view.Start();
            if (_rolodex == null) return false;   // the probe failed synchronously

            view.Post(BuildRolodexInit(tour ? "tour" : "edit", tour ? null : slot,
                                       tour ? RolodexBridgeRule.TourPickCount : 1));
            App.Logger?.Information("[Rolodex] open (mode={Mode}, slot={Slot})", tour ? "tour" : "edit", slot);
            return true;
        }

        /// <summary>
        /// One pick, and then the view goes. The order is the airspace rule made procedural: the
        /// HWND is torn down FIRST, and only then does the shared accepted-edit path run - because
        /// that path may raise the Replace / Split question, and a WPF dialog over a browser
        /// rectangle is a dialog nobody can see.
        ///
        /// <para>POSTED, not run here. This arrives on the browser's own message loop
        /// (<c>WebMessageReceived</c>), and the first thing the teardown does is dispose the
        /// WebView2 that is calling us - a reentrant dispose from inside a WebView2 callback. Both
        /// halves go into ONE posted callback so the order above survives the hop.</para>
        /// </summary>
        private void OnRolodexPicked(string key)
        {
            var slot = _rolodexSlot;
            PostAfterRolodexMessage(() =>
            {
                CloseRolodex(RolodexMessageKind.Pick);
                HandleDashboardPick(slot, key);
            });
        }

        /// <summary>Esc, the close button, or the page giving up on itself. Posted for the same
        /// reason a pick is, and it is the close that SPENDS a tour: skipping is an answer.</summary>
        private void OnRolodexCloseRequested()
            => PostAfterRolodexMessage(() => CloseRolodex(RolodexMessageKind.Close));

        /// <summary>
        /// Hop off the browser's message loop before tearing its host down. A throwing callback is
        /// logged rather than allowed to reach the dispatcher's unhandled handler, because by the
        /// time this runs there is nobody left on the stack who knows what it was for.
        /// </summary>
        private void PostAfterRolodexMessage(Action work)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { work(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[Rolodex] posted teardown failed"); }
                }));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Rolodex] posting the teardown failed"); }
        }

        private void OnRolodexInitFailed(string reason)
        {
            RolodexAvailability.GiveUp(reason);
            FallBackFromRolodex();
        }

        /// <summary>This open failed, the session did not. Same fallback, no latch - and the next
        /// pencil probes the browser again.</summary>
        private void OnRolodexOpenAborted(string reason)
        {
            App.Logger?.Debug("[Rolodex] open aborted: {Reason}", reason);
            FallBackFromRolodex();
        }

        private void FallBackFromRolodex()
        {
            var slot = _rolodexSlot;
            var tour = _rolodexTour;
            var opening = _rolodexOpening;

            // Unknown, not Close: a rolodex that never got going made no offer, so it spends
            // nothing. The next launch offers the tour again.
            CloseRolodex(RolodexMessageKind.Unknown);

            // The opener is still on the stack and will fall back itself. A tour has no flat
            // version at all - there is nothing to fall back TO - so it simply does not happen.
            if (opening || tour) { if (tour) _dashboardTourOffered = false; return; }
            OpenFlatDashboardPicker(slot);
        }

        /// <summary>Removes and disposes the view and gives the wall back. Idempotent - the page
        /// may send <c>close</c> after a <c>tourDone</c>, and a tab hide may arrive on top of
        /// both.</summary>
        internal void CloseRolodex() => CloseRolodex(RolodexMessageKind.Close);

        /// <summary>
        /// As above, plus what ENDED it - which is the whole of the tour's bookkeeping.
        ///
        /// <para>The tour is a once-ever offer, and every way out of it is an answer: Done, Esc,
        /// the close button, leaving the Home tab, a session starting underneath. All of them come
        /// through here, so all of them mark it shown, and the offer is not made again next launch
        /// to somebody who already said no. The one exception is a rolodex that never started
        /// (<see cref="RolodexMessageKind.Unknown"/>): an offer the app could not make is not an
        /// offer the user waved away. <see cref="RolodexBridgeRule.TourOutcomeFor"/> holds the
        /// rule, where it can be read without a browser.</para>
        /// </summary>
        private void CloseRolodex(RolodexMessageKind why)
        {
            try
            {
                var view = _rolodex;
                var tour = _rolodexTour;
                _rolodex = null;
                _rolodexTour = false;

                if (view != null)
                {
                    if (RolodexBridgeRule.TourOutcomeFor(tour ? "tour" : "edit", why)
                        == RolodexCloseOutcome.MarkTourShown)
                    {
                        MarkDashboardTourShown();
                    }

                    view.Picked -= OnRolodexPicked;
                    view.TourDone -= OnRolodexTourDone;
                    view.CloseRequested -= OnRolodexCloseRequested;
                    view.InitFailed -= OnRolodexInitFailed;
                    view.OpenAborted -= OnRolodexOpenAborted;
                    (view.Parent as Panel)?.Children.Remove(view);
                    view.Dispose();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] close: {E}", ex.Message); }
            finally { RestoreWallAfterRolodex(); }
        }

        // ---- parking -------------------------------------------------------------------

        /// <summary>
        /// Everything under the rectangle stops. The nine cells are COLLAPSED rather than merely
        /// covered, which parks each card's ambient clock through the gate it already has
        /// (<c>FeatureCard.AmbientAllowed</c> reads IsVisible) and takes the pencils down with
        /// them; the mosaic canvas is paused and hidden; the lock ribbon is collapsed and
        /// remembered. None of it is visible either way - the point is that none of it is DRAWING
        /// either, behind a browser that is.
        /// </summary>
        private void ParkWallForRolodex()
        {
            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                _dashboardRolodexParked = true;

                // EVERYTHING IS CAPTURED BEFORE ANYTHING IS TOUCHED, in a try of its own. Parking
                // can throw halfway - Pause() is the likely one - and a restore that then wrote a
                // default nobody chose would hand the user back a wall that is not the wall they
                // had. Reading two Visibility properties cannot throw in any way that matters, and
                // if it somehow does the fallbacks below are at least written down.
                try
                {
                    if (tab.MosaicFx != null) _mosaicBeforeRolodex = tab.MosaicFx.Visibility;
                    if (tab.ProgramFeatureLockRibbon != null)
                        _ribbonBeforeRolodex = tab.ProgramFeatureLockRibbon.Visibility;
                }
                catch (Exception ex) { App.Logger?.Debug("[Rolodex] capturing the wall: {E}", ex.Message); }

                foreach (var host in DashboardSlotHosts())
                    if (host != null) host.Visibility = Visibility.Collapsed;

                if (tab.MosaicFx != null)
                {
                    try { tab.MosaicFx.Pause(); }
                    catch (Exception ex) { App.Logger?.Debug("[Rolodex] mosaic pause: {E}", ex.Message); }
                    tab.MosaicFx.Visibility = Visibility.Collapsed;
                }

                if (tab.ProgramFeatureLockRibbon != null)
                    tab.ProgramFeatureLockRibbon.Visibility = Visibility.Collapsed;

                ApplyDashboardFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Rolodex] parking the wall failed"); }
        }

        /// <summary>The other half, and it runs even when the close went wrong: a wall left
        /// collapsed is a Home tab with nothing on it.</summary>
        private void RestoreWallAfterRolodex()
        {
            if (!_dashboardRolodexParked) return;
            _dashboardRolodexParked = false;

            try
            {
                var tab = SettingsTab;
                if (tab == null) return;

                foreach (var host in DashboardSlotHosts())
                    if (host != null) host.Visibility = Visibility.Visible;

                // Both restored to what they WERE, never to a default: a mosaic the user turned
                // off does not come back on because a picker closed.
                if (tab.MosaicFx != null) tab.MosaicFx.Visibility = _mosaicBeforeRolodex;
                if (tab.ProgramFeatureLockRibbon != null)
                    tab.ProgramFeatureLockRibbon.Visibility = _ribbonBeforeRolodex;

                // Re-derived, never restored from a remembered value: a session may have started
                // while the picker was up, and the pencils answer to that and nothing else.
                ApplyDashboardEditLock(IsSessionFeatureLockActive);
                ApplyDashboardFxLoops();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Rolodex] restoring the wall failed"); }
        }

        /// <summary>The nine cells, in slot order. Null when the tab has not been built.</summary>
        private IEnumerable<Grid?> DashboardSlotHosts()
        {
            var tab = SettingsTab;
            if (tab == null) yield break;
            yield return tab.Slot0; yield return tab.Slot1; yield return tab.Slot2;
            yield return tab.Slot3; yield return tab.Slot4; yield return tab.Slot5;
            yield return tab.Slot6; yield return tab.Slot7; yield return tab.Slot8;
        }

        // ---- the init message ----------------------------------------------------------

        /// <summary>
        /// The whole scene, in one envelope. Titles and blurbs go up already localized and already
        /// mod-aware, through the SAME helper the tiles use, so the face a user picks and the tile
        /// they get are named the same thing.
        /// </summary>
        private Newtonsoft.Json.Linq.JObject BuildRolodexInit(string mode, int? slot, int picks)
            => RolodexInitBuilder.BuildInit(
                mode, slot, picks,
                // The bridge's own meaning of reduced, matching SpiralEmbedView: anything but Full
                // collapses the page's tweens to cuts.
                reducedMotion: MotionFx.Level != Models.MotionLevel.Full,
                lang: SafeLanguage(),
                title: DashboardTitle,
                blurb: f => Loc.Get(f.BlurbLocKey),
                entitled: IsDashboardFeatureEntitled,
                art: RolodexInitBuilder.ArtDataUri);

        private static string SafeLanguage()
        {
            try { return LocalizationManager.Instance.CurrentLanguage; }
            catch (Exception ex) { App.Logger?.Debug("[Rolodex] language: {E}", ex.Message); return "en"; }
        }

        // ---- the cloud adopt re-render -------------------------------------------------

        /// <summary>
        /// Subscribe once to the fill-if-empty adopt. It fires OFF the UI thread, from the middle
        /// of a sync response, so the handler does nothing but hop threads and re-read the setting
        /// the adopt just wrote - the wall is rendered from settings, never from an event payload.
        /// </summary>
        private void HookDashboardCloudAdopt()
        {
            if (_dashboardAdoptHandler != null) return;
            try
            {
                _dashboardAdoptHandler = OnDashboardLayoutAdopted;
                ProfileSyncService.DashboardLayoutAdopted += _dashboardAdoptHandler;
            }
            catch (Exception ex) { App.Logger?.Debug("[Dashboard] adopt hook: {E}", ex.Message); }
        }

        /// <summary>Let the static event go, so it cannot keep this window alive past its close.</summary>
        private void UnhookDashboardCloudAdopt()
        {
            try
            {
                if (_dashboardAdoptHandler == null) return;
                ProfileSyncService.DashboardLayoutAdopted -= _dashboardAdoptHandler;
                _dashboardAdoptHandler = null;
            }
            catch (Exception ex) { App.Logger?.Debug("[Dashboard] adopt unhook: {E}", ex.Message); }
        }

        private void OnDashboardLayoutAdopted()
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // A wall that has never been built has nothing to re-render; the next show
                        // of the tab reads the same setting and gets there first.
                        if (!_dashboardSlotsRendered) return;

                        var wire = App.Settings?.Current?.DashboardLayoutWire;
                        if (string.IsNullOrWhiteSpace(wire)) return;

                        App.Logger?.Information("[Dashboard] re-rendering the wall after a cloud adopt");
                        RenderDashboardSlots(DashboardLayoutRule.FromWire(wire));
                    }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[Dashboard] adopt re-render failed"); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("[Dashboard] adopt dispatch: {E}", ex.Message); }
        }

        // ---- the first-run tour --------------------------------------------------------

        /// <summary>
        /// Offer the tour, once ever, to a wall nobody has touched.
        ///
        /// <para>It goes through <see cref="Services.Startup.StartupPresenter.PresentOrInbox"/>,
        /// the app's one route for a passive first-launch surface. That is what keeps it off the
        /// short walk's toes: the presenter counts a running tutorial, a running session and the
        /// startup ladder as quiet, so on a busy first launch the tour becomes a row in the Inbox
        /// with its own copy and opens when the user asks for it. <c>EnqueueModal</c> was the
        /// alternative and is the wrong shape - it requires a call that BLOCKS until the surface
        /// is gone, and this one is an in-tab overlay with an async browser inside it.</para>
        ///
        /// <para>TODO(6.10 merge): move into the first-run queue. Main's first-run redesign
        /// (client #1036, "two screens, one walk, one queue") owns first launch there and this
        /// belongs in it as one more queue step; on this 6.9.5 base that queue does not exist.</para>
        /// </summary>
        private void MaybeOfferDashboardTour()
        {
            try
            {
                if (_dashboardTourOffered) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;
                if (settings.DashboardTourShown || settings.DashboardLayoutTouched) return;

                // No runtime, no tour. There is no flat version of this: the tour is an offer, and
                // an offer the app cannot keep is better never made.
                if (RolodexAvailability.GivenUp) return;

                // A session running means the window was re-shown mid-session, not launched.
                if (_sessionEngine?.IsRunning == true) return;
                if (IsSessionFeatureLockActive) return;

                _dashboardTourOffered = true;

                var presenter = App.StartupLadder;
                if (presenter == null) { StartDashboardTour(); return; }

                presenter.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "dashboard-tour",
                    Glyph = "🎛️",
                    Title = Loc.Get("dash_tour_title"),
                    Summary = Loc.Get("dash_tour_pick_three"),
                    Open = StartDashboardTour,
                    // Waved away without opening: still spent. The wall has a default and it is a
                    // good one; asking again next launch would make a one-time offer a nag.
                    Dismiss = MarkDashboardTourShown,
                });
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Dashboard] tour offer failed"); }
        }

        /// <summary>
        /// Opens the rolodex in tour mode over the Home wall.
        ///
        /// <para>THE WALK COMES FIRST. This is the Inbox row's <c>Open</c>, and an Inbox row can be
        /// clicked from any tab in the app - while the picker covers the Home grid and nothing
        /// else. Refusing when Home is not on screen made the row a dead click from everywhere but
        /// the one tab that did not need it, and <c>StartupPresenter.OpenItem</c> has already
        /// removed the row by then, so the offer was gone and nothing had happened. Walk to Home
        /// and then open, the way every other row in the Inbox takes the user where it lives.</para>
        ///
        /// <para>And if it still cannot open, the offer is handed BACK: the session latch comes off
        /// so a later visit to Home offers it again, and the once-ever flag is never spent by an
        /// attempt that showed the user nothing.</para>
        /// </summary>
        private void StartDashboardTour()
        {
            var opened = false;
            try
            {
                if (App.Settings?.Current?.DashboardTourShown == true) return;
                if (RolodexAvailability.GivenUp) return;
                if (IsSessionFeatureLockActive) return;

                // "settings" IS the Home tab (SettingsTabView, the nine-cell wall). Cheap when it
                // is already the one on screen: ShowTab re-shows the tab it is on.
                if (SettingsTab?.IsVisible != true) ShowTab("settings");

                var grid = SettingsTab?.VelvetFeatureGrid;
                if (grid == null || SettingsTab?.IsVisible != true) return;

                CloseDashboardPicker();
                CloseRolodex(RolodexMessageKind.Unknown);
                EnsureDashboardSlotsRendered();

                _rolodexOpening = true;
                try { opened = ShowRolodex(grid, 0, tour: true); }
                finally { _rolodexOpening = false; }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Dashboard] tour open failed"); }
            finally
            {
                // Nothing opened, so nothing was offered. DashboardTourShown is untouched, which
                // means the next launch asks again; dropping the session latch means this one can
                // too, the next time the Home tab comes up.
                if (!opened && App.Settings?.Current?.DashboardTourShown != true)
                    _dashboardTourOffered = false;
            }
        }

        /// <summary>
        /// Three picks, three cells. The keys go through <see cref="DashboardLayoutRule.Place"/>
        /// like any other edit, so one that was already on the shipped wall MOVES rather than
        /// appearing twice, and the result is written through the same pair the picker writes -
        /// touched, saved, nudged. A tour that ends with fewer than three picks fills what it has.
        /// </summary>
        private void OnRolodexTourDone(IReadOnlyList<string> keys)
        {
            // Posted off the browser's message loop for the same reason a pick is, and in one
            // callback so the order below survives the hop: the HWND goes first, exactly as it
            // does after a pick, because everything after it re-renders the wall and none of that
            // can be seen through a browser.
            PostAfterRolodexMessage(() =>
            {
                CloseRolodex(RolodexMessageKind.TourDone);
                CommitDashboardTour(keys);
            });
        }

        private void CommitDashboardTour(IReadOnlyList<string> keys)
        {
            try
            {
                MarkDashboardTourShown();

                var placements = RolodexBridgeRule.TourPlacements(keys);
                if (placements.Count == 0)
                {
                    App.Logger?.Information("[Dashboard] tour finished with nothing picked; the default wall stands");
                    return;
                }

                foreach (var (slot, key) in placements)
                    DashboardLayoutRule.Place(CurrentLayout, slot, key, split: false);

                RenderDashboardSlots(CurrentLayout);
                SaveDashboardLayout(DashboardPickerRule.Commit(CurrentLayout));
                App.Logger?.Information("[Dashboard] tour placed {Count} features", placements.Count);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Dashboard] tour commit failed"); }
        }

        /// <summary>Spent once, either way. Skipping is an answer.</summary>
        private static void MarkDashboardTourShown()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || settings.DashboardTourShown) return;
                settings.DashboardTourShown = true;
                App.Settings?.Save();
            }
            catch (Exception ex) { App.Logger?.Debug("[Dashboard] tour flag: {E}", ex.Message); }
        }
    }
}
