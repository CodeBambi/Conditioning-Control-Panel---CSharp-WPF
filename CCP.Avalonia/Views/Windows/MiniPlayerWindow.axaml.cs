using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Borderless mini preview window: plays a video (LibVLC), animates a GIF, or shows a still
    /// image, with a drag-anywhere title bar and keyboard transport.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MiniPlayerWindow.xaml.cs. Deviations:
    ///  - LibVLCSharp.WPF (VideoView/MediaPlayer/Media) and XamlAnimatedGif are both WPF-only
    ///    packages, and SharedLibVLC lives in ConditioningControlPanel/Services/Video/VideoService.cs.
    ///    So LoadVideo, LoadGif's animation, the position timer, seeking and play/pause are stubs;
    ///    each is marked. Everything that is view-only - the chrome, the drag, Escape/Space/
    ///    arrow key routing, the play glyph, the time formatting - is ported for real.
    ///  - LoadImage IS real: Avalonia's Bitmap loads from a path with no WPF dependency, and a
    ///    GIF falls back to its first frame through it, exactly as the WPF catch block did.
    ///  - MessageBox.Show maps to this head's <c>Dialogs.MessageDialog</c> everywhere else, but not
    ///    here: the failure paths all fire from <see cref="LoadFile"/>, which callers run BEFORE
    ///    Show(), and <c>ShowDialog(owner)</c> needs an owner that is already shown. So the failure
    ///    paths log through Serilog and close - the same outcome the user saw, minus the notice.
    ///  - PreviewMouseDown/Up become plain PointerPressed/PointerReleased. The tunnelling pass
    ///    existed to beat the Thumb to the event; Avalonia's Slider raises these on the way out
    ///    too, and the flag they set is only read by the position timer.
    ///  - DragMove() -> BeginMoveDrag(e), which needs the event args.
    /// </summary>
    public partial class MiniPlayerWindow : Window
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp" };
        private static readonly string[] GifExtensions = { ".gif" };

        private readonly TextBlock _txtFileName, _txtTime;
        private readonly Button _btnPlayPause;
        private readonly Slider _seekSlider;
        private readonly Border _videoContainer;
        private readonly Image _imagePreview;
        private readonly Grid _loadingOverlay, _videoControls;

        private bool _isDraggingSlider;
        private bool _isPlaying;
        private string? _currentFilePath;

        public MiniPlayerWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _txtFileName = this.FindControl<TextBlock>("TxtFileName")!;
            _txtTime = this.FindControl<TextBlock>("TxtTime")!;
            _btnPlayPause = this.FindControl<Button>("BtnPlayPause")!;
            _seekSlider = this.FindControl<Slider>("SeekSlider")!;
            _videoContainer = this.FindControl<Border>("VideoContainer")!;
            _imagePreview = this.FindControl<Image>("ImagePreview")!;
            _loadingOverlay = this.FindControl<Grid>("LoadingOverlay")!;
            _videoControls = this.FindControl<Grid>("VideoControls")!;

            // Set, not bound: LoadFile overwrites both, and an Avalonia binding survives a local
            // set. See the header comment in the .axaml.
            _txtFileName.Text = Loc.Get("section_preview");
            Title = Loc.Get("section_preview");

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            _btnPlayPause.Click += (_, _) => TogglePlayPause();
            _seekSlider.PointerPressed += (_, _) => _isDraggingSlider = true;
            _seekSlider.PointerReleased += (_, _) => { _isDraggingSlider = false; SeekToSliderPosition(); };
            _seekSlider.AddHandler(RangeBase.ValueChangedEvent, SeekSlider_ValueChanged);
            PointerPressed += Window_PointerPressed;
            KeyDown += Window_KeyDown;

            // ponytail: placeholder state, for the same missing player LoadVideo names
            // (SharedLibVLC in ConditioningControlPanel/Services/Video/VideoService.cs).
            // Without it nothing ever calls LoadFile here, and an all-collapsed window renders as
            // a black rectangle that proves nothing. This is the state LoadVideo leaves behind
            // one line in - overlay up, transport visible - so --render-view draws the real
            // slider and button templates. LoadFile still drives these normally.
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
            // ponytail: needs SharedLibVLC from ConditioningControlPanel/Services/Video/VideoService.cs
            // plus a non-WPF video surface to parent a player into; neither exists on this head.
            // The WPF original built a MediaPlayer here, hung Playing/Paused/EndReached/
            // LengthChanged off it, parented a VideoView into VideoContainer and started a 100ms
            // DispatcherTimer to drive SeekSlider.
            //
            // This IS the WPF "libVLC == null" branch, which said so and closed the window. It is
            // kept open instead so the transport draws for --render-view; the spinner therefore
            // never resolves and nothing plays.
            Log.Warning("MiniPlayerWindow: {Msg} ({Path})",
                Loc.Get("msg_video_playback_not_available_libvlc_not_initi"), filePath);
            _videoContainer.IsVisible = true;
            _loadingOverlay.IsVisible = true;
            _videoControls.IsVisible = true;
            _isPlaying = false;
            _btnPlayPause.Content = "▶";
            UpdateTimeDisplay();
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

        private void UpdateTimeDisplay()
        {
            // ponytail: needs the LibVLCSharp MediaPlayer LoadVideo would build (see its note) for
            // Time/Length; the format is the WPF one verbatim.
            var current = TimeSpan.Zero;
            var total = TimeSpan.Zero;

            _txtTime.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
        }

        private void TogglePlayPause()
        {
            // ponytail: needs the LibVLCSharp MediaPlayer LoadVideo would build (see its note) to
            // Pause()/Play(); the glyph swap is the view half of the WPF Playing/Paused handlers
            // and is kept so the button is not inert.
            _isPlaying = !_isPlaying;
            _btnPlayPause.Content = _isPlaying ? "⏸" : "▶";
        }

        private void SeekSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDraggingSlider)
            {
                // Live preview while dragging
                UpdateTimeDisplay();
            }
        }

        private void SeekToSliderPosition()
        {
            // ponytail: needs the LibVLCSharp MediaPlayer LoadVideo would build (see its note);
            // Position is a 0-1 float of SeekSlider.Value / 100.
            _ = Math.Clamp(_seekSlider.Value / 100.0, 0.0, 1.0);
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
                    // ponytail: needs the LibVLCSharp MediaPlayer LoadVideo would build (see its
                    // note) - this was Time -= 5000ms.
                    e.Handled = true;
                    break;
                case Key.Right:
                    // ponytail: needs the LibVLCSharp MediaPlayer LoadVideo would build (see its
                    // note) - this was Time += 5000ms, clamped to Length.
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // The WPF original tore down the position timer, the VideoView, the MediaPlayer, the
            // Media and the GIF animation here. All of those come back with the video service; the
            // Bitmap LoadImage sets is the only unmanaged handle this port owns.
            try { (_imagePreview.Source as Bitmap)?.Dispose(); } catch { /* already disposed */ }
            _imagePreview.Source = null;

            base.OnClosed(e);
        }
    }
}
