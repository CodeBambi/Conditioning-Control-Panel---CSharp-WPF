using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Popup window shown when Pink Rush activates, with a live countdown timer.
    ///
    /// PORTED from ConditioningControlPanel/Windows/PinkRushPopup.xaml.cs. Deviations:
    ///  - The constructor takes the boost's end time instead of re-reading
    ///    <c>CoreSettings.Current.PinkRushEndTime</c> on every tick. The render harness needs a
    ///    parameterless constructor that DRAWS, and headless that setting is null, which on the
    ///    WPF path closes the popup immediately - a blank PNG. Call shape is
    ///    <c>new PinkRushPopup(CoreSettings.Current.PinkRushEndTime.Value)</c>.
    ///  - <c>DoubleAnimation</c> on Opacity becomes a <see cref="DoubleTransition"/> plus a plain
    ///    Opacity assignment - Avalonia animates through the property system, not a Storyboard.
    ///  - <c>SystemParameters.WorkArea</c> becomes <c>Screens.Primary.WorkingArea</c>, populated
    ///    only once the window has a handle, so placement moves to OnOpened.
    ///  - <c>App.Logger</c> becomes Serilog's static <c>Log</c>.
    /// </summary>
    public partial class PinkRushPopup : Window
    {
        private const double FadeMs = 300;

        private readonly DispatcherTimer _countdownTimer;
        private readonly DateTime _endTime;
        private readonly TextBlock _txtCountdown;

        /// <summary>Render/design constructor: a sample 60s boost so --render-view can draw it.</summary>
        internal PinkRushPopup() : this(DateTime.Now.AddSeconds(60))
        {
            // The fade-in cannot complete inside a headless render's two dispatcher passes, so the
            // PNG would capture a fully transparent window. Skip the animation for the render.
            Transitions = null;
            Opacity = 1;
        }

        /// <param name="endTime">When the boost expires - <c>AppSettings.PinkRushEndTime</c> on WPF.</param>
        public PinkRushPopup(DateTime endTime)
        {
            AvaloniaXamlLoader.Load(this);

            _endTime = endTime;
            _txtCountdown = this.FindControl<TextBlock>("TxtCountdown")!;

            // Mod override for the title. The TextBlock carries a plain literal in the markup (no
            // {loc:Str} binding), so assigning Text is safe. CoreMods answers with the vanilla
            // default when no mod layer is up, which is what App.Mods == null gave WPF.
            this.FindControl<TextBlock>("TxtPinkRushTitle")!.Text = "\u26A1 " + CoreMods.PinkRushName;

            // ponytail: needs CoreMods.PinkRushDescription for the subtitle - the provider does not
            // exist in CCP.Core/CoreMods.cs; the value is ModService.GetPinkRushDescription()
            // (ConditioningControlPanel/Services/ModService.cs:1340). The markup literal is the
            // un-modded default, exactly as in the WPF original.

            // ponytail: needs Helpers.PassiveToastWindow (Win32 WS_EX_NOACTIVATE), which kept this
            // toast from stealing mouse capture from fullscreen games (ccp-bugs #1000), wired when
            // a per-platform equivalent lands. ShowActivated="False" is the portable half.

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();

            // Update immediately
            UpdateCountdown();

            // Fade in
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }
            };
            Opacity = 0;
            Loaded += (_, _) => Opacity = 1;

            this.FindControl<Button>("BtnClose")!.Click += BtnClose_Click;
            AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, handledEventsToo: false);
        }

        /// <summary>Bottom-right of the work area, or centred if the work area is unreadable.</summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                var workArea = Screens.Primary?.WorkingArea
                    ?? throw new InvalidOperationException("no primary screen");

                Position = new PixelPoint(
                    workArea.Right - (int)Width - 20,
                    workArea.Bottom - (int)Height - 20);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to position Pink Rush popup");
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e) => UpdateCountdown();

        private void UpdateCountdown()
        {
            var remaining = _endTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                _countdownTimer.Stop();
                FadeOutAndClose();
                return;
            }

            _txtCountdown.Text = $"{(int)remaining.TotalSeconds}s remaining";
        }

        private void FadeOutAndClose()
        {
            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* already closing */ } },
                    TimeSpan.FromMilliseconds(FadeMs));
            }
            catch
            {
                try { Close(); } catch { /* already closing */ }
            }
        }

        private void BtnClose_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            FadeOutAndClose();
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _countdownTimer.Stop();
            FadeOutAndClose();
        }

        protected override void OnClosed(EventArgs e)
        {
            _countdownTimer.Stop();
            base.OnClosed(e);
        }
    }
}
