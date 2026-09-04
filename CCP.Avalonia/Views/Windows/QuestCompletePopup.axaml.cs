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
    /// Popup window shown when a quest is completed.
    ///
    /// PORTED from ConditioningControlPanel/Windows/QuestCompletePopup.xaml.cs. Deviations:
    ///  - <c>BeginAnimation(OpacityProperty, DoubleAnimation)</c> has no Avalonia equivalent: a
    ///    <see cref="DoubleTransition"/> on Opacity gives the same 300ms fade for a plain
    ///    assignment, and the close is deferred by one timer tick so the fade-out is seen.
    ///  - <c>SystemParameters.WorkArea</c> becomes <c>Screens.Primary.WorkingArea</c>, which is
    ///    only populated once the window has a platform handle, so placement moves to OnOpened.
    ///  - <c>MouseLeftButtonDown</c> / <c>Click</c> become PointerPressed / Click wired here.
    ///  - <c>App.Logger</c> is Serilog's static <c>Log</c>; the two error templates are unchanged.
    ///  - <see cref="ForceToTopMost"/>'s Win32 body is stubbed; see the note on it.
    /// </summary>
    public partial class QuestCompletePopup : Window
    {
        private const double FadeMs = 300;

        private readonly DispatcherTimer _autoCloseTimer;

        /// <summary>Render/design constructor: sample data so --render-view can draw the popup.</summary>
        internal QuestCompletePopup() : this("Wear the plug for one hour", 250)
        {
            // The fade-in cannot complete inside a headless render's two dispatcher passes, so the
            // PNG would capture a fully transparent window. Skip the animation for the render.
            Transitions = null;
            Opacity = 1;
        }

        public QuestCompletePopup(string questName, int xpAwarded)
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<TextBlock>("TxtQuestName")!.Text = questName;
            this.FindControl<TextBlock>("TxtXPAwarded")!.Text = $"+{xpAwarded} XP";

            // Auto-close after 5 seconds
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            _autoCloseTimer.Start();

            // Fade in
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(FadeMs) }
            };
            Opacity = 0;
            Loaded += (_, _) =>
            {
                // Re-assert to the top of the topmost band WITHOUT activating, so the popup is
                // visible over an in-app fullscreen video and playback keeps focus (#332).
                ForceToTopMost();
                Opacity = 1;
            };

            // Handlers live here rather than in markup, per the porting convention. After the
            // timer exists, because the close button stops it.
            this.FindControl<Button>("BtnClose")!.Click += (_, _) =>
            {
                _autoCloseTimer.Stop();
                FadeOutAndClose();
            };
            AddHandler(InputElement.PointerPressedEvent, Window_PointerPressed, handledEventsToo: false);
        }

        /// <summary>
        /// WPF re-asserted <c>SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)</c> here so the toast rose
        /// to the TOP of the topmost band without stealing focus, beating the app's own fullscreen
        /// video surface - which is also Topmost but was activated more recently (#332).
        ///
        /// ponytail: only the portable half survives. Re-assigning Topmost re-issues
        /// <c>_NET_WM_STATE_ABOVE</c>, which puts the window in the keep-above layer but does NOT
        /// reorder it against a sibling already in that layer, so an in-app fullscreen video can
        /// still cover this popup. <c>X11Overlay.RestackAbove</c> is the right primitive and is
        /// already on this head, but it needs the sibling TopLevel and this popup has no handle to
        /// the video window - so wiring it needs whoever owns that surface, in its own layer.
        /// </summary>
        private void ForceToTopMost()
        {
            try { Topmost = true; }
            catch (Exception ex) { Log.Error(ex, "QuestCompletePopup: ForceToTopMost failed"); }
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
                var screen = Screens.Primary
                    ?? throw new InvalidOperationException("no primary screen");

                // WorkingArea is physical pixels; Width/Height and the 20px margin are DIPs.
                var scale = screen.Scaling;
                var area = screen.WorkingArea;

                // Position in bottom-right corner with 20px margin
                Position = new PixelPoint(
                    area.Right - (int)((Width + 20) * scale),
                    area.Bottom - (int)((Height + 20) * scale));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to position quest complete popup");
                // Fallback: centre on screen, as the WPF original did.
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void FadeOutAndClose()
        {
            try
            {
                Opacity = 0;
                DispatcherTimer.RunOnce(() => { try { Close(); } catch { /* already closed */ } },
                    TimeSpan.FromMilliseconds(FadeMs));
            }
            catch
            {
                try { Close(); } catch { /* Ignore close errors */ }
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
