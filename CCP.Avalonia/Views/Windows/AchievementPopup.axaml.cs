using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

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
    ///  - <c>SystemParameters.WorkArea</c> becomes <c>Screens.Primary.WorkingArea</c>, which is only
    ///    populated once the window has a platform handle, so placement moves to OnOpened.
    ///  - <c>MouseLeftButtonDown</c> becomes PointerPressed, wired in the constructor.
    ///  - <c>App.Logger</c> calls are dropped; there is no logger on this head yet.
    /// </summary>
    public partial class AchievementPopup : Window
    {
        private const double FadeMs = 300;

        private readonly DispatcherTimer _autoCloseTimer;

        /// <summary>Render/design constructor: sample data so --render-view can draw the popup.</summary>
        internal AchievementPopup() : this("Good Girl", "The first time it stopped feeling like a choice.", "good_girl.png")
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

            // Never take the foreground - same focus-theft gap as the Pink Rush toast (ccp-bugs
            // #1000). ponytail: needs Helpers.PassiveToastWindow (Win32 WS_EX_NOACTIVATE), wired
            // when the per-platform equivalent lands. ShowActivated="False" is the portable half.

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
        /// Position the window in the bottom-right corner of the primary screen.
        /// Screens is null until the window has a handle, so this runs on open rather than in the
        /// constructor as WPF's SystemParameters.WorkArea allowed.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                var workArea = Screens.Primary?.WorkingArea
                    ?? throw new InvalidOperationException("no primary screen");

                // Position in bottom-right corner with 20px margin
                Position = new PixelPoint(
                    workArea.Right - (int)Width - 20,
                    workArea.Bottom - (int)Height - 20);
            }
            catch
            {
                // Fallback: centre on screen, as the WPF original did.
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        /// <summary>
        /// ponytail: needs Services.ModResourceResolver (mod override, then a pack:// resource) and
        /// the Resources/achievements/ payload; both stay in the WPF head. The art box renders empty
        /// until they move to Core, exactly as the WPF original does for a missing file.
        /// </summary>
        private void LoadAchievementImage(string imageName)
        {
            _ = imageName;
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
