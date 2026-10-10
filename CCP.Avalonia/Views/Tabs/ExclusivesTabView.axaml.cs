using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/ExclusivesTabView.xaml.cs plus the
    /// view half of MainWindow.Exclusives.cs and MainWindow.Exclusives.Flair.cs. ONE view, two uses:
    ///
    /// <para><b>The Premium page</b> (Home > Premium, tab key "premium", PlansMode off): the full
    /// vault. Spotlight on top, then the shelf grouped Basic, Prime, Free (Core
    /// <see cref="PremiumShelfOrder"/>), open doors first in each group. Its flair (polish 12 round
    /// 2): the room's canvas runs VaultMotes off the cards and signs (gold off Basic, cyan diamonds
    /// off Prime), a shimmer crosses each group's tier sign, a card that is yours wears a breathing
    /// rim in its tier colour, a click throws a burst in the card's colour, and the cards rise in,
    /// staggered, as the page opens. Full = all of it; Reduced = a few slow motes, a slow shimmer,
    /// a short fade-in, no burst; Off = static (rims lit, no clocks).</para>
    ///
    /// <para><b>Account &amp; Plans</b> (Settings, PlansMode on): the header with the tier plates, a
    /// "See everything Premium gets you" link and the invites. The spotlight and the shelf are the
    /// Premium page's. The inner ScrollViewer is lifted out so a wheel notch reaches the Settings
    /// page's scroller.</para>
    ///
    /// <para>Dropped: <c>RoundClipOnResize</c> (an Avalonia Border clips its child to its own
    /// CornerRadius). The Ken Burns drift, the FREE TODAY pulse, the rim breath and the sign
    /// shimmer ride one BeatLoop (StartFlairLoop); the card glass sheen is CardSheenAdorner.</para>
    /// </summary>
    public partial class ExclusivesTabView : UserControl
    {
        private readonly AmbientFxCanvas _ambientFx;
        private bool _plansMode;
        private bool _contentLifted;
        private bool _motionOn;
        private bool _zonesQueued;
        private readonly List<DispatcherTimer> _entranceTimers = new();
        private readonly List<CardSheenAdorner> _cardSheens = new();
        private int _cardSheenRetries;

        /// <summary>Gold of the BASIC SUBJECT plate, cyan of PRIME's, lilac for the free doors.
        /// Commerce colours: they never follow the mod accent.</summary>
        internal static readonly Color VaultBasicGold = Color.FromRgb(0xFF, 0xC8, 0x5A);
        internal static readonly Color VaultPrimeCyan = Color.FromRgb(0x6F, 0xE8, 0xFF);
        internal static readonly Color VaultFreeLilac = Color.FromRgb(0xB7, 0x9C, 0xFF);

        /// <summary>Entrance stagger step and cap (Full), WPF Flair.cs.</summary>
        internal const int VaultEntranceStepMs = 38;
        internal const int VaultEntranceCap = 16;

        public ExclusivesTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _ambientFx = this.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;

            LoadBackdrop();
            RefreshVault();

            // The Premium page parks and resumes with its own visibility (the shell shows it as a
            // tab panel); Account & Plans is driven by AppSettingsTabView.SyncPlansMotion instead,
            // because Settings is one scrolling page and the copy reads "visible" on every section.
            AttachedToVisualTree += OnAttached;
            DetachedFromVisualTree += OnDetached;
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty || _plansMode) return;
                if (IsVisible) ShowPage(); else SetMotion(false);
            };

            var shelf = Find<ItemsControl>("ExclusivesShelf");
            shelf.SizeChanged += (_, e) => { if (e.WidthChanged) FitShelf(); QueueZones(); };
            Find<ScrollViewer>("ContentScroll").ScrollChanged += (_, _) => QueueZones();
            _ambientFx.SizeChanged += (_, _) => QueueZones();
        }

        // =====================================================================================
        //  the two views
        // =====================================================================================

        /// <summary>
        /// Account &amp; Plans mode (WPF nav rework, trimmed in polish 12): the header with the tier
        /// plates, the link to the Premium page and the invites; no spotlight, no shelf. Its own
        /// header words (no Vault or Exclusives names), rebound so a language switch repaints them.
        /// </summary>
        public bool PlansMode
        {
            get => _plansMode;
            set
            {
                _plansMode = value;
                Find<ItemsControl>("ExclusivesShelf").IsVisible = !value;
                Find<Border>("SpotlightCard").IsVisible = !value;
                Find<Button>("BtnSeePremium").IsVisible = value;
                Find<Border>("InvitesWrap").IsVisible = value;   // the panel shows itself once the server answers
                BindLoc(Find<TextBlock>("TxtVaultTitle"), value ? "plans_header_title" : "premium_page_title");
                BindLoc(Find<TextBlock>("TxtVaultSub"), value ? "plans_header_sub" : "premium_page_sub");
                // Settings sizes this view to its content: the painted room would otherwise ask
                // for the picture's own height.
                Find<Image>("VaultBackdrop").IsVisible = !value;
                if (value) LiftContentOutOfScroller();
                RefreshVault();
            }
        }

        private void LiftContentOutOfScroller()
        {
            if (_contentLifted) return;
            var scroll = Find<ScrollViewer>("ContentScroll");
            if (scroll.Parent is not Panel host || scroll.Content is not Control content) return;
            // A ScrollViewer takes the wheel even with nothing to scroll; the Settings page's own
            // scroller must get every notch.
            var index = host.Children.IndexOf(scroll);
            scroll.Content = null;
            host.Children.RemoveAt(index);
            host.Children.Insert(index, content);
            _contentLifted = true;
        }

        /// <summary>Rebind, never assign: a local Text would lose the live loc binding.</summary>
        private static void BindLoc(TextBlock target, string key) =>
            target.Bind(TextBlock.TextProperty, (Binding)new Localization.StrExtension(key).ProvideValue(null!));

        /// <summary>Account &amp; Plans' link to the full Premium page.</summary>
        private void SeePremium_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.ShowTab("premium");

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

        // =====================================================================================
        //  lifetime + motion
        // =====================================================================================

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged += OnModChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            if (!_plansMode && IsVisible) ShowPage();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            SetMotion(false);
        }

        /// <summary>ModChanged may be raised off the UI thread; marshal before touching the Image.</summary>
        private void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() => { LoadBackdrop(); RefreshVault(); });

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshVault);

        /// <summary>WPF ShowVaultPage: repaint, start the room, play the entrance.</summary>
        private void ShowPage()
        {
            RefreshVault();
            SetMotion(true);
            PlayEntrance();
        }

        /// <summary>WPF StartExclusivesMotion / StopVaultMotion for this view. The Premium page is
        /// the vault: the room plus its own motes at Full, only the motes (a few, slow) below that.
        /// Account &amp; Plans keeps the plain room.</summary>
        internal void SetMotion(bool on)
        {
            _motionOn = on;
            try
            {
                if (!on)
                {
                    _ambientFx.Stop();
                    StopFlair();
                    return;
                }
                var level = AmbientFxCanvas.Env.Level;
                var room = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash;
                _ambientFx.StartLayers(new AmbientFxConfig
                {
                    Layers = _plansMode ? room
                        : level == MotionLevel.Full ? room | AmbientFxLayers.VaultMotes
                        : AmbientFxLayers.VaultMotes,
                    Intensity = _plansMode ? 0.55 : 0.75,
                    FogPuffs = 3,
                });
                RefreshTierPlates();
                if (!_plansMode) StartFlair(level);
            }
            catch (Exception ex) { Serilog.Log.Debug("Vault motion: {E}", ex.Message); }
        }

        // =====================================================================================
        //  the vault (WPF EnsureExclusivesBuilt + RefreshVaultCore)
        // =====================================================================================

        /// <summary>WPF LoadBackdrop: null keeps what is already painted rather than blanking the room.</summary>
        private void LoadBackdrop()
        {
            var art = ModArt.TryLoad("exclusives/vault_backdrop.png");
            if (art != null) Find<Image>("VaultBackdrop").Source = art;
        }

        /// <summary>
        /// Repaints the header, the spotlight and the grouped shelf from
        /// <see cref="ExclusiveFeature.All"/> and each feature's live gate. Called on construction,
        /// on every show (MainShellWindow.OnTabShown, the Settings section seam), on a mod switch
        /// and on a language change.
        /// </summary>
        internal void RefreshVault()
        {
            try
            {
                PaintHero();
                RefreshTierPlates();
                if (_plansMode) return;

                // Every roster entry the build shows, grouped Basic, Prime, Free, open doors first.
                // Just Drop until the server opens its door and the Arcademy behind its build flag
                // are hidden, not veiled (ExclusiveFeature.Shown).
                var rows = ExclusiveFeature.All.Where(f => f.Shown()).Select(f => new ExclusiveCardRow(f)).ToList();
                var groups = PremiumShelfOrder.Arrange(rows, r => r.Feature.Key, r => r.Tier, r => r.IsMine);
                StopSheens();
                Find<ItemsControl>("ExclusivesShelf").ItemsSource =
                    groups.Select((g, i) => new ExclusiveShelfGroup(g.Group, g.Items, first: i == 0)).ToList();
                Dispatcher.UIThread.Post(() =>
                {
                    FitShelf();
                    if (_motionOn) StartFlair(AmbientFxCanvas.Env.Level);
                    QueueZones();
                }, DispatcherPriority.Background);

                PaintSpotlight();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Premium page repaint failed"); }
        }

        private void PaintHero()
        {
            // The header plate's vault art, mod-aware (WPF ModTileVariant("vault")): the mod's
            // override of features/vault.png wins inside ModArt.
            var hero = ModArt.TryLoad(CoreMods.ActiveModId == BuiltInMods.BambiSleepId
                           ? "features/vault_bambi.png" : "features/vault.png", 720)
                       ?? ModArt.TryLoad("features/vault.png", 720);
            Find<Image>("VaultHeroArt").Source = hero;
            Find<TextBlock>("TxtVaultHeroGlyph").IsVisible = hero == null;
        }

        private void PaintSpotlight()
        {
            var spot = new ExclusiveCardRow(ExclusiveFeature.All[0]);
            Find<TextBlock>("TxtSpotArtGlyph").Text = spot.Art == null ? spot.Feature.Emoji : "";
            var banner = ModArt.TryLoad(ArtName(spot.Feature.BannerArtResource), 1400);
            var spotArt = Find<Image>("SpotArtImage");
            spotArt.Source = banner ?? spot.Art;
            // WPF ApplySpotlightArt: banner art drifts from its centre; card art used as a fallback
            // drifts from the feature's focal point, which pushes the subject back into view.
            spotArt.RenderTransformOrigin = banner != null
                ? new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
                : new RelativePoint(spot.Feature.FocalX, spot.Feature.FocalY, RelativeUnit.Relative);
            Find<TextBlock>("TxtSpotTitle").Text = spot.Title;
            Find<TextBlock>("TxtSpotTagline").Text = spot.Tagline;
            Find<Border>("SpotBadge").IsVisible = spot.HasBadge;
            Find<TextBlock>("TxtSpotBadge").Text = spot.BadgeText;
            Find<Border>("SpotVeil").IsVisible = spot.IsLocked;
            Find<TextBlock>("TxtSpotFreeToday").Text = Loc.Get("mosaic_free_today");
            Find<Border>("SpotFreeToday").IsVisible = spot.HasFreePill;
            var badge = Find<TierBadge>("SpotTierBadge");
            badge.Tier = spot.Tier;
            badge.FreeToday = spot.BadgeFreeToday;
            var card = Find<Border>("SpotlightCard");
            card.Cursor = spot.Cursor;
            ToolTip.SetTip(card, spot.UnavailableTip);
            var open = Find<Button>("BtnSpotOpen");
            open.IsEnabled = spot.IsAvailable;
            ToolTip.SetTip(open, spot.UnavailableTip);
            ToolTip.SetShowOnDisabled(open, true);
        }

        private static Bitmap? _plate1, _plate2;
        private static bool _platesTried;

        /// <summary>WPF RefreshExclusiveTierPlates: the access properties, not the raw tier, light the
        /// plate (Prime full over a dim Basic, Basic full over a dim Prime, both dim when free).</summary>
        private void RefreshTierPlates()
        {
            if (!_platesTried)
            {
                _platesTried = true;
                _plate1 = ModArt.TryLoad("Patreon tier1.png", 200);
                _plate2 = ModArt.TryLoad("Patreon tier2.png", 200);
            }
            Paint("TierPlate1", _plate1);
            Paint("TierPlate2", _plate2);

            bool top = CoreEntitlement.HasLab, premium = CoreEntitlement.HasPremium;
            Find<Panel>("TierPlate1").Opacity = top ? 0.55 : premium ? 1.0 : 0.3;
            Find<Panel>("TierPlate2").Opacity = top ? 1.0 : 0.3;

            void Paint(string name, Bitmap? art)
            {
                var img = Find<Image>(name + "Art");
                img.Source = art;
                img.IsVisible = art != null;
                Find<Border>(name + "Vector").IsVisible = art == null;
            }
        }

        /// <summary>"Resources/features/x.png" -> "features/x.png", the name ModArt resolves.</summary>
        internal static string? ArtName(string? resource) =>
            resource?.StartsWith("Resources/", StringComparison.Ordinal) == true ? resource["Resources/".Length..] : resource;

        /// <summary>WPF FitExclusiveShelf: every card sized for the shelf's width; the Gap is each
        /// card's right/bottom margin. One fit for every group's wrap.</summary>
        internal void FitShelf()
        {
            var shelf = Find<ItemsControl>("ExclusivesShelf");
            if (shelf.Bounds.Width <= 0) return;
            var (_, w, h) = ExclusiveShelfFit.For(shelf.Bounds.Width);
            foreach (var wrap in shelf.GetVisualDescendants().OfType<WrapPanel>())
            {
                wrap.ItemWidth = w + ExclusiveShelfFit.Gap;
                wrap.ItemHeight = h + ExclusiveShelfFit.Gap;
            }
        }

        // =====================================================================================
        //  flair (WPF MainWindow.Exclusives.Flair.cs)
        // =====================================================================================

        private IEnumerable<T> Shelf<T>(string cls) where T : StyledElement =>
            Find<ItemsControl>("ExclusivesShelf").GetVisualDescendants().OfType<T>().Where(c => c.Classes.Contains(cls));

        private void StartFlair(MotionLevel level)
        {
            if (_plansMode) return;
            // Rims: lit at every level, breathing at Full only.
            foreach (var aura in Shelf<Border>("aura")) aura.Classes.Set("breathe", level == MotionLevel.Full);

            // Sign shimmer: a band crosses each tier sign, staggered by group.
            StopSheens();
            ApplyAmbientFlair();
            if (level == MotionLevel.Off) { QueueZones(); return; }
            int i = 0;
            double cross = level == MotionLevel.Full ? 1.1 : 2.2;
            double period = level == MotionLevel.Full ? 4.2 : 9.0;
            foreach (var band in Shelf<Rectangle>("vault-sheen"))
            {
                double span = Math.Max(120, (band.Parent as Control)?.Bounds.Width ?? 160) + 90;
                Canvas.SetLeft(band, -90);
                _flairBands.Add((band, span, 0.5 + 0.7 * i++));
            }
            _flairCross = cross;
            _flairPeriod = period;
            StartFlairLoop();
            QueueZones();
        }

        // ---- one beat for every loop on the page -------------------------------------------
        // The rim breath, the spotlight's Ken Burns drift, the FREE TODAY pulse and the sign
        // shimmer were four infinite Avalonia Animations; one of those anywhere makes the whole
        // window compose at 60 Hz (AGENTS.md "GPU CACHE + 60 Hz TRAP"). They ride one BeatLoop on
        // the window's shared 30 fps beat now, with WPF's periods and curves unchanged. The style
        // classes (breathe / kenburns / pulse) stay as the state the gate sets.
        private BeatLoop? _flairLoop;
        private readonly List<(Rectangle Band, double Span, double Delay)> _flairBands = new();
        private (Border Rim, double Still)[] _flairAuras = Array.Empty<(Border, double)>();
        private (Border Pill, ScaleTransform Scale)[] _flairPills = Array.Empty<(Border, ScaleTransform)>();
        private ScaleTransform? _flairSpotScale;
        private double _flairCross = 1.1, _flairPeriod = 4.2;

        /// <summary>Test seam: the page's one ambient loop is ticking.</summary>
        internal bool FlairLoopRunning => _flairLoop?.IsRunning == true;

        /// <summary>Test seam: one tick of the loop at <paramref name="t"/> seconds.</summary>
        internal void FlairStepForTests(double t) => FlairStep(t);

        private void StartFlairLoop()
        {
            _flairAuras = Shelf<Border>("aura").Where(a => a.Classes.Contains("breathe")).Select(a => (a, a.Opacity)).ToArray();
            _flairPills = Shelf<Border>("free-pill").Append(Find<Border>("SpotFreeToday")).Where(p => p.Classes.Contains("pulse"))
                .Select(p =>
                {
                    if (p.RenderTransform is not ScaleTransform sc) p.RenderTransform = sc = new ScaleTransform(1, 1);
                    return (p, sc);
                }).ToArray();
            var spot = Find<Image>("SpotArtImage");
            if (spot.Classes.Contains("kenburns"))
            {
                if (spot.RenderTransform is not ScaleTransform sc) spot.RenderTransform = sc = new ScaleTransform(1, 1);
                _flairSpotScale = sc;
            }
            else _flairSpotScale = null;
            if (_flairAuras.Length == 0 && _flairPills.Length == 0 && _flairSpotScale == null && _flairBands.Count == 0) return;
            _flairLoop ??= new BeatLoop(this, FlairStep);
            _flairLoop.Start();
        }

        private void FlairStep(double t)
        {
            try
            {
                // Rim: 0.5 -> 1 -> 0.5 of its own opacity over 2 x 2.8 s (WPF VaultCardAura).
                double rim = 0.5 + 0.5 * BeatLoop.Breath(t, 2.8);
                foreach (var (a, rest) in _flairAuras) a.Opacity = Math.Clamp(rim, 0, 1);
                // Spotlight: 1.00 -> 1.07 and back over 2 x 26 s.
                if (_flairSpotScale != null) _flairSpotScale.ScaleX = _flairSpotScale.ScaleY = 1 + 0.07 * BeatLoop.Breath(t, 26);
                // FREE TODAY: opacity 0.72 -> 1, scale 1 -> 1.06 over 2 x 1.9 s.
                double k = BeatLoop.Breath(t, 1.9);
                foreach (var (pill, sc) in _flairPills)
                {
                    pill.Opacity = Math.Clamp(0.72 + 0.28 * k, 0, 1);
                    sc.ScaleX = sc.ScaleY = 1 + 0.06 * k;
                }
                // Sign shimmer: a band crosses in `cross` seconds out of every `period`, sine
                // in-out over the period (as the Animation's one Easing did), staggered by group.
                foreach (var (band, span, delay) in _flairBands)
                {
                    double local = t - delay;
                    if (local < 0) { Canvas.SetLeft(band, -90); continue; }
                    double p = local % _flairPeriod / _flairPeriod;
                    double e = (1 - Math.Cos(Math.PI * p)) / 2;
                    double cue = _flairCross / _flairPeriod;
                    Canvas.SetLeft(band, e >= cue ? span : -90 + (span + 90) * (e / cue));
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("ExclusivesTabView.FlairStep: {E}", ex.Message); }
        }

        /// <summary>The OUT: the loop stops and everything it moved rests where the still page has it.</summary>
        private void StopFlairLoop()
        {
            _flairLoop?.Stop();
            foreach (var (a, rest) in _flairAuras) a.Opacity = rest;
            foreach (var (pill, sc) in _flairPills) { pill.Opacity = 1; sc.ScaleX = sc.ScaleY = 1; }
            if (_flairSpotScale != null) _flairSpotScale.ScaleX = _flairSpotScale.ScaleY = 1;
            foreach (var (band, _, _) in _flairBands) Canvas.SetLeft(band, -90);
            _flairAuras = Array.Empty<(Border, double)>();
            _flairPills = Array.Empty<(Border, ScaleTransform)>();
            _flairSpotScale = null;
            _flairBands.Clear();
        }

        private void StopSheens()
        {
            StopFlairLoop();
            foreach (var sheen in _cardSheens) CardSheenAdorner.Detach(sheen);
            _cardSheens.Clear();
        }

        /// <summary>
        /// WPF StartExclusivesMotion + ApplyFreeTodayPulse + AttachExclusiveSheens: the spotlight's
        /// Ken Burns drift, the FREE TODAY pills' breath and every card's glass sheen run only under
        /// AllowAmbientLoops; the pills keep their gold glow wherever the tier allows glow.
        /// </summary>
        private void ApplyAmbientFlair()
        {
            bool loops = AmbientFxCanvas.Env.AllowAmbientLoops;
            bool glow = AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier);
            Find<Image>("SpotArtImage").Classes.Set("kenburns", loops);
            var pills = Shelf<Border>("free-pill").Append(Find<Border>("SpotFreeToday"));
            foreach (var pill in pills)
            {
                pill.Classes.Set("pulse", loops);
                pill.Classes.Set("glow", glow);
            }
            if (loops) AttachCardSheens();
        }

        /// <summary>WPF AttachExclusiveSheens: adorner layers exist only once the shelf has rendered,
        /// so a card without one retries a bounded number of times at Background priority.</summary>
        private void AttachCardSheens()
        {
            foreach (var sheen in _cardSheens) CardSheenAdorner.Detach(sheen);
            _cardSheens.Clear();
            bool missing = false;
            foreach (var card in Shelf<Border>("vault-card"))
            {
                var sheen = CardSheenAdorner.Attach(card, 12);
                if (sheen == null) { missing = true; continue; }
                _cardSheens.Add(sheen);
            }
            if (!missing) { _cardSheenRetries = 0; return; }
            if (_cardSheenRetries++ >= 5) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_motionOn && !_plansMode && AmbientFxCanvas.Env.AllowAmbientLoops) AttachCardSheens();
            }, DispatcherPriority.Background);
        }

        private void StopFlair()
        {
            foreach (var t in _entranceTimers) t.Stop();
            _entranceTimers.Clear();
            StopSheens();
            if (this.FindControl<ItemsControl>("ExclusivesShelf") == null) return;
            foreach (var aura in Shelf<Border>("aura")) aura.Classes.Set("breathe", false);
            foreach (var pill in Shelf<Border>("free-pill").Append(Find<Border>("SpotFreeToday"))) pill.Classes.Set("pulse", false);
            Find<Image>("SpotArtImage").Classes.Set("kenburns", false);
            foreach (var card in Shelf<Border>("vault-card")) { card.Transitions = null; card.Opacity = 1; card.RenderTransform = null; }
        }

        /// <summary>The cards rise into place one after another as the page opens (WPF
        /// PlayVaultEntrance): hidden until their turn, then a fade, plus a rise at Full.</summary>
        private void PlayEntrance()
        {
            if (_plansMode) return;
            var level = AmbientFxCanvas.Env.Level;
            foreach (var t in _entranceTimers) t.Stop();
            _entranceTimers.Clear();
            Dispatcher.UIThread.Post(() =>
            {
                int i = 0;
                bool full = level == MotionLevel.Full;
                foreach (var card in Shelf<Border>("vault-card"))
                {
                    card.Transitions = null;
                    card.Opacity = 1;
                    card.RenderTransform = null;
                    if (level == MotionLevel.Off) continue;
                    var delay = (full ? VaultEntranceStepMs : 20) * Math.Min(i++, full ? VaultEntranceCap : 8);
                    card.Opacity = 0;
                    if (full) card.RenderTransform = TransformOperations.Parse("translateY(18px)");
                    var transitions = new Transitions
                    {
                        new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(full ? 320 : 200), Easing = new QuadraticEaseOut() },
                    };
                    if (full)
                        transitions.Add(new TransformOperationsTransition
                        {
                            Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new BackEaseOut(),
                        });
                    card.Transitions = transitions;
                    var target = card;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay)) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        target.Opacity = 1;
                        if (full) target.RenderTransform = TransformOperations.Parse("translateY(0px)");
                    };
                    _entranceTimers.Add(timer);
                    timer.Start();
                }
            }, DispatcherPriority.Background);
        }

        /// <summary>Coalesced: a burst of scroll steps recomputes the motes' zones once, after layout.</summary>
        private void QueueZones()
        {
            if (_zonesQueued || _plansMode) return;
            _zonesQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _zonesQueued = false;
                try { UpdateZones(); }
                catch (Exception ex) { Serilog.Log.Debug("Vault zones: {E}", ex.Message); }
            }, DispatcherPriority.Background);
        }

        /// <summary>WPF UpdateVaultZones: every card and sign on screen is a place motes rise from.
        /// A card that is yours counts twice, so the glitter gathers where the doors are open.</summary>
        internal void UpdateZones()
        {
            if (!IsVisible || _plansMode) return;
            double h = _ambientFx.Bounds.Height;
            if (_ambientFx.Bounds.Width <= 0 || h <= 0) return;
            var zones = new List<VaultZone>();
            void Add(Control c, Color hue, bool diamond, int weight)
            {
                if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0) return;
                if (c.TranslatePoint(new Point(0, 0), _ambientFx) is not { } tl) return;
                var r = new Rect(tl, c.Bounds.Size);
                if (r.Bottom < 0 || r.Top > h) return;
                for (int k = 0; k < weight; k++) zones.Add(new VaultZone(r, hue, diamond));
            }
            foreach (var card in Shelf<Border>("vault-card"))
                if (card.DataContext is ExclusiveCardRow row)
                    Add(card, row.Hue, row.Group == PremiumGroup.Prime, row.IsMine ? 2 : 1);
            foreach (var sign in Shelf<Panel>("vault-sign"))
                if (sign.DataContext is ExclusiveShelfGroup g)
                    Add(sign, g.Hue, g.Group == PremiumGroup.Prime, 2);
            _ambientFx.SetVaultZones(zones);
        }

        // =====================================================================================
        //  clicks
        // =====================================================================================

        private void Spotlight_Click(object? sender, RoutedEventArgs e) => Open(ExclusiveFeature.All[0]);

        // WPF MouseLeftButtonUp: left button only.
        private void Spotlight_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left) Open(ExclusiveFeature.All[0]);
        }

        /// <summary>WPF OnVaultCardClicked: a burst in the card's colour, then the door. The door
        /// waits 140 ms only when there is a burst to see (particles allowed); else it opens at once.</summary>
        private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left || sender is not Border card
                || card.DataContext is not ExclusiveCardRow row) return;
            bool burst = false;
            try
            {
                if (!_plansMode && row.IsAvailable && AmbientFxCanvas.Env.AllowParticles && card.Bounds.Width > 0
                    && card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _ambientFx) is { } at)
                {
                    _ambientFx.Burst(at.X, at.Y, row.Hue, 90);
                    burst = true;
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("Vault card burst: {E}", ex.Message); }
            if (!burst) { Open(row.Feature); return; }
            DispatcherTimer.RunOnce(() => Open(row.Feature), TimeSpan.FromMilliseconds(140));
        }

        /// <summary>
        /// "fyp" / "justdrop" are shell window keys: ShowTab opens the feed / the shop (MainShellWindow.JustDrop.cs, lane k9).
        /// Focus Gaze opens the Play wall on its switch (lane u1).
        /// Every card on the shelf has a door on this head now; a card with none would stay visible but inert (no hand, honest tooltip).
        /// The games (Back Room, Breakout, Goon, Down the Rabbit Hole, Arcademy) open through
        /// MainShellWindow.LaunchCardGame, the Play wall's own door (wave 3 r5).
        /// </summary>
        internal static bool IsOnThisBuild(string key) => !string.IsNullOrEmpty(key);

        /// <summary>WPF OpenExclusiveFeature: the card never blocks, the destination's own gate does.</summary>
        private void Open(ExclusiveFeature feature)
        {
            if (IsOnThisBuild(feature.Key))
                (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OpenExclusiveFeature(feature.Key);
        }

        /// <summary>WPF OnExclusiveCardHover: the shared hover pop on the art, driven from the card.</summary>
        private void Card_PointerEntered(object? sender, PointerEventArgs e) => HoverPop.Enter(CardArt(sender));

        private void Card_PointerExited(object? sender, PointerEventArgs e) => HoverPop.Leave(CardArt(sender));

        private static Control? CardArt(object? card) =>
            (card as Control)?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "CardArt");
    }

    /// <summary>One shelf group on the Premium page: its header and its cards (WPF VaultGroupHead).</summary>
    public sealed class ExclusiveShelfGroup
    {
        public ExclusiveShelfGroup(PremiumGroup group, IReadOnlyList<ExclusiveCardRow> cards, bool first)
        {
            Group = group;
            Cards = cards;
            Hue = ExclusiveCardRow.HueOf(group);
            string titleKey, subKey;
            (Sign, titleKey, subKey) = group switch
            {
                PremiumGroup.Basic => (Load("features/tier_badge_t1.png"), "premium_group_basic", "premium_group_basic_sub"),
                PremiumGroup.Prime => (Load("features/tier_badge_t2.png"), "premium_group_prime", "premium_group_prime_sub"),
                _ => ((Bitmap?)null, "premium_group_free", "premium_group_free_sub"),
            };
            Title = Loc.Get(titleKey);
            Sub = Loc.Get(subKey);
            CountText = string.Format(Loc.Get("premium_group_count"), cards.Count(c => c.IsMine), cards.Count);
            HeadMargin = new Thickness(0, first ? 0 : 14, 0, 16);
        }

        private static readonly Dictionary<string, Bitmap?> SignCache = new();

        private static Bitmap? Load(string name)
        {
            if (!SignCache.TryGetValue(name, out var bmp)) SignCache[name] = bmp = ModArt.TryLoad(name, 240);
            return bmp;
        }

        public PremiumGroup Group { get; }
        public IReadOnlyList<ExclusiveCardRow> Cards { get; }
        public Color Hue { get; }
        public Bitmap? Sign { get; }
        public bool HasSign => Sign != null;
        /// <summary>The sign's own pixels as an opacity mask, so the shimmer crosses the neon only.</summary>
        public IBrush? SignMask => Sign == null ? null : new ImageBrush(Sign) { Stretch = Stretch.Uniform };
        public string Title { get; }
        public string Sub { get; }
        public string CountText { get; }
        public Thickness HeadMargin { get; }
        public IBrush HueBrush => new SolidColorBrush(Hue);
        public IBrush CountFill => new SolidColorBrush(Color.FromArgb(0x1F, Hue.R, Hue.G, Hue.B));
        public IBrush CountEdge => new SolidColorBrush(Color.FromArgb(0x66, Hue.R, Hue.G, Hue.B));
        /// <summary>A hairline in the plan's colour under the row, fading out to the right.</summary>
        public IBrush RuleBrush => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xAA, Hue.R, Hue.G, Hue.B), 0),
                new GradientStop(Color.FromArgb(0x00, Hue.R, Hue.G, Hue.B), 1),
            },
        };
    }

    /// <summary>
    /// One shelf card: an <see cref="ExclusiveFeature"/> plus the state
    /// MainWindow.Exclusives.cs paints onto it (ApplyExclusiveCardState / VaultLivery.Apply).
    /// </summary>
    public sealed class ExclusiveCardRow
    {
        public ExclusiveCardRow(ExclusiveFeature feature)
        {
            Feature = feature;
            State = feature.GateState();
            FreeToday = feature.IsFreeToday(State);
            // WPF ExclusiveArtPath: Takeover forks its art under BambiSleep.
            Art = ModArt.TryLoad(feature.Key == "bambitakeover" && CoreMods.ActiveModId == BuiltInMods.BambiSleepId
                ? "features/bambi takeover.png"
                : ExclusivesTabView.ArtName(feature.ArtResource), 700);
        }

        internal static Color HueOf(PremiumGroup g) => g switch
        {
            PremiumGroup.Basic => ExclusivesTabView.VaultBasicGold,
            PremiumGroup.Prime => ExclusivesTabView.VaultPrimeCyan,
            _ => ExclusivesTabView.VaultFreeLilac,
        };

        public ExclusiveFeature Feature { get; }
        public ExclusiveGateState State { get; }
        public bool FreeToday { get; }
        public Bitmap? Art { get; }
        public bool HasArt => Art != null;
        public bool IsAvailable => ExclusivesTabView.IsOnThisBuild(Feature.Key);
        public Cursor Cursor => new(IsAvailable ? StandardCursorType.Hand : StandardCursorType.Arrow);
        public string? UnavailableTip => IsAvailable ? null : Loc.Get("exclusives_not_on_this_build");
        public int Tier => Feature.Tier;

        /// <summary>The shelf this card stands on (Core PremiumShelfOrder) and its colour.</summary>
        public PremiumGroup Group => PremiumShelfOrder.GroupOf(Feature.Key, Feature.Tier);
        public Color Hue => HueOf(Group);

        /// <summary>WPF IsVaultDoorOpen: owned, a pass ready, or today's free door.</summary>
        public bool IsMine => State != ExclusiveGateState.Locked || FreeToday;

        /// <summary>WPF VaultCardAura: the rim and its soft glow, in the card's tier colour.</summary>
        public IBrush AuraBrush => new SolidColorBrush(Color.FromArgb(0xD8, Hue.R, Hue.G, Hue.B));
        public BoxShadows AuraShadow => new(new BoxShadow
        {
            Blur = 14, Spread = 1, Color = Color.FromArgb(0x70, Hue.R, Hue.G, Hue.B),
        });

        /// <summary>"emoji + title"; WPF ExclusiveTitle takes Takeover's name from the mod.</summary>
        public string Title => $"{Feature.Emoji} " + (Feature.Key == "bambitakeover"
            ? App.Mods?.GetTakeoverLabel() ?? Loc.Get(Feature.TitleLocKey)
            : Loc.Get(Feature.TitleLocKey));
        public string Tagline => Loc.Get(Feature.TaglineLocKey);
        public string Emoji => Feature.Emoji;

        public bool HasBadge => Feature.BadgeLocKey != null;
        public string BadgeText => Feature.BadgeLocKey == null ? "" : Loc.Get(Feature.BadgeLocKey);

        /// <summary>The daily free unlock outranks the padlock.</summary>
        public bool IsLocked => State == ExclusiveGateState.Locked && !FreeToday;

        /// <summary>WPF dims the art to 0.75 under the veil; the glyph fallback keeps its own ratio.</summary>
        public double ArtOpacity => IsLocked ? 0.75 : 1.0;
        public double GlyphOpacity => IsLocked ? 0.13 : 0.18;

        public bool HasChip => !IsLocked && !FreeToday;

        /// <summary>VaultLivery.Apply: a tiered card says its free day with the badge re-stamp;
        /// only an untiered one wears the gold pill.</summary>
        public bool HasFreePill => FreeToday && Tier <= 0;
        public bool BadgeFreeToday => FreeToday && Tier > 0;

        public Thickness ChipMargin => Tier > 0 ? new Thickness(0, 84, 8, 0) : new Thickness(0, 8, 8, 0);

        public string ChipText => Loc.Get(State == ExclusiveGateState.PassReady
            ? "exclusives_chip_pass_ready"
            : "exclusives_chip_unlocked");

        public IBrush ChipForeground => State == ExclusiveGateState.PassReady ? Brush("#FFD27A") : Brush("#7FE7E0");
        public IBrush ChipBackground => State == ExclusiveGateState.PassReady ? Brush("#33FFD27A") : Brush("#2E7FE7E0");
        public IBrush ChipBorderBrush => State == ExclusiveGateState.PassReady ? Brush("#73FFD27A") : Brush("#667FE7E0");

        /// <summary>
        /// Resting rim (VaultLivery.Apply). Untiered free-today is EdgeFree at 2px.
        /// ponytail: the untiered edge is the default mod's accent hue-shifted (ShiftHue is head-only
        /// on WPF) and a tiered card's 3px animated TierFxBorder livery is not applied here.
        /// </summary>
        public IBrush EdgeBrush => Tier > 0 ? Brush("#66FFC94E") : FreeToday ? Brush("#E6FFD27A") : Brush("#4DB478FF");
        public Thickness EdgeThickness => new(Tier <= 0 && FreeToday ? 2 : 1);

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
