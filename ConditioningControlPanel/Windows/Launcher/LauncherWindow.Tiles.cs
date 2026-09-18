using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The game tiles. Each one is built from a <see cref="LauncherEntry"/> and dressed in the
/// entry's hue: a tinted rim, a soft under-glow, a title that warms on hover and a Play button
/// that fills with the same colour. The pointer tilts the tile a little and slides the art the
/// other way, so the card reads as a thing with depth rather than a rectangle.
///
/// <para>Rails: the glow is a single DropShadowEffect per tile, only when the performance tier
/// allows glow and never past its blur cap; every tween is gated on
/// <see cref="MotionFx.AllowTransitions"/>, uses <c>BeginAnimation</c>, and sits inside a
/// try/catch so decoration never throws. The transform group keeps its scale at index 0 and its
/// translate at index 1 (the motion helpers look them up by type, the order is a courtesy) and
/// appends the tilt rotate at index 2.</para>
/// </summary>
public partial class LauncherWindow
{
    private const double TileArtHeight = 156;
    private const double TileTiltDegrees = 1.2;
    private const double TileArtParallaxPx = 7;
    private const int TileTiltMs = 90;
    private const int TileHoverMs = 160;
    private const double TileGlowRest = 0.35;
    private const double TileGlowHover = 0.7;

