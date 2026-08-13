using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Behaviors;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Exclusives tab ("the Velvet Vault"): the registry-driven showcase that replaced
    // the launcher popup. ExclusiveFeature.All is the single source of truth - this
    // file turns each entry into a card and keeps entitlement chips/veils current.
    // Cards never block: ShowTab(key) always runs and the destination tab's own
    // premium gate does the enforcement, exactly as the popup items did.
    public partial class MainWindow
    {
        private sealed class ExclusiveCardUi
        {
            public required ExclusiveFeature Feature;
            public required Border Card;
            public required Image Art;
            public required TextBlock Title;
            public required Border Chip;
            public required TextBlock ChipText;
            public required Border Veil;
            public required TextBlock VeilLock;
            /// <summary>The gold FREE TODAY pill; collapsed on every other day. Only ever used on
            /// an UNTIERED card - a tiered one wears the stamped re-stamp composition instead.</summary>
            public required Border FreePill;
            public required TextBlock FreePillText;
            /// <summary>The pill's live pulse, kept so the next repaint can stop it.</summary>
            public Storyboard? FreeFx;
            public CardSheenAdorner? Sheen;
            /// <summary>The stamped tier badge, or null on an untiered card (Graded Intake).</summary>
            public TierBadge? Badge;
        }

        // The rim weights, the edge brushes and the "what does this surface wear" decision live in
        // Features\VaultLivery.cs - out here so the render suite can exercise them without a live
        // MainWindow. This file assembles cards; that one decides how they are dressed.

        private readonly List<ExclusiveCardUi> _exclusiveCards = new();
        private readonly List<TextBlock> _exclusiveTeaserMarks = new();

        /// <summary>The three teaser cards' outer borders, kept for the accent re-tint.</summary>
        private readonly List<Border> _exclusiveTeaserCards = new();

        /// <summary>
        /// Every accent-derived gradient stop this tab builds in code, with the alpha it was
        /// authored at and which half of the accent pair it belongs to. The vault's badges, pills
        /// and edges were literal Bambi pink/violet, so a Dronification user got a green room with
        /// pink furniture; keeping the stops lets the mod-switch refresh re-tint them in place
        /// instead of rebuilding the shelf. Code-behind brushes only - no theme dictionary is
        /// touched (a fresh cross-dictionary StaticResource in Resources/Theme/*.xaml is what kills
        /// the render suites).
        /// </summary>
        private readonly List<(GradientStop Stop, byte Alpha, bool Partner)> _exclusiveAccentStops = new();

        /// <summary>Title drop-shadows (cards and teasers) that wear the accent.</summary>
        private readonly List<DropShadowEffect> _exclusiveAccentShadows = new();

        private bool _exclusivesBuilt;
        private bool _exclusivesSheenRetryQueued;

        /// <summary>The spotlight's FREE TODAY pulse - same contract as ExclusiveCardUi.FreeFx.</summary>
        private Storyboard? _spotFreeFx;

        private static readonly FontFamily FredokaFont =
            new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka");

        // ---- FREE TODAY livery ---------------------------------------------------------
        // Gold, deliberately: the same gold the dashboard's ? box gift tag and the rail's
        // "pass ready" chip wear, so "open for one day only" reads the same everywhere.
        // Literal colours, never a theme StaticResource - see CLAUDE.md's BAML note.
        private static readonly Color FreeTodayGold = Color.FromRgb(0xFF, 0xD2, 0x7A);

        // The FREE TODAY gold edge moved to Features\VaultLivery.cs (VaultLivery.EdgeFree) along
        // with the rest of the livery vocabulary - same literal, one home. It is still deliberately
        // NOT re-tinted below: "gold means open for one day only" is a contract this tab, the
        // dashboard's ? box and the rail all keep.

        private static SolidColorBrush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ---- mod accent chrome ---------------------------------------------------------
        // Everything below is decor, and decor follows the active mod. The two exceptions stay
        // literal on purpose: the FREE TODAY gold above ("gold means one day only" is a contract
        // the dashboard and the rail also keep), and the tier plates, which are commerce.

        /// <summary>
        /// The vault's primary accent: the active mod's FX glow, which is the same chain
        /// (fxPalette → theme.filterColor → theme.accentColor → #FF69B4) the sheen, the padlock
        /// breath and the hover bloom already read. Bambi resolves to the #FF69B4 these surfaces
        /// used to hardcode, so the flagship mod looks unchanged.
        /// </summary>
        private static Color VaultAccent() => FxTheme.GlowColor;

        /// <summary>The teaser "?" silhouette's alpha over the dark plate.</summary>
        private const byte TeaserMarkAlpha = 0x8C;

        /// <summary>
        /// The second half of the vault's gradient pair. The shipped pair is #FF69B4 → #B478FF,
        /// which is a -59 degree hue rotation at the same saturation and value, so that is exactly
        /// what this applies to whatever accent the mod supplies. Deriving it (rather than reading
        /// a second mod colour) keeps the two-tone identity: every FxTheme slot falls back to the
        /// same accent, so a mod that only sets accentColor would otherwise flatten every gradient
        /// on the tab into one colour.
        /// </summary>
        private const double VaultPartnerHueShift = -59.0;

        internal static Color VaultPartner(Color accent) => ShiftHue(accent, VaultPartnerHueShift);

        private static Color VaultPartner() => VaultPartner(VaultAccent());

        // WithAlpha(Color, byte) - the vault's chrome is all one hue at several alphas - already
        // exists on this partial class (MainWindow.ProgramBanner.cs); reused rather than duplicated.

        /// <summary>
        /// Rotates a colour's hue by <paramref name="degrees"/>, preserving saturation, value and
        /// alpha. Round-trips through HSV rather than nudging channels so a green accent shifts to
        /// a neighbouring green-yellow the way pink shifts to violet.
        /// </summary>
        internal static Color ShiftHue(Color c, double degrees)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h;
            if (delta <= 0.0) h = 0;                                   // grey: hue is arbitrary
            else if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);

            h = (h + degrees) % 360;
            if (h < 0) h += 360;

            double v = max;
            double s = max <= 0 ? 0 : delta / max;

            double chroma = v * s;
            double x = chroma * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - chroma;

            (double R, double G, double B) rgb = h switch
            {
                < 60 => (chroma, x, 0),
                < 120 => (x, chroma, 0),
                < 180 => (0, chroma, x),
                < 240 => (0, x, chroma),
                < 300 => (x, 0, chroma),
                _ => (chroma, 0, x),
            };

            static byte Byte(double v01) => (byte)Math.Clamp(Math.Round((v01) * 255), 0, 255);
            return Color.FromArgb(c.A, Byte(rgb.R + m), Byte(rgb.G + m), Byte(rgb.B + m));
        }

        /// <summary>A live card's resting edge (the partner hue, 1px) - was a literal #4DB478FF.</summary>
        private static SolidColorBrush ExclusiveEdgeDefault() => Freeze(WithAlpha(VaultPartner(), 0x4D));

        /// <summary>The spotlight band's resting edge (the accent, 1px) - was a literal #66FF69B4.</summary>
        private static SolidColorBrush SpotlightEdgeDefault() => Freeze(WithAlpha(VaultAccent(), 0x66));

        /// <summary>
        /// An accent → partner gradient, with both stops registered for re-tinting. Every
        /// badge/pill gradient on this tab is built through here so the mod-switch refresh has one
        /// list to walk.
        /// </summary>
        private LinearGradientBrush AccentGradient(byte alphaA, byte alphaB, double angle = 45)
        {
            var a = new GradientStop(WithAlpha(VaultAccent(), alphaA), 0.0);
            var b = new GradientStop(WithAlpha(VaultPartner(), alphaB), 1.0);
            _exclusiveAccentStops.Add((a, alphaA, false));
            _exclusiveAccentStops.Add((b, alphaB, true));
            return new LinearGradientBrush(new GradientStopCollection { a, b }, angle);
        }

        /// <summary>An accent drop-shadow registered for re-tinting (title plates).</summary>
        private DropShadowEffect AccentTitleShadow(double blur = 10, double opacity = 0.7)
        {
            var fx = new DropShadowEffect
            {
                Color = VaultAccent(),
                BlurRadius = blur,
                ShadowDepth = 0,
                Opacity = opacity,
            };
            _exclusiveAccentShadows.Add(fx);
            return fx;
        }

        /// <summary>
        /// True when this exclusive is today's daily free unlock AND the account does not
        /// already own it. Mirrors the ? box's rule (MainWindow.Presets.cs): premium accounts
        /// own the whole pool, so they get the normal unlocked chip and no gift tag - the tag
        /// only ever means "open to you today, and only today".
        /// </summary>
        private static bool IsExclusiveFreeToday(ExclusiveFeature feature, ExclusiveGateState state) =>
            state == ExclusiveGateState.Locked
            && feature.DailyFreeKey != null
            && App.DailyFree?.IsFreeToday(feature.DailyFreeKey) == true;

        /// <summary>Spotlight = the first registry entry (the newest exclusive).</summary>
        internal void OpenExclusiveSpotlight() => ShowTab(ExclusiveFeature.All[0].Key);

        // ============================== build ==============================

        private void EnsureExclusivesBuilt()
        {
            if (_exclusivesBuilt || ExclusivesTab == null) return;
            _exclusivesBuilt = true;

            try
            {
                var spot = ExclusiveFeature.All[0];
                ApplySpotlightArt(spot);
                ExclusivesTab.TxtSpotTitle.Text = $"{spot.Emoji} {ExclusiveTitle(spot)}";
                ExclusivesTab.TxtSpotTagline.Text = Loc.Get(spot.TaglineLocKey);
                if (spot.BadgeLocKey != null)
                    ExclusivesTab.TxtSpotBadge.Text = Loc.Get(spot.BadgeLocKey);
                else
                    ExclusivesTab.SpotBadge.Visibility = Visibility.Collapsed;

                // EVERY feature gets a card - the spotlight is a highlight on top of
                // the collection, not a hole in it. (The hero and its card share the
                // same registry entry, so chips/veils/titles refresh identically.)
                foreach (var feature in ExclusiveFeature.All)
                    ExclusivesTab.ExclusivesShelf.Children.Add(BuildExclusiveCard(feature));

                // The shelf never just ends: three teaser silhouettes close it out,
                // one per unannounced feature. They are deliberately NOT registry
                // entries - no ShowTab key, no gate, no refresh contract - because
                // the features behind them don't exist yet.
                for (int i = 1; i <= 3; i++)
                    ExclusivesTab.ExclusivesShelf.Children.Add(BuildComingSoonCard(i));

                // Parks/resumes with tab switches like every other ambient canvas.
                RegisterTabFx("exclusives", ExclusivesTab.ExclusivesAmbientFx);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Exclusives shelf build failed");
            }
        }

        /// <summary>
        /// Paints the hero band's art. The band is roughly 5:1, so a card-aspect PNG
        /// would have to be cropped hard: features may ship a wide
        /// <see cref="ExclusiveFeature.BannerArtResource"/> cut for it. That banner is
        /// optional and may not exist yet (art lands after code), so a failed load
        /// falls back to the card art instead of leaving the band empty.
        /// Stretch stays UniformToFill - crop, never skew - under SpotArtHost's
        /// rounded clip.
        /// </summary>
        private void ApplySpotlightArt(ExclusiveFeature spot)
        {
            try
            {
                var img = ExclusivesTab.SpotArtImage;
                ImageSource? art = null;
                bool banner = false;

                if (!string.IsNullOrWhiteSpace(spot.BannerArtResource))
                {
                    // Hero band spans the whole tab (~1400 DIP) and Ken-Burns-zooms
                    // to 1.07, so it gets a wider decode cap than a 336-wide card
                    // (the shipped banner is 1376 wide, i.e. essentially native).
                    art = LoadPackImage(spot.BannerArtResource!, 1400);
                    banner = art != null;
                }

                art ??= LoadPackImage(ExclusiveArtPath(spot));
                // Assigned in place: the hero Image is built once in XAML and carries the Ken
                // Burns ScaleTransform, so a mod switch swaps the source, never the element.
                if (art != null) img.Source = art;

                // UniformToFill centres its crop, which is exactly right for banner
                // art composed around the central horizontal band. Card art used as a
                // fallback is not, so the zoom origin follows the feature's focal
                // point and the drift pushes the subject back into view.
                img.Stretch = Stretch.UniformToFill;
                img.RenderTransformOrigin = banner
                    ? new Point(0.5, 0.5)
                    : new Point(spot.FocalX, spot.FocalY);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Exclusives spotlight art failed");
            }
        }

        private Border BuildExclusiveCard(ExclusiveFeature feature)
        {
            var host = new Grid();

            // --- art, rounded-clipped, slightly zoomable on hover ---
            var art = new Image
            {
                Source = LoadPackImage(ExclusiveArtPath(feature)),
                Stretch = Stretch.UniformToFill,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var artHost = new Border { Child = art };
            Views.Tabs.ExclusivesTabView.RoundClipOnResize(artHost, 12);
            host.Children.Add(artHost);

            // --- bottom plate: gradient scrim + title + tagline ---
            var title = new TextBlock
            {
                Text = $"{feature.Emoji} {ExclusiveTitle(feature)}",
                FontFamily = FredokaFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Foreground = Brushes.White,
                Effect = AccentTitleShadow(),
            };
            var tagline = new TextBlock
            {
                Text = Loc.Get(feature.TaglineLocKey),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x93, 0xB8)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 30,
                Margin = new Thickness(0, 2, 0, 0),
            };
            var plateStack = new StackPanel { Children = { title, tagline } };
            var plate = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(12, 26, 12, 10),
                Child = plateStack,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromArgb(0x00, 0x0C, 0x0A, 0x18), 0.0),
                        new(Color.FromArgb(0xE0, 0x0C, 0x0A, 0x18), 0.55),
                        new(Color.FromArgb(0xF2, 0x0C, 0x0A, 0x18), 1.0),
                    },
                    new Point(0.5, 0), new Point(0.5, 1)),
            };
            host.Children.Add(plate);

            // --- entitlement chip (top-right; colors set per state in refresh) ---
            var chipText = new TextBlock { FontSize = 10, FontWeight = FontWeights.Bold };
            var chip = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // On a tiered card the badge owns this corner, so the chip drops below it rather
                // than fighting it. Both are top-right on purpose: entitlement and price belong in
                // the same column, read top to bottom.
                Margin = feature.Tier > 0
                    ? new Thickness(0, VaultLivery.ChipTopWhenTiered, 8, 0)
                    : new Thickness(0, 8, 8, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(1),
                Child = chipText,
            };
            host.Children.Add(chip);

            // --- FREE TODAY pill: takes the chip's corner on the one day this feature is
            //     the daily free unlock. Built for every card (collapsed) rather than on
            //     demand so the refresh never has to touch the visual tree - only
            //     visibility, text and the pulse. ---
            var freePillText = new TextBlock
            {
                Text = Loc.Get("mosaic_free_today"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x05)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            };
            var freePill = new Border
            {
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 3, 9, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xF0, 0xC2)),
                Background = new LinearGradientBrush(
                    FreeTodayGold, Color.FromRgb(0xFF, 0x9C, 0x4A), 45),
                // Grows from its own centre; the pill floats over art, so nothing reflows.
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            freePill.Child = freePillText;
            host.Children.Add(freePill);

            // Optional NEW/BETA badge, top-left, in the brand gradient.
            if (feature.BadgeLocKey != null)
            {
                host.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(8, 8, 0, 0),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = AccentGradient(0xFF, 0xFF),
                    Child = new TextBlock
                    {
                        Text = Loc.Get(feature.BadgeLocKey),
                        Foreground = Brushes.White,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                    },
                });
            }

            // --- locked veil: fog scrim + breathing padlock + unlock pill.
            //     Decoration only - the card still navigates, the destination gates. ---
            var veilLock = new TextBlock
            {
                Text = "🔒",
                FontSize = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var veil = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Background = new RadialGradientBrush(
                    Color.FromArgb(0x86, 0x1E, 0x16, 0x32),
                    Color.FromArgb(0xD6, 0x0C, 0x0A, 0x18)),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        veilLock,
                        new Border
                        {
                            Margin = new Thickness(0, 8, 0, 0),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(10, 4, 10, 4),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Background = AccentGradient(0xE6, 0xD9),
                            Child = new TextBlock
                            {
                                Text = Loc.Get("exclusives_chip_lab"),
                                Foreground = Brushes.White,
                                FontSize = 10,
                                FontWeight = FontWeights.Bold,
                            },
                        },
                    },
                },
            };
            Views.Tabs.ExclusivesTabView.RoundClipOnResize(veil, 12);
            host.Children.Add(veil);

            // --- the tier badge: the neon sign pinned over the art's top-right corner. Built ONLY
            //     for tiered features, and added last inside the host so it sits above the veil -
            //     a locked card must still advertise what it costs, which is the whole job of a
            //     price tag. Its state (and the FREE TODAY re-stamp) is set in the refresh. ---
            TierBadge? badge = null;
            if (feature.Tier > 0)
            {
                badge = new TierBadge
                {
                    Tier = feature.Tier,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    // Tucked over the corner so it reads as pinned ON the card, not laid inside it.
                    Margin = new Thickness(0, VaultLivery.CardBadgeTopMargin, -6, 0),
                };
                host.Children.Add(badge);
            }

            var card = new Border
            {
                Width = 336,
                Height = 200,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                // Mod-aware resting edge (lane B): the shelf's furniture follows the active mod.
                // A tiered card overwrites this with its livery on the first refresh - the livery
                // is commerce and stays constant across mods - but the untiered cards keep it.
                BorderBrush = ExclusiveEdgeDefault(),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = host,
            };

            card.MouseEnter += (_, _) => OnExclusiveCardHover(card, art, true);
            card.MouseLeave += (_, _) => OnExclusiveCardHover(card, art, false);
            card.MouseLeftButtonUp += (_, _) => ShowTab(feature.Key);

            _exclusiveCards.Add(new ExclusiveCardUi
            {
                Feature = feature,
                Card = card,
                Art = art,
                Title = title,
                Chip = chip,
                ChipText = chipText,
                Veil = veil,
                VeilLock = veilLock,
                FreePill = freePill,
                FreePillText = freePillText,
                Badge = badge,
            });
            return card;
        }

        /// <summary>
        /// A "coming soon" teaser: same card geometry as the collection, but a dark
        /// silhouette - big breathing "?" instead of art, SOON badge, "???" title.
        /// Not clickable (arrow cursor, no navigation) and kept out of
        /// _exclusiveCards so the entitlement refresh never touches it; only the
        /// breathing mark is re-painted (see RefreshExclusivesTab) so it obeys the
        /// same motion/perf gates as the veil padlocks.
        /// </summary>
        private Border BuildComingSoonCard(int ordinal)
        {
            var host = new Grid();

            var mark = new TextBlock
            {
                Text = "?",
                FontFamily = FredokaFont,
                FontSize = 64,
                FontWeight = FontWeights.SemiBold,
                Foreground = Freeze(WithAlpha(VaultAccent(), TeaserMarkAlpha)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // Sit in the open area above the title plate, like the art focal.
                Margin = new Thickness(0, 0, 0, 34),
            };
            host.Children.Add(mark);
            _exclusiveTeaserMarks.Add(mark);
            ApplyVeilLockBreath(mark, true);

            var title = new TextBlock
            {
                Text = $"{TeaserEmoji(ordinal)} ???",
                FontFamily = FredokaFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Foreground = Brushes.White,
                Effect = AccentTitleShadow(),
            };
            var tagline = new TextBlock
            {
                Text = Loc.Get($"exclusives_soon_tag_{ordinal}"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x93, 0xB8)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 30,
                Margin = new Thickness(0, 2, 0, 0),
            };
            host.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(12, 26, 12, 10),
                Child = new StackPanel { Children = { title, tagline } },
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromArgb(0x00, 0x0C, 0x0A, 0x18), 0.0),
                        new(Color.FromArgb(0xE0, 0x0C, 0x0A, 0x18), 0.55),
                        new(Color.FromArgb(0xF2, 0x0C, 0x0A, 0x18), 1.0),
                    },
                    new Point(0.5, 0), new Point(0.5, 1)),
            });

            // SOON badge, top-left, same brand gradient as NEW/BETA.
            host.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 8, 0, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Background = AccentGradient(0xFF, 0xFF),
                Child = new TextBlock
                {
                    Text = Loc.Get("exclusives_badge_soon"),
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                },
            });

            // Rounded clip so the vault-glow backdrop and plate respect the corner
            // radius (same helper the art and veil use on real cards).
            var fill = new Border
            {
                Child = host,
                Background = new RadialGradientBrush(
                    Color.FromArgb(0x5C, 0x2A, 0x1E, 0x46),
                    Color.FromArgb(0xFF, 0x11, 0x0E, 0x20)),
            };
            Views.Tabs.ExclusivesTabView.RoundClipOnResize(fill, 12);

            var card = new Border
            {
                Width = 336,
                Height = 200,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x26)),
                // Dimmer edge than a live card: present, but clearly not open yet.
                BorderBrush = Freeze(WithAlpha(VaultPartner(), 0x33)),
                BorderThickness = new Thickness(1),
                Child = fill,
            };

            // Alive to the touch but honest about it: lifts on hover, arrow cursor,
            // no navigation - there is nowhere to go yet.
            card.MouseEnter += (_, _) => { try { MotionFx.HoverLift(card, true); } catch { } };
            card.MouseLeave += (_, _) => { try { MotionFx.HoverLift(card, false); } catch { } };
            _exclusiveTeaserCards.Add(card);
            return card;
        }

        private static string TeaserEmoji(int ordinal) => ordinal switch
        {
            1 => "🔮",
            2 => "💗",
            _ => "🤫",
        };

        private static void OnExclusiveCardHover(Border card, Image art, bool on)
        {
            try
            {
                MotionFx.HoverLift(card, on);

                if (on && PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier))
                {
                    card.Effect ??= new DropShadowEffect
                    {
                        BlurRadius = Math.Min(24, PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier)),
                        ShadowDepth = 0,
                        Opacity = 0.55,
                    };
                    // Re-read every hover, not just on the ??= that created it: the effect is
                    // cached on the card for the life of the window, so a bloom born under one
                    // mod kept that mod's colour forever (same defect as the veil padlock).
                    if (card.Effect is DropShadowEffect bloom) bloom.Color = FxTheme.GlowColor;
                }
                else
                {
                    card.ClearValue(UIElement.EffectProperty);
                }

                // Gentle art zoom under the rounded clip. This is the shared "hover pop" (1.06 on
                // a back-ease overshoot plus a two-degree wobble) rather than the bespoke flat
                // 1.05 it used to run, so vault art settles the same way dashboard tiles, quest
                // art and achievement badges do. Driven from the card's hover, not the Image's
                // own, because the veil and the title band sit over the art. HoverPop keeps the
                // reduced-motion gate for us.
                if (on) HoverPop.Enter(art); else HoverPop.Leave(art);
            }
            catch (Exception ex) { App.Logger?.Debug("Exclusives card hover: {E}", ex.Message); }
        }

        // ============================== refresh ==============================

        /// <summary>
        /// Repaints every entitlement surface on the tab (chips, veils, tier plates,
        /// mod-aware Takeover title). Called from UpdatePatreonUI and on tab show,
        /// mirroring the old RefreshExclusivesSubmenuLocks contract.
        ///
        /// <para>It is ALSO the tab's mod-switch repaint: ApplyModFeatureNames calls it from
        /// ModService.ModChanged (Dispatcher.Invoke'd, so this always runs on the UI thread), and
        /// the resolver's caches are cleared before that event fires. Everything a mod can change
        /// - art, accent chrome, the sheen's tint - is therefore re-applied here rather than only
        /// at build time, which is what it used to be.</para>
        /// </summary>
        internal void RefreshExclusivesTab()
        {
            // Lazily built on first tab show (ShowTab calls EnsureExclusivesBuilt);
            // until then there is nothing to repaint and startup pays nothing.
            if (ExclusivesTab == null || !_exclusivesBuilt) return;

            try
            {
                RetintVaultChrome();

                foreach (var ui in _exclusiveCards)
                {
                    // Just Drop is the one card that can be ABSENT rather than veiled. Every other
                    // feature on this shelf exists for everybody and is merely gated by tier, so a
                    // padlock is the honest treatment; this one does not exist at all until the
                    // server opens the door, and advertising it would be selling a thing that
                    // cannot be bought. Everything below still runs for it, so the card is fully
                    // painted the moment it is revealed mid-session.
                    if (string.Equals(ui.Feature.Key, "justdrop", StringComparison.Ordinal))
                        ui.Card.Visibility = Services.JustDrop.JustDropService.DoorAvailable
                            ? Visibility.Visible
                            : Visibility.Collapsed;

                    // Mod-aware titles (Drone mod -> "Drone Takeover", etc.).
                    ui.Title.Text = $"{ui.Feature.Emoji} {ExclusiveTitle(ui.Feature)}";
                    // ...and mod-aware art. Assigned in place on the existing Image: the shelf is
                    // built once, so without this a .ccpmod's cards stayed on the embedded art
                    // until the app was restarted. Null keeps whatever is painted.
                    var art = LoadPackImage(ExclusiveArtPath(ui.Feature));
                    if (art != null) ui.Art.Source = art;
                    ApplyExclusiveCardState(ui, ui.Feature.GateState());
                }

                // Teaser "?" marks breathe under the same motion/perf gates as the
                // veil padlocks, so a tier change repaints them here too.
                foreach (var mark in _exclusiveTeaserMarks)
                    ApplyVeilLockBreath(mark, true);

                // Spotlight veil follows the same probe - including the daily free unlock, so
                // the hero can never sit padlocked above its own card wearing FREE TODAY.
                var spot = ExclusiveFeature.All[0];
                var spotState = spot.GateState();
                bool spotFree = IsExclusiveFreeToday(spot, spotState);
                ExclusivesTab.TxtSpotTitle.Text = $"{spot.Emoji} {ExclusiveTitle(spot)}";
                ApplySpotlightArt(spot);   // banner/card art, in place on the hero Image

                // The header plate's vault art. ImageSource is swapped ON the existing brush -
                // replacing the brush would drop the OpacityMask fade the header is composed with.
                var heroArt = ModTileVariant("vault", ExclusiveHeroDecodeWidth);
                if (heroArt != null && ExclusivesTab.VaultHeroBrush is { IsFrozen: false } heroBrush)
                    heroBrush.ImageSource = heroArt;
                ExclusivesTab.SpotVeil.Visibility =
                    spotState == ExclusiveGateState.Locked && !spotFree ? Visibility.Visible : Visibility.Collapsed;
                ApplyVeilLockBreath(ExclusivesTab.SpotVeilLock, ExclusivesTab.SpotVeil.Visibility == Visibility.Visible);

                // Hero weight of the same livery the shelf wears (4px, not the cards' 3), plus the
                // stamped sign. A tiered spotlight says its free day with the re-stamp, so the
                // gold pill is only asked for when the hero has no badge to stamp over.
                // Hero weight of the same livery the shelf wears (4px, not the cards' 3), plus the
                // stamped sign. The band's resting edge is the mod-aware one (lane B); the livery
                // that overwrites it on a tiered spotlight is not, by design. VaultLivery.Apply
                // owns BOTH writes, which is why the old unconditional BorderBrush/Thickness pair
                // is gone rather than moved - two writers is how the two states drift apart.
                bool spotPill = VaultLivery.Apply(
                    ExclusivesTab.SpotlightCard, ExclusivesTab.SpotTierBadge,
                    spot.Tier, spotFree, SpotlightEdgeDefault(), VaultLivery.SpotlightRim);

                ExclusivesTab.TxtSpotFreeToday.Text = Loc.Get("mosaic_free_today");
                ExclusivesTab.SpotFreeToday.Visibility = spotPill ? Visibility.Visible : Visibility.Collapsed;
                _spotFreeFx = ApplyFreeTodayPulse(ExclusivesTab.SpotFreeToday, _spotFreeFx, spotPill);

                RefreshExclusiveTierPlates();
                RestartExclusiveSheens();

                // Opportunistic server-override check, exactly as RefreshMosaicTierBadges does:
                // the 6h/10min gates inside make this free on every repaint, so a user parked on
                // the vault catches a same-day override without this tab owning a clock.
                _ = App.DailyFree?.RefreshAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshExclusivesTab failed");
            }
        }

        /// <summary>
        /// Re-tints every accent-derived surface this tab paints in code, plus the three the view
        /// authors in XAML (the spotlight title's shadow, its NEW badge and its unlock pill).
        /// Called at the top of every refresh, so it lands on a mod switch, a tier change and a
        /// tab revisit alike - the cost is a couple of dozen colour writes.
        ///
        /// <para><b>Not re-tinted, deliberately:</b> the FREE TODAY gold family (a documented
        /// contract - gold means "open for one day only" here, on the dashboard's ? box and on the
        /// rail alike) and the tier plates (commerce, not decor). The view's vault-gold TabHue*
        /// keys are the same tier livery and stay put.</para>
        /// </summary>
        private void RetintVaultChrome()
        {
            try
            {
                var accent = VaultAccent();
                var partner = VaultPartner(accent);

                foreach (var (stop, alpha, isPartner) in _exclusiveAccentStops)
                    stop.Color = WithAlpha(isPartner ? partner : accent, alpha);

                foreach (var fx in _exclusiveAccentShadows)
                    fx.Color = accent;

                foreach (var mark in _exclusiveTeaserMarks)
                    mark.Foreground = Freeze(WithAlpha(accent, TeaserMarkAlpha));

                foreach (var teaser in _exclusiveTeaserCards)
                    teaser.BorderBrush = Freeze(WithAlpha(partner, 0x33));

                // XAML-authored, so these are reached by name rather than by list.
                TintShadow(ExclusivesTab.TxtSpotTitle, accent);
                TintGradient(ExclusivesTab.SpotBadgeFill, accent, partner);
                TintGradient(ExclusivesTab.SpotVeilPillFill, accent, partner);
            }
            catch (Exception ex) { App.Logger?.Debug("Exclusives chrome re-tint: {E}", ex.Message); }
        }

        /// <summary>
        /// Recolours an element's drop-shadow. A XAML-authored Freezable may arrive frozen
        /// depending on where the parser found it, so this clones rather than assuming: a throwing
        /// re-tint would abort the rest of the repaint.
        /// </summary>
        private static void TintShadow(UIElement element, Color color)
        {
            if (element?.Effect is not DropShadowEffect shadow) return;
            if (!shadow.IsFrozen) { shadow.Color = color; return; }

            var thawed = shadow.Clone();
            thawed.Color = color;
            element.Effect = thawed;
        }

        /// <summary>
        /// Rewrites a two-stop accent gradient's colours in place, keeping each stop's authored
        /// alpha. Frozen brushes are skipped rather than replaced - the caller holds them through
        /// the view's generated field, and swapping the brush would not reach the Border anyway.
        /// </summary>
        private static void TintGradient(GradientBrush? brush, Color accent, Color partner)
        {
            if (brush == null || brush.IsFrozen || brush.GradientStops.Count < 2) return;
            var a = brush.GradientStops[0];
            var b = brush.GradientStops[1];
            a.Color = WithAlpha(accent, a.Color.A);
            b.Color = WithAlpha(partner, b.Color.A);
        }

        private void ApplyExclusiveCardState(ExclusiveCardUi ui, ExclusiveGateState state)
        {
            // The daily free unlock outranks the padlock. On its one day, the pool feature's
            // card opens exactly like an owned one - no veil, no dimmed art - and swaps its
            // entitlement chip for the gold FREE TODAY pill. Nothing here enforces anything:
            // the destination's TierGate call already ORs in DailyFreeService, so the card is
            // simply telling the truth about a door that really is open.
            bool freeToday = IsExclusiveFreeToday(ui.Feature, state);

            // Rim + badge in one place for both branches, so a tiered card cannot end a repaint
            // wearing the untiered violet edge. Returns true only when there is no badge to
            // re-stamp, which is the one case still needing the old gold pill.
            // The untiered resting edge is the mod-aware one (lane B) and is passed IN rather than
            // looked up, so the one place that knows the vault's accent is the vault.
            bool wantPill = VaultLivery.Apply(ui.Card, ui.Badge, ui.Feature.Tier, freeToday,
                                              ExclusiveEdgeDefault());

            if (freeToday)
            {
                ui.Veil.Visibility = Visibility.Collapsed;
                ui.Art.Opacity = 1.0;
                ui.Chip.Visibility = Visibility.Collapsed;
                ui.FreePillText.Text = Loc.Get("mosaic_free_today");
                ui.FreePill.Visibility = wantPill ? Visibility.Visible : Visibility.Collapsed;
                ApplyVeilLockBreath(ui.VeilLock, false);
                ui.FreeFx = ApplyFreeTodayPulse(ui.FreePill, ui.FreeFx, wantPill);
                return;
            }

            ui.FreePill.Visibility = Visibility.Collapsed;
            ui.FreeFx = ApplyFreeTodayPulse(ui.FreePill, ui.FreeFx, false);
            // No BorderBrush/Thickness pair here any more: VaultLivery.Apply above already wrote
            // the rim for this branch too, with the mod-aware edge on untiered cards and the
            // (mod-agnostic) livery on tiered ones.

            switch (state)
            {
                case ExclusiveGateState.Unlocked:
                    ui.Veil.Visibility = Visibility.Collapsed;
                    ui.Art.Opacity = 1.0;
                    ui.Chip.Visibility = Visibility.Visible;
                    ui.ChipText.Text = Loc.Get("exclusives_chip_unlocked");
                    ui.ChipText.Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xE7, 0xE0));
                    ui.Chip.Background = new SolidColorBrush(Color.FromArgb(0x2E, 0x7F, 0xE7, 0xE0));
                    ui.Chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x7F, 0xE7, 0xE0));
                    break;

                case ExclusiveGateState.PassReady:
                    ui.Veil.Visibility = Visibility.Collapsed;
                    ui.Art.Opacity = 1.0;
                    ui.Chip.Visibility = Visibility.Visible;
                    ui.ChipText.Text = Loc.Get("exclusives_chip_pass_ready");
                    ui.ChipText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x7A));
                    ui.Chip.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xD2, 0x7A));
                    ui.Chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xD2, 0x7A));
                    break;

                default:
                    ui.Veil.Visibility = Visibility.Visible;
                    ui.Art.Opacity = 0.75;
                    ui.Chip.Visibility = Visibility.Collapsed;
                    break;
            }
            ApplyVeilLockBreath(ui.VeilLock, ui.Veil.Visibility == Visibility.Visible);
        }

        /// <summary>
        /// Breathing glow behind a veil padlock - same recipe as PremiumGateFx
        /// (DropShadow at zero depth, opacity-only animation), same park rules.
        /// </summary>
        private static void ApplyVeilLockBreath(TextBlock padlock, bool on)
        {
            try
            {
                var tier = PerformanceProfile.CurrentTier;
                bool want = on && MotionFx.AllowAmbientLoops && PerformanceProfile.AllowGlow(tier);
                if (!want)
                {
                    if (padlock.Effect is DropShadowEffect old)
                        old.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    padlock.ClearValue(UIElement.EffectProperty);
                    return;
                }

                if (padlock.Effect is not DropShadowEffect glow)
                {
                    padlock.Effect = glow = new DropShadowEffect
                    {
                        BlurRadius = Math.Min(20, PerformanceProfile.MaxGlowBlurRadius(tier)),
                        ShadowDepth = 0,
                        Opacity = 0.8,
                    };
                }
                // Unconditional, not just on the create branch: the effect survives every repaint,
                // so a padlock built under one mod kept that mod's glow for the life of the app.
                // The effect is code-built and never frozen, so this is a plain write.
                glow.Color = FxTheme.GlowColor;
                var anim = new DoubleAnimation(0.35, 0.9, TimeSpan.FromSeconds(3.4))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("Exclusives lock breath: {E}", ex.Message); }
        }

        /// <summary>
        /// The FREE TODAY pill's pulse: a slow opacity breath plus a barely-there 1.06 scale,
        /// on one controllable <see cref="Storyboard"/> so the next repaint can stop it cleanly
        /// (a stray Forever clock on a hidden pill is exactly how the motion kill-switch gets
        /// quietly defeated).
        ///
        /// <para><b>Gates, in order:</b> the glow only appears where
        /// <see cref="PerformanceProfile.AllowGlow"/> says it may, and the clock only starts at
        /// <see cref="MotionFx.AllowAmbientLoops"/> - Reduced/Off motion and low tiers get a
        /// static gold pill, which still says everything it needs to. Frame rate is capped at
        /// <see cref="AmbientFrameRate"/> like every other ambient loop on this tab.</para>
        /// </summary>
        /// <param name="existing">The pill's previous storyboard, or null.</param>
        /// <returns>The new storyboard to remember, or null when nothing is running.</returns>
        private static Storyboard? ApplyFreeTodayPulse(Border pill, Storyboard? existing, bool on)
        {
            try
            {
                // Always park first: this is called on every repaint, and a second Begin on a
                // pill that is already breathing would stack clocks.
                if (existing != null)
                {
                    existing.Stop(pill);
                    existing.Remove(pill);
                }
                pill.BeginAnimation(UIElement.OpacityProperty, null);
                pill.Opacity = 1.0;
                if (pill.RenderTransform is ScaleTransform rest) rest.ScaleX = rest.ScaleY = 1.0;

                if (!on)
                {
                    pill.ClearValue(UIElement.EffectProperty);
                    return null;
                }

                var tier = PerformanceProfile.CurrentTier;
                if (PerformanceProfile.AllowGlow(tier))
                {
                    if (pill.Effect is not DropShadowEffect)
                    {
                        pill.Effect = new DropShadowEffect
                        {
                            Color = FreeTodayGold,
                            BlurRadius = Math.Min(16, PerformanceProfile.MaxGlowBlurRadius(tier)),
                            ShadowDepth = 0,
                            Opacity = 0.7,
                        };
                    }
                }
                else
                {
                    pill.ClearValue(UIElement.EffectProperty);
                }

                if (!MotionFx.AllowAmbientLoops) return null;   // static gold pill, no clock

                var sb = new Storyboard();

                var fade = new DoubleAnimation(0.72, 1.0, TimeSpan.FromSeconds(1.9))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(fade, AmbientFrameRate);
                Storyboard.SetTarget(fade, pill);
                Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(fade);

                if (pill.RenderTransform is ScaleTransform)
                {
                    foreach (var axis in new[] { "ScaleX", "ScaleY" })
                    {
                        var swell = new DoubleAnimation(1.0, 1.06, TimeSpan.FromSeconds(1.9))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        };
                        Timeline.SetDesiredFrameRate(swell, AmbientFrameRate);
                        Storyboard.SetTarget(swell, pill);
                        Storyboard.SetTargetProperty(swell, new PropertyPath(
                            $"(UIElement.RenderTransform).(ScaleTransform.{axis})"));
                        sb.Children.Add(swell);
                    }
                }

                sb.Begin(pill, true);
                return sb;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Exclusives free-today pulse: {E}", ex.Message);
                return null;
            }
        }

        private void RefreshExclusiveTierPlates()
        {
            var p1 = ExclusivesTab.TierPlate1;
            var p2 = ExclusivesTab.TierPlate2;
            if (p1 == null || p2 == null) return;

            var tier = App.Patreon?.CurrentTier ?? PatreonTier.None;
            // Whitelist is permanent top tier by policy.
            bool topTier = tier >= PatreonTier.Level2 || App.Patreon?.IsWhitelisted == true;
            bool premium = App.Patreon?.HasPremiumAccess == true;

            MotionFx.Stop(p1);
            MotionFx.Stop(p2);
            if (topTier)
            {
                p1.Opacity = 0.55;
                p2.Opacity = 1.0;
                MotionFx.GlowBreath(p2, 0.75, 1.0);
            }
            else if (premium)
            {
                p1.Opacity = 1.0;
                p2.Opacity = 0.3;
                MotionFx.GlowBreath(p1, 0.75, 1.0);
            }
            else
            {
                p1.Opacity = 0.3;
                p2.Opacity = 0.3;
            }
        }

        // ============================== motion ==============================

        /// <summary>
        /// Starts the tab's ambient motion: the room's fog/dust/aurora canvas, the
        /// spotlight's Ken Burns drift, and the card sheen adorners. Everything here
        /// is gated on MotionFx/PerformanceProfile and parked again by
        /// StopExclusivesMotion on the way out of the tab.
        /// </summary>
        private void StartExclusivesMotion()
        {
            if (!_exclusivesBuilt) return;
            try
            {
                ExclusivesTab.ExclusivesAmbientFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField | AmbientFxLayers.AuroraWash,
                    Intensity = 0.55,
                    FogPuffs = 3,
                });

                if (MotionFx.AllowAmbientLoops)
                {
                    var drift = new DoubleAnimation(1.0, 1.07, TimeSpan.FromSeconds(26))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(drift, AmbientFrameRate);
                    ExclusivesTab.SpotArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
                    ExclusivesTab.SpotArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, drift);

                    AttachExclusiveSheens();
                }

                // The tier livery's own loops. OUTSIDE the AllowAmbientLoops branch above because
                // each of these re-reads the gate for itself and settles into its static look when
                // it is shut - calling them either way is what makes a motion-level change land on
                // the next visit to the tab instead of needing a rebuild.
                foreach (var ui in _exclusiveCards)
                {
                    TierFxBorder.Resume(ui.Card);
                    ui.Badge?.StartMotion();
                }
                TierFxBorder.Resume(ExclusivesTab.SpotlightCard);
                ExclusivesTab.SpotTierBadge?.StartMotion();
            }
            catch (Exception ex) { App.Logger?.Debug("StartExclusivesMotion: {E}", ex.Message); }
        }

        /// <summary>Parks every loop this tab started. Runs at the top of ShowTab.</summary>
        private void StopExclusivesMotion()
        {
            if (!_exclusivesBuilt) return;
            try
            {
                ExclusivesTab.SpotArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ExclusivesTab.SpotArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                foreach (var ui in _exclusiveCards)
                {
                    ui.Sheen?.Stop();
                    TierFxBorder.Park(ui.Card);
                    ui.Badge?.StopMotion();
                }
                TierFxBorder.Park(ExclusivesTab.SpotlightCard);
                ExclusivesTab.SpotTierBadge?.StopMotion();
                // The AmbientFxCanvas parks itself via SwitchTabFx (it's registered).
            }
            catch (Exception ex) { App.Logger?.Debug("StopExclusivesMotion: {E}", ex.Message); }
        }

        /// <summary>
        /// The periodic glass sheen every card wears. Adorner layers don't exist until
        /// the shelf has rendered once, so this retries a bounded number of times at
        /// Background priority. Background is BELOW Render on purpose: a self-requeue
        /// at Normal priority (which outranks Render) never lets the first render
        /// happen and freezes the UI thread - that exact livelock shipped in the first
        /// cut of this tab. The retry counter resets once every card has its sheen,
        /// and a tab revisit calls this again anyway.
        /// </summary>
        private int _exclusivesSheenRetries;

        /// <summary>
        /// THE HEADLINE BUG. A <see cref="CardSheenAdorner"/> reads its tint at
        /// <see cref="CardSheenAdorner.Start"/>, and the adorners are built once and then only
        /// re-Started when the tab is entered - so switching from Dronification's green to
        /// BambiSleep left every card glimmering green until the app was restarted. The mod-switch
        /// repaint has to restart them itself.
        ///
        /// <para>Stop-then-Start rather than a bespoke re-tint entry point: Start already re-reads
        /// the theme, and this way there is exactly one place a sheen's colour comes from. Only
        /// runs while the tab is on screen with ambient motion allowed - off-tab sheens are parked
        /// by StopExclusivesMotion and will be re-tinted by the Start on the way back in, and
        /// starting a clock the motion gate refused is how the kill-switch gets defeated.</para>
        ///
        /// <para>The spotlight wears no sheen (AttachExclusiveSheens only decorates shelf cards);
        /// its Ken Burns drift is colourless, so there is nothing there to re-tint.</para>
        /// </summary>
        private void RestartExclusiveSheens()
        {
            try
            {
                if (ExclusivesTab?.Visibility != Visibility.Visible || !MotionFx.AllowAmbientLoops) return;
                foreach (var ui in _exclusiveCards)
                {
                    if (ui.Sheen == null) continue;
                    ui.Sheen.Stop();
                    ui.Sheen.Start();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Exclusives sheen restart: {E}", ex.Message); }
        }

        private void AttachExclusiveSheens()
        {
            bool missing = false;
            foreach (var ui in _exclusiveCards)
            {
                if (ui.Sheen != null)
                {
                    ui.Sheen.Start();
                    continue;
                }
                var layer = AdornerLayer.GetAdornerLayer(ui.Card);
                if (layer == null) { missing = true; continue; }
                ui.Sheen = new CardSheenAdorner(ui.Card, 12);
                layer.Add(ui.Sheen);
                ui.Sheen.Start();
            }

            if (!missing)
            {
                _exclusivesSheenRetries = 0;
                return;
            }

            if (!_exclusivesSheenRetryQueued && _exclusivesSheenRetries < 5)
            {
                _exclusivesSheenRetryQueued = true;
                _exclusivesSheenRetries++;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _exclusivesSheenRetryQueued = false;
                    try
                    {
                        if (ExclusivesTab?.Visibility == Visibility.Visible && MotionFx.AllowAmbientLoops)
                            AttachExclusiveSheens();
                    }
                    catch (Exception ex) { App.Logger?.Debug("Exclusives sheen retry: {E}", ex.Message); }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // ============================== helpers ==============================

        private static string ExclusiveTitle(ExclusiveFeature feature) =>
            feature.Key == "bambitakeover"
                ? App.Mods?.GetTakeoverLabel() ?? Loc.Get(feature.TitleLocKey)
                : Loc.Get(feature.TitleLocKey);

        /// <summary>
        /// The art path this feature's card should paint. Normally the registry's own
        /// <see cref="ExclusiveFeature.ArtResource"/> - except for Takeover, whose art forks by
        /// filename under BambiSleep exactly as it does in LoadFeatureImages (MainWindow.xaml.cs)
        /// and in ModTileVariant. The registry cannot express that: it is one static entry and the
        /// fork is a runtime question, so the vault answered "takeover.png" forever while the
        /// feature's own tab, the wall tile and the description card all showed Bambi's cut.
        /// A .ccpmod overriding either path still wins - both go through the resolver below.
        /// </summary>
        private static string ExclusiveArtPath(ExclusiveFeature feature) =>
            feature.Key == "bambitakeover" && App.Mods?.ActiveModId == BuiltInMods.BambiSleepId
                ? "features/bambi takeover.png"
                : feature.ArtResource;

        /// <summary>
        /// Card decode cap. A card paints 336 DIP, so this is already ~2x for a 200% display;
        /// nine sources at 1376x768 native would cost ~32 MB of bitmaps.
        /// </summary>
        private const int ExclusiveCardDecodeWidth = 700;

        /// <summary>The header's 240-wide vault plate, matching the XAML's authored DecodePixelWidth.</summary>
        private const int ExclusiveHeroDecodeWidth = 480;

        /// <summary>
        /// Resolves card art THROUGH THE MOD CHAIN (event skin → active mod's resources/ →
        /// embedded pack://), or null if nothing in it decodes - callers rely on that null to fall
        /// back (see <see cref="ApplySpotlightArt"/>), so this must never throw.
        ///
        /// <para>This used to build a raw <c>pack://application:,,,/</c> URI, which meant the one
        /// tab whose entire job is showing off features was also the one place a .ccpmod's art for
        /// those features was ignored. <see cref="ModResourceResolver.ResolvePackPath"/> tolerates
        /// the registry's "Resources/"-prefixed spelling, so no roster data had to change.</para>
        /// </summary>
        /// <param name="decodePixelWidth">
        /// Bounded decode width; the hero band passes a larger value because it is drawn wider.
        /// </param>
        private static ImageSource? LoadPackImage(string relativePath, int decodePixelWidth = ExclusiveCardDecodeWidth)
        {
            try
            {
                var art = ModResourceResolver.ResolvePackPath(relativePath, decodePixelWidth);
                if (art == null)
                    App.Logger?.Warning("Exclusives art missing: {Path}", relativePath);
                return art;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Exclusives art missing: {Path} ({E})", relativePath, ex.Message);
                return null;
            }
        }
    }
}
