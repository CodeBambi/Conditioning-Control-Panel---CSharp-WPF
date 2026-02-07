using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window for server-triggered announcements with optional image support.
/// </summary>
public partial class AnnouncementPopup : Window
{
    private readonly string _announcementId;

    public AnnouncementPopup(string id, string title, string message, string? imageUrl)
    {
        InitializeComponent();

        _announcementId = id;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

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
