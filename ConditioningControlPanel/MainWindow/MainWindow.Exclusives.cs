using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Behaviors;
using ConditioningControlPanel.Controls;
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
            public CardSheenAdorner? Sheen;
        }

        private readonly List<ExclusiveCardUi> _exclusiveCards = new();
        private readonly List<TextBlock> _exclusiveTeaserMarks = new();
        private bool _exclusivesBuilt;
        private bool _exclusivesSheenRetryQueued;

        private static readonly FontFamily FredokaFont =
            new(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka");

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

                art ??= LoadPackImage(spot.ArtResource);
                img.Source = art;

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
                Source = LoadPackImage(feature.ArtResource),
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
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.7,
                },
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
                Margin = new Thickness(0, 8, 8, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                BorderThickness = new Thickness(1),
                Child = chipText,
            };
            host.Children.Add(chip);

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
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0xFF, 0x69, 0xB4), Color.FromRgb(0xB4, 0x78, 0xFF), 45),
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
                            Background = new LinearGradientBrush(
                                Color.FromArgb(0xE6, 0xFF, 0x69, 0xB4),
                                Color.FromArgb(0xD9, 0xB4, 0x78, 0xFF), 45),
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

            var card = new Border
            {
                Width = 336,
                Height = 200,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xB4, 0x78, 0xFF)),
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
                Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0x69, 0xB4)),
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
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.7,
                },
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
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x69, 0xB4), Color.FromRgb(0xB4, 0x78, 0xFF), 45),
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
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xB4, 0x78, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = fill,
            };

            // Alive to the touch but honest about it: lifts on hover, arrow cursor,
            // no navigation - there is nowhere to go yet.
            card.MouseEnter += (_, _) => { try { MotionFx.HoverLift(card, true); } catch { } };
            card.MouseLeave += (_, _) => { try { MotionFx.HoverLift(card, false); } catch { } };
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
                        Color = FxTheme.GlowColor,
                        BlurRadius = Math.Min(24, PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier)),
                        ShadowDepth = 0,
                        Opacity = 0.55,
                    };
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
        /// </summary>
        internal void RefreshExclusivesTab()
        {
            // Lazily built on first tab show (ShowTab calls EnsureExclusivesBuilt);
            // until then there is nothing to repaint and startup pays nothing.
            if (ExclusivesTab == null || !_exclusivesBuilt) return;

            try
            {
                foreach (var ui in _exclusiveCards)
                {
                    // Mod-aware titles (Drone mod -> "Drone Takeover", etc.).
                    ui.Title.Text = $"{ui.Feature.Emoji} {ExclusiveTitle(ui.Feature)}";
                    ApplyExclusiveCardState(ui, ui.Feature.GateState());
                }

                // Teaser "?" marks breathe under the same motion/perf gates as the
                // veil padlocks, so a tier change repaints them here too.
                foreach (var mark in _exclusiveTeaserMarks)
                    ApplyVeilLockBreath(mark, true);

                // Spotlight veil follows the same probe.
                var spot = ExclusiveFeature.All[0];
                ExclusivesTab.TxtSpotTitle.Text = $"{spot.Emoji} {ExclusiveTitle(spot)}";
                ExclusivesTab.SpotVeil.Visibility =
                    spot.GateState() == ExclusiveGateState.Locked ? Visibility.Visible : Visibility.Collapsed;
                ApplyVeilLockBreath(ExclusivesTab.SpotVeilLock, ExclusivesTab.SpotVeil.Visibility == Visibility.Visible);

                RefreshExclusiveTierPlates();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshExclusivesTab failed");
            }
        }

        private void ApplyExclusiveCardState(ExclusiveCardUi ui, ExclusiveGateState state)
        {
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
                        Color = FxTheme.GlowColor,
                        BlurRadius = Math.Min(20, PerformanceProfile.MaxGlowBlurRadius(tier)),
                        ShadowDepth = 0,
                        Opacity = 0.8,
                    };
                }
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
                foreach (var ui in _exclusiveCards) ui.Sheen?.Stop();
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
        /// Loads pack-embedded art, or null if the resource is absent/undecodable -
        /// callers rely on that null to fall back (see <see cref="ApplySpotlightArt"/>),
        /// so this must never throw. OnLoad caching means a missing resource throws
        /// inside EndInit, right here, and not later on the render thread.
        /// </summary>
        /// <param name="decodePixelWidth">
        /// Bounded decode width. Card art renders at ~340 wide inside the Viewbox; a
        /// bounded decode keeps nine 1376x768 sources from costing ~32 MB of bitmaps.
        /// The hero band passes a larger value because it is drawn much wider.
        /// </param>
        private static ImageSource? LoadPackImage(string relativePath, int decodePixelWidth = 700)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/" + relativePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                // Touching PixelWidth forces any deferred decode failure to surface
                // here, inside the catch, rather than as an empty Image later.
                if (bmp.PixelWidth <= 0) return null;
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Exclusives art missing: {Path} ({E})", relativePath, ex.Message);
                return null;
            }
        }
    }
}
