using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/ExclusivesTabView.xaml.cs.
    ///
    /// <para>The WPF shell owns only the backdrop and geometry chores; all roster/card logic lives
    /// in MainWindow.Exclusives.cs. What survived here is the ambient canvas tuning (fog + dust +
    /// aurora at 0.55, copied from StartExclusivesMotion - canvas composition, not service logic).
    /// Everything else is a stub or a placeholder.</para>
    ///
    /// <para>Dropped: <c>LoadBackdrop</c> and the <c>ModChanged</c> subscription that re-ran it.
    /// ponytail: the resolver is NOT the blocker - <c>Helpers.ModArt.TryLoad</c> plus
    /// <c>CoreMods.ModChanged</c> answer it, and the picture exists at
    /// <c>Assets/exclusives/vault_backdrop.png</c>. Two things are missing and neither is in this
    /// file: <c>Assets/exclusives/**</c> is not among the <c>AvaloniaResource</c> globs in
    /// CCP.Avalonia.csproj, so <c>avares://CCP.Avalonia/Resources/exclusives/vault_backdrop.png</c>
    /// does not resolve; and the markup has no <c>VaultBackdrop</c> Image to paint into - the
    /// spotlight carries <c>TxtSpotArtGlyph</c> instead. Link the folder, add the Image, then this
    /// is four lines. Also dropped:
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

            LoadPlaceholderVault();

            // The tab is permanently mounted on WPF and MainWindow parks its canvas through
            // RegisterTabFx. No tab host on this head: the view runs its own room and stops it on
            // unload. The canvas self-gates on motion, tier and window focus regardless.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
            => _ambientFx.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash,
                Intensity = 0.55,
                FogPuffs = 3,
            });

        private void OnUnloaded(object? sender, RoutedEventArgs e) => _ambientFx.Stop();

        // ------------------------------------------------------------------
        // Placeholder vault
        // ------------------------------------------------------------------

        /// <summary>
        /// Paints the spotlight and fills the shelf with sample cards.
        ///
        /// ponytail: DailyFreeService is NOT a blocker any more - it is in Core
        /// (CCP.Core/Services/DailyFreeService.cs). What is still head-side is the registry itself,
        /// ConditioningControlPanel/Models/ExclusiveFeature.cs (its gate probe reads App.Patreon),
        /// plus ConditioningControlPanel/Features/VaultLivery.cs and
        /// ConditioningControlPanel/Services/FxTheme.cs. The rows are the real registry's first six entries with their real loc
        /// keys, spread across every branch the card template carries - tier 1, tier 2, untiered,
        /// NEW, BETA, no badge, a locked veil and both entitlement chips - so the render proof
        /// exercises the markup rather than one happy path.
        /// </summary>
        private void LoadPlaceholderVault()
        {
            // The spotlight is ExclusiveFeature.All[0] - "fyp", tier 1, badged NEW. Keys and the
            // "emoji + title" shape are EnsureExclusivesBuilt's.
            this.FindControl<TextBlock>("TxtSpotArtGlyph")!.Text = "📱";
            this.FindControl<TextBlock>("TxtSpotTitle")!.Text = "📱 " + Loc.Get("tab_fyp");
            this.FindControl<TextBlock>("TxtSpotTagline")!.Text = Loc.Get("exclusives_tag_fyp");
            this.FindControl<TextBlock>("TxtSpotBadge")!.Text = Loc.Get("exclusives_badge_new");
            this.FindControl<TextBlock>("TxtSpotFreeToday")!.Text = Loc.Get("mosaic_free_today");
            this.FindControl<TierBadge>("SpotTierBadge")!.Tier = 1;

            this.FindControl<ItemsControl>("ExclusivesShelf")!.ItemsSource = new List<ExclusiveCardRow>
            {
                new() { Emoji = "📱", TitleKey = "tab_fyp", TaglineKey = "exclusives_tag_fyp",
                        Tier = 1, BadgeKey = "exclusives_badge_new", State = ExclusiveCardState.Unlocked },
                new() { Emoji = "🎚", TitleKey = "jd_door_title", TaglineKey = "exclusives_tag_justdrop",
                        Tier = 2, State = ExclusiveCardState.Locked },
                new() { Emoji = "💫", TitleKey = "tab_blink_trainer", TaglineKey = "exclusives_tag_blinktrainer",
                        Tier = 1, State = ExclusiveCardState.PassReady },
                new() { Emoji = "🎙️", TitleKey = "tab_shelistening", TaglineKey = "exclusives_tag_shelistening",
                        Tier = 1, BadgeKey = "exclusives_badge_beta", State = ExclusiveCardState.Locked },
                new() { Emoji = "❓", TitleKey = "tab_gradedintake", TaglineKey = "exclusives_tag_gradedintake",
                        State = ExclusiveCardState.Unlocked },
                new() { Emoji = "💜", TitleKey = "tab_haptics", TaglineKey = "exclusives_tag_haptics",
                        Tier = 1, State = ExclusiveCardState.Unlocked },
            };
        }

        // ------------------------------------------------------------------
        // Stubs for what MainWindow owned
        // ------------------------------------------------------------------

        private void Spotlight_Click(object? sender, RoutedEventArgs e) => OpenSpotlight();

        private void Spotlight_PointerReleased(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
            => OpenSpotlight();

        /// <summary>
        /// WPF: <c>MainWindow.OpenExclusiveSpotlight()</c>, i.e. ShowTab(ExclusiveFeature.All[0].Key).
        /// ponytail: needs the shell's tab host and
        /// ConditioningControlPanel/Models/ExclusiveFeature.cs (for All[0].Key) - so the hero is
        /// inert here rather than navigating somewhere wrong.
        /// </summary>
        private static void OpenSpotlight() { }
    }

    /// <summary>Gate state of a vault card, mirroring Models.ExclusiveGateState.</summary>
    public enum ExclusiveCardState
    {
        Locked,
        PassReady,
        Unlocked,
    }

    /// <summary>
    /// One shelf card. The Avalonia-side stand-in for an ExclusiveFeature plus the state
    /// MainWindow.Exclusives.cs paints onto it (ApplyExclusiveCardState / VaultLivery.Apply): the
    /// chip's three colours and its offset under a tier sign, the veil, the resting edge. Every
    /// literal below is copied from that file so the port is a rename away from the real model.
    /// </summary>
    public sealed class ExclusiveCardRow
    {
        public string Emoji { get; set; } = "";
        public string TitleKey { get; set; } = "";
        public string TaglineKey { get; set; } = "";
        public string? BadgeKey { get; set; }
        public int Tier { get; set; }
        public ExclusiveCardState State { get; set; }

        /// <summary>"emoji + title", the shape BuildExclusiveCard gives every card.</summary>
        public string Title => $"{Emoji} {Loc.Get(TitleKey)}";
        public string Tagline => Loc.Get(TaglineKey);

        public bool HasBadge => BadgeKey != null;
        public string BadgeText => BadgeKey == null ? "" : Loc.Get(BadgeKey);

        public bool IsLocked => State == ExclusiveCardState.Locked;

        /// <summary>The WPF card dims its ART to 0.75 under the veil; the glyph stand-in carries
        /// that as the same proportional dim of its own resting opacity.</summary>
        public double ArtOpacity => IsLocked ? 0.13 : 0.18;

        /// <summary>A veiled card shows the padlock, not a price chip.</summary>
        public bool HasChip => !IsLocked;

        /// <summary>On a tiered card the badge owns the top-right corner, so the chip drops below
        /// it (VaultLivery.ChipTopWhenTiered = 84) rather than fighting it.</summary>
        public Thickness ChipMargin => Tier > 0 ? new Thickness(0, 84, 8, 0) : new Thickness(0, 8, 8, 0);

        public string ChipText => Loc.Get(State == ExclusiveCardState.PassReady
            ? "exclusives_chip_pass_ready"
            : "exclusives_chip_unlocked");

        // Teal for owned, gold for a pass that is ready to burn - ApplyExclusiveCardState's own
        // three-brush recipe per state, alphas included.
        public IBrush ChipForeground => State == ExclusiveCardState.PassReady
            ? Brush("#FFD27A") : Brush("#7FE7E0");

        public IBrush ChipBackground => State == ExclusiveCardState.PassReady
            ? Brush("#33FFD27A") : Brush("#2E7FE7E0");

        public IBrush ChipBorderBrush => State == ExclusiveCardState.PassReady
            ? Brush("#73FFD27A") : Brush("#667FE7E0");

        /// <summary>
        /// The card's resting rim. WPF takes the untiered one from the active mod's accent
        /// (ExclusiveEdgeDefault) and overwrites a tiered card's with the constant vault livery.
        /// ponytail: the untiered rim is reachable today - CoreMods.AccentColorHex plus
        /// CoreMods.TryParseHexColor is what FxTheme's ExclusiveEdgeDefault reduces to. The tiered
        /// rim still needs ConditioningControlPanel/Features/VaultLivery.cs. Both literals here are
        /// what those two produce with the default mod, so nothing reads wrong meanwhile.
        /// </summary>
        public IBrush EdgeBrush => Tier > 0 ? Brush("#66FFC94E") : Brush("#4DB478FF");

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
