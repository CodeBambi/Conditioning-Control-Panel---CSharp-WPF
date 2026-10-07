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

            LoadBackdrop();
            RefreshVault();

            // The tab is permanently mounted on WPF and MainWindow parks its canvas through
            // RegisterTabFx. No tab host on this head: the view runs its own room and stops it on
            // unload. The canvas self-gates on motion, tier and window focus regardless.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
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
            StartAmbient();
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
            _ambientFx.Stop();
        }

        // ------------------------------------------------------------------
        // The vault (WPF MainWindow.Exclusives.cs EnsureExclusivesBuilt + RefreshExclusivesTab)
        // ------------------------------------------------------------------

        /// <summary>
        /// Repaints the spotlight and the shelf from <see cref="ExclusiveFeature.All"/> and each
        /// feature's live gate. Called on construction, on every show of the tab
        /// (MainShellWindow.OnTabShown) and on a mod switch, as WPF's refresh is.
        /// ponytail: no Ken Burns, sheen, veil breath, FREE TODAY pulse, tier-plate refresh,
        /// accent re-tint or "coming soon" teasers yet - see the parity ledger.
        /// </summary>
        internal void RefreshVault()
        {
            // Main 2e9080399: Prime first, then Basic, then the untiered doors. Just Drop until the server opens
            // its door and the Arcademy behind its build flag are hidden, not veiled (ExclusiveFeature.IsShown).
            var rows = new List<ExclusiveCardRow>();
            foreach (var f in ExclusiveFeature.ShelfOrder(ExclusiveFeature.All))
                if (f.Shown()) rows.Add(new ExclusiveCardRow(f));
            this.FindControl<ItemsControl>("ExclusivesShelf")!.ItemsSource = rows;

            var spot = new ExclusiveCardRow(ExclusiveFeature.All[0]);
            this.FindControl<TextBlock>("TxtSpotArtGlyph")!.Text = spot.Art == null ? spot.Feature.Emoji : "";
            var spotArt = this.FindControl<Image>("SpotArtImage")!;
            spotArt.Source = ModArt.TryLoad(ArtName(spot.Feature.BannerArtResource), 1400) ?? spot.Art;
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
            card.Cursor = spot.Cursor;
            ToolTip.SetTip(card, spot.UnavailableTip);
            var open = this.FindControl<Button>("BtnSpotOpen")!;
            open.IsEnabled = spot.IsAvailable;
            ToolTip.SetTip(open, spot.UnavailableTip);
            ToolTip.SetShowOnDisabled(open, true);
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
        /// ponytail: the untiered edge is the default mod's accent hue-shifted (ShiftHue is head-only
        /// on WPF) and a tiered card's 3px animated TierFxBorder livery is not applied here.
        /// </summary>
        public IBrush EdgeBrush => Tier > 0 ? Brush("#66FFC94E") : FreeToday ? Brush("#E6FFD27A") : Brush("#4DB478FF");
        public Thickness EdgeThickness => new(Tier <= 0 && FreeToday ? 2 : 1);

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
