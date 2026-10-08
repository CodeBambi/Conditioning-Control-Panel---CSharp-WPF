using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Borderless mini preview window: plays a video (LibVLC), animates a GIF, or shows a still
    /// image, with a drag-anywhere title bar and keyboard transport.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MiniPlayerWindow.xaml.cs. Deviations:
    ///  - Video: WPF parented a LibVLCSharp.WPF VideoView. LibVLCSharp.Avalonia targets Avalonia 11,
    ///    so LibVLC decodes into a buffer through its video callbacks and each frame is copied into a
    ///    WriteableBitmap shown by VideoImage (the same frame copy WPF's InlineLoopVideo does). The
    ///    LibVLC is the process-wide one audio already initialised (<see cref="LibVlcAudio.Shared"/>),
    ///    as WPF shares VideoService.SharedLibVLC.
    ///  - LoadGif: XamlAnimatedGif is WPF-only, so GIFs loop through SpiralOverlay's SkiaSharp
    ///    decoder and a frame timer (see LoadGif for its caps).
    ///  - MessageBox.Show -> <c>Dialogs.MessageDialog</c>, deferred to Opened: the failure paths fire
    ///    from <see cref="LoadFile"/>, which callers run BEFORE Show(), and <c>ShowDialog(owner)</c>
    ///    needs a shown owner. The window closes once the notice is dismissed.
    ///  - PreviewMouseDown/Up become PointerPressed/PointerReleased with handledEventsToo: the
    ///    Slider marks them handled, which is what WPF's tunnelling pass got around.
    ///  - DragMove() -> BeginMoveDrag(e), which needs the event args.
    ///
    /// <para><b>No opener yet, and the blocker is the CALLER, not this window.</b> Its one WPF call
    /// site is <c>MainWindow.OpenAssetPreview</c> (MainWindow.Assets.cs:1233), reached from a
    /// thumbnail click in the Library tab. Nothing on this head raises that click: the asset tree
    /// and the thumbnail grid are held back in MainShellWindow.Assets.cs because
    /// <c>App.ContentPacks</c> (Services/Content/ContentPackService.cs) is not ported, and
    /// OpenAssetPreview's first branch is a pack-file extraction through that same service. When
    /// the tree lands, the wiring is <c>LoadFile(path)</c> then <c>Show(owner)</c>. Until then
    /// <c>--video-check &lt;file&gt;</c> (VideoCheck.cs) opens it on the real desktop.</para>
    /// </summary>
    public partial class MiniPlayerWindow : Window
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp" };
        private static readonly string[] GifExtensions = { ".gif" };

        private readonly TextBlock _txtFileName, _txtTime;
        private readonly Button _btnPlayPause;
        private readonly Slider _seekSlider;
        private readonly Border _videoContainer;
        private readonly Image _imagePreview, _videoImage;
        private readonly Grid _loadingOverlay, _videoControls;

        private MediaPlayer? _mediaPlayer;
        private Media? _media;
        private DispatcherTimer? _positionTimer;
        private bool _isDraggingSlider;
        private bool _isPlaying;
        private string? _currentFilePath;

        // LibVLC's decode target, copied into a WriteableBitmap on the UI thread.
        private VlcFrameSink? _frames;

        private List<Bitmap>? _gifFrames;
        private int _gifIndex;
        private DispatcherTimer? _gifTimer;
        private string? _pendingNotice;
        private bool _closed;

        /// <summary>For tests: the GIF decode in flight, the error notice once shown, the loop state.</summary>
        internal Task? GifDecoding { get; private set; }
        internal Task<bool>? NoticeShowing { get; private set; }
        internal int GifFrameCount => _gifFrames?.Count ?? 0;
        internal bool GifAnimating => _gifTimer?.IsEnabled == true;

        /// <summary>For --video-check and its test: the live player (null before load and after close).</summary>
        internal MediaPlayer? Player => _mediaPlayer;
        internal WriteableBitmap? VideoFrame => _frames?.Bitmap;
        internal bool FrameBufferFreed => _frames?.Freed ?? true;

        public MiniPlayerWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _txtFileName = this.FindControl<TextBlock>("TxtFileName")!;
            _txtTime = this.FindControl<TextBlock>("TxtTime")!;
            _btnPlayPause = this.FindControl<Button>("BtnPlayPause")!;
            _seekSlider = this.FindControl<Slider>("SeekSlider")!;
            _videoContainer = this.FindControl<Border>("VideoContainer")!;
            _imagePreview = this.FindControl<Image>("ImagePreview")!;
            _videoImage = this.FindControl<Image>("VideoImage")!;
            _loadingOverlay = this.FindControl<Grid>("LoadingOverlay")!;
            _videoControls = this.FindControl<Grid>("VideoControls")!;

            // Set, not bound: LoadFile overwrites both, and an Avalonia binding survives a local
            // set. See the header comment in the .axaml.
            _txtFileName.Text = Loc.Get("section_preview");
            Title = Loc.Get("section_preview");

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            _btnPlayPause.Click += (_, _) => TogglePlayPause();
            _seekSlider.AddHandler(PointerPressedEvent, (_, _) => _isDraggingSlider = true,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            _seekSlider.AddHandler(PointerReleasedEvent, (_, _) => { _isDraggingSlider = false; SeekToSliderPosition(); },
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            _seekSlider.AddHandler(RangeBase.ValueChangedEvent, SeekSlider_ValueChanged);
            PointerPressed += Window_PointerPressed;
            KeyDown += Window_KeyDown;
            Opened += (_, _) => _ = ShowNoticeAndClose();

            // Render-proof state only: --render-all builds this window without LoadFile, and an
            // all-collapsed window renders as a black rectangle that proves nothing. This is the
            // state LoadVideo is in until the first frame - overlay up, transport visible.
            _loadingOverlay.IsVisible = true;
            _videoControls.IsVisible = true;
        }

        public void LoadFile(string filePath)
        {
            _currentFilePath = filePath;
            var fileName = Path.GetFileName(filePath);
            _txtFileName.Text = fileName;
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
                _loadingOverlay.IsVisible = true;

                var libVLC = LibVlcAudio.Shared;
                if (libVLC == null)
                {
                    Log.Warning("MiniPlayerWindow: {Msg}", Loc.Get("msg_video_playback_not_available_libvlc_not_initi"));
                    FailWithNotice(Loc.Get("msg_video_playback_not_available_libvlc_not_initi"));
                    return;
                }

                _imagePreview.IsVisible = false;
                _videoContainer.IsVisible = true;

                _mediaPlayer = new MediaPlayer(libVLC);
                _mediaPlayer.EnableHardwareDecoding = true;
                _frames = new VlcFrameSink(_mediaPlayer, () => _media, bmp => _videoImage.Source = bmp, _videoImage.InvalidateVisual);

                _mediaPlayer.Playing += (s, e) => Dispatcher.UIThread.Post(() =>
                {
                    _isPlaying = true;
                    _btnPlayPause.Content = "⏸";
                    _loadingOverlay.IsVisible = false;
                });

                _mediaPlayer.Paused += (s, e) => Dispatcher.UIThread.Post(() =>
                {
                    _isPlaying = false;
                    _btnPlayPause.Content = "▶";
                });

                _mediaPlayer.EndReached += (s, e) =>
                {
                    // Loop playback - must detach from LibVLC thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_mediaPlayer != null && _media != null)
                        {
                            _mediaPlayer.Stop();
                            _mediaPlayer.Play(_media);
                        }
                    });
                };

                _mediaPlayer.LengthChanged += (s, e) => Dispatcher.UIThread.Post(UpdateTimeDisplay);

                _media = new Media(libVLC, filePath, FromType.FromPath);
                // No audio for preview, as WPF (Services.LibVlcSilence.NoAudioOption).
                _media.AddOption(":no-audio");
                _mediaPlayer.Play(_media);

                _videoControls.IsVisible = true;

                _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _positionTimer.Tick += PositionTimer_Tick;
                _positionTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MiniPlayerWindow: Failed to load video");
                FailWithNotice(Loc.GetF("msg_video_load_failed", ex.Message));
            }
        }

        /// <summary>WPF AnimationBehavior (AutoStart, RepeatBehavior Forever) on the spiral's SkiaSharp
        /// decoder: the still first frame shows at once (LoadImage, WPF's fallback), the frames decode
        /// off the UI thread and then loop. Deviation: SpiralFrames' caps apply (1280 px long side,
        /// at most 120 frames by stride) and every frame plays at the first frame's delay.</summary>
        private void LoadGif(string filePath)
        {
            LoadImage(filePath);
            if (_pendingNotice != null) return;
            GifDecoding = DecodeGif(filePath);
        }

        private async Task DecodeGif(string filePath)
        {
            var (frames, delay) = await Task.Run(() => SpiralOverlay.Decode(filePath));
            if (_closed || frames.Count < 2)
            {
                foreach (var f in frames) f.Dispose();
                return;
            }
            (_imagePreview.Source as Bitmap)?.Dispose();
            _gifFrames = frames;
            _gifIndex = 0;
            _imagePreview.Source = frames[0];
            _gifTimer = new DispatcherTimer { Interval = delay };
            _gifTimer.Tick += (_, _) => NextGifFrame();
            _gifTimer.Start();
        }

        /// <summary>One tick of the GIF loop (the timer's handler; tests step it directly).</summary>
        internal void NextGifFrame()
        {
            if (_gifFrames == null) return;
            _gifIndex = (_gifIndex + 1) % _gifFrames.Count;
            _imagePreview.Source = _gifFrames[_gifIndex];
        }

        /// <summary>WPF MessageBox.Show then Close. LoadFile runs before Show(), and a dialog needs a
        /// shown owner, so the notice waits for Opened and the window closes once it is dismissed.</summary>
        private void FailWithNotice(string message)
        {
            _pendingNotice = message;
            _loadingOverlay.IsVisible = false;
            if (IsVisible) _ = ShowNoticeAndClose();
        }

        private async Task ShowNoticeAndClose()
        {
            var message = _pendingNotice;
            _pendingNotice = null;
            if (message == null) return;
            NoticeShowing = MessageDialog.ShowAsync(this, Loc.Get("title_error"), message);
            await NoticeShowing;
            Close();
        }

        private void LoadImage(string filePath)
        {
            try
            {
                _imagePreview.IsVisible = true;
                _videoControls.IsVisible = false;
                _loadingOverlay.IsVisible = false;

                _imagePreview.Source = new Bitmap(filePath);
            }
            catch (Exception ex)
            {
                // ponytail: WPF showed a MessageBox here. Dialogs.MessageDialog is this head's
                // equivalent, but LoadFile runs before the window is shown and ShowDialog needs a
                // shown owner, so telling the user means deferring the notice to Opened. Logged
                // until then; closing is the WPF behaviour and is what matters to the user.
                Log.Error(ex, "MiniPlayerWindow: Failed to load image");
                FailWithNotice(Loc.GetF("msg_image_load_failed", ex.Message));
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _isDraggingSlider) return;

            // Update slider position (0-100)
            _seekSlider.Value = _mediaPlayer.Position * 100;
            UpdateTimeDisplay();
        }

        private void UpdateTimeDisplay()
        {
            if (_mediaPlayer == null) return;

            var current = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));
            var total = TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));

            _txtTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
        }

        internal void TogglePlayPause()
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

        private void SeekSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDraggingSlider && _mediaPlayer != null)
            {
                // Live preview while dragging
                UpdateTimeDisplay();
            }
        }

        /// <summary>The slider's pointer-release path, as WPF SeekSlider_PreviewMouseUp.</summary>
        internal void SeekToSliderPosition()
        {
            if (_mediaPlayer == null) return;

            var position = (float)(_seekSlider.Value / 100.0);
            _mediaPlayer.Position = Math.Clamp(position, 0f, 1f);
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Allow dragging the window
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
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
            _closed = true;
            try
            {
                _positionTimer?.Stop();
                _positionTimer = null;

                _gifTimer?.Stop();
                _gifTimer = null;
                if (_gifFrames != null)
                {
                    _imagePreview.Source = null;
                    foreach (var f in _gifFrames) f.Dispose();
                    _gifFrames = null;
                }

                // Stop joins the decoder thread, so after it no callback touches the buffer.
                if (_mediaPlayer != null)
                {
                    try { _mediaPlayer.Stop(); } catch { /* Ignore stop errors */ }
                    try { _mediaPlayer.Dispose(); } catch { /* Ignore dispose errors */ }
                    _mediaPlayer = null;
                }

                try { _media?.Dispose(); } catch { /* Ignore dispose errors */ }
                _media = null;

                _videoImage.Source = null;
                _frames?.Free();

                (_imagePreview.Source as Bitmap)?.Dispose();
                _imagePreview.Source = null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during MiniPlayerWindow cleanup");
            }

            base.OnClosed(e);
        }
    }
}
