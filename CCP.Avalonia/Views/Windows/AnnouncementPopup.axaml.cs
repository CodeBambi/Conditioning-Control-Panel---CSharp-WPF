using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Popup window for server-triggered announcements with optional image, link, and theme support.
    ///
    /// PORTED from ConditioningControlPanel/Windows/AnnouncementPopup.xaml.cs. Deviations:
    ///  - <c>ImageSource</c> is a WPF type; the card-art parameter takes Avalonia's
    ///    <see cref="IImage"/> instead. Call shape is otherwise unchanged.
    ///  - The <c>DoubleAnimation</c>s (window fade in/out, card-art pop) become
    ///    <see cref="Transitions"/> plus a plain property assignment - Avalonia animates through
    ///    the property system, not a Storyboard, so there is no clock to tear down and the WPF
    ///    <c>Closed</c> handler that unpinned one has nothing to do here.
    ///  - <c>ApplyMatrixButtonStyle</c>'s runtime <c>FrameworkElementFactory</c> template becomes
    ///    the declared <c>AnnouncementMatrixPillButton</c> ControlTheme in the XAML, assigned to
    ///    <c>Button.Theme</c>. Same three colours, none of the factory plumbing. <c>Freeze()</c>
    ///    has no Avalonia twin and is dropped (Avalonia brushes are not thread-affine here).
    ///  - <c>Dispatcher.BeginInvoke</c> -&gt; <c>Dispatcher.UIThread.Post</c>.
    ///  - <c>App.Logger</c> has no twin on this head yet, so the guarded catches simply swallow,
    ///    as AchievementPopup's port does.
    ///  - The default dismissal bookkeeping (<c>App.Settings.Current.DismissedAnnouncementId</c>)
    ///    is stubbed; see DismissAndClose.
    /// </summary>
    public partial class AnnouncementPopup : Window
    {
        /// <summary>Window width once the card-art rail is showing. The default 440 is sized for the
        /// stacked, text-only server announcement; the rail eats 210 of it, so the copy column would
        /// be unreadably narrow without this. At 620 the copy column lands near 346px, which holds a
        /// ~130-character body in three lines - the ceiling the nudge copy is written to.</summary>
        private const double CardLayoutWidth = 620;

        private const double FadeMs = 300;

        private readonly string _announcementId;
        private readonly string? _linkUrl;
        private readonly Action? _onDismiss;
        private readonly Action? _onAction;

        private readonly TextBlock _txtTitle, _txtMessage;
        private readonly Button _btnDownload, _btnDismiss, _btnClose;
        private readonly Border _cardArtPanel, _imageContainer;
        private readonly Image _cardArtImage, _announcementImage;
        private readonly StackPanel _buttonRow;

        /// <summary>Only non-null in card layout.</summary>
        private ScaleTransform? _cardArtScale;

        /// <summary>Render constructor: the card layout, which is the strict superset - art rail,
        /// its gradient and seam, the horizontal button row and both templated pill buttons. The
        /// stacked server layout is the same window with column 0 collapsed.</summary>
        internal AnnouncementPopup() : this(
            id: "render-sample",
            title: "Weekly Intake",
            message: "Two minutes of questions keeps the program tuned to where you actually are.",
            imageUrl: null,
            linkUrl: null,
            theme: null,
            onDismiss: () => { },
            cardImage: SampleCardArt(),
            actionText: "Start intake",
            onAction: () => { },
            dismissText: "Not now")
        {
            // A transition cannot complete inside a headless render's two dispatcher passes, so the
            // PNG would capture a transparent window and a 0.94-scaled card. Land both immediately.
            Transitions = null;
            Opacity = 1;
            if (_cardArtScale is not null)
            {
                _cardArtScale.Transitions = null;
                _cardArtScale.ScaleX = _cardArtScale.ScaleY = 1;
            }

            // RenderProof forces a NaN Height to 780, which SizeToContent would then fight and
            // MaxHeight=600 would clamp - a tall frame with a dead band under the card. Pin it.
            SizeToContent = SizeToContent.Manual;
            Height = 320;
        }

        /// <summary>Flat swatch standing in for real card art, so the render proves the rail,
        /// its Uniform stretch and the drop shadow without shipping a bitmap in the repo.</summary>
        private static IImage SampleCardArt() => new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.Parse("#FF69B4")),
                Geometry = new RectangleGeometry(new Rect(0, 0, 240, 240))
            }
        };

        /// <param name="onDismiss">Optional replacement for the default dismissal bookkeeping. The
        /// popup normally records <c>AppSettings.DismissedAnnouncementId</c>, which is a SINGLE slot
        /// owned by server-triggered announcements. Any local, recurring popup that reuses this
        /// window must pass its own handler, or dismissing it would silently consume the slot and
        /// suppress the next real server announcement.</param>
        /// <param name="cardImage">Optional LOCAL art (a resolved <see cref="IImage"/>, not a
        /// URL). Supplying it switches the window to the wider card layout: art rail on the left,
        /// copy and buttons beside it. Server announcements pass <paramref name="imageUrl"/> instead
        /// and keep the original stacked 440px layout untouched - the two paths are deliberately
        /// separate so a local card can never restyle a server announcement. Null (the resource was
        /// missing) simply leaves the text-only layout in place; it never renders an empty box.</param>
        /// <param name="actionText">Label for the primary button when <paramref name="onAction"/> is
        /// supplied. Ignored otherwise.</param>
        /// <param name="onAction">Optional primary action. When set, the primary button runs this
        /// instead of opening <paramref name="linkUrl"/>, and the popup dismisses itself through the
        /// normal path first - so the caller's <paramref name="onDismiss"/> bookkeeping still runs.</param>
        /// <param name="dismissText">Optional replacement label for the secondary button.</param>
        public AnnouncementPopup(string id, string title, string message, string? imageUrl,
            string? linkUrl = null, string? theme = null, Action? onDismiss = null,
            IImage? cardImage = null, string? actionText = null, Action? onAction = null,
            string? dismissText = null)
        {
            AvaloniaXamlLoader.Load(this);

            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtMessage = this.FindControl<TextBlock>("TxtMessage")!;
            _btnDownload = this.FindControl<Button>("BtnDownload")!;
            _btnDismiss = this.FindControl<Button>("BtnDismiss")!;
            _btnClose = this.FindControl<Button>("BtnClose")!;
            _cardArtPanel = this.FindControl<Border>("CardArtPanel")!;
            _imageContainer = this.FindControl<Border>("ImageContainer")!;
            _cardArtImage = this.FindControl<Image>("CardArtImage")!;
            _announcementImage = this.FindControl<Image>("AnnouncementImage")!;
            _buttonRow = this.FindControl<StackPanel>("ButtonRow")!;

            _announcementId = id;
            _linkUrl = linkUrl;
            _onDismiss = onDismiss;
            _onAction = onAction;
            _txtTitle.Text = title;
            _txtMessage.Text = message;

            _btnDownload.Click += BtnDownload_Click;
            _btnDismiss.Click += BtnDismiss_Click;
            _btnClose.Click += BtnClose_Click;

            // Primary button: a caller-supplied action wins over the server's link_url. The
            // link_url branch below is byte-identical to the original behaviour when onAction is null.
            if (onAction != null && !string.IsNullOrWhiteSpace(actionText))
            {
                _btnDownload.Content = actionText;
                _btnDownload.IsVisible = true;
            }
            else if (!string.IsNullOrWhiteSpace(linkUrl))
            {
                _btnDownload.IsVisible = true;
            }

            if (!string.IsNullOrWhiteSpace(dismissText))
            {
                _btnDismiss.Content = dismissText;
            }

            if (cardImage != null)
            {
                ApplyCardLayout(cardImage);
            }

            // Apply theme
            if (string.Equals(theme, "matrix", StringComparison.OrdinalIgnoreCase))
            {
                ApplyMatrixTheme();
            }

            // Fade in
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }
            };
            Opacity = 0;
            Loaded += (_, _) =>
            {
                Opacity = 1;
                if (_cardArtScale != null)
                {
                    _cardArtScale.ScaleX = 1.0;
                    _cardArtScale.ScaleY = 1.0;
                }
            };

            // Load image asynchronously if URL provided
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                _ = LoadImageAsync(imageUrl);
            }
        }

        /// <summary>
        /// Switch from the stacked announcement layout to the two-column card: art rail left, copy
        /// and buttons right. Every change here is a local set on an element the server path leaves
        /// alone, so nothing about this can leak into an announcement that did not ask for it.
        /// </summary>
        private void ApplyCardLayout(IImage art)
        {
            try
            {
                Width = CardLayoutWidth;

                _cardArtImage.Source = art;
                _cardArtPanel.IsVisible = true;

                // Centred copy reads as a notice; ranged against art it reads as a card face.
                _txtTitle.TextAlignment = TextAlignment.Left;
                _txtMessage.TextAlignment = TextAlignment.Left;
                _txtMessage.Margin = new Thickness(0, 0, 0, 18);

                // Buttons go side by side under the copy instead of stacking down the middle.
                _buttonRow.Orientation = Orientation.Horizontal;
                _buttonRow.HorizontalAlignment = HorizontalAlignment.Left;

                _btnDownload.Width = 176;
                _btnDownload.Height = 38;
                _btnDownload.Margin = new Thickness(0, 0, 10, 0);

                _btnDismiss.Width = 116;
                _btnDismiss.Height = 38;

                // The WPF original ran the pop off the transform with BeginAnimation; a transition
                // on the same transform is the Avalonia twin - Loaded pushes it to 1.0.
                _cardArtScale = new ScaleTransform(0.94, 0.94)
                {
                    Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = ScaleTransform.ScaleXProperty,
                            Duration = TimeSpan.FromMilliseconds(420),
                            Easing = new CubicEaseOut()
                        },
                        new DoubleTransition
                        {
                            Property = ScaleTransform.ScaleYProperty,
                            Duration = TimeSpan.FromMilliseconds(420),
                            Easing = new CubicEaseOut()
                        }
                    }
                };
                _cardArtImage.RenderTransform = _cardArtScale;
            }
            catch
            {
                // A layout failure must not cost the user the message itself.
                // ponytail: needs App.Logger, wired when it moves to Core.
            }
        }

        private void ApplyMatrixTheme()
        {
            var matrixGreen = Color.Parse("#00FF41");
            var matrixGreenBrush = new SolidColorBrush(matrixGreen);
            var matrixLightGreenBrush = new SolidColorBrush(Color.Parse("#39FF14"));

            var matrixBg = Color.Parse("#0D0D0D");
            var matrixBgBrush = new SolidColorBrush(Color.FromArgb(0xF0, matrixBg.R, matrixBg.G, matrixBg.B));

            var consolasFont = new FontFamily("Consolas, Courier New");

            // Find the outer border (first child of the window)
            if (Content is Border outerBorder)
            {
                outerBorder.Background = matrixBgBrush;
                outerBorder.BorderBrush = matrixGreenBrush;
                outerBorder.Effect = new DropShadowEffect
                {
                    Color = matrixGreen,
                    BlurRadius = 20,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = 0.6
                };
            }

            // Title
            _txtTitle.Foreground = matrixGreenBrush;
            _txtTitle.FontFamily = consolasFont;

            // Message
            _txtMessage.Foreground = matrixLightGreenBrush;
            _txtMessage.FontFamily = consolasFont;

            // Download button — matrix style
            if (_btnDownload.IsVisible)
            {
                ApplyMatrixButtonStyle(_btnDownload, consolasFont);
            }

            // Dismiss button — matrix style
            ApplyMatrixButtonStyle(_btnDismiss, consolasFont);
        }

        /// <summary>
        /// Swap the pill theme for its green twin. WPF assembled the same template from
        /// FrameworkElementFactory at runtime; the ControlTheme is declared in the XAML instead,
        /// so the hover and pressed colours are selectors rather than Trigger objects.
        /// </summary>
        private void ApplyMatrixButtonStyle(Button button, FontFamily font)
        {
            button.FontFamily = font;
            if (Resources.TryGetResource("AnnouncementMatrixPillButton", ActualThemeVariant, out var theme)
                && theme is ControlTheme matrixTheme)
            {
                button.Theme = matrixTheme;
            }
        }

        private async Task LoadImageAsync(string imageUrl)
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await httpClient.GetByteArrayAsync(imageUrl);

                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);

                Dispatcher.UIThread.Post(() =>
                {
                    _announcementImage.Source = bitmap;
                    _imageContainer.IsVisible = true;
                });
            }
            catch
            {
                // ponytail: needs App.Logger ("Failed to load announcement image"), wired when it
                // moves to Core. A missing image leaves the text-only layout, as before.
            }
        }

        private void BtnDownload_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_onAction != null)
            {
                // Dismiss FIRST, through the normal path, so the caller's onDismiss bookkeeping runs
                // (acting on a weekly nudge has to count as answering it, or it fires again next
                // launch). The action itself is then queued behind the fade: it can be heavy - the
                // intake boots a web view window - and running it inline would stall the animation.
                DismissAndClose();

                var act = _onAction;
                Dispatcher.UIThread.Post(() =>
                {
                    try { act(); }
                    catch { /* ponytail: needs App.Logger ("Announcement action failed") */ }
                }, DispatcherPriority.Background);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_linkUrl) &&
                Uri.TryCreate(_linkUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // ponytail: needs App.Logger ("Failed to open announcement link").
                }
            }
        }

        private void DismissAndClose()
        {
            if (_onDismiss != null)
            {
                // Caller owns its own dismissal record - do NOT touch the shared server-announcement slot.
                try { _onDismiss(); }
                catch { /* ponytail: needs App.Logger ("Announcement dismiss handler failed") */ }
            }
            else
            {
                // ponytail: needs App.Settings (the single DismissedAnnouncementId slot), wired when
                // it moves to Core. Until then a server announcement re-shows on the next launch
                // rather than being recorded as seen.
                _ = _announcementId;
            }

            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* already closed */ } },
                    TimeSpan.FromMilliseconds(FadeMs));
            }
            catch
            {
                try { Close(); } catch { }
            }
        }

        private void BtnDismiss_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            DismissAndClose();
        }

        private void BtnClose_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            DismissAndClose();
        }

        // Window_MouseLeftButtonDown is not ported: the WPF handler is deliberately empty
        // ("Don't dismiss on click — announcement has action buttons, use close button or Got it"),
        // and not attaching a handler is the same behaviour.
    }
}
