using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Borderless popup that plays a short muted, looping tutorial clip for a
    /// <see cref="HelpContent"/> topic, with a caption below and an optional
    /// "watch full tutorial" link. Modeled on Features\FeaturePopupWindow.
    ///
    /// Fail-soft: if LibVLC is unavailable or the clip is missing, the video
    /// surface stays hidden but the caption and link still show. Never throws to
    /// the caller.
    ///
    /// Video load / loop / mute / dispose logic is copied verbatim from
    /// MiniPlayerWindow (including the dispatcher re-marshal on EndReached). One
    /// MediaPlayer is created on open and disposed on close; no hidden player is
    /// ever kept alive.
    /// </summary>
    public partial class HelpVideoWindow : Window
    {
        // Only one help video may play at a time. Opening a new one closes (and
        // disposes) whichever is already open, so two LibVLC players never run at once.
        private static HelpVideoWindow? _current;

        private readonly string? _clipPath;
        private readonly string? _fullTutorialUrl;
        private readonly string? _whatItDoes;
        private bool _captionShown;

        private VideoView? _videoView;
        private MediaPlayer? _mediaPlayer;
        private Media? _media;

        private HelpVideoWindow(HelpContent content)
        {
            InitializeComponent();

            TxtGlyph.Text = string.IsNullOrEmpty(content.Icon) ? "?" : content.Icon;
            TxtTitle.Text = content.Title;
            Title = content.Title; // also set Window.Title for accessibility

            // Caption: reproduce {loc:Str CaptionKey} as a live OneWay binding to
            // the LocalizationManager indexer, so it hot-swaps on language change
            // exactly like StrExtension would for a literal key.
            if (!string.IsNullOrWhiteSpace(content.CaptionKey))
            {
                TxtCaption.SetBinding(TextBlock.TextProperty, new Binding($"[{content.CaptionKey}]")
                {
                    Source = LocalizationManager.Instance,
                    Mode = BindingMode.OneWay
                });
                TxtCaption.Visibility = Visibility.Visible;
                _captionShown = true;
            }

            // Fallback so the window is never empty: with no clip and no caption,
            // show the topic's "what it does" blurb. (Also used if a configured clip
            // fails soft at runtime - see StartClip.)
            _whatItDoes = content.WhatItDoes;
            if (!content.HasClip) ShowWhatItDoesFallback();

            _fullTutorialUrl = content.FullTutorialUrl;
            if (!string.IsNullOrWhiteSpace(_fullTutorialUrl))
            {
                BtnFullTutorial.Visibility = Visibility.Visible;
            }

            if (content.HasClip)
            {
                _clipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Resources", "tutorial_videos", content.ClipFile!);
            }

            // Start playback once the window (and the VideoView HwndHost) is realized.
            Loaded += (_, _) => StartClip();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <summary>
        /// Opens a modeless help video popup for the given topic, centered on owner.
        /// Safe to call for any surface (including full-screen calibration). Never throws.
        /// Any help video already open is closed and disposed first (single live instance).
        /// </summary>
        /// <param name="topmost">
        /// True for surfaces that are themselves topmost/full-screen (e.g. webcam
        /// calibration) so the popup layers above them. Defaults to false elsewhere.
        /// </param>
        public static void Show(HelpContent content, Window? owner, bool topmost = false)
        {
            try
            {
                // Single live instance - never let two players run at once.
                CloseCurrent();

                var win = new HelpVideoWindow(content) { Owner = owner, Topmost = topmost };
                _current = win;
                ((Window)win).Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "HelpVideoWindow: failed to open");
            }
        }

        private static void CloseCurrent()
        {
            var existing = _current;
            _current = null;
            if (existing != null)
            {
                try { existing.Close(); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Shows the topic's "what it does" text in the caption slot when nothing
        /// else would fill it (no clip playing and no localized caption). Idempotent.
        /// </summary>
        private void ShowWhatItDoesFallback()
        {
            if (_captionShown) return;
            if (string.IsNullOrWhiteSpace(_whatItDoes)) return;
            TxtCaption.Text = _whatItDoes;
            TxtCaption.Visibility = Visibility.Visible;
            _captionShown = true;
        }

        private void StartClip()
        {
            // Fail soft: no clip configured, or file missing -> leave video hidden.
            if (string.IsNullOrEmpty(_clipPath) || !File.Exists(_clipPath))
            {
                if (!string.IsNullOrEmpty(_clipPath))
                    App.Logger?.Warning("HelpVideoWindow: clip not found: {Path}", _clipPath);
                ShowWhatItDoesFallback();
                return;
            }

            try
            {
                var libVLC = Services.VideoService.SharedLibVLC;
                if (libVLC == null)
                {
                    // Fail soft - caption + link remain visible.
                    App.Logger?.Warning("HelpVideoWindow: LibVLC not available; hiding video surface");
                    ShowWhatItDoesFallback();
                    return;
                }

                // --- copied from MiniPlayerWindow.LoadVideo (sans transport UI) ---
                _videoView = new VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = System.Windows.Media.Brushes.Black
                };
                VideoContainer.Child = _videoView;
                VideoContainer.Visibility = Visibility.Visible;

                _mediaPlayer = new MediaPlayer(libVLC);
                _mediaPlayer.Mute = true; // muted tutorial loop
                _mediaPlayer.EnableHardwareDecoding = true;

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

                _videoView.MediaPlayer = _mediaPlayer;
                _media = new Media(libVLC, _clipPath, FromType.FromPath);
                _mediaPlayer.Play(_media);
                // ------------------------------------------------------------------
            }
            catch (Exception ex)
            {
                // Fail soft - hide the surface, keep caption + link, never throw.
                App.Logger?.Error(ex, "HelpVideoWindow: failed to start clip");
                try { VideoContainer.Visibility = Visibility.Collapsed; } catch { /* ignore */ }
                ShowWhatItDoesFallback();
            }
        }

        private void BtnFullTutorial_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_fullTutorialUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_fullTutorialUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "HelpVideoWindow: failed to open tutorial url {Url}", _fullTutorialUrl);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { /* dragging can throw if not pressed */ }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            // Drop the single-instance reference if it points at us (whether we were
            // closed by the user or superseded by a newer help video).
            if (ReferenceEquals(_current, this)) _current = null;

            // Disposal copied from MiniPlayerWindow.OnClosed (minus timer / GIF).
            try
            {
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
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error during HelpVideoWindow cleanup");
            }

            base.OnClosed(e);
        }
    }
}