    private static bool GlowAllowed => PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);
    private static double GlowRadius => Math.Min(20, PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier));
    private static bool TiltAllowed => MotionFx.AllowTransitions && PerformanceProfile.CurrentTier != PerformanceTier.Performance;

    private Border CreateTile(LauncherEntry entry)
    {
        // Signed out, every tile asks for an account first; the tier lock only shows once there
        // is an account to hold a tier.
        bool needsAccount = entry.NeedsAccount;
        bool locked = !needsAccount && entry.Locked;
        var hue = entry.Hue;
        var tilt = new RotateTransform();

        var tile = new Border
        {
            CornerRadius = new CornerRadius(TileRadius),
            Background = (Brush)FindResource("SurfaceBgBrush"),
            BorderBrush = locked ? (Brush)FindResource("Tier2DiamondBorderBrush") : RimBrush(hue),
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(9),
            Tag = entry,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform(), tilt },
            },
        };

        DropShadowEffect? glow = null;
        if (GlowAllowed)
        {
            glow = new DropShadowEffect
            {
                Color = hue, ShadowDepth = 0, BlurRadius = GlowRadius, Opacity = TileGlowRest,
                RenderingBias = RenderingBias.Performance,
            };
            tile.Effect = glow;
        }

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileArtHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- the art plate, rounded at the top only (the clip runs past the bottom edge) ---
        var plate = new Grid { ClipToBounds = true };
        plate.SizeChanged += (_, _) =>
            plate.Clip = new RectangleGeometry(new Rect(0, 0, plate.ActualWidth, plate.ActualHeight + TileRadius),
                                               TileRadius, TileRadius);

        // The art rides in its own host so the parallax slide and HoverPop's own rig never
        // share an element.
        var artSlide = new TranslateTransform();
        var artHost = new Grid { RenderTransform = artSlide, Margin = new Thickness(-TileArtParallaxPx) };
        ImageSource? art = null;
        if (entry.ArtPath != null)
        {
            try { art = ModResourceResolver.ResolveImageDecoded(entry.ArtPath, 640); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] art {Path} failed", entry.ArtPath); }
        }
        if (art != null)
        {
            var image = new Image { Source = art, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            artHost.Children.Add(image);
            FxDecorateArt(image);
        }
        else
        {
            BuildGlyphPlate(artHost, entry);
        }
        plate.Children.Add(artHost);

        // A fade into the card so the plate never ends on a hard line and the title sits on
        // colour: transparent through the top half, the card's own surface at the foot.
        var surface = ((SolidColorBrush)FindResource("SurfaceBgBrush")).Color;
        plate.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.55),
                    new GradientStop(Color.FromArgb(0xB0, surface.R, surface.G, surface.B), 0.88),
                    new GradientStop(surface, 1),
                },
            },
        });
        if (needsAccount) plate.Children.Add(BuildSignInPill());
        else if (locked) plate.Children.Add(BuildPrimePill());

        var shortcutBtn = new Button
        {
            Content = "🔗", Style = (Style)FindResource("LauncherIconButton"),
            ToolTip = Loc.Get("launcher_add_shortcut"), Opacity = 0,
            Margin = new Thickness(0, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)FindResource("SurfaceBgBrush"),
        };
        shortcutBtn.Click += (_, _) =>
        {
            LauncherSfx.Click();
            ShowShortcutResult(LauncherShortcuts.TryCreateDesktopShortcut(entry.Id));
        };
        plate.Children.Add(shortcutBtn);
        body.Children.Add(plate);

        // --- title, blurb, play ---
        var text = new StackPanel { Margin = new Thickness(16, 10, 16, 16) };
        Grid.SetRow(text, 1);
        var textLight = ((SolidColorBrush)FindResource("TextLightBrush")).Color;
        var titleBrush = new SolidColorBrush(textLight);
        text.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 19, FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
            Foreground = titleBrush, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = entry.Blurb, FontSize = 14, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"), MinHeight = 20,
        });
        var play = BuildPlayButton(entry, locked, needsAccount);
        play.PreviewMouseLeftButtonDown += Press_Down;
        play.PreviewMouseLeftButtonUp += Press_Up;
        play.Click += (_, _) =>
        {
            if (locked || needsAccount) LauncherSfx.Denied(); else LauncherSfx.Click();
            FxOnPlay(tile, entry);
            if (needsAccount) OpenSignIn();
            else LauncherHost.LaunchGame(entry.Id);
        };
        if (needsAccount)
        {
            // The whole card is the ask, not only its button.
            tile.MouseLeftButtonUp += (_, _) => { LauncherSfx.Denied(); FxOnPlay(tile, entry); OpenSignIn(); };
        }
        text.Children.Add(play);
        body.Children.Add(text);
        tile.Child = body;

        var titleHue = Lighten(hue, 0.35);
        tile.MouseEnter += (_, _) =>
        {
            MotionFx.HoverLift(tile, true);
            shortcutBtn.Opacity = 1;
            LauncherSfx.Hover();
            TintTile(glow, titleBrush, titleHue, true);
            FxOnTileHover(tile, entry, true);
        };
        tile.MouseLeave += (_, _) =>
        {
            MotionFx.HoverLift(tile, false);
            shortcutBtn.Opacity = 0;
            TintTile(glow, titleBrush, textLight, false);
            SettleTilt(tilt, artSlide);
            FxOnTileHover(tile, entry, false);
        };
        tile.MouseMove += (_, e) => TiltToward(tile, tilt, artSlide, e.GetPosition(tile));
        FxDecorateTile(tile, entry);
        return tile;
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>Outline at rest, a hue fill on hover: the template does the crossfade, the
    /// tile hands it the colours.</summary>
    private Button BuildPlayButton(LauncherEntry entry, bool locked, bool needsAccount)
    {
        var hue = entry.Hue;
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (!needsAccount)
            label.Children.Add(new TextBlock
            {
                Text = locked ? "🔒" : "▶", FontSize = locked ? 13 : 11, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        label.Children.Add(new TextBlock
        {
            Text = Loc.Get(needsAccount ? "launcher_sign_in" : locked ? "launcher_locked" : "launcher_play"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var fill = needsAccount
            ? (Brush)FindResource("AccentGradientBrush")
            : locked
                ? (Brush)FindResource("Tier2DiamondBorderBrush")
                : new LinearGradientBrush(Lighten(hue, 0.15), Darken(hue, 0.75), 0);
        return new Button
        {
            Content = label,
            Style = (Style)FindResource("LauncherPlay"),
            Height = 42, Margin = new Thickness(0, 10, 0, 0),
            Background = fill,
            BorderBrush = new SolidColorBrush(Lighten(hue, 0.25)),
            Foreground = new SolidColorBrush(Lighten(hue, 0.45)),
        };
    }

    /// <summary>The premium tag on a locked tile: a small gold pill instead of a bare lock.</summary>
    private Border BuildPrimePill() => BuildPill("launcher_prime_pill", "🔒",
        (Brush)FindResource("Tier2DiamondBorderBrush"), new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x08)));

    /// <summary>The ask on a signed-out tile: the same pill in the accent, no lock.</summary>
    private Border BuildSignInPill() => BuildPill("launcher_pill_sign_in", null,
        (Brush)FindResource("AccentGradientBrush"), Brushes.White);

    private Border BuildPill(string textKey, string? icon, Brush background, Brush ink)
    {
        var pill = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 3),
            Margin = new Thickness(12, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null)
            row.Children.Add(new TextBlock { Text = icon, FontSize = 10, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get(textKey), FontSize = 11, FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
            Foreground = ink, VerticalAlignment = VerticalAlignment.Center,
        });
        pill.Child = row;
        return pill;
    }

    /// <summary>
    /// A plate for an entry without art: a warm radial in the entry's hue, a road drawn in
    /// perspective (three lanes converging on a low horizon, a dashed centre line) and the glyph
    /// sitting on it with a glow. Reads as a picture, not a placeholder.
    /// </summary>
    private void BuildGlyphPlate(Grid host, LauncherEntry entry)
    {
        var hue = entry.Hue;
        host.Background = new RadialGradientBrush(Lighten(hue, 0.1), Darken(hue, 0.4))
        {
            GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.3),
            RadiusX = 0.85, RadiusY = 0.95,
        };

        var road = new Canvas { IsHitTestVisible = false, Opacity = 0.22 };
        var ink = new SolidColorBrush(Colors.White);
        void Lane(double x0, double x1) => road.Children.Add(new Line
        {
            X1 = x0, Y1 = 200, X2 = x1, Y2 = 96, Stroke = ink, StrokeThickness = 1.5,
        });
        // The canvas is laid out at the plate's size; the geometry assumes ~330 x 180 and the
        // Viewbox below scales it with the tile.
        Lane(-30, 150); Lane(90, 158); Lane(240, 172); Lane(360, 180);
        road.Children.Add(new Line
        {
            X1 = 165, Y1 = 200, X2 = 165, Y2 = 96, Stroke = ink, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 5 },
        });
        road.Children.Add(new Line { X1 = 0, Y1 = 96, X2 = 330, Y2 = 96, Stroke = ink, StrokeThickness = 1, Opacity = 0.6 });
        host.Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = new Grid { Width = 330, Height = 200, Children = { road } } });

        var glyph = new TextBlock
        {
            Text = entry.Glyph, FontSize = 64, FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
            Foreground = Brushes.White, Opacity = 0.95,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        if (GlowAllowed)
            glyph.Effect = new DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = GlowRadius, Opacity = 0.6 };
        host.Children.Add(glyph);
    }

    // ------------------------------------------------------------------ hover

    private static void TintTile(DropShadowEffect? glow, SolidColorBrush title, Color to, bool on)
    {
        try
        {
            var span = TimeSpan.FromMilliseconds(TileHoverMs);
            if (!MotionFx.AllowTransitions)
            {
                title.Color = to;
                if (glow != null) glow.Opacity = on ? TileGlowHover : TileGlowRest;
                return;
            }
            title.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, span));
            glow?.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(on ? TileGlowHover : TileGlowRest, span));
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tile tint failed"); }
    }

    /// <summary>Tilt toward the pointer: the card leans up to 1.2 degrees, the art slides the
    /// other way by up to 7 px. Both eased over 90 ms so a fast sweep still feels like weight.</summary>
    private static void TiltToward(Border tile, RotateTransform tilt, TranslateTransform art, Point at)
    {
        try
        {
            if (!TiltAllowed || tile.ActualWidth <= 0 || tile.ActualHeight <= 0) return;
            double nx = Math.Clamp(at.X / tile.ActualWidth * 2 - 1, -1, 1);
            double ny = Math.Clamp(at.Y / tile.ActualHeight * 2 - 1, -1, 1);
            var span = TimeSpan.FromMilliseconds(TileTiltMs);
            // Lean the way a card would if pressed at that corner: right side down when the
            // pointer is right, and the sign flips with the vertical half.
            double angle = nx * -ny * TileTiltDegrees;
            tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, span));
            art.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-nx * TileArtParallaxPx, span));
            art.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-ny * TileArtParallaxPx, span));
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tilt failed"); }
    }

    private static void SettleTilt(RotateTransform tilt, TranslateTransform art)
    {
        try
        {
            var span = TimeSpan.FromMilliseconds(TileHoverMs);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            if (!MotionFx.AllowTransitions)
            {
                tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                art.BeginAnimation(TranslateTransform.XProperty, null);
                art.BeginAnimation(TranslateTransform.YProperty, null);
                tilt.Angle = 0; art.X = art.Y = 0;
                return;
            }
            tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, span) { EasingFunction = ease });
            art.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, span) { EasingFunction = ease });
            art.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, span) { EasingFunction = ease });
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tilt settle failed"); }
    }

    // ------------------------------------------------------------------ colour

    /// <summary>The rim: the hue at 60 percent along the top-left, fading to the glass line.</summary>
    private Brush RimBrush(Color hue)
    {
        var glass = ((SolidColorBrush)FindResource("GlassBorderBrush")).Color;
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x99, hue.R, hue.G, hue.B), 0),
                new GradientStop(glass, 0.6),
                new GradientStop(Color.FromArgb(0x55, hue.R, hue.G, hue.B), 1),
            },
        };
    }

    private static Color Darken(Color c, double keep) =>
        Color.FromRgb((byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));
}
