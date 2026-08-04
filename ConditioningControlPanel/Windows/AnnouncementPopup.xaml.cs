using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window for server-triggered announcements with optional image, link, and theme support.
/// </summary>
public partial class AnnouncementPopup : Window
{
    /// <summary>Window width once the card-art rail is showing. The default 440 is sized for the
    /// stacked, text-only server announcement; the rail eats 210 of it, so the copy column would
    /// be unreadably narrow without this. At 620 the copy column lands near 346px, which holds a
    /// ~130-character body in three lines - the ceiling the nudge copy is written to.</summary>
    private const double CardLayoutWidth = 620;

    private readonly string _announcementId;
    private readonly string? _linkUrl;
    private readonly Action? _onDismiss;
    private readonly Action? _onAction;

    /// <summary>Only non-null in card layout. Held so the entrance animation can be torn down on
    /// close - a live animation pinned to a transform keeps the clock alive.</summary>
    private ScaleTransform? _cardArtScale;

    /// <param name="onDismiss">Optional replacement for the default dismissal bookkeeping. The
    /// popup normally records <c>AppSettings.DismissedAnnouncementId</c>, which is a SINGLE slot
    /// owned by server-triggered announcements. Any local, recurring popup that reuses this
    /// window must pass its own handler, or dismissing it would silently consume the slot and
    /// suppress the next real server announcement.</param>
    /// <param name="cardImage">Optional LOCAL art (a resolved <see cref="ImageSource"/>, not a
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
        ImageSource? cardImage = null, string? actionText = null, Action? onAction = null,
        string? dismissText = null)
    {
        InitializeComponent();

        _announcementId = id;
        _linkUrl = linkUrl;
        _onDismiss = onDismiss;
        _onAction = onAction;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

        // Primary button: a caller-supplied action wins over the server's link_url. The
        // link_url branch below is byte-identical to the original behaviour when onAction is null.
        if (onAction != null && !string.IsNullOrWhiteSpace(actionText))
        {
            BtnDownload.Content = actionText;
            BtnDownload.Visibility = Visibility.Visible;
        }
        else if (!string.IsNullOrWhiteSpace(linkUrl))
        {
            BtnDownload.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(dismissText))
        {
            BtnDismiss.Content = dismissText;
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
        Opacity = 0;
        Loaded += (s, e) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);

            // Transform animations are started straight off the transform object. A Storyboard
            // with SetTargetName silently no-ops across namescopes in this codebase.
            if (_cardArtScale != null)
            {
                var pop = new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _cardArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _cardArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        };

        Closed += (s, e) =>
        {
            try
            {
                BeginAnimation(OpacityProperty, null);
                if (_cardArtScale != null)
                {
                    _cardArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    _cardArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
            }
            catch { }
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
    private void ApplyCardLayout(ImageSource art)
    {
        try
        {
            Width = CardLayoutWidth;

            CardArtImage.Source = art;
            CardArtPanel.Visibility = Visibility.Visible;

            // Centred copy reads as a notice; ranged against art it reads as a card face.
            TxtTitle.TextAlignment = TextAlignment.Left;
            TxtMessage.TextAlignment = TextAlignment.Left;
            TxtMessage.Margin = new Thickness(0, 0, 0, 18);

            // Buttons go side by side under the copy instead of stacking down the middle.
            ButtonRow.Orientation = Orientation.Horizontal;
            ButtonRow.HorizontalAlignment = HorizontalAlignment.Left;

            BtnDownload.Width = 176;
            BtnDownload.Height = 38;
            BtnDownload.Margin = new Thickness(0, 0, 10, 0);

            BtnDismiss.Width = 116;
            BtnDismiss.Height = 38;

            _cardArtScale = new ScaleTransform(0.94, 0.94);
            CardArtImage.RenderTransform = _cardArtScale;
        }
        catch (Exception ex)
        {
            // A layout failure must not cost the user the message itself.
            App.Logger?.Warning("AnnouncementPopup card layout failed: {Error}", ex.Message);
        }
    }

    private void ApplyMatrixTheme()
    {
        var matrixGreen = (Color)ColorConverter.ConvertFromString("#00FF41");
        var matrixGreenBrush = new SolidColorBrush(matrixGreen);
        matrixGreenBrush.Freeze();

        var matrixLightGreen = (Color)ColorConverter.ConvertFromString("#39FF14");
        var matrixLightGreenBrush = new SolidColorBrush(matrixLightGreen);
        matrixLightGreenBrush.Freeze();

        var matrixBg = (Color)ColorConverter.ConvertFromString("#0D0D0D");
        var matrixBgBrush = new SolidColorBrush(Color.FromArgb(0xF0, matrixBg.R, matrixBg.G, matrixBg.B));
        matrixBgBrush.Freeze();

        var matrixBlack = new SolidColorBrush(Colors.Black);
        matrixBlack.Freeze();

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
                ShadowDepth = 0,
                Opacity = 0.6
            };
        }

        // Title
        TxtTitle.Foreground = matrixGreenBrush;
        TxtTitle.FontFamily = consolasFont;

        // Message
        TxtMessage.Foreground = matrixLightGreenBrush;
        TxtMessage.FontFamily = consolasFont;

        // Download button — matrix style
        if (BtnDownload.Visibility == Visibility.Visible)
        {
            ApplyMatrixButtonStyle(BtnDownload, matrixGreenBrush, matrixLightGreenBrush, matrixBlack, consolasFont);
        }

        // Dismiss button — matrix style
        ApplyMatrixButtonStyle(BtnDismiss, matrixGreenBrush, matrixLightGreenBrush, matrixBlack, consolasFont);
    }

    private static void ApplyMatrixButtonStyle(Button button, SolidColorBrush normalBg,
        SolidColorBrush hoverBg, SolidColorBrush foreground, FontFamily font)
    {
        button.FontFamily = font;

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "border";
        borderFactory.SetValue(Border.BackgroundProperty, normalBg);
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(16, 6, 16, 6));

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentPresenter.SetValue(TextElement.ForegroundProperty, foreground);
        borderFactory.AppendChild(contentPresenter);

        template.VisualTree = borderFactory;

        // Hover trigger
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "border"));
        template.Triggers.Add(hoverTrigger);

        // Pressed trigger
        var pressedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00CC33"));
        pressedBrush.Freeze();
        var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressedBrush, "border"));
        template.Triggers.Add(pressedTrigger);

        button.Template = template;
    }

    private async System.Threading.Tasks.Task LoadImageAsync(string imageUrl)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await httpClient.GetByteArrayAsync(imageUrl);

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            Dispatcher.Invoke(() =>
            {
                AnnouncementImage.Source = bitmap;
                ImageContainer.Visibility = Visibility.Visible;
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Failed to load announcement image: {Error}", ex.Message);
        }
    }

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_onAction != null)
        {
            // Dismiss FIRST, through the normal path, so the caller's onDismiss bookkeeping runs
            // (acting on a weekly nudge has to count as answering it, or it fires again next
            // launch). The action itself is then queued behind the fade: it can be heavy - the
            // intake boots a WebView2 window - and running it inline would stall the animation.
            DismissAndClose();

            var act = _onAction;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (Application.Current?.Dispatcher == null) return;
                if (Application.Current.Dispatcher.HasShutdownStarted) return;
                try { act(); }
                catch (Exception ex) { App.Logger?.Warning("Announcement action failed: {Error}", ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
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
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to open announcement link: {Error}", ex.Message);
            }
        }
    }

    private void DismissAndClose()
    {
        if (_onDismiss != null)
        {
            // Caller owns its own dismissal record - do NOT touch the shared server-announcement slot.
            try { _onDismiss(); }
            catch (Exception ex) { App.Logger?.Warning("Announcement dismiss handler failed: {Error}", ex.Message); }
        }
        else if (App.Settings?.Current != null)
        {
            App.Settings.Current.DismissedAnnouncementId = _announcementId;
        }

        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                try { Close(); }
                catch { }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch
        {
            try { Close(); } catch { }
        }
    }

    private void BtnDismiss_Click(object sender, RoutedEventArgs e)
    {
        DismissAndClose();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DismissAndClose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't dismiss on click — announcement has action buttons, use close button or "Got it"
    }
}
