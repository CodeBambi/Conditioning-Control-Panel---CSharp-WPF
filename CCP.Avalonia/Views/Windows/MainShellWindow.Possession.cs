// PORTED (head half) from ConditioningControlPanel/MainWindow/MainWindow.Possession.cs,
// Services/Possession/EmberAttribution.cs (EdgePulse) and Effects/BreatheEffect.cs + NudgeEffect.cs.
// The director and every rule are Core (Services/Possession/PossessionDirector.cs, PossessionDeck.cs);
// this is the room it haunts: the shell's edge pulse, the short list of possessable controls and the
// two micro-tics this head can draw.
//
// Narrower than WPF on purpose (HB13): the registry is an explicit list of DISPLAY-ONLY controls, so no
// button, toggle, timer or exit ever moves under the pointer (PossessionOffLimits.IsReservedName is
// applied on top). WPF walks the whole visual tree for poss:Possession.Role; that walk (673 lines), the
// other 30 effects, the three scenes, the warden glide, the ember charge / outline / cursor ring, the
// possession audio tics and PossessionRemember are not on this head.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Ember #FF8A5C is reserved for Possession (WPF EmberAttribution).</summary>
        internal static readonly Color PossessionEmber = Color.Parse("#FF8A5C");
        internal static readonly Color PossessionEmberDim = Color.Parse("#33FF8A5C");

        /// <summary>Startup, once, right after the Dose keeper (WPF App.xaml.cs: App.Possession).</summary>
        internal static void InstallPossession(LockdownService lockdown)
        {
            try
            {
                PossessionDirector.Current?.Dispose();
                PossessionDirector.Current = new PossessionDirector(lockdown, PossessionHeadEffects(), PossessionHostFor(() => Current));
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: install failed"); }
        }

        internal static IPossessionEffect[] PossessionHeadEffects() => new IPossessionEffect[] { new PossessionNudge(), new PossessionBreathe() };

        /// <summary>The director's host over a shell (the live one at run time, a test's own in tests).</summary>
        internal static PossessionHost PossessionHostFor(Func<MainShellWindow?> shell) => new()
        {
            Targets = () => shell()?.PossessionTargets() ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>(),
            // WPF IPossessionHost.IsUsable: never START a ghost on a window nobody can see, or while a
            // game surface owns the screen.
            IsUsable = () => shell() is { IsVisible: true } sh && sh.WindowState != WindowState.Minimized && !PanicSurfaces.AnyOwnsTheScreen(),
            EdgePulse = strength => shell()?.PlayPossessionPulse(strength),
            Shake = (amp, ms) => Deeper.ScreenShake.Shake(amp, ms),
            OnUi = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); },
        };

        /// <summary>Panic: every ghost back at once, the pulse and the shake gone (PanicSurfaces "possession").</summary>
        internal static void StopPossessionForPanic(MainShellWindow? shell)
        {
            try { PossessionDirector.Current?.PanicStop(); } catch (Exception ex) { Log.Warning(ex, "Possession: panic stop failed"); }
            try { (shell ?? Current)?.PaintPossessionPulse(double.MaxValue); } catch (Exception ex) { Log.Debug("Possession: pulse drop failed: {E}", ex.Message); }
            try { Deeper.ScreenShake.Stop(); } catch (Exception ex) { Log.Debug("Possession: shake drop failed: {E}", ex.Message); }
        }

        // ---- the registry ------------------------------------------------------------------------

        private readonly Dictionary<string, PossessionTarget> _possessionTargets = new(StringComparer.Ordinal);

        /// <summary>(name, tab that owns it or null for the shell, role, display name for the warden).</summary>
        private static readonly (string Name, string? Tab, PossessionRole Role, string Display)[] PossessionRegistry =
        {
            ("TxtPossessionRung", "LockdownTab", PossessionRole.Label, "the readout"),
            ("PossessionPips", "LockdownTab", PossessionRole.Card, "the pips"),
            ("TxtTitleBarVersion", null, PossessionRole.Label, "the title"),
            ("TxtPlayerTitle", null, PossessionRole.Label, "your title"),
        };

        /// <summary>The possessable controls that exist right now. Targets are cached by key so a
        /// cooldown or a live booking survives the next read.</summary>
        internal IReadOnlyList<PossessionTarget> PossessionTargets()
        {
            var list = new List<PossessionTarget>();
            foreach (var (name, tab, role, display) in PossessionRegistry)
            {
                if (PossessionOffLimits.IsReservedName(name)) continue;   // a room the user must be able to leave
                if (!_possessionTargets.TryGetValue(name, out var t))
                {
                    Control? c = tab == null ? Named<Control>(name) : Named<Control>(tab)?.FindControl<Control>(name);
                    if (c == null) continue;
                    t = new PossessionTarget
                    {
                        Element = c, Role = role, Key = name, DisplayName = display,
                        IsVisible = () => c.IsEffectivelyVisible && c.Bounds.Width > 0,
                    };
                    _possessionTargets[name] = t;
                }
                list.Add(t);
            }
            return list;
        }

        // ---- the edge pulse (WPF EmberAttribution.EdgePulse) --------------------------------------
        // An ember frame around the whole window: in over 120 ms, out over the rest of 700 ms. Photosafe
        // never blinks: it rises over 300 ms to half the strength and takes a full second to leave.
        private Border? _possessionPulse;
        private DispatcherTimer? _possessionPulseTimer;
        private double _possessionPulsePeak, _possessionPulseIn, _possessionPulseTotal;
        internal Border? PossessionPulse => _possessionPulse;

        internal void PlayPossessionPulse(double strength)
        {
            try
            {
                if (Named<Grid>("RootGrid") is not { } root) return;
                bool photosafe = CoreSettings.Current?.LockdownPhotosafe == true;
                PaintPossessionPulse(double.MaxValue);   // a second pulse restarts it
                _possessionPulsePeak = Math.Clamp(strength, 0, 1) * (photosafe ? 0.5 : 1.0);
                if (_possessionPulsePeak <= 0) return;
                _possessionPulseIn = photosafe ? 300 : 120;
                _possessionPulseTotal = photosafe ? 1000 : 700;
                _possessionPulse = new Border
                {
                    BorderBrush = new SolidColorBrush(PossessionEmber),
                    BorderThickness = new Thickness(6),
                    BoxShadow = BoxShadows.Parse("inset 0 0 36 0 #88FF8A5C"),
                    IsHitTestVisible = false,
                    Opacity = 0,
                    ZIndex = int.MaxValue,
                };
                if (root.RowDefinitions.Count > 1) Grid.SetRowSpan(_possessionPulse, root.RowDefinitions.Count);
                if (root.ColumnDefinitions.Count > 1) Grid.SetColumnSpan(_possessionPulse, root.ColumnDefinitions.Count);
                root.Children.Add(_possessionPulse);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _possessionPulseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
                    (_, _) => PaintPossessionPulse(clock.Elapsed.TotalMilliseconds));
                _possessionPulseTimer.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: edge pulse failed"); }
        }

        /// <summary>One frame of the pulse at <paramref name="ms"/> in; past its length it is removed.</summary>
        internal void PaintPossessionPulse(double ms)
        {
            if (_possessionPulse is not { } pulse) return;
            if (ms < _possessionPulseTotal)
            {
                double o = ms < _possessionPulseIn
                    ? _possessionPulsePeak * (ms / _possessionPulseIn)
                    : _possessionPulsePeak * (1 - (ms - _possessionPulseIn) / (_possessionPulseTotal - _possessionPulseIn));
                pulse.Opacity = Math.Clamp(o, 0, 1);
                return;
            }
            _possessionPulseTimer?.Stop();
            _possessionPulseTimer = null;
            (pulse.Parent as Panel)?.Children.Remove(pulse);
            _possessionPulse = null;
        }
    }

    /// <summary>The shared half of this head's two micro-tics: borrow the victim's RenderTransform, give
    /// it back exactly. Undo is safe before Apply, twice, and with zero duration (panic: now).</summary>
    internal abstract class PossessionHeadEffect : IPossessionEffect
    {
        private Control? _victim;
        private ITransform? _priorTransform;
        private RelativePoint _priorOrigin;
        private CancellationTokenSource? _tween;
        protected Transform? Lease { get; private set; }

        public abstract string Id { get; }
        public PossessionRung MinRung => PossessionRung.Settle;
        public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public bool IsBig => false;          // micro-tics stay silent
        public bool UsesFlicker => false;
        public abstract double Weight { get; }
        public abstract TimeSpan HoldFor { get; }
        public abstract IReadOnlyList<PossessionRole> Roles { get; }
        public bool IsLive { get; private set; }

        public bool CanApply(PossessionContext ctx, PossessionTarget? target) =>
            !IsLive && ctx != null && target?.Element is Control c && c.IsEffectivelyVisible
            && !PossessionOffLimits.IsReservedName(c.Name);

        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            if (target?.Element is not Control c) return Task.CompletedTask;
            _victim = c;
            _priorTransform = c.RenderTransform;
            _priorOrigin = c.RenderTransformOrigin;
            _tween = new CancellationTokenSource();
            Lease = NewTransform();
            c.RenderTransformOrigin = RelativePoint.Center;
            c.RenderTransform = Lease;
            IsLive = true;
            // Photosafe halves every motion amplitude (WPF PossessionEffectBase.Amp).
            Start(Lease, ctx.Photosafe ? 0.5 : 1.0, ctx.Rng, _tween.Token);
            return Task.CompletedTask;
        }

        public async Task UndoAsync(TimeSpan duration)
        {
            var c = _victim;
            var lease = Lease;
            if (c == null || lease == null) { IsLive = false; return; }
            try { _tween?.Cancel(); } catch { }
            // WPF UndoMs: zero is the synchronous path, no animation at all.
            double ms = duration <= TimeSpan.Zero ? 0 : Math.Clamp(duration.TotalMilliseconds, 220, 700);
            if (ms > 0)
            {
                try
                {
                    Settle(lease, TimeSpan.FromMilliseconds(ms));
                    await Task.Delay((int)ms + 20).ConfigureAwait(true);
                }
                catch (Exception ex) { Log.Debug("Possession {Id}: undo ease failed: {E}", Id, ex.Message); }
            }
            // A newer haunt (or anyone else) may own the transform by now: only give back what is ours.
            if (ReferenceEquals(c.RenderTransform, lease))
            {
                c.RenderTransform = _priorTransform;
                c.RenderTransformOrigin = _priorOrigin;
            }
            if (ReferenceEquals(Lease, lease)) { Lease = null; _victim = null; IsLive = false; }
        }

        protected abstract Transform NewTransform();
        protected abstract void Start(Transform lease, double amp, Random rng, CancellationToken ct);
        protected abstract void Settle(Transform lease, TimeSpan over);
    }

    /// <summary>WPF BreatheEffect: a 3% swell in and out from the centre for the length of the hold.</summary>
    internal sealed class PossessionBreathe : PossessionHeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Card, PossessionRole.Button };
        public override string Id => "breathe";
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override Transform NewTransform() => new ScaleTransform(1, 1);

        protected override void Start(Transform lease, double amp, Random rng, CancellationToken ct)
        {
            double peak = 1.0 + 0.03 * amp;
            // Bounded by HoldFor and the token: a tween loop on a DispatcherTimer, not an Animation.
            TransformTween.Run(lease, TimeSpan.FromMilliseconds(3200), new (double, AvaloniaProperty, double)[]
            {
                (0, ScaleTransform.ScaleXProperty, 1.0), (0.5, ScaleTransform.ScaleXProperty, peak), (1, ScaleTransform.ScaleXProperty, 1.0),
                (0, ScaleTransform.ScaleYProperty, 1.0), (0.5, ScaleTransform.ScaleYProperty, peak), (1, ScaleTransform.ScaleYProperty, 1.0),
            }, loop: true, token: ct);
        }

        protected override void Settle(Transform lease, TimeSpan over)
        {
            var s = (ScaleTransform)lease;
            TransformTween.Run(s, over, new (double, AvaloniaProperty, double)[]
            {
                (0, ScaleTransform.ScaleXProperty, s.ScaleX), (1, ScaleTransform.ScaleXProperty, 1.0),
                (0, ScaleTransform.ScaleYProperty, s.ScaleY), (1, ScaleTransform.ScaleYProperty, 1.0),
            });
        }
    }

    /// <summary>WPF NudgeEffect: a few pixels, overshoot then settle, so it reads as SHOVED.</summary>
    internal sealed class PossessionNudge : PossessionHeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Button, PossessionRole.Toggle };
        public override string Id => "nudge";
        public override double Weight => 3;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(2);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override Transform NewTransform() => new TranslateTransform(0, 0);

        protected override void Start(Transform lease, double amp, Random rng, CancellationToken ct)
        {
            double dx = amp * (3.0 + rng.NextDouble() * 1.5) * (rng.Next(2) == 0 ? -1 : 1);
            double dy = amp * (2.0 + rng.NextDouble() * 1.5) * (rng.Next(2) == 0 ? -1 : 1);
            TransformTween.Run(lease, TimeSpan.FromMilliseconds(320), new (double, AvaloniaProperty, double)[]
            {
                (0, TranslateTransform.XProperty, 0), (0.4, TranslateTransform.XProperty, dx * 1.35), (1, TranslateTransform.XProperty, dx),
                (0, TranslateTransform.YProperty, 0), (0.4, TranslateTransform.YProperty, dy * 1.35), (1, TranslateTransform.YProperty, dy),
            }, token: ct);
        }

        protected override void Settle(Transform lease, TimeSpan over)
        {
            var t = (TranslateTransform)lease;
            TransformTween.Run(t, over, new (double, AvaloniaProperty, double)[]
            {
                (0, TranslateTransform.XProperty, t.X), (1, TranslateTransform.XProperty, 0),
                (0, TranslateTransform.YProperty, t.Y), (1, TranslateTransform.YProperty, 0),
            });
        }
    }
}
