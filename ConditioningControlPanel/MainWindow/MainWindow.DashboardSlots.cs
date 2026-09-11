using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dashboard;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE HOME WALL'S NINE MOVABLE CELLS, built from the layout rather than from XAML.
    ///
    /// <para>Every tile the user can move is created here, from one <see cref="FeatureCatalog"/>
    /// row: its title, its art, its tier livery and both of its gestures. The four places that
    /// used to address those tiles by x:Name (art in <c>LoadFeatureImages</c>, titles in
    /// <c>ApplyModFeatureNames</c>, rings in <c>RefreshWallActiveStates</c>, motion in
    /// <c>MainWindow.DashboardFx</c>) now call the four Refresh methods below, so a slot's
    /// contents can change without any of them learning a new name.</para>
    ///
    /// <para>Phase B renders the default layout only - there is no edit UI yet - so the wall this
    /// paints is the wall that shipped, tile for tile. The public seam Phase C needs is
    /// <see cref="RenderDashboardSlots"/>: change <see cref="CurrentLayout"/>, call it, done.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- state ---------------------------------------------------------------------

        private DashboardLayout? _dashboardLayout;
        private bool _dashboardSlotsRendered;

        private readonly List<FeatureCard> _dashboardSingles = new();
        private readonly List<SplitFeatureCard> _dashboardSplits = new();

        /// <summary>Which catalog row each live card is showing. Rebuilt on every render, because
        /// every card is a new object after one.</summary>
        private readonly Dictionary<FeatureCard, DashboardFeature> _dashboardCardRows = new();
        private readonly Dictionary<SplitFeatureCard, (DashboardFeature A, DashboardFeature B)> _dashboardSplitRows = new();

        /// <summary>
        /// The English literal a mod's text replacements are matched against. It IS the lookup key
        /// (<c>ModService.FeatureLabelTwins</c> says so out loud), so these are the same strings the
        /// XAML tiles carried and <c>ApplyModFeatureNames</c> passed - losing one would silently
        /// lose a mod's rename of that feature. Brand names are absent on purpose: they are never
        /// localized and never renamed (<c>MainWindow.PlayTab.cs:79-80</c>).
        /// </summary>
        private static readonly Dictionary<string, string> DashboardEnglishTitles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flash"] = "Flash Images", ["video"] = "Mandatory Video", ["subliminal"] = "Subliminals",
            ["bouncingtext"] = "Bouncing Text", ["bubblecount"] = "Bubble Count", ["bubbles"] = "Bubble Pop",
            ["spiral"] = "Spiral Overlay", ["pinkfilter"] = "Pink Filter", ["mindwipe"] = "Mind Wipe",
            ["braindrain"] = "Brain Drain", ["lockcard"] = "Lock Card", ["gradedintake"] = "Graded Intake",
            ["fyp"] = "For You", ["blinktrainer"] = "Blink Trainer", ["remotecontrol"] = "Remote Control",
            ["bambitakeover"] = "Takeover", ["shelistening"] = "She's Listening", ["haptics"] = "Haptics",
            ["awareness"] = "Awareness", ["lockdown"] = "Lockdown Mode", ["justdrop"] = "Just Drop",
            ["gaze"] = "Gaze Minigame", ["focusgaze"] = "Focus Gaze",
        };

        /// <summary>The five tiles that shipped a "?" help popover. Absent = no icon, which is what
        /// <see cref="FeatureCard.HelpSectionId"/> null already means.</summary>
        private static readonly Dictionary<string, string> DashboardHelpSections = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flash"] = "FlashImages", ["subliminal"] = "Subliminals", ["bouncingtext"] = "BouncingText",
            ["bubbles"] = "BubblePop", ["lockcard"] = "LockCard",
        };

        // ---- the layout ----------------------------------------------------------------

        /// <summary>
        /// The wall as it stands, sanitized. Read from settings the first time it is asked for;
        /// an unreadable or empty string answers with the shipped wall rather than nine holes.
        /// </summary>
        internal DashboardLayout CurrentLayout =>
            _dashboardLayout ??= DashboardLayoutRule.Sanitize(
                DashboardLayoutRule.FromWire(App.Settings?.Current?.DashboardLayoutWire));

        /// <summary>Renders once. Cheap enough to call from every entry point that might be first.</summary>
        internal void EnsureDashboardSlotsRendered()
        {
            if (_dashboardSlotsRendered) return;
            RenderDashboardSlots(CurrentLayout);
        }

        /// <summary>The single card holding <paramref name="key"/>, or null when it is not on the
        /// wall (the user removed it) or is half of a split tile.</summary>
        internal FeatureCard? DashboardCardFor(string key)
            => _dashboardCardRows.FirstOrDefault(p => string.Equals(p.Value.Key, key, StringComparison.OrdinalIgnoreCase)).Key;

        /// <summary>The nine live single tiles. Fed to the FX clocks; see the fixed cells in
        /// <c>MainWindow.DashboardFx.DashboardFeatureCards</c>.</summary>
        internal IEnumerable<FeatureCard> DashboardSingleCards => _dashboardSingles;

        /// <summary>The live split tiles - however many the layout currently has.</summary>
        internal IEnumerable<SplitFeatureCard> DashboardSplitCards => _dashboardSplits;

        // ---- the render ----------------------------------------------------------------

        /// <summary>
        /// Rebuilds all nine cells from <paramref name="layout"/>. Every card is a NEW object
        /// afterwards, which is why this re-runs the four refreshes and the FX loops itself:
        /// anything holding a card from before this call is holding a card nobody can see.
        /// </summary>
        internal void RenderDashboardSlots(DashboardLayout layout)
        {
            var tab = SettingsTab;
            if (tab == null) return;

            try
            {
                _dashboardLayout = layout ?? DashboardLayout.Default();

                _dashboardSingles.Clear();
                _dashboardSplits.Clear();
                _dashboardCardRows.Clear();
                _dashboardSplitRows.Clear();

                // The pencils go with the cards: the loop below empties every host. Cleared HERE
                // rather than only in BuildDashboardPencils, which is the last thing this method
                // does - a render that throws in the middle would never reach it, and the map
                // would keep nine ghosts that ApplyDashboardEditLock and the hover fade would
                // then go on addressing for the rest of the session.
                _dashboardPencilByHost.Clear();

                var hosts = new[] { tab.Slot0, tab.Slot1, tab.Slot2, tab.Slot3, tab.Slot4,
                                    tab.Slot5, tab.Slot6, tab.Slot7, tab.Slot8 };

                for (int i = 0; i < DashboardLayout.SlotCount && i < hosts.Length; i++)
                {
                    var host = hosts[i];
                    if (host == null) continue;
                    host.Children.Clear();

                    var slot = _dashboardLayout.Slots[i];
                    var primary = FeatureCatalog.Find(slot.Primary);
                    if (primary == null) continue;   // an empty slot renders as a hole, by design

                    var secondary = FeatureCatalog.Find(slot.Secondary);
                    host.Children.Add(secondary == null
                        ? BuildDashboardCard(primary)
                        : BuildDashboardSplit(primary, secondary));
                }

                // Set BEFORE the refreshes: each one ensures a render, and this is the base case.
                _dashboardSlotsRendered = true;

                RefreshDashboardArt();
                RefreshDashboardTitles();
                RefreshDashboardActiveStates();
                RefreshDashboardLivery();

                // The clocks hold card references, and every one of them just went stale.
                ApplyDashboardFxLoops();

                // ... and so did the nine pencils, which sit in the hosts the cards just moved
                // into. Rebuilt last so they end up on top of whatever the slot now holds.
                BuildDashboardPencils(hosts);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "RenderDashboardSlots failed"); }
        }

        /// <summary>
        /// One tile. The gestures are the wall's grammar verbatim: an FX tile opens its Studio
        /// module on the left and toggles the feature on the right; a destination navigates on the
        /// left and ignores the right (right-click is the rail's pin gesture, and a mosaic tile
        /// never pins).
        /// </summary>
        private FeatureCard BuildDashboardCard(DashboardFeature f)
        {
            var card = new FeatureCard
            {
                // Only an FX tile with a state to read has an "off" to be dim about; a
                // destination never receives IsActive. focusgaze has no rack key but it does have
                // a switch, so it dims off the settings flag its right-click flips.
                DimWhenInactive = f.Kind == DashboardKind.Fx && (f.RackKey != null || IsFocusGazeRow(f)),
                HelpSectionId = DashboardHelpSections.TryGetValue(f.Key, out var help) ? help : null,
            };

            card.Click += (_, _) => OpenDashboardFeature(f);
            if (f.Kind == DashboardKind.Fx) card.ToggleRequested += (_, _) => ToggleDashboardFeature(f);

            _dashboardSingles.Add(card);
            _dashboardCardRows[card] = f;
            return card;
        }

        /// <summary>
        /// A diagonal tile. A is the top-left triangle, exactly as the three authored combos were,
        /// and each half carries its own feature's two gestures - the same four wires
        /// <c>SettingsTabView.xaml.cs</c> carried for ComboVideoBubble, ComboSpiralPink and
        /// ComboMindDrain.
        /// </summary>
        private SplitFeatureCard BuildDashboardSplit(DashboardFeature a, DashboardFeature b)
        {
            var card = new SplitFeatureCard();
            card.ClickA += (_, _) => OpenDashboardFeature(a);
            card.ClickB += (_, _) => OpenDashboardFeature(b);
            card.ToggleA += (_, _) => ToggleDashboardFeature(a);
            card.ToggleB += (_, _) => ToggleDashboardFeature(b);

            _dashboardSplits.Add(card);
            _dashboardSplitRows[card] = (a, b);
            return card;
        }

        // ---- the gestures --------------------------------------------------------------

        /// <summary>
        /// Left-click. FX opens the Studio module; a destination navigates to the one entry it
        /// already has, and the destination's own gate does the refusing - which is how every card
        /// in this app works. The five doors with no ShowTab key of their own are the switch below.
        /// </summary>
        private void OpenDashboardFeature(DashboardFeature f)
        {
            try
            {
                if (f.Kind == DashboardKind.Fx)
                {
                    // focusgaze is FX-shaped without being a rack module: it has no Studio page to
                    // open, so the left click lands on the tab its checkbox lives on.
                    if (f.RackKey != null) OpenStudioModule(f.RackKey);
                    else ShowTab("lab");
                    return;
                }

                // Just Drop is still teased: the click owns both branches (teaser popup while the
                // door is withheld, navigation once it is not), so it is asked before TabKey.
                if (string.Equals(f.Key, "justdrop", StringComparison.OrdinalIgnoreCase))
                {
                    TeaseCardClicked();
                    return;
                }

                if (f.TabKey != null) { ShowTab(f.TabKey); return; }

                var e = new RoutedEventArgs();
                switch (f.Key)
                {
                    case "goon": BtnStartGoon_Click(this, e); break;
                    case "dtrh": BtnStartChaos_Click(this, e); break;
                    case "arcademy": BtnStartArcademy_Click(this, e); break;
                    case "gaze": BtnGazeMinigame_Click(this, e); break;
                    // Piece by Piece has no host service on this branch (the chess stack is on
                    // main). The Play door carries its card, so the tile navigates there until the
                    // 6.10 merge brings the launcher with it.
                    // TODO(6.10 merge): swap to PieceByPieceHostService.Launch()
                    case "piecebypiece": ShowTab("play"); break;
                    default: ShowTab("play"); break;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard slot open failed for {Key}", f.Key); }
        }

        /// <summary>
        /// Right-click. The rack key goes through <see cref="ToggleWallFeature"/> with its session
        /// refusal intact; focusgaze has no rack key, so its tile flips the Lab checkbox and lets
        /// <c>ChkFocusGaze_Changed</c> run - the Tier 2 gate, the consent dialog and the webcam
        /// pre-warm all live in that handler and must not be bypassed.
        /// </summary>
        private void ToggleDashboardFeature(DashboardFeature f)
        {
            try
            {
                if (f.RackKey != null) { ToggleWallFeature(f.RackKey); return; }

                if (string.Equals(f.Key, "focusgaze", StringComparison.OrdinalIgnoreCase)
                    && PlayTab?.ChkPlayFocusGaze != null)
                {
                    if (RefuseIfSessionFeatureLocked($"card:{f.Key}")) return;
                    PlayTab.ChkPlayFocusGaze.IsChecked = PlayTab.ChkPlayFocusGaze.IsChecked != true;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Dashboard slot toggle failed for {Key}", f.Key); }
        }

        // ---- the four refreshes --------------------------------------------------------

        /// <summary>
        /// Titles. Mod-aware through the SAME <see cref="ModAwareLabel"/> path the tile block of
        /// <c>ApplyModFeatureNames</c> used, de-emoji'd the same way (the shared section keys carry
        /// a leading glyph and these cards draw their own art). Just Drop is skipped and then
        /// repainted by <see cref="ApplyTeaseCard"/>, which owns its title, badge and tooltip
        /// together - naming a feature nobody is allowed to name would defeat the tease.
        /// </summary>
        internal void RefreshDashboardTitles()
        {
            EnsureDashboardSlotsRendered();
            try
            {
                foreach (var (card, f) in _dashboardCardRows)
                {
                    if (string.Equals(f.Key, "justdrop", StringComparison.OrdinalIgnoreCase)) continue;
                    card.Title = DashboardTitle(f);
                }
                foreach (var (card, pair) in _dashboardSplitRows)
                {
                    card.TitleA = DashboardTitle(pair.A);
                    card.TitleB = DashboardTitle(pair.B);
                }
                ApplyTeaseCard();
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshDashboardTitles: {E}", ex.Message); }
        }

        /// <summary>
        /// Art. The catalog's paths ARE the mod override contract - never renamed, never dropped
        /// (mod contract rule 2) - and they resolve through the same chain every tile used, now at
        /// the tile decode cap rather than at full resolution.
        /// </summary>
        internal void RefreshDashboardArt()
        {
            EnsureDashboardSlotsRendered();
            try
            {
                foreach (var (card, f) in _dashboardCardRows) card.Icon = DashboardArt(f);
                foreach (var (card, pair) in _dashboardSplitRows)
                {
                    card.IconA = DashboardArt(pair.A);
                    card.IconB = DashboardArt(pair.B);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshDashboardArt: {E}", ex.Message); }
        }

        /// <summary>focusgaze is the one FX row whose state is not a wall flag.</summary>
        private static bool IsFocusGazeRow(DashboardFeature f)
            => string.Equals(f.Key, "focusgaze", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether a row's feature is on, or null for a row with no state to read.</summary>
        private static bool? DashboardRowActive(DashboardFeature f)
        {
            if (f.RackKey != null) return IsWallFeatureOn(f.RackKey);
            if (IsFocusGazeRow(f)) return App.Settings?.Current?.FocusGazeEnabled == true;
            return null;
        }

        /// <summary>Rings. A split tile lights per half; both halves on = the whole card glows.</summary>
        internal void RefreshDashboardActiveStates()
        {
            EnsureDashboardSlotsRendered();
            try
            {
                foreach (var (card, f) in _dashboardCardRows)
                    if (DashboardRowActive(f) is bool on) card.IsActive = on;
                foreach (var (card, pair) in _dashboardSplitRows)
                {
                    if (DashboardRowActive(pair.A) is bool a) card.IsActiveA = a;
                    if (DashboardRowActive(pair.B) is bool b) card.IsActiveB = b;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshDashboardActiveStates: {E}", ex.Message); }
        }

        /// <summary>
        /// Price tags. A tile the account cannot open wears the rail's own lock wording, the
        /// animated tier rim and a lockband along its bottom edge; an entitled one, or one whose
        /// feature is today's free rotation, wears nothing at all - absence is how this wall says
        /// "free", and the default layout is all free, so the shipped wall carries no tag but the
        /// tease's.
        ///
        /// <para>Deliberately NOT <c>TeaseTier</c>, which the plan's shorthand named: that
        /// property is the tease COSTUME (it blurs the art past recognition and hangs a glyph over
        /// it), and a locked door the user chose to put on their wall must still show what it is.
        /// <see cref="FeatureCard.LockedTier"/> is the same livery without the disguise, and the
        /// card keeps the two apart itself so no tile is ever wearing both.</para>
        /// </summary>
        internal void RefreshDashboardLivery()
        {
            EnsureDashboardSlotsRendered();
            try
            {
                foreach (var (card, f) in _dashboardCardRows)
                {
                    if (string.Equals(f.Key, "justdrop", StringComparison.OrdinalIgnoreCase)) continue;
                    if (f.Tier <= 0) { card.TierBadge = null; card.LockedTier = 0; continue; }

                    var allowed = IsDashboardFeatureEntitled(f);
                    SetTierBadge(card, allowed, Loc.Get(f.Tier >= 2 ? "hm3_rail_lock_t2" : "hm3_rail_lock_t1"));
                    card.LockedTier = allowed ? 0 : f.Tier;
                }

                // The tease owns its own badge, blur and title as one costume.
                ApplyTeaseCard();
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshDashboardLivery: {E}", ex.Message); }
        }

        /// <summary>
        /// May this account open <paramref name="f"/> right now? One reader for the wall and the
        /// picker both, so a tile and its entry in the picker can never disagree about the bar.
        /// Presentation only - <see cref="TierGate"/> is still the only thing that refuses, and
        /// it is asked again at the click.
        /// </summary>
        internal static bool IsDashboardFeatureEntitled(DashboardFeature f)
        {
            if (f == null || f.Tier <= 0) return true;
            var name = DashboardTitle(f);
            var verdict = f.Tier >= 2
                ? (f.DailyFreeKey != null ? TierGate.RequiresLab(name, f.DailyFreeKey) : TierGate.RequiresLab(name))
                : (f.DailyFreeKey != null ? TierGate.RequiresPremium(name, f.DailyFreeKey) : TierGate.RequiresPremium(name));
            return verdict.Allowed;
        }

        // ---- helpers -------------------------------------------------------------------

        internal static string DashboardTitle(DashboardFeature f)
        {
            if (f.TitleLocKey == null) return f.TitleLiteral ?? f.Key;
            var english = DashboardEnglishTitles.TryGetValue(f.Key, out var e) ? e : Loc.Get(f.TitleLocKey);
            return StripLeadingGlyph(ModAwareLabel(english, f.TitleLocKey));
        }

        internal static System.Windows.Media.ImageSource? DashboardArt(DashboardFeature f)
            => ModResourceResolver.ResolveImageDecoded(f.ArtPath, TileDecodeWidth)
               ?? ModResourceResolver.ResolveImage(f.ArtPath);
    }
}
