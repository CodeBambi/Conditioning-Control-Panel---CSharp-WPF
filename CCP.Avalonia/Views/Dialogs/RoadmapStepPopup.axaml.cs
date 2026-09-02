using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Popup window shown when a roadmap step is completed.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/RoadmapStepPopup.xaml.cs. Deviations:
    ///  - <c>App.Logger</c> calls are dropped; the logger lives on the WPF App singleton.
    ///  - <c>App.Roadmap.GetFullPhotoPath</c> is stubbed, so the thumbnail never loads yet and the
    ///    checkmark stays. <see cref="LoadPhotoThumbnail"/> is otherwise the WPF body.
    ///  - <c>SystemParameters.WorkArea</c> -> <c>Screens.Primary.WorkingArea</c>, which is in
    ///    physical pixels, so the 20px margin and the window size are scaled before subtracting.
    ///  - <c>BeginAnimation(OpacityProperty, DoubleAnimation)</c> has no Avalonia equivalent. A
    ///    <see cref="DoubleTransition"/> on Opacity does the same 300ms fade for a plain assignment,
    ///    and the close is deferred by one timer tick so the fade-out is seen.
    ///  - The fade is skipped in the render constructor: a headless render never advances the
    ///    animation clock, so a window that starts at Opacity 0 would capture a blank PNG.
    /// </summary>
    public partial class RoadmapStepPopup : Window
    {
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);

        private readonly DispatcherTimer _autoCloseTimer;
        private readonly TextBlock _txtStepTitle;
        private readonly TextBlock _txtTrackName;
        private readonly TextBlock _checkmarkIcon;
        private readonly Ellipse _photoEllipse;
        private readonly bool _animate;
        private bool _closing;

        /// <summary>Render/design constructor: sample data so --render-view can draw the popup.</summary>
        internal RoadmapStepPopup()
            : this(new RoadmapStepDefinition("sample", RoadmapTrack.EmptyDoll, 3, "Wear the collar all evening",
                       "Objective", "Photo requirement"),
                   new RoadmapStepProgress("sample"),
                   animate: false)
        {
        }

        public RoadmapStepPopup(RoadmapStepDefinition stepDef, RoadmapStepProgress progress)
            : this(stepDef, progress, animate: true)
        {
        }

        private RoadmapStepPopup(RoadmapStepDefinition stepDef, RoadmapStepProgress progress, bool animate)
        {
            AvaloniaXamlLoader.Load(this);
            _animate = animate;

            _txtStepTitle = this.FindControl<TextBlock>("TxtStepTitle")!;
            _txtTrackName = this.FindControl<TextBlock>("TxtTrackName")!;
            _checkmarkIcon = this.FindControl<TextBlock>("CheckmarkIcon")!;
            _photoEllipse = this.FindControl<Ellipse>("PhotoEllipse")!;

            // Set content
            _txtStepTitle.Text = stepDef.Title;

            // Get track name
            var trackDef = RoadmapTrackDefinition.GetByTrack(stepDef.Track);
            _txtTrackName.Text = trackDef != null
                ? $"{trackDef.Name} - {trackDef.Subtitle}"
                : stepDef.Track.ToString();

            // Load photo thumbnail if available
            LoadPhotoThumbnail(progress);

            // Position in bottom-right corner of primary screen
            PositionWindow();

            // Auto-close after 5 seconds
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoCloseTimer.Tick += (s, e) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            _autoCloseTimer.Start();

            // Handlers live here rather than in markup, per the porting convention. After the timer
            // exists, because the close button stops it.
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => { _autoCloseTimer.Stop(); FadeOutAndClose(); };
            PointerPressed += Window_PointerPressed;

            if (!_animate) return;

            // Fade in animation
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = FadeDuration }
            };
            Opacity = 0;
            Loaded += (s, e) => Opacity = 1;
        }

        /// <summary>
        /// Position the window in the bottom-right corner of the primary screen
        /// </summary>
        private void PositionWindow()
        {
            try
            {
                // Get the working area of the primary screen (excludes taskbar)
                var screen = Screens.Primary;
                if (screen is null) return;

                // WorkingArea is physical pixels; Width/Height and the 20px margin are DIPs.
                var scale = screen.Scaling;
                var area = screen.WorkingArea;

                // Position in bottom-right corner with 20px margin
                Position = new PixelPoint(
                    area.Right - (int)((Width + 20) * scale),
                    area.Bottom - (int)((Height + 20) * scale));
            }
            catch
            {
                // Fallback: centre on screen. Screens is unavailable on some backends before the
                // window is shown, exactly as the WPF original guarded against.
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void LoadPhotoThumbnail(RoadmapStepProgress progress)
        {
            try
            {
                if (string.IsNullOrEmpty(progress.PhotoPath)) return;

                // ponytail: needs App.Roadmap (RoadmapService.GetFullPhotoPath), wired when it moves to Core
                string? fullPath = null;
                if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) return;

                using var stream = System.IO.File.OpenRead(fullPath);
                var bitmap = Bitmap.DecodeToWidth(stream, 100);

                _photoEllipse.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                _photoEllipse.IsVisible = true;
                _checkmarkIcon.IsVisible = false;
            }
            catch
            {
                // Keep showing checkmark icon
            }
        }

        private void FadeOutAndClose()
        {
            if (_closing) return;
            _closing = true;

            if (!_animate)
            {
                Close();
                return;
            }

            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* already closed */ } }, FadeDuration);
            }
            catch
            {
                try { Close(); } catch { /* Ignore close errors */ }
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                FadeOutAndClose();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer.Stop();
            base.OnClosed(e);
        }
    }
}
