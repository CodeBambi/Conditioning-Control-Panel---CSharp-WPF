using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The reward half of an achievement unlock: a small toast naming the wardrobe item the
    /// achievement just handed over. Shown ~900ms after <see cref="AchievementPopup"/> so it reads
    /// as a consequence of it, and stacked directly ABOVE it in the bottom-right corner.
    ///
    /// Same window recipe as <see cref="AchievementPopup"/> (borderless, transparent, topmost,
    /// never activated, click-anywhere dismiss, 300ms fades, auto-close) - deliberately, so both
    /// toasts behave identically no matter which one the user clicks at.
    ///
    /// No haptics here: <c>AchievementService</c> already fires exactly one achievement pattern per
    /// unlock, and a second pattern only stacks two overlapping copies on the toy.
    ///
    /// PORTED from ConditioningControlPanel/Windows/ItemUnlockedPopup.xaml.cs. Deviations:
    ///  - <c>DoubleAnimation</c> on Opacity becomes a <see cref="DoubleTransition"/>.
    ///  - <c>SystemParameters.WorkArea</c> + PassiveToastWindow become AchievementPopup.PlacePassive
    ///    (device pixels, X11 override-redirect before Show()).
    ///  - The Twemoji header/gift SVG lookups and their fallbacks collapse to plain TextBlocks.
    ///  - <c>App.Logger</c> is Serilog's static <c>Log</c>; the templates are unchanged.
    /// </summary>
    public partial class ItemUnlockedPopup : Window
    {
        /// <summary>
        /// <see cref="AchievementPopup"/>'s window height. This toast sits above that popup, so its
        /// height is part of our own placement maths - keep in sync with AchievementPopup.axaml.
        /// </summary>
        private const double AchievementPopupHeight = 200;

        /// <summary>Gap between this toast and the achievement popup below it.</summary>
        private const double StackGap = 12;

        /// <summary>Gap between two stacked item toasts.</summary>
        private const double SiblingGap = 8;

        /// <summary>The markup Height (ItemUnlockedPopup.axaml); static so BottomDip needs no instance.</summary>
        internal const double ToastHeight = 104;

        private const double FadeMs = 300;

        private readonly DispatcherTimer _autoCloseTimer;
        private readonly int _stackIndex;

        /// <summary>Render/design constructor: sample data so --render-view can draw the toast.</summary>
        internal ItemUnlockedPopup() : this("Silk Bow", "bambi")
        {
            // The fade-in cannot complete inside a headless render's two dispatcher passes, so the
            // PNG would capture a fully transparent window. Skip the animation for the render.
            Transitions = null;
            Opacity = 1;
        }

        /// <param name="itemName">WardrobeItem.Name - a registry proper noun, deliberately NOT localized.</param>
        /// <param name="mod">WardrobeItem.Mod ("bambi" / "sissy" / "drone" / "circe"); drives the accent.</param>
        /// <param name="stackIndex">
        /// 0 for the first toast of this unlock, 1/2/... for extra items on the same achievement -
        /// each one is pushed a further (Height + gap) upward so they never overlap.
        /// </param>
        public ItemUnlockedPopup(ConditioningControlPanel.Services.WardrobeItem item, int stackIndex = 0)
            : this(item.Name, item.Mod, stackIndex, item.Id) { }

        private ItemUnlockedPopup(string itemName, string? mod, int stackIndex = 0, string? itemId = null)
        {
            if (itemName == null) throw new ArgumentNullException(nameof(itemName));

            AvaloniaXamlLoader.Load(this);

            _stackIndex = stackIndex < 0 ? 0 : stackIndex;

            var accent = AccentFor(mod);
            ApplyAccent(accent);

            // Header glyph and shout are static in the markup now ({loc:Str toast_item_unlocked_header}
            // / _sub): the WPF original only set them from code so the Twemoji fallback could prefix
            // the ribbon onto the text, and that fallback no longer exists.

            // Item name is a registry proper noun - NOT localized (DESIGN.md / WardrobeItem.Name).
            var txtItemName = this.FindControl<TextBlock>("TxtItemName")!;
            txtItemName.Text = itemName;
            txtItemName.Foreground = new SolidColorBrush(accent);

            LoadItemArt(itemId);

            // Never take the foreground - same focus-theft gap as the Pink Rush toast (ccp-bugs #1000).
            // Above the achievement popup; stackIndex pushes each extra toast a further (Height + 8) up.
            AchievementPopup.PlacePassive(this, BottomDip(_stackIndex));

            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            _autoCloseTimer.Start();

            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }
            };
            Opacity = 0;
            Loaded += (_, _) => Opacity = 1;

            AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, handledEventsToo: false);
        }

        /// <summary>Mod accent for the border, glow and item name. Unknown mods fall back to bambi pink.</summary>
        /// <summary>DIPs from the work area's bottom edge to toast #<paramref name="stackIndex"/>'s bottom
        /// (WPF PositionWindow: above the 200-DIP achievement popup, 12 gap, then Height + 8 per toast).</summary>
        internal static double BottomDip(int stackIndex) => AchievementPopupHeight + StackGap + Math.Max(0, stackIndex) * (ToastHeight + SiblingGap);

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
            var card = this.FindControl<Border>("CardBorder")!;
            card.BorderBrush = new SolidColorBrush(accent);
            card.Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 20,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 0.5,
            };
        }

        /// <summary>
        /// Paints the item art, or a gift glyph when the PNG never landed. "No art" is a normal
        /// path, not an error - what must never happen is a broken-image box in the toast.
        /// </summary>
        private void LoadItemArt(string? itemId)
        {
            var art = Helpers.ModArt.Wardrobe(itemId);
            var image = this.FindControl<Image>("ItemImage")!;
            image.Source = art;
            image.IsVisible = art != null;
            this.FindControl<TextBlock>("FallbackGlyphText")!.IsVisible = art == null;
        }

        private void FadeOutAndClose()
        {
            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* Ignore close errors */ } },
                    TimeSpan.FromMilliseconds(FadeMs));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during item toast fade out, closing directly");
                try { Close(); } catch { /* Ignore close errors */ }
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer.Stop();
            base.OnClosed(e);
        }
    }
}
