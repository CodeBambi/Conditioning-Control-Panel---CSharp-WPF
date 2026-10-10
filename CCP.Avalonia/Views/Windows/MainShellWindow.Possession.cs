// PORTED (head half) from ConditioningControlPanel/MainWindow/MainWindow.Possession.cs,
// Services/Possession/EmberAttribution.cs (EdgePulse) and Effects/BreatheEffect.cs + NudgeEffect.cs.
// The director and every rule are Core (Services/Possession/PossessionDirector.cs, PossessionDeck.cs);
// this is the room it haunts: the shell's edge pulse, the short list of possessable controls and the
// two micro-tics this head can draw.
//
// Reach = WPF 7.1.5 (owner, 10 Oct 2026; k22) inside the owner's hard limits: the rail doors, Start, the
// lockdown card, the Lockdown toggles and the timer are enrolled, and no SAFETY control ever is. Views mark victims with poss:Possession.Role and the shell
// walks its visual tree (Services/Possession/Possession.cs: PossessionTree holds the refusals). The
// deck here: nudge, typo, breathe, drift, rewrite, melt, glyphrot, crack, retitle (Services/Possession/*.cs).

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
                PossessionDirector.Current.Scenes.AddRange(PossessionHeadScenes());
                // A Start control is a STOP control while the engine or a session runs.
                PossessionTree.SomethingRunning = () => CoreEngine.IsRunning || App.Sessions?.IsRunning == true;
                InstallPossessionRemember(lockdown);
                // WPF PossessionAudio.Install: the two synthesised tics (tones, never speech).
                PossessionAudio.Install(lockdown, () => PossessionDirector.Current);
                lockdown.LockdownActivated += () => Dispatcher.UIThread.Post(() => PostPossessionRulesIfFirstTime(() => Current));
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: install failed"); }
        }

        /// <summary>The deck this head can draw, in the WPF catalog's rung order (k18 added typo,
        /// drift, rewrite, melt, glyphrot, crack, retitle from Services/Possession/).</summary>
        internal static IPossessionEffect[] PossessionHeadEffects() => new IPossessionEffect[]
        {
            new PossessionNudge(), new Services.Possession.Effects.TypoEffect(), new PossessionBreathe(),
            new Services.Possession.Effects.DriftEffect(), new Services.Possession.Effects.RewriteEffect(),
            new Services.Possession.Effects.ToastEffect(),
            new Services.Possession.Effects.MeltEffect(), new Services.Possession.Effects.GlyphRotEffect(),
            new Services.Possession.Effects.XpDrainEffect(),
            new Services.Possession.Effects.CrackEffect(),
            new Services.Possession.Effects.RetitleEffect(),
            new Services.Possession.Effects.GlitchPortraitEffect(),
            // k22, reach = WPF: the effects written for the roles the user acts on.
            new Services.Possession.Effects.DodgeEffect(), new Services.Possession.Effects.WobbleEffect(),
            new Services.Possession.Effects.RelabelEffect(), new Services.Possession.Effects.ToggleLieEffect(), new Services.Possession.Effects.ReorderDoorsEffect(),
        };

        internal const string PossessionRulesKey = "intro:possession";

        /// <summary>WPF ShowPossessionRulesIfFirstTime (MainWindow.Lab.cs): the first lockdown that runs
        /// with Possession on, the warden states the rules: a bark now, and the rules card. WPF opens
        /// the card at once when the app is quiet; the card is a modal over the shell, which would sit
        /// on the Emergency Exit as the lockdown starts, so this head always files it as an Inbox row
        /// (the title and summary are WPF's own literals). The flag is spent when the card OPENS.</summary>
        internal static void PostPossessionRulesIfFirstTime(Func<MainShellWindow?> shell)
        {
            try
            {
                var s = CoreSettings.Current;
                if (s == null || !s.LockdownPossessionEnabled || s.LockdownPossessionIntroSeen) return;
                Platform.StartupLadder.Inbox.File(new Services.Startup.InboxItem
                {
                    Key = PossessionRulesKey,
                    Glyph = "\U0001F576",
                    Title = "The warden's rules",
                    Summary = "What possession does before the room starts moving.",
                    Open = () =>
                    {
                        var live = CoreSettings.Current;
                        if (live != null && !live.LockdownPossessionIntroSeen)
                        {
                            live.LockdownPossessionIntroSeen = true;
                            CoreSettings.Save();
                        }
                        FeatureIntroPopup.ShowIfFirstTime("possession", shell());
                    },
                });
                CoreBark.Raise(PossessionBarkTriggers.Rules, null);
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: failed to post the first-run rules"); }
        }

        /// <summary>WPF PossessionRemember.Install: arm on a Full Doki exit, and spend a charge armed by
        /// the last run once this launch's window has been up for twenty seconds.</summary>
        private static DispatcherTimer? _possessionRememberTimer;

        private static void InstallPossessionRemember(LockdownService lockdown)
        {
            try
            {
                _possessionRememberTimer?.Stop();
                PossessionRemember.Current?.Dispose();
                var remember = PossessionRemember.Current = new PossessionRemember(lockdown, PossessionHostFor(() => Current));
                remember.SchedulePendingCharge(DateTime.UtcNow);
                if (!remember.IsWaiting) return;
                _possessionRememberTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (t, _) =>
                {
                    remember.Tick(DateTime.UtcNow);
                    if (!remember.IsWaiting) (t as DispatcherTimer)?.Stop();
                });
                _possessionRememberTimer.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "Possession: remember install failed"); }
        }

        /// <summary>WPF PossessionSceneCatalog (the rail sweep joined with the doors, k22).</summary>
        internal static IPossessionScene[] PossessionHeadScenes() => new IPossessionScene[]
        {
            new Services.Possession.Scenes.TheCountScene(), new Services.Possession.Scenes.WhereYouAreScene(),
            new Services.Possession.Scenes.RailSweepScene(),
        };

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
            try { PossessionAudio.StopForPanic(); } catch (Exception ex) { Log.Debug("Possession: audio stop failed: {E}", ex.Message); }
            try { (shell ?? Current)?.PaintPossessionPulse(double.MaxValue); } catch (Exception ex) { Log.Debug("Possession: pulse drop failed: {E}", ex.Message); }
            try { Deeper.ScreenShake.Stop(); } catch (Exception ex) { Log.Debug("Possession: shake drop failed: {E}", ex.Message); }
        }

        // ---- the registry ------------------------------------------------------------------------

        private readonly Dictionary<string, PossessionTarget> _possessionTargets = new(StringComparer.Ordinal);

        /// <summary>The possessable controls that exist right now: every control in this window's
        /// visual tree that a view tagged with poss:Possession.Role (WPF EnumerateTagged), minus all
        /// that PossessionTree refuses (anything pressed, typed in, excluded or reserved by name).
        /// Targets are cached by key so a cooldown or a live booking survives the next read.</summary>
        internal IReadOnlyList<PossessionTarget> PossessionTargets()
        {
            HookPossessionPress();
            return PossessionTree.Collect(this, _possessionTargets);
        }

        // ---- the reactive layer (WPF PossessionEvents, the card half) -----------------------------
        // A press on a haunted room's card makes that card breathe. The press is only WATCHED: it is
        // never handled, so whatever was pressed still gets it. The pointer reaching Start asks for a
        // dodge (a START only: a Start that reads Stop is a safety control). Not here, on purpose: a
        // pressed rail door dropping its letters (drop is not ported), and the typo that answers a
        // changed setting (a setting can be changed by a remote controller, and no remote path may
        // feed the haunt).
        private bool _possessionPressHooked;

        private void HookPossessionPress()
        {
            if (_possessionPressHooked) return;
            _possessionPressHooked = true;
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                try { PossessionReactToPress(e.Source as Visual); }
                catch (Exception ex) { Log.Debug("Possession: reactive press failed: {E}", ex.Message); }
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            // WPF PossessionEvents.OnHoverChanged: the pointer reaching Start asks for a dodge. The
            // effect itself refuses a Start that is a Stop right now (DodgeEffect, PossessionTree.IsSafety).
            if (Named<Button>("BtnStart") is { } start)
                start.PointerEntered += (_, _) =>
                {
                    try { PossessionReactToStartHover(start); }
                    catch (Exception ex) { Log.Debug("Possession: reactive hover failed: {E}", ex.Message); }
                };
        }

        /// <summary>The pointer is on Start: while the local lockdown haunts (Drift or deeper) and
        /// nothing is running, it may slip away. Never a stop, an exit or a cancel.</summary>
        internal void PossessionReactToStartHover(Control start)
        {
            if (PossessionDirector.Current is not { IsHaunting: true } director) return;
            if (PossessionTree.IsSafety(start)) return;
            foreach (var t in PossessionTargets())
            {
                if (!ReferenceEquals(t.Element, start)) continue;
                director.RequestReactive("dodge", t, PossessionRung.Drift);
                return;
            }
        }

        /// <summary>The card (if any) the pressed visual sits in asks the director for a breath.</summary>
        internal void PossessionReactToPress(Visual? source)
        {
            if (PossessionDirector.Current is not { IsHaunting: true } director) return;
            for (var v = source; v != null; v = global::Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
            {
                if (v is not Control c || Services.Possession.Possession.GetRole(c) != PossessionRole.Card) continue;
                foreach (var t in PossessionTargets())
                {
                    if (!ReferenceEquals(t.Element, c)) continue;
                    director.RequestReactive("breathe", t);
                    return;
                }
                return;
            }
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

        /// <summary>True for a tic that leaves bounds and input alone on a card that holds controls.</summary>
        protected virtual bool GlowsGuarded => false;
        private bool _guarded;
        private IDisposable? _guard;

        private bool Permits(Control c, PossessionRole role)
        {
            // The control's own tag is the louder statement (a registry entry can be older than it).
            if (Services.Possession.Possession.GetRole(c) is var own && own != PossessionRole.None) role = own;
            if (PossessionTree.IsGuardedContainer(c)) return GlowsGuarded && !PossessionTree.IsSafety(c);
            if (PossessionOffLimits.IsReservedName(c.Name)) return false;
            return PossessionTree.MayTouch(c, role, takesInteractive: true);
        }

        public bool CanApply(PossessionContext ctx, PossessionTarget? target) =>
            !IsLive && ctx != null && target?.Element is Control c && c.IsEffectivelyVisible && Permits(c, target.Role);

        protected virtual bool StartGuarded(Control c, PossessionContext ctx) => false;
        protected virtual void StopGuarded() { }

        public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            if (IsLive || target?.Element is not Control c || !Permits(c, target.Role)) return Task.CompletedTask;
            if (PossessionTree.IsGuardedContainer(c))
            {
                // A card that holds controls: glow only. No transform is ever set on it.
                if (!StartGuarded(c, ctx)) return Task.CompletedTask;
                _victim = c;
                _guarded = true;
                IsLive = true;
                return Task.CompletedTask;
            }
            if (PossessionTree.IsInteractiveRole(target.Role))
                _guard = PossessionGuard.Watch(c, () => _ = UndoAsync(TimeSpan.Zero));
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
            try { _guard?.Dispose(); } catch { }
            _guard = null;
            if (_guarded)
            {
                _guarded = false;
                try { StopGuarded(); } catch (Exception ex) { Log.Debug("Possession {Id}: glow drop failed: {E}", Id, ex.Message); }
                _victim = null;
                IsLive = false;
                return;
            }
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

        // The lockdown card breathes scale-free: an ember glow (a BoxShadow laid over the card's own
        // at animation priority) swells and fades on the same 3.2 s breath. The card's transform,
        // size, opacity and hit testing are never touched, so the Emergency Exit inside it keeps its
        // hit target at rest position and size on every frame. A slow sine, never a blink.
        internal const double GlowPeriodMs = 3200;
        protected override bool GlowsGuarded => true;
        private Border? _glowCard;
        private IDisposable? _glow;
        private DispatcherTimer? _glowTimer;
        private double _glowPeak;
        internal bool IsGlowing => _glowCard != null;

        protected override bool StartGuarded(Control c, PossessionContext ctx)
        {
            if (c is not Border card) return false;
            _glowCard = card;
            _glowPeak = ctx.Photosafe ? 0.22 : 0.45;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            PaintGlow(0);
            _glowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
                (_, _) => PaintGlow(clock.Elapsed.TotalMilliseconds));
            _glowTimer.Start();
            return true;
        }

        /// <summary>One frame of the glow at <paramref name="ms"/> into the breath.</summary>
        internal void PaintGlow(double ms)
        {
            if (_glowCard is not { } card) return;
            double w = (1 - Math.Cos(ms / GlowPeriodMs * 2 * Math.PI)) / 2;
            byte a = (byte)Math.Round(255 * Math.Clamp(_glowPeak * w, 0, 1));
            var shadow = new BoxShadows(new BoxShadow { Blur = 26, Spread = 1, Color = Color.FromArgb(a, 0xFF, 0x8A, 0x5C) });
            var next = card.SetValue(Border.BoxShadowProperty, shadow, global::Avalonia.Data.BindingPriority.Animation);
            try { _glow?.Dispose(); } catch { }
            _glow = next;
        }

        protected override void StopGuarded()
        {
            _glowTimer?.Stop();
            _glowTimer = null;
            try { _glow?.Dispose(); } catch { }
            _glow = null;
            _glowCard = null;
        }

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
