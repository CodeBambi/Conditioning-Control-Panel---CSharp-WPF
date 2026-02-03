using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using XamlAnimatedGif;

namespace ConditioningControlPanel
{
    public partial class MiniPlayerWindow : Window
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp" };
        private static readonly string[] GifExtensions = { ".gif" };

        private VideoView? _videoView;
        private MediaPlayer? _mediaPlayer;
        private Media? _media;
        private DispatcherTimer? _positionTimer;
        private bool _isDraggingSlider;
        private bool _isPlaying;
        private string? _currentFilePath;

        public MiniPlayerWindow()
        {
            InitializeComponent();
        }

        public void LoadFile(string filePath)
        {
            _currentFilePath = filePath;
            var fileName = Path.GetFileName(filePath);
            TxtFileName.Text = fileName;
            Title = $"Preview - {fileName}";

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (IsVideoFile(extension))
            {
                LoadVideo(filePath);
            }
            else if (IsGifFile(extension))
            {
                LoadGif(filePath);
            }
            else
            {
                LoadImage(filePath);
            }
        }

        private bool IsVideoFile(string extension)
        {
            return Array.Exists(VideoExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsGifFile(string extension)
        {
            return Array.Exists(GifExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadVideo(string filePath)
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var libVLC = Services.VideoService.SharedLibVLC;
                if (libVLC == null)
                {
                    MessageBox.Show("Video playback not available. LibVLC not initialized.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                // Create VideoView programmatically
                _videoView = new VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = System.Windows.Media.Brushes.Black
                };
                VideoContainer.Child = _videoView;
                VideoContainer.Visibility = Visibility.Visible;

                // Create media player
                _mediaPlayer = new MediaPlayer(libVLC);
                _mediaPlayer.Mute = true; // No audio for preview
                _mediaPlayer.EnableHardwareDecoding = true;

                // Handle events
                _mediaPlayer.Playing += (s, e) => Dispatcher.BeginInvoke(() =>
                {
                    _isPlaying = true;
                    BtnPlayPause.Content = "⏸";
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                });

                _mediaPlayer.Paused += (s, e) => Dispatcher.BeginInvoke(() =>
                {
                    _isPlaying = false;
                    BtnPlayPause.Content = "▶";
                });

                _mediaPlayer.EndReached += (s, e) =>
                {
                    // Loop playback - must detach from LibVLC thread
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_mediaPlayer != null && _media != null)
                        {
                            _mediaPlayer.Stop();
                            _mediaPlayer.Play(_media);
                        }
                    });
                };

                _mediaPlayer.LengthChanged += (s, e) => Dispatcher.BeginInvoke(() =>
                {
                    UpdateTimeDisplay();
                });

                // Attach to view and play
                _videoView.MediaPlayer = _mediaPlayer;
                _media = new Media(libVLC, filePath, FromType.FromPath);
                _mediaPlayer.Play(_media);

                // Show video controls
                VideoControls.Visibility = Visibility.Visible;

                // Start position timer for slider updates
                _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _positionTimer.Tick += PositionTimer_Tick;
                _positionTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MiniPlayerWindow: Failed to load video");
                MessageBox.Show($"Failed to load video: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void LoadGif(string filePath)
        {
            try
            {
                ImagePreview.Visibility = Visibility.Visible;
                VideoControls.Visibility = Visibility.Collapsed;

                var uri = new Uri(filePath, UriKind.Absolute);
                AnimationBehavior.SetSourceUri(ImagePreview, uri);
                AnimationBehavior.SetAutoStart(ImagePreview, true);
                AnimationBehavior.SetRepeatBehavior(ImagePreview, System.Windows.Media.Animation.RepeatBehavior.Forever);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MiniPlayerWindow: Failed to load GIF");
                // Fallback to static image
                LoadImage(filePath);
            }
        }

        private void LoadImage(string filePath)
        {
            try
            {
                ImagePreview.Visibility = Visibility.Visible;
                VideoControls.Visibility = Visibility.Collapsed;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "MiniPlayerWindow: Failed to load image");
                MessageBox.Show($"Failed to load image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _isDraggingSlider) return;

            // Update slider position (0-100)
            SeekSlider.Value = _mediaPlayer.Position * 100;
            UpdateTimeDisplay();
        }

        private void UpdateTimeDisplay()
        {
            if (_mediaPlayer == null) return;

            var currentMs = _mediaPlayer.Time;
            var totalMs = _mediaPlayer.Length;

            var current = TimeSpan.FromMilliseconds(Math.Max(0, currentMs));
            var total = TimeSpan.FromMilliseconds(Math.Max(0, totalMs));

            TxtTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
        }

        private void TogglePlayPause()
        {
            if (_mediaPlayer == null) return;

            if (_isPlaying)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
        }

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            SeekToSliderPosition();
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider && _mediaPlayer != null)
            {
                // Live preview while dragging
                UpdateTimeDisplay();
            }
        }

        private void SeekToSliderPosition()
        {
            if (_mediaPlayer == null) return;

            var position = (float)(SeekSlider.Value / 100.0);
            _mediaPlayer.Position = Math.Clamp(position, 0f, 1f);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.Space:
                    TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000); // -5 seconds
                    }
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 5000); // +5 seconds
                    }
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Stop position timer first
                _positionTimer?.Stop();
                _positionTimer = null;

                // Detach VideoView from MediaPlayer before disposing
                if (_videoView != null)
                {
                    _videoView.MediaPlayer = null;
                }

                // Stop and dispose media player safely
                if (_mediaPlayer != null)
                {
                    try
                    {
                        if (_mediaPlayer.IsPlaying)
                        {
                            _mediaPlayer.Stop();
                        }
                    }
                    catch { /* Ignore stop errors */ }

                    try
                    {
                        _mediaPlayer.Dispose();
                    }
                    catch { /* Ignore dispose errors */ }

                    _mediaPlayer = null;
                }

                // Dispose media
                try
                {
                    _media?.Dispose();
                }
                catch { /* Ignore dispose errors */ }
                _media = null;

                _videoView = null;

                // Clear GIF animation
                try
                {
                    AnimationBehavior.SetSourceUri(ImagePreview, null);
                }
                catch { /* Ignore GIF cleanup errors */ }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error during MiniPlayerWindow cleanup");
            }

            base.OnClosed(e);
        }
    }
}
