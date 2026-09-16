using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The picture on a ring card, shared by the ring itself (<c>EmiRingWindow</c>) and the pin wall
/// in her options (<c>EmiRingPicker</c>), so the two can never disagree about what a door looks like.
///
/// <para>Two fits, same split as the favourites rail (<see cref="FavoritesRailArt"/>):</para>
/// <list type="bullet">
/// <item><b>Cover</b> - scene art (the feature illustrations) filling the card, cropped.</item>
/// <item><b>Plate</b> - a square icon (the 64x64 nav door medallions). Cover-fitting one of those into
/// a 4:3 card blows it up to mush and cuts its head off, so it is drawn centred at a fixed size over a
/// soft, darkened blow-up of itself. That is how Profile, Settings, Companion and Progress get a face:
/// they are rooms with no illustration, and their rail medallion is the picture the app already uses
/// for them everywhere else.</item>
/// </list>
///
/// <para>The flat hue tile is only what a runtime miss falls back to (a mod that breaks the path),
/// never what a door ships with: <c>EmiRingCatalogueTests</c> fails a target with no art.</para>
/// </summary>
internal static class EmiCardFace
{
    /// <summary>Decode cap for cover art: a 136 DIP card at 125-150% scaling, with headroom.</summary>
    private const int CoverDecodeWidth = 192;

    /// <summary>The icon a plate carries is a 64px medallion; decode no wider than the mod editor's crisp size.</summary>
    private const int PlateIconDecodeWidth = 128;

    /// <summary>Twelve pixels stretched across the card is the blur, same trick as the rail plate.</summary>
    private const int PlateBackdropDecodeWidth = 12;

    /// <summary>
    /// Add the card's picture to <paramref name="grid"/>: the art, or the flat hue tile when the art
    /// cannot be loaded.
    /// </summary>
    /// <param name="iconSize">Side of a plate's icon, in DIP.</param>
    /// <param name="stripReserve">Height of the name strip at the card's foot, so a plate's icon is
    /// centred in the space ABOVE the label rather than half under it.</param>
    public static void AddArt(Grid grid, EmiTarget t, bool locked, double iconSize, double stripReserve)
    {
        double artOpacity = locked ? 0.42 : 0.92;

        if (t.ThumbIsIcon)
        {
            var icon = Load(t, PlateIconDecodeWidth);
            if (icon != null)
            {
                var soft = Load(t, PlateBackdropDecodeWidth) ?? icon;
                grid.Children.Add(new Rectangle
                {
                    Fill = new ImageBrush(soft) { Stretch = Stretch.UniformToFill },
                    Opacity = artOpacity,
                    IsHitTestVisible = false,
                });
                // Takes the backdrop down to a wash, with the target's own hue through it, so the
                // medallion over it stays the subject and each plate still reads as its own colour.
                grid.Children.Add(new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0x0E, 0x0A, 0x1A)),
                    IsHitTestVisible = false,
                });
                grid.Children.Add(new Rectangle
                {
                    Fill = new SolidColorBrush(t.Hue) { Opacity = 0.14 },
                    IsHitTestVisible = false,
                });

                var img = new Image
                {
                    Source = icon,
                    Width = iconSize,
                    Height = iconSize,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, stripReserve),
                    Opacity = locked ? 0.5 : 1.0,
                    IsHitTestVisible = false,
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grid.Children.Add(img);
                return;
            }
        }
        else
        {
            var art = Load(t, CoverDecodeWidth);
            if (art != null)
            {
                grid.Children.Add(new Image
                {
                    Source = art,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    Opacity = artOpacity,
                });
                return;
            }
        }

        grid.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(t.Hue) { Opacity = locked ? 0.28 : 0.62 },
            IsHitTestVisible = false,
        });
    }

    private static ImageSource? Load(EmiTarget t, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(t.ThumbPath)) return null;
        try
        {
            // Through the mod resolver so a .ccpmod's own card art wins, exactly like the dashboard.
            var src = ModResourceResolver.ResolveImageDecoded(t.ThumbPath, decodeWidth);
            if (src == null) Log.Debug("[EmiDesk] ring art missing for {Target}", t.Id);
            return src;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring art failed for {Target}", t.Id);
            return null;
        }
    }
}
