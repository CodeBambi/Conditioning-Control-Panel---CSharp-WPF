using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

/// <summary>
/// The reward half of an achievement unlock: a small toast naming the wardrobe item the
/// achievement just handed over. Shown ~900ms after <see cref="AchievementPopup"/> so it reads
/// as a consequence of it, and stacked directly ABOVE it in the bottom-right corner.
///
/// Same window recipe as <see cref="AchievementPopup"/> (borderless, layered, topmost, never
/// activated, click-anywhere dismiss, 300ms fades, auto-close) - deliberately, so both toasts
/// behave identically no matter which one the user clicks at.
///
/// No haptics here: <c>AchievementService</c> already fires exactly one achievement pattern per
/// unlock, and a second pattern only stacks two overlapping copies on the toy (see the ONE-call
/// comment at AchievementService.cs:845).
/// </summary>
public partial class ItemUnlockedPopup : Window
{
    /// <summary>
    /// <see cref="AchievementPopup"/>'s window height. This toast sits above that popup, so its
    /// height is part of our own placement maths - keep in sync with AchievementPopup.xaml.
    /// </summary>
    private const double AchievementPopupHeight = 200;

    /// <summary>Gap between this toast and the achievement popup below it.</summary>
    private const double StackGap = 12;

    /// <summary>Gap between two stacked item toasts.</summary>
    private const double SiblingGap = 8;

    private readonly DispatcherTimer _autoCloseTimer;

    /// <param name="item">The wardrobe item that just became wearable.</param>
    /// <param name="stackIndex">
    /// 0 for the first toast of this unlock, 1/2/... for extra items on the same achievement -
    /// each one is pushed a further (Height + gap) upward so they never overlap.
    /// </param>
    public ItemUnlockedPopup(WardrobeItem item, int stackIndex = 0)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        InitializeComponent();

        App.Logger?.Debug("Creating ItemUnlockedPopup for wardrobe item: {Id}", item.Id);

        var accent = AccentFor(item.Mod);
        ApplyAccent(accent);

        // Header: Twemoji ribbon + the localized shout. Falls back to an inline glyph if the SVG
        // is missing, because a naked TextBlock emoji renders monochrome (see EmojiImage).
        try
        {
            var ribbon = Helpers.EmojiImage.Get("\U0001F380");
            if (ribbon != null)
            {
                HeaderIcon.Source = ribbon;
                TxtHeader.Text = Loc.Get("toast_item_unlocked_header");
            }
            else
            {
                HeaderIcon.Visibility = Visibility.Collapsed;
                TxtHeader.Text = "\U0001F380 " + Loc.Get("toast_item_unlocked_header");
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ItemUnlockedPopup: header icon failed: {E}", ex.Message);
            HeaderIcon.Visibility = Visibility.Collapsed;
            TxtHeader.Text = Loc.Get("toast_item_unlocked_header");
        }

        // Item name is a registry proper noun - NOT localized (DESIGN.md / WardrobeItem.Name).
        TxtItemName.Text = item.Name;
        TxtItemName.Foreground = new SolidColorBrush(accent);
        TxtSub.Text = Loc.Get("toast_item_unlocked_sub");

        LoadItemArt(item);

        PositionWindow(stackIndex);

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

        Opacity = 0;
        Loaded += (s, e) =>
        {
            try
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ItemUnlockedPopup: fade-in failed: {E}", ex.Message);
                Opacity = 1;
            }
        };
    }

    /// <summary>Mod accent for the border, glow and item name. Unknown mods fall back to bambi pink.</summary>
    private static Color AccentFor(string? mod)
    {
        return (mod ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sissy" => Color.FromRgb(0xB4, 0x78, 0xFF),
            "drone" => Color.FromRgb(0x5E, 0xC8, 0xF2),
            "circe" => Color.FromRgb(0x5E, 0xC8, 0xF2),
            _ => Color.FromRgb(0xFF, 0x69, 0xB4),
        };
    }

    private void ApplyAccent(Color accent)
    {
        try
        {
            var brush = new SolidColorBrush(accent);
            if (brush.CanFreeze) brush.Freeze();
            CardBorder.BorderBrush = brush;
            CardBorder.Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.5,
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ItemUnlockedPopup: accent failed: {E}", ex.Message);
        }
    }

    /// <summary>
    /// Paints the item art, or a gift glyph when the PNG never landed. WardrobeCatalog returns
    /// null for a missing/unreadable file rather than throwing, so "no art" is a normal path -
    /// what must never happen is a broken-image box in the toast.
    /// </summary>
    private void LoadItemArt(WardrobeItem item)
    {
        ImageSource? art = null;
        try
        {
            art = WardrobeCatalog.GetImage(item.Id);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ItemUnlockedPopup: art for {Id} failed: {E}", item.Id, ex.Message);
        }

        if (art != null)
        {
            ItemImage.Source = art;
            return;
        }

        ItemImage.Visibility = Visibility.Collapsed;
        try
        {
            var gift = Helpers.EmojiImage.Get("\U0001F381");
            if (gift != null)
            {
                FallbackGlyphImage.Source = gift;
                FallbackGlyphImage.Visibility = Visibility.Visible;
                return;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ItemUnlockedPopup: gift glyph failed: {E}", ex.Message);
        }

        FallbackGlyphText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Bottom-right of the work area, stacked ABOVE the achievement popup (and above any earlier
    /// toast from the same unlock). Falls back to CenterScreen if the work area is unreadable -
    /// same contract as AchievementPopup.
    /// </summary>
    private void PositionWindow(int stackIndex)
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var index = stackIndex < 0 ? 0 : stackIndex;

            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - AchievementPopupHeight - Height - StackGap
                  - index * (Height + SiblingGap);

            App.Logger?.Debug("Positioned item toast #{Index} at Left={Left}, Top={Top}", index, Left, Top);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to position item unlocked popup, using defaults");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void FadeOutAndClose()
    {
        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                try { Close(); } catch { /* Ignore close errors */ }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during item toast fade out, closing directly");
            try { Close(); } catch { }
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
        App.Logger?.Debug("ItemUnlockedPopup closed");
    }
}
