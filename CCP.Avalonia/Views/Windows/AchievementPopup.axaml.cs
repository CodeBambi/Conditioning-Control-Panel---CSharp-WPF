using System;
using System.IO;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Popup window shown when an achievement is unlocked.
    ///
    /// PORTED from ConditioningControlPanel/Windows/AchievementPopup.xaml.cs. Deviations:
    ///  - The constructor takes the three fields it actually reads instead of an
    ///    <c>Achievement</c>: that model lives in the WPF head, and this project may not
    ///    reference it. Call shape is <c>new AchievementPopup(a.Name, a.FlavorText, a.ImageName)</c>.
    ///  - <c>DoubleAnimation</c> on Opacity becomes a <see cref="DoubleTransition"/> plus a plain
    ///    Opacity assignment - Avalonia animates through the property system, not a Storyboard.
    ///  - <c>SystemParameters.WorkArea</c> + PassiveToastWindow become <see cref="PlacePassive"/>:
    ///    Screens.Primary.WorkingArea in device pixels, then X11 override-redirect before Show().
    ///  - <c>MouseLeftButtonDown</c> becomes PointerPressed, wired in the constructor.
    ///  - <c>App.Logger</c> becomes Serilog's static <c>Log</c>.
    /// </summary>
    public partial class AchievementPopup : Window
    {
        private const double FadeMs = 300;

        private readonly DispatcherTimer _autoCloseTimer;

        /// <summary>Render/design constructor: sample data so --render-view can draw the popup.</summary>
        internal AchievementPopup() : this("Retinal Burn", "The first time it stopped feeling like a choice.", "retinal_burn.png")
        {
            // The fade-in cannot complete inside a headless render's two dispatcher passes, so the
            // PNG would capture a fully transparent window. Skip the animation for the render.
            Transitions = null;
            Opacity = 1;
        }

        /// <param name="name">Achievement.Name.</param>
        /// <param name="flavorText">Achievement.FlavorText.</param>
        /// <param name="imageName">Achievement.ImageName - the file under Resources/achievements/.</param>
        /// <param name="headerIcon">Optional emoji replacing the trophy.</param>
        /// <param name="headerText">Optional shout replacing "ACHIEVEMENT UNLOCKED!".</param>
        public AchievementPopup(string name, string flavorText, string imageName,
                                string? headerIcon = null, string? headerText = null)
        {
            AvaloniaXamlLoader.Load(this);

            // Set content
            this.FindControl<TextBlock>("TxtName")!.Text = name;
            this.FindControl<TextBlock>("TxtFlavor")!.Text = flavorText;

            // Custom header text/icon if provided. The WPF original ran headerIcon through
            // EmojiImage/Twemoji; Avalonia draws the codepoint directly (CLAUDE.md trap 3).
            if (headerIcon != null) this.FindControl<TextBlock>("TxtHeaderIcon")!.Text = headerIcon;
            if (headerText != null) this.FindControl<TextBlock>("TxtHeaderText")!.Text = headerText;

            LoadAchievementImage(imageName);

            // Never take the foreground - same focus-theft gap as the Pink Rush toast (ccp-bugs #1000).
            PlacePassive(this, 20);

            // Auto-close after 6 seconds
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            _autoCloseTimer.Start();

            // Fade in animation
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }
            };
            Opacity = 0;
            Loaded += (_, _) => Opacity = 1;

            this.FindControl<Button>("BtnClose")!.Click += (_, _) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, handledEventsToo: false);
        }

        /// <summary>
        /// WPF PositionWindow + Helpers.PassiveToastWindow.Apply: bottom-right of the primary work area,
        /// 20 DIP from its right edge and <paramref name="bottomDip"/> above its bottom, then
        /// override-redirect - the X11 form of WS_EX_NOACTIVATE|TOOLWINDOW + HWND_TOPMOST: never takes
        /// focus (clicks still arrive), no taskbar entry, above a fullscreen game. Must run before
        /// Show(), so it is called from the constructor. Where the platform refuses (Wayland,
        /// headless) the window is still placed and ShowActivated="False" is the remaining half.
        /// </summary>
        internal static void PlacePassive(Window window, double bottomDip)
        {
            try
            {
                var screen = window.Screens.Primary ?? throw new InvalidOperationException("no primary screen");
                // The screen's scaling, not DesktopScaling: before Show() that still reads 1 and only
                // becomes the screen's after the move (live: a 400x200 toast shrank to 223x112).
                if (!Platform.X11Overlay.SetOverrideRedirect(window,
                        CornerRect(screen.WorkingArea, screen.Scaling, window.Width, window.Height, bottomDip), passive: true))
                    Log.Debug("{Toast}: no override-redirect on this platform; placed only", window.GetType().Name);
            }
            catch (Exception ex)
            {
                // Fallback: centre on screen, as the WPF original did.
                Log.Debug("{Toast}: placement failed, centring: {E}", window.GetType().Name, ex.Message);
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        /// <summary>The toast's rect in device pixels: WorkingArea is pixels, Width/Height are DIPs, so
        /// both are scaled (a raw subtraction pushed the toast off the right edge on a 1.79 screen).</summary>
        internal static PixelRect CornerRect(PixelRect workArea, double scaling, double width, double height, double bottomDip)
            => new((int)(workArea.Right - (width + 20) * scaling), (int)(workArea.Bottom - (height + bottomDip) * scaling),
                   (int)Math.Round(width * scaling), (int)Math.Round(height * scaling));

        /// <summary>
        /// The WPF chain, step for step: the mod's override first (probing the shipped copy first
        /// would make mod art unreachable), then this head's <c>avares://</c> copy, then a loose
        /// file on disk beside the exe (a content pack or hand-dropped art). Nothing found leaves
        /// the art box empty, which is the WPF original's own path for a missing file.
        /// </summary>
        private void LoadAchievementImage(string imageName)
        {
            var image = this.FindControl<Image>("AchievementImage");
            if (image == null || string.IsNullOrWhiteSpace(imageName)) return;

            // A name with a folder in it ("skills/milestone_rewards.png") is resolved as given (WPF :91).
            var relative = imageName.Contains('/') ? imageName : $"achievements/{imageName}";
            var art = Helpers.ModArt.TryLoad(relative);
            if (art != null) { image.Source = art; return; }

            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                        "Resources", relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) image.Source = new Bitmap(path);
                else Log.Warning("Achievement image not found: {Name}", imageName);
            }
            catch (Exception ex) { Log.Warning(ex, "Achievement image {Name} would not load", imageName); }
        }

        private void FadeOutAndClose()
        {
            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* Ignore close errors */ } },
                    TimeSpan.FromMilliseconds(FadeMs));
            }
            catch
            {
                try { Close(); } catch { }
            }
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            FadeOutAndClose();   // the original does not stop the timer here either; OnClosed does
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer.Stop();
            base.OnClosed(e);
        }
    }
}
