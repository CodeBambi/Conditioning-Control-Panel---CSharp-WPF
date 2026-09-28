using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
    ///  - LoadGif still shows the first frame: XamlAnimatedGif is WPF-only (see its note).
    ///  - MessageBox.Show maps to this head's <c>Dialogs.MessageDialog</c> everywhere else, but not
    ///    here: the failure paths all fire from <see cref="LoadFile"/>, which callers run BEFORE
    ///    Show(), and <c>ShowDialog(owner)</c> needs an owner that is already shown. So the failure
    ///    paths log through Serilog and close - the same outcome the user saw, minus the notice.
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

        // LibVLC's decode target (vmem): written by the decoder thread, copied to _videoBitmap on
        // the UI thread. _frameLock guards the buffer's lifetime, not tearing - as WPF InlineLoopVideo.
        private readonly object _frameLock = new();
        private IntPtr _frameBuffer;
        private int _frameWidth, _frameHeight, _blitQueued;
        private WriteableBitmap? _videoBitmap;
        private byte[] _row = Array.Empty<byte>();

        /// <summary>For --video-check and its test: the live player (null before load and after close).</summary>
        internal MediaPlayer? Player => _mediaPlayer;
        internal WriteableBitmap? VideoFrame => _videoBitmap;
        internal bool FrameBufferFreed { get { lock (_frameLock) return _frameBuffer == IntPtr.Zero; } }

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
                    Close();
                    return;
                }

                _imagePreview.IsVisible = false;
                _videoContainer.IsVisible = true;

                _mediaPlayer = new MediaPlayer(libVLC);
                _mediaPlayer.EnableHardwareDecoding = true;
                _mediaPlayer.SetVideoFormatCallbacks(VideoFormat, (ref IntPtr _) => { });
                _mediaPlayer.SetVideoCallbacks(VideoLock, null, VideoDisplay);

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
                Close();
            }
        }

        // ---- LibVLC video callbacks (decoder thread) ----

        private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            // RV32 = B,G,R,X in memory: Bgra8888 with the alpha ignored (AlphaFormat.Opaque).
            Marshal.Copy(new[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
            // VLC offers padded decoder dimensions (320x240 arrives as 320x258) and scales into
            // whatever we answer, so answer the track's display size to keep the aspect ratio.
            foreach (var t in _media?.Tracks ?? Array.Empty<MediaTrack>())
            {
                if (t.TrackType != TrackType.Video || t.Data.Video.Width == 0 || t.Data.Video.Height == 0) continue;
                var v = t.Data.Video;
                width = v.SarNum > 0 && v.SarDen > 0 ? v.Width * v.SarNum / v.SarDen : v.Width;
                height = v.Height;
                break;
            }
            pitches = width * 4;
            lines = height;
            lock (_frameLock)
            {
                if (_frameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_frameBuffer);
                _frameBuffer = Marshal.AllocHGlobal((int)(pitches * lines));
                _frameWidth = (int)width;
                _frameHeight = (int)height;
            }
            return 1;
        }

        private IntPtr VideoLock(IntPtr opaque, IntPtr planes)
        {
            lock (_frameLock) Marshal.WriteIntPtr(planes, _frameBuffer);
            return IntPtr.Zero;
        }

        private void VideoDisplay(IntPtr opaque, IntPtr picture)
        {
            // One pending blit at a time: a slow UI thread drops frames rather than queueing them.
            if (Interlocked.Exchange(ref _blitQueued, 1) == 0)
                Dispatcher.UIThread.Post(BlitFrame, DispatcherPriority.Render);
        }

        private void BlitFrame()
        {
            Volatile.Write(ref _blitQueued, 0);
            lock (_frameLock)
            {
                if (_frameBuffer == IntPtr.Zero || _mediaPlayer == null) return;
                if (_videoBitmap == null || _videoBitmap.PixelSize.Width != _frameWidth || _videoBitmap.PixelSize.Height != _frameHeight)
                {
                    _videoBitmap?.Dispose();
                    _videoBitmap = new WriteableBitmap(new PixelSize(_frameWidth, _frameHeight), new Vector(96, 96),
                        PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    _videoImage.Source = _videoBitmap;
                }
                using var fb = _videoBitmap.Lock();
                var rowBytes = _frameWidth * 4;
                if (_row.Length != rowBytes) _row = new byte[rowBytes];
                for (var y = 0; y < _frameHeight; y++)
                {
                    Marshal.Copy(_frameBuffer + y * rowBytes, _row, 0, rowBytes);
                    Marshal.Copy(_row, 0, fb.Address + y * fb.RowBytes, rowBytes);
                }
            }
            _videoImage.InvalidateVisual();
        }

        private void LoadGif(string filePath)
        {
            // ponytail: needs an animated-GIF renderer; XamlAnimatedGif is WPF-only and Avalonia
            // 12 has none built in, so this would be a new package - out of scope for a view layer.
            // The WPF original set AnimationBehavior's SourceUri/AutoStart/RepeatBehavior here and
            // fell back to LoadImage on failure - the still first frame - so take that directly.
            LoadImage(filePath);
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
                Close();
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
            try
            {
                _positionTimer?.Stop();
                _positionTimer = null;

                // Stop joins the decoder thread, so after it no callback touches the buffer.
                if (_mediaPlayer != null)
                {
                    try { _mediaPlayer.Stop(); } catch { /* Ignore stop errors */ }
                    try { _mediaPlayer.Dispose(); } catch { /* Ignore dispose errors */ }
                    _mediaPlayer = null;
                }

                try { _media?.Dispose(); } catch { /* Ignore dispose errors */ }
                _media = null;

                lock (_frameLock)
                {
                    if (_frameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_frameBuffer);
                    _frameBuffer = IntPtr.Zero;
                }
                _videoImage.Source = null;
                _videoBitmap?.Dispose();
                _videoBitmap = null;

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
