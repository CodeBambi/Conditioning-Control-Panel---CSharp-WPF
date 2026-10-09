using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/ExclusivesTabView.xaml.cs.
    ///
    /// <para>The WPF shell owns only the backdrop and geometry chores; all roster/card logic lives
    /// in MainWindow.Exclusives.cs. What survived here is the ambient canvas tuning (fog + dust +
    /// aurora at 0.55, copied from StartExclusivesMotion - canvas composition, not service logic)
    /// plus the roster/gate repaint (RefreshVault), which reads Core's ExclusiveFeature.All.</para>
    ///
    /// <para>Dropped:
    /// <c>RoundClipOnResize</c> - WPF's ClipToBounds is rectangular, so a rounded host needed clip
    /// geometry tracked against every resize; an Avalonia Border clips its child to its own
    /// CornerRadius, so the helper has no work left. Its two other callers were MainWindow's card
    /// builder, which is not ported either.</para>
    /// </summary>
    public partial class ExclusivesTabView : UserControl
    {
        private readonly AmbientFxCanvas _ambientFx;

        public ExclusivesTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _ambientFx = this.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            _spotEdge = this.FindControl<Border>("SpotlightCard")!.BorderBrush;
            this.FindControl<Image>("SpotArtImage")!.RenderTransform = _kenBurns;
            foreach (var name in new[] { "SpotVeilLock", "SpotFreeToday" }) _fxParts.Add(this.FindControl<Control>(name)!);

            LoadBackdrop();
            RefreshVault();

            // The tab is permanently mounted on WPF and MainWindow parks its canvas through
            // RegisterTabFx. No tab host on this head: the view runs its own room and stops it on
            // unload. The canvas self-gates on motion, tier and window focus regardless.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            // PLAYBOOK P01: the shell hides tabs with IsVisible, so the motion clock follows it too.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateMotion(); };
            // Main bf57cecdf: columns follow the width, cards stretch to fill the row (Core ExclusiveShelfFit).
            this.FindControl<ItemsControl>("ExclusivesShelf")!.SizeChanged += (_, e) => { if (e.WidthChanged) FitShelf(); };
        }

        /// <summary>WPF FitExclusiveShelf: every card sized for the shelf's width; the Gap is each card's right/bottom margin.</summary>
        internal void FitShelf()
        {
            var shelf = this.FindControl<ItemsControl>("ExclusivesShelf")!;
            if (shelf.Bounds.Width <= 0 || shelf.ItemsPanelRoot is not WrapPanel wrap) return;
            var (_, w, h) = Services.UI.ExclusiveShelfFit.For(shelf.Bounds.Width);
            wrap.ItemWidth = w + Services.UI.ExclusiveShelfFit.Gap;
            wrap.ItemHeight = h + Services.UI.ExclusiveShelfFit.Gap;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            CoreMods.ModChanged += OnModChanged;
            AmbientFxCanvas.Env.MotionGateChanged += UpdateMotion;
            StartAmbient();
            UpdateMotion();
        }

        /// <summary>ModChanged may be raised off the UI thread; marshal before touching the Image.</summary>
        private void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() => { LoadBackdrop(); RefreshVault(); });

        /// <summary>WPF LoadBackdrop: null keeps what is already painted rather than blanking the room.</summary>
        private void LoadBackdrop()
        {
            var art = Helpers.ModArt.TryLoad("exclusives/vault_backdrop.png");
            if (art != null) this.FindControl<Image>("VaultBackdrop")!.Source = art;
        }

        private void StartAmbient()
            => _ambientFx.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash,
                Intensity = 0.55,
                FogPuffs = 3,
            });

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            AmbientFxCanvas.Env.MotionGateChanged -= UpdateMotion;
            _ambientFx.Stop();
            UpdateMotion();
        }

        // ------------------------------------------------------------------
        // The vault (WPF MainWindow.Exclusives.cs EnsureExclusivesBuilt + RefreshExclusivesTab)
        // ------------------------------------------------------------------

        /// <summary>
        /// Repaints the spotlight and the shelf from <see cref="ExclusiveFeature.All"/> and each
        /// feature's live gate. Called on construction, on every show of the tab
        /// (MainShellWindow.OnTabShown) and on a mod switch, as WPF's refresh is.
        /// ponytail: no accent re-tint yet (needs WPF ShiftHue in Core) - see the parity ledger.
        /// </summary>
        internal void RefreshVault()
        {
            // Main 2e9080399: Prime first, then Basic, then the untiered doors. Just Drop until the server opens
            // its door and the Arcademy behind its build flag are hidden, not veiled (ExclusiveFeature.IsShown).
            var rows = new List<object>();
            foreach (var f in ExclusiveFeature.ShelfOrder(ExclusiveFeature.All))
                if (f.Shown()) rows.Add(new ExclusiveCardRow(f));
            // WPF EnsureExclusivesBuilt: the casino filled the first teaser; two reserved seats remain.
            rows.Add(new ExclusiveTeaserRow("play_racing_reserved_title", "play_racing_reserved_blurb"));
            rows.Add(new ExclusiveTeaserRow("play_future_reserved_title", "play_future_reserved_blurb"));
            this.FindControl<ItemsControl>("ExclusivesShelf")!.ItemsSource = rows;

            var spot = new ExclusiveCardRow(ExclusiveFeature.All[0]);
            this.FindControl<TextBlock>("TxtSpotArtGlyph")!.Text = spot.Art == null ? spot.Feature.Emoji : "";
            var spotArt = this.FindControl<Image>("SpotArtImage")!;
            var banner = ModArt.TryLoad(ArtName(spot.Feature.BannerArtResource), 1400);
            spotArt.Source = banner ?? spot.Art;
            // WPF ApplySpotlightArt: the Ken Burns zoom centres on a banner, on the focal point of card art.
            spotArt.RenderTransformOrigin = banner != null
                ? RelativePoint.Center
                : new RelativePoint(spot.Feature.FocalX, spot.Feature.FocalY, RelativeUnit.Relative);
            this.FindControl<TextBlock>("TxtSpotTitle")!.Text = spot.Title;
            this.FindControl<TextBlock>("TxtSpotTagline")!.Text = spot.Tagline;
            this.FindControl<Border>("SpotBadge")!.IsVisible = spot.HasBadge;
            this.FindControl<TextBlock>("TxtSpotBadge")!.Text = spot.BadgeText;
            this.FindControl<Border>("SpotVeil")!.IsVisible = spot.IsLocked;
            this.FindControl<TextBlock>("TxtSpotFreeToday")!.Text = Loc.Get("mosaic_free_today");
            this.FindControl<Border>("SpotFreeToday")!.IsVisible = spot.HasFreePill;
            var badge = this.FindControl<TierBadge>("SpotTierBadge")!;
            badge.Tier = spot.Tier;
            badge.FreeToday = spot.BadgeFreeToday;
            var card = this.FindControl<Border>("SpotlightCard")!;
            // WPF VaultLivery.Apply at hero weight: tiered = livery + 4px living rim; untiered = resting/gold edge.
            TierFxBorder.SetRimThickness(card, 4);
            TierFxBorder.SetTier(card, spot.Tier);
            card.BorderBrush = spot.Tier > 0 ? ExclusiveCardRow.Livery(spot.Tier) : spot.FreeToday ? ExclusiveCardRow.EdgeFree : _spotEdge;
            card.BorderThickness = new Thickness(spot.Tier > 0 ? 4 : spot.FreeToday ? 2 : 1);
            card.Cursor = spot.Cursor;
            ToolTip.SetTip(card, spot.UnavailableTip);
            var open = this.FindControl<Button>("BtnSpotOpen")!;
            open.IsEnabled = spot.IsAvailable;
            ToolTip.SetTip(open, spot.UnavailableTip);
            ToolTip.SetShowOnDisabled(open, true);
            RefreshTierPlates();
            Dress(this.FindControl<Control>("SpotFreeToday")!);
            Dress(this.FindControl<Control>("SpotVeilLock")!);   // WPF :799 breathes only under a shown veil
        }

        /// <summary>WPF RefreshExclusiveTierPlates: the plate matching the account's access lights and breathes.
        /// The access gates, not the raw tier, so SubscribeStar/whitelist/grace light it as they unlock features.</summary>
        private void RefreshTierPlates()
        {
            var (p1, p2) = (this.FindControl<Border>("TierPlate1")!, this.FindControl<Border>("TierPlate2")!);
            bool lab = CoreEntitlement.HasLab, premium = CoreEntitlement.HasPremium;
            p1.Opacity = lab ? 0.55 : premium ? 1.0 : 0.3;
            p2.Opacity = lab ? 1.0 : 0.3;
            LitPlate = lab ? p2 : premium ? p1 : null;
        }

        /// <summary>The plate the motion clock breathes (0.75..1.0), or null. Test seam.</summary>
        internal Border? LitPlate { get; private set; }

        // ------------------------------------------------------------------
        // Motion (WPF Start/StopExclusivesMotion, ApplyVeilLockBreath, ApplyFreeTodayPulse, sheens)
        // ------------------------------------------------------------------

        private static readonly Color FreeTodayGold = Color.FromRgb(0xFF, 0xD2, 0x7A);
        private readonly IBrush? _spotEdge;
        private readonly ScaleTransform _kenBurns = new();
        private readonly HashSet<Control> _fxParts = new();
        private readonly Dictionary<Control, CardSheenAdorner?> _sheens = new();
        private DispatcherTimer? _motion;
        private long _motionStart;

        /// <summary>True while the one ambient clock runs. Test seam.</summary>
        internal bool MotionRunning => _motion != null;

        /// <summary>Shelf cards currently wearing a sheen. Test seam.</summary>
        internal int SheenCount => _sheens.Values.Count(s => s != null);

        /// <summary>The only start/stop: visible, attached and MotionFx.AllowAmbientLoops (PLAYBOOK P01).</summary>
        private void UpdateMotion()
        {
            bool want = IsVisible && this.IsAttachedToVisualTree() && AmbientFxCanvas.Env.AllowAmbientLoops;
            // Re-dressed either way: a glow-gate change (performance tier) alone must re-apply the glows.
            if (want == (_motion != null)) { }
            else if (want)
            {
                _motionStart = FxAdorner.Time.GetTimestamp();
                // WPF caps every ambient loop on this tab at 24fps (AmbientFrameRate).
                _motion = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 24), DispatcherPriority.Background, (_, _) => MotionFrame());
            }
            else
            {
                _motion!.Stop();
                _motion = null;
                foreach (var sheen in _sheens.Values) CardSheenAdorner.Detach(sheen);
                foreach (var k in _sheens.Keys.ToList()) _sheens[k] = null;
                _kenBurns.ScaleX = _kenBurns.ScaleY = 1;
                if (LitPlate != null) LitPlate.Opacity = 1;
            }
            foreach (var part in _fxParts) Dress(part);
        }

        /// <summary>One frame of every loop, read from <see cref="FxAdorner.Time"/> so tests step it.</summary>
        internal void MotionFrame()
        {
            if (_motion == null) return;
            double t = FxAdorner.Time.GetElapsedTime(_motionStart).TotalSeconds;
            _kenBurns.ScaleX = _kenBurns.ScaleY = Breath(t, 26, 1.0, 1.07);
            if (LitPlate != null) LitPlate.Opacity = Breath(t, 3.4, 0.75, 1.0);
            double glow = Breath(t, 3.4, 0.35, 0.9), fade = Breath(t, 1.9, 0.72, 1.0), swell = Breath(t, 1.9, 1.0, 1.06);
            foreach (var part in _fxParts)
            {
                if (!part.IsEffectivelyVisible) continue;   // a padlock under a hidden veil neither glows nor ticks (WPF :964)
                if (IsPill(part))
                {
                    part.Opacity = fade;
                    if (part.RenderTransform is ScaleTransform s) s.ScaleX = s.ScaleY = swell;
                }
                else if (part.Effect is DropShadowEffect g) g.Opacity = glow;
            }
            // WPF AttachExclusiveSheens retries cards whose adorner layer was not there yet.
            foreach (var card in _sheens.Where(p => p.Value == null).Select(p => p.Key).ToList())
                _sheens[card] = CardSheenAdorner.Attach(card, 12);
        }

        /// <summary>Sine ease in/out, auto-reversed: WPF's DoubleAnimation(min, max, seconds) recipe.</summary>
        internal static double Breath(double t, double seconds, double min, double max)
        {
            double u = t % (2 * seconds) / seconds;
            if (u > 1) u = 2 - u;
            return min + (max - min) * (1 - Math.Cos(Math.PI * u)) / 2;
        }

        private static bool IsPill(Control part) => part.Name is "FreePill" or "SpotFreeToday";

        /// <summary>The resting look for the current gates. Padlocks and teaser marks glow only while the clock
        /// runs (WPF clears it when loops are off); the FREE TODAY pill keeps a static gold glow wherever glow is allowed.</summary>
        private void Dress(Control part)
        {
            var tier = AmbientFxCanvas.Env.CurrentTier;
            bool glow = AmbientFxCanvas.Env.AllowGlow(tier);
            if (IsPill(part))
            {
                part.Opacity = 1;
                if (part.RenderTransform is ScaleTransform s) s.ScaleX = s.ScaleY = 1;
                part.Effect = glow ? new DropShadowEffect { Color = FreeTodayGold, BlurRadius = Math.Min(16, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)), OffsetX = 0, OffsetY = 0, Opacity = 0.7 } : null;
            }
            else
            {
                part.Effect = _motion != null && glow && part.IsEffectivelyVisible
                    ? new DropShadowEffect { Color = AmbientFxCanvas.Env.GlowColor, BlurRadius = Math.Min(20, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)), OffsetX = 0, OffsetY = 0, Opacity = 0.8 }
                    : null;
            }
        }

        /// <summary>Template parts (padlock, FREE TODAY pill, teaser mark, card) join the clock while realized.</summary>
        private void FxPart_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control part) return;
            if (part.Name == "ExCard") { if (!_sheens.ContainsKey(part)) _sheens[part] = _motion == null ? null : CardSheenAdorner.Attach(part, 12); return; }
            if (_fxParts.Add(part)) Dress(part);
        }

        private void FxPart_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control part) return;
            if (_sheens.Remove(part, out var sheen)) CardSheenAdorner.Detach(sheen);
            _fxParts.Remove(part);
        }

        /// <summary>"Resources/features/x.png" -> "features/x.png", the name ModArt resolves.</summary>
        internal static string? ArtName(string? resource) =>
            resource?.StartsWith("Resources/", StringComparison.Ordinal) == true ? resource["Resources/".Length..] : resource;

        private void Spotlight_Click(object? sender, RoutedEventArgs e) => Open(ExclusiveFeature.All[0]);

        // WPF MouseLeftButtonUp: left button only.
        private void Spotlight_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left) Open(ExclusiveFeature.All[0]);
        }

        private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left && (sender as Control)?.DataContext is ExclusiveCardRow row)
                Open(row.Feature);
        }

        /// <summary>
        /// ponytail: "fyp"/"justdrop" are shell WindowKeys; "backroom" (WPF BtnStartBackRoom_Click) and main
        /// 2e9080399's Breakout, Goon hosting, Down the Rabbit Hole and Arcademy have no host on this head, and
        /// Focus Gaze's Play-wall switch is still inert (PlayTabView.ChkFocusGaze_Changed); add it back when that works.
        /// Their cards stay visible but inert (no hand, honest tooltip) and never reach ShowTab or a bark.
        /// </summary>
        internal static bool IsOnThisBuild(string key) =>
            key is not ("fyp" or "justdrop" or "backroom" or "breakout" or "goon" or "dtrh" or "arcademy" or "focusgaze");

        /// <summary>WPF OpenExclusiveFeature: the card never blocks, the destination's own gate does.</summary>
        private void Open(ExclusiveFeature feature)
        {
            if (IsOnThisBuild(feature.Key))
                (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OpenExclusiveFeature(feature.Key);
        }

        /// <summary>WPF OnExclusiveCardHover: the shared hover pop on the art, driven from the card.
        /// ponytail: no MotionFx.HoverLift or glow bloom on this head yet.</summary>
        private void Card_PointerEntered(object? sender, PointerEventArgs e) => HoverPop.Enter(CardArt(sender));

        private void Card_PointerExited(object? sender, PointerEventArgs e) => HoverPop.Leave(CardArt(sender));

        private static Control? CardArt(object? card) =>
            (card as Control)?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "CardArt");
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

        public ExclusiveFeature Feature { get; }
        public ExclusiveGateState State { get; }
        public bool FreeToday { get; }
        public Bitmap? Art { get; }
        public bool HasArt => Art != null;
        public bool IsAvailable => ExclusivesTabView.IsOnThisBuild(Feature.Key);
        public Cursor Cursor => new(IsAvailable ? StandardCursorType.Hand : StandardCursorType.Arrow);
        public string? UnavailableTip => IsAvailable ? null : Loc.Get("exclusives_not_on_this_build");
        public int Tier => Feature.Tier;

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
        /// A tiered card wears its livery at 3px (the template's TierFxBorder laps it).
        /// ponytail: the untiered edge is the default mod's accent hue-shifted (ShiftHue is head-only
        /// on WPF), so it stays the Bambi partner literal on every mod.
        /// </summary>
        public IBrush EdgeBrush => Tier > 0 ? Livery(Tier) : FreeToday ? EdgeFree : Brush("#4DB478FF");
        public Thickness EdgeThickness => new(Tier > 0 ? 3 : FreeToday ? 2 : 1);

        /// <summary>VaultLivery.EdgeFree: gold means "open for one day only".</summary>
        internal static IBrush EdgeFree => Brush("#E6FFD27A");

        /// <summary>WPF TierLivery.BorderBrush: gold for Tier 1, diamond for Tier 2, from the shared theme.</summary>
        internal static IBrush Livery(int tier) =>
            Application.Current?.TryGetResource(tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush", null, out var r) == true
                && r is IBrush b ? b : Brush(tier >= 2 ? "#8FD4EF" : "#F0C24B");

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }

    /// <summary>WPF BuildComingSoonCard: a reserved seat - silhouette, breathing "?", SOON badge, never clickable.</summary>
    public sealed class ExclusiveTeaserRow(string titleKey, string taglineKey)
    {
        public string Title { get; } = Loc.Get(titleKey);
        public string Tagline { get; } = Loc.Get(taglineKey);
        public string Soon { get; } = Loc.Get("exclusives_badge_soon");

        /// <summary>WPF TeaserMarkAlpha over the vault accent (FxTheme glow).</summary>
        public IBrush MarkBrush { get; } = new SolidColorBrush(AmbientFxCanvas.Env.GlowColor, 0x8C / 255.0);
    }
}
