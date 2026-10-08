// PORTED from WPF LauncherWindow.xaml:597 (the veil), LauncherWindow.xaml.cs:70-74/658-669
// (OnLockdownChanged / RefreshLockdownVeil) and LauncherWindow.Choreo.cs:557-600 (the breath).
// WPF veils the launcher only; the panel stays usable under Lockdown (its own exits live there) and
// refuses per control (docs/avalonia-decisions.md, Lockdown veil). The veil takes the pointer by
// covering every row; this file also swallows keys, so Tab/Enter/Space cannot reach a veiled control
// (WPF leaves keyboard focus alone; here the veil is structural for both).
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        private const double VeilBreathSeconds = 3;   // WPF Choreo.cs:54

        /// <summary>WPF's ColorAnimation at AmbientFps; ticks only while the veil is on screen.</summary>
        private readonly DispatcherTimer _veilBreath = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
        private LockdownService? _veilSource;
        private GradientStop? _veilEdge;
        private double _veilT;

        internal bool VeilBreathing => _veilBreath.IsEnabled;

        private void HookVeil()
        {
            _veilBreath.Tick += (_, _) => StepVeilBreath(_veilBreath.Interval.TotalSeconds);
            AddHandler(KeyDownEvent, SwallowWhenVeiled, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyUpEvent, SwallowWhenVeiled, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(TextInputEvent, SwallowWhenVeiled, RoutingStrategies.Tunnel, handledEventsToo: true);
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) RefreshLockdownVeil();
            };
            Closed += (_, _) => { Bind(null); _veilBreath.Stop(); };
            RefreshLockdownVeil();
        }

        private void SwallowWhenVeiled(object? sender, RoutedEventArgs e)
        {
            if (LockdownVeil.IsVisible) e.Handled = true;
        }

        /// <summary>WPF subscribes in the ctor to App.Lockdown; here Current can be replaced (tests,
        /// App startup order), so the veil follows whichever service is current at each refresh.</summary>
        private void Bind(LockdownService? ld)
        {
            if (ReferenceEquals(_veilSource, ld)) return;
            if (_veilSource != null)
            {
                _veilSource.LockdownActivated -= OnLockdownChanged;
                _veilSource.LockdownDeactivated -= OnLockdownChanged;
            }
            _veilSource = ld;
            if (ld != null)
            {
                ld.LockdownActivated += OnLockdownChanged;
                ld.LockdownDeactivated += OnLockdownChanged;
            }
        }

        private void OnLockdownChanged()
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshLockdownVeil();
            else Dispatcher.UIThread.Post(RefreshLockdownVeil);
        }

        /// <summary>WPF RefreshLockdownVeil + RefreshVeilBreath (breathes only while shown, not
        /// minimised, and ambient loops are allowed).</summary>
        internal void RefreshLockdownVeil()
        {
            try
            {
                Bind(LockdownService.Current);
                var on = MainShellWindow.LockdownActive;
                var rising = on && !LockdownVeil.IsVisible;
                LockdownVeil.IsVisible = on;
                if (rising) LockdownVeil.Focus();   // nothing under the veil keeps focus
                if (on && IsVisible && WindowState != WindowState.Minimized && AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    _veilEdge ??= BuildVignette();
                    _veilBreath.Start();
                }
                else
                {
                    _veilBreath.Stop();
                    if (_veilEdge != null) _veilEdge.Color = Color.FromArgb(0x66, 0xAA, 0x00, 0x22);
                    LockdownText.Opacity = 1;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] veil refresh failed"); }
        }

        private GradientStop BuildVignette()
        {
            var edge = new GradientStop(Color.FromArgb(0x66, 0xAA, 0x00, 0x22), 1);
            LockdownVignette.Background = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Colors.Transparent, 0.45), edge },
                RadiusX = new global::Avalonia.RelativeScalar(0.8, global::Avalonia.RelativeUnit.Relative),
                RadiusY = new global::Avalonia.RelativeScalar(0.8, global::Avalonia.RelativeUnit.Relative),
            };
            return edge;
        }

        /// <summary>One frame of the breath: edge alpha 0x55..0xB3 and the text 0.7..1.0, sine in-out,
        /// auto-reversing every <see cref="VeilBreathSeconds"/> (WPF ColorAnimation + GlowBreath).</summary>
        internal void StepVeilBreath(double dt)
        {
            if (_veilEdge == null) return;
            _veilT = (_veilT + dt) % (2 * VeilBreathSeconds);
            var u = _veilT / VeilBreathSeconds;
            if (u > 1) u = 2 - u;
            var ease = (1 - Math.Cos(Math.PI * u)) / 2;
            _veilEdge.Color = Color.FromArgb((byte)(0x55 + (0xB3 - 0x55) * ease), 0xAA, 0x00, 0x22);
            LockdownText.Opacity = 0.7 + 0.3 * ease;
        }
    }
}
