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
    private readonly string _announcementId;
    private readonly string? _linkUrl;

    public AnnouncementPopup(string id, string title, string message, string? imageUrl,
        string? linkUrl = null, string? theme = null)
    {
        InitializeComponent();

        _announcementId = id;
        _linkUrl = linkUrl;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

        // Show download button if link_url is provided
        if (!string.IsNullOrWhiteSpace(linkUrl))
        {
            BtnDownload.Visibility = Visibility.Visible;
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
        };

        // Load image asynchronously if URL provided
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            _ = LoadImageAsync(imageUrl);
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
        if (App.Settings?.Current != null)
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
        try { DragMove(); }
        catch { }
    }
}
