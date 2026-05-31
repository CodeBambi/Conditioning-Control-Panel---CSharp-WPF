using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Interactive replacement for the old WPF <see cref="ToolTip"/> help popover.
    /// Unlike a ToolTip, the cursor can travel from the "?" button into the card
    /// (a short close-grace timer bridges the gap) and the card can be pinned open
    /// by clicking the button. The body is the exact same panel
    /// <see cref="HelpTooltipBuilder.BuildPanel"/> produces, hosted in a styled
    /// border that matches HelpTooltipStyle (rounded corners, pink border, shadow).
    ///
    /// When the topic ships a tutorial clip (<see cref="HelpContent.HasClip"/>), a
    /// static poster + play button is rendered at the TOP of the card; clicking it
    /// opens the existing <see cref="HelpVideoWindow"/>. No LibVLC is instantiated
    /// inside the popover.
    ///
    /// Usage:  HelpPopover.Attach(button, content);   // idempotent per button
    ///         HelpPopover.Clear(button);             // detach
    ///         HelpPopover.CloseActive();             // global dismiss (e.g. panic)
    ///
    /// Lifecycle / scoping:
    ///   * Only ONE popover is open at a time (tracked by <see cref="_active"/>).
    ///   * Window-level watchers (click-away, Deactivated, StateChanged) are
    ///     subscribed on open and removed on close — never left always-live.
    /// </summary>
    public sealed class HelpPopover
    {
        // Match HelpTooltipStyle / HelpButtonStyle timing.
        private const int OpenDelayMs = 100;
        private const int CloseGraceMs = 250;

        /// <summary>The single popover currently open/pinned, if any.</summary>
        private static HelpPopover? _active;

        private static readonly DependencyProperty InstanceProperty =
            DependencyProperty.RegisterAttached(
                "Instance", typeof(HelpPopover), typeof(HelpPopover),
                new PropertyMetadata(null));

        private readonly Button _button;
        private readonly HelpContent _content;
        private readonly Popup _popup;
        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;

        private FrameworkElement? _childRoot;   // popup child root (mouse target)
        private Window? _hostWindow;             // window we watch while open
        private InlineLoopVideo? _inlineVideo;   // muted looping preview (clip topics only)
        private bool _pinned;
        private bool _contentBuilt;

        /// <summary>
        /// Attaches (or re-attaches) an interactive help popover to <paramref name="button"/>.
        /// Idempotent: any popover already attached is detached first.
        /// </summary>
        public static void Attach(Button button, HelpContent content)
        {
            if (button == null || content == null) return;
            Clear(button);
            var instance = new HelpPopover(button, content);
            button.SetValue(InstanceProperty, instance);
        }

        /// <summary>Detaches and disposes any popover attached to <paramref name="button"/>.</summary>
        public static void Clear(Button button)
        {
            if (button?.GetValue(InstanceProperty) is HelpPopover existing)
            {
                existing.Detach();
                button.ClearValue(InstanceProperty);
            }
        }

        /// <summary>
        /// Closes whichever popover is currently open. Safe no-op when none is open.
        /// Wired to the panic flow and window lifecycle so a pinned card never lingers.
        /// </summary>
        public static void CloseActive() => _active?.Close();

        private HelpPopover(Button button, HelpContent content)
        {
            _button = button;
            _content = content;

            _popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Right,
                HorizontalOffset = 10,        // parity with old HelpTooltipStyle
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                StaysOpen = true,             // closing is managed manually
                AllowDrop = false
            };
            _popup.Closed += OnPopupClosed;

            _openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OpenDelayMs) };
            _openTimer.Tick += OnOpenTick;
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CloseGraceMs) };
            _closeTimer.Tick += OnCloseTick;

            _button.MouseEnter += OnButtonMouseEnter;
            _button.MouseLeave += OnButtonMouseLeave;
            // The "?" no longer has any other Click handler that marks the event
            // Handled (the video-on-click wiring was retired in M2), so a plain
            // Click subscription is sufficient — no handledEventsToo needed.
            _button.Click += OnButtonClick;
            _button.Unloaded += OnButtonUnloaded;
        }

        // ----- content (built lazily so host resources are resolvable) -----------

        private void EnsureContent()
        {
            if (_contentBuilt) return;
            _contentBuilt = true;

            var pink = HelpTooltipBuilder.FindThemeResource<Brush>(_button, "PinkBrush")
                       ?? new SolidColorBrush(Color.FromRgb(255, 105, 180));

            // Card body: optional video poster on top, then the rich text panel.
            var body = new StackPanel();
            if (_content.HasClip) body.Children.Add(BuildVideoPoster(pink));
            body.Children.Add(HelpTooltipBuilder.BuildPanel(_content, _button));

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x25, 0x25, 0x42)),
                BorderBrush = pink,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                MaxWidth = 380,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.15
                },
                Child = body
            };

            // Transparent padding ring gives the drop shadow room and keeps the
            // popover open while the cursor hovers the shadow halo (less flicker).
            var shadowHost = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12),
                Child = card
            };
            shadowHost.MouseEnter += OnChildMouseEnter;
            shadowHost.MouseLeave += OnChildMouseLeave;

            _childRoot = shadowHost;
            _popup.Child = shadowHost;
        }

        /// <summary>
        /// Static poster (no live video) with a centered play glyph and the
        /// localized caption. Clicking it opens the existing HelpVideoWindow.
        /// </summary>
        private FrameworkElement BuildVideoPoster(Brush pink)
        {
            var poster = new Border
            {
                Height = 184,                      // ~16:9 against the card's inner width
                Margin = new Thickness(12, 12, 12, 0),
                CornerRadius = new CornerRadius(8),
                BorderBrush = pink,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x20)),
                ClipToBounds = true                // clips the video to the rounded poster
            };

            var overlay = new Grid();

            // Live muted, looping preview (autoplays while the card is open). Rendered
            // bottom-most; fails soft to a blank dark poster if LibVLC/clip is missing.
            // No click-to-pop-out: the inline loop is the whole affordance here.
            var clipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "tutorial_videos", _content.ClipFile!);
            _inlineVideo = new InlineLoopVideo(clipPath);
            overlay.Children.Add(_inlineVideo.Surface);

            // Localized caption (hot-swaps on language change, like HelpVideoWindow).
            if (!string.IsNullOrWhiteSpace(_content.CaptionKey))
            {
                var captionBar = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00))
                };
                var caption = new TextBlock
                {
                    Foreground = HelpTooltipBuilder.FindThemeResource<Brush>(_button, "TextLightBrush")
                                 ?? Brushes.White,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                caption.SetBinding(TextBlock.TextProperty, new Binding($"[{_content.CaptionKey}]")
                {
                    Source = LocalizationManager.Instance,
                    Mode = BindingMode.OneWay
                });
                captionBar.Child = caption;
                overlay.Children.Add(captionBar);
            }

            poster.Child = overlay;
            return poster;
        }

        // ----- hover open / close-grace -----------------------------------------

        private void OnButtonMouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer.Stop();
            if (_pinned || _popup.IsOpen) return;
            _openTimer.Start();
        }

        private void OnButtonMouseLeave(object sender, MouseEventArgs e)
        {
            _openTimer.Stop();
            if (!_pinned && _popup.IsOpen) _closeTimer.Start();
        }

        private void OnChildMouseEnter(object sender, MouseEventArgs e)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
        }

        private void OnChildMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_pinned) _closeTimer.Start();
        }

        private void OnOpenTick(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            Open();
        }

        private void OnCloseTick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (!_pinned) Close();
        }

        // ----- pin ---------------------------------------------------------------

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (_pinned)
            {
                Close();
            }
            else
            {
                _openTimer.Stop();
                _closeTimer.Stop();
                _pinned = true;
                Open(); // subscribes window watchers (incl. click-away)
            }
        }

        // ----- open/close core ---------------------------------------------------

        private void Open()
        {
            // Enforce a single open popover at a time.
            if (_active != null && !ReferenceEquals(_active, this)) _active.Close();
            _active = this;

            EnsureContent();
            SubscribeWindowWatchers();
            _popup.IsOpen = true;
            _inlineVideo?.Resume(); // autoplay the muted loop while the card is open
        }

        private void Close()
        {
            _popup.IsOpen = false; // OnPopupClosed performs the cleanup
        }

        private void OnPopupClosed(object? sender, EventArgs e)
        {
            _inlineVideo?.Pause(); // stop decoding behind a closed card
            _pinned = false;
            _openTimer.Stop();
            _closeTimer.Stop();
            UnsubscribeWindowWatchers();
            if (ReferenceEquals(_active, this)) _active = null;
        }

        // ----- window-level watchers (subscribed only while open) ----------------

        private void SubscribeWindowWatchers()
        {
            if (_hostWindow != null) return; // already subscribed
            _hostWindow = Window.GetWindow(_button);
            if (_hostWindow == null) return;
            _hostWindow.Deactivated += OnHostDeactivated;
            _hostWindow.StateChanged += OnHostStateChanged;
            // handledEventsToo so a click on a control that marks PreviewMouseDown
            // Handled still dismisses a pinned card.
            _hostWindow.AddHandler(UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnWindowPreviewMouseDown), true);
        }

        private void UnsubscribeWindowWatchers()
        {
            if (_hostWindow == null) return;
            _hostWindow.Deactivated -= OnHostDeactivated;
            _hostWindow.StateChanged -= OnHostStateChanged;
            _hostWindow.RemoveHandler(UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnWindowPreviewMouseDown));
            _hostWindow = null;
        }

        private void OnHostDeactivated(object? sender, EventArgs e) => Close();

        private void OnHostStateChanged(object? sender, EventArgs e)
        {
            if (_hostWindow != null && _hostWindow.WindowState != WindowState.Normal) Close();
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_pinned) return;
            // Clicks inside the popup render in its own HWND and never reach this
            // window-level handler. Clicks on the button itself are left to the
            // button's own Click (which toggles the pin off).
            if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, _button)) return;
            Close();
        }

        // ----- teardown ----------------------------------------------------------

        private void OnButtonUnloaded(object sender, RoutedEventArgs e) => Close();

        private void Detach()
        {
            Close();
            _openTimer.Stop();
            _openTimer.Tick -= OnOpenTick;
            _closeTimer.Stop();
            _closeTimer.Tick -= OnCloseTick;
            _popup.Closed -= OnPopupClosed;
            _button.MouseEnter -= OnButtonMouseEnter;
            _button.MouseLeave -= OnButtonMouseLeave;
            _button.Click -= OnButtonClick;
            _button.Unloaded -= OnButtonUnloaded;
            if (_childRoot != null)
            {
                _childRoot.MouseEnter -= OnChildMouseEnter;
                _childRoot.MouseLeave -= OnChildMouseLeave;
                _childRoot = null;
            }
            _inlineVideo?.Dispose();
            _inlineVideo = null;
            _popup.Child = null;
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            var current = node;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;
                // ContentElements (Run, Hyperlink) aren't in the visual tree;
                // VisualTreeHelper.GetParent throws on them, so fall back to logical.
                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
