// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileFx.cs (238 lines): the Profile
// tab's entrance stagger, the search box's focus glow and the OG card's spinning gold border.
//
// MotionFx is AmbientFxCanvas.Env here (AllowTransitions / AllowAmbientLoops / GlowColor). The OG
// border's 3 s storyboard spun the gradient's RelativeTransform; Avalonia brushes carry a Transform
// (origin 50%,50%), so the same RotateTransform turns 0 -> 360 forever - only while the gold frame
// shows, the Profile tab is visible, ambient loops are allowed and the window is active and not
// minimised (WPF ApplyOgBorderLoop's gate, P01). Every input to that gate re-runs it.
//
// ponytail: the vat poll half of OnProfileTabVisibilityChanged / OnProfileFxWindowStateish
// (EvaluateVatPoll, OnProfileVatVisibilityChanged) needs DescentService - row shell-profile-vat.

using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const int ProfileSearchGlowMs = 150;
        private const byte ProfileSearchGlowAlpha = 0xC0;
        private const int ProfileStaggerMs = 40, ProfileStaggerCap = 6;   // MotionFx.StaggerMs / StaggerCap

        private bool _profileFxInitialized;
        private CancellationTokenSource? _ogBorderLoop;
        private RotateTransform? _ogBorderRotation;

        /// <summary>True while the OG border spins (tests read it; the loop itself is an Animation).</summary>
        internal bool OgBorderLoopRunning => _ogBorderLoop != null;

        /// <summary>WPF InitializeProfileFx: search focus glow + the window-state hooks that re-gate the loop.</summary>
        private void InitializeProfileFx()
        {
            if (_profileFxInitialized) return;
            _profileFxInitialized = true;
            try
            {
                var page = ProfilePage;
                if (page?.FindControl<TextBox>("TxtProfileSearch") is { } search)
                {
                    search.GotFocus += (_, _) => ApplyProfileSearchGlow(true);
                    search.LostFocus += (_, _) => ApplyProfileSearchGlow(false);
                }
                if (page?.FindControl<Border>("ProfileSearchBox") is { } box)
                    box.BorderBrush = new SolidColorBrush(GlowAt(0));

                Activated += OnProfileFxWindowStateish;
                Deactivated += OnProfileFxWindowStateish;
                PropertyChanged += OnProfileFxWindowProperty;
                if (page != null) page.PropertyChanged += OnProfileFxVisibility;
                if (page?.FindControl<Border>("OgBorderContainer") is { } og) og.PropertyChanged += OnProfileFxVisibility;
                if (page?.FindControl<Grid>("ProfileCardWrapper") is { } wrapper) wrapper.PropertyChanged += OnProfileFxVisibility;
                AmbientFxCanvas.Env.MotionGateChanged += ApplyOgBorderLoop;
                Closed += (_, _) => { AmbientFxCanvas.Env.MotionGateChanged -= ApplyOgBorderLoop; StopOgBorderLoop(); };
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeProfileFx failed"); }
        }

        private void OnProfileFxWindowStateish(object? sender, EventArgs e) => ApplyOgBorderLoop();

        private void OnProfileFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) ApplyOgBorderLoop();
        }

        private void OnProfileFxVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty) ApplyOgBorderLoop();
        }

        /// <summary>WPF OnProfileTabVisibilityChanged's incoming-tab half (OnTabShown "discord").</summary>
        private void OnProfileTabShownFx()
        {
            ApplyOgBorderLoop();
            StaggerProfileCards();
        }

        /// <summary>WPF ApplyOgBorderLoop: start or stop the spin from the full gate; idempotent.</summary>
        internal void ApplyOgBorderLoop()
        {
            try
            {
                var page = ProfilePage;
                if (page?.FindControl<Border>("OgBorderContainer") is not { } container) return;
                bool wanted = container.IsEffectivelyVisible   // also off when only ProfileCardWrapper hides
                              && AmbientFxCanvas.Env.AllowAmbientLoops
                              && IsActive
                              && WindowState != WindowState.Minimized
                              && page.IsEffectivelyVisible;
                if (!wanted) { StopOgBorderLoop(); return; }
                if (_ogBorderLoop != null || container.Background is not Brush brush) return;

                _ogBorderRotation ??= new RotateTransform();
                brush.TransformOrigin = RelativePoint.Center;
                brush.Transform = _ogBorderRotation;
                _ogBorderLoop = new CancellationTokenSource();
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } },
                    },
                }.RunAsync(_ogBorderRotation, _ogBorderLoop.Token);
            }
            catch (Exception ex) { Log.Debug("ApplyOgBorderLoop: {E}", ex.Message); }
        }

        private void StopOgBorderLoop()
        {
            _ogBorderLoop?.Cancel();
            _ogBorderLoop?.Dispose();
            _ogBorderLoop = null;
        }

        /// <summary>WPF StaggerProfileCards -> MotionFx.StaggerIn: visible cards fade in from a 10 px rise,
        /// 40 ms apart, capped at 6 slots.</summary>
        private void StaggerProfileCards()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                if (ProfilePage?.FindControl<StackPanel>("ProfileColumnStack") is not { } stack) return;
                int i = 0;
                foreach (var card in stack.Children.Where(c => c.IsVisible))
                {
                    var delay = TimeSpan.FromMilliseconds(ProfileStaggerMs * Math.Min(i++, ProfileStaggerCap));
                    if (card.RenderTransform is not TranslateTransform)
                        card.RenderTransform = new TranslateTransform();
                    Entrance(card, delay, 220, OpacityProperty, 0.0, 1.0);
                    Entrance(card, delay, 260, TranslateTransform.YProperty, 10.0, 0.0);
                }
            }
            catch (Exception ex) { Log.Debug("StaggerProfileCards: {E}", ex.Message); }
        }

        private static void Entrance(Control target, TimeSpan delay, int ms, AvaloniaProperty property, double from, double to) =>
            _ = new Animation
            {
                Delay = delay,
                Duration = TimeSpan.FromMilliseconds(ms),
                Easing = new QuadraticEaseOut(),
                FillMode = FillMode.Backward,   // holds "from" through the delay, then hands back to the local value
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
                },
            }.RunAsync(target);

        private static Color GlowAt(byte alpha)
        {
            var tint = AmbientFxCanvas.Env.GlowColor;
            return Color.FromArgb(alpha, tint.R, tint.G, tint.B);
        }

        /// <summary>WPF ApplyProfileSearchGlow: the search border brightens to the glow tint on focus (150 ms).</summary>
        private void ApplyProfileSearchGlow(bool on)
        {
            try
            {
                if (ProfilePage?.FindControl<Border>("ProfileSearchBox") is not { } box) return;
                if (box.BorderBrush is not SolidColorBrush brush) box.BorderBrush = brush = new SolidColorBrush(GlowAt(0));
                var to = GlowAt(on ? ProfileSearchGlowAlpha : (byte)0);
                brush.Transitions = AmbientFxCanvas.Env.AllowTransitions
                    ? new Transitions { new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = TimeSpan.FromMilliseconds(ProfileSearchGlowMs), Easing = new QuadraticEaseOut() } }
                    : null;
                brush.Color = to;
            }
            catch (Exception ex) { Log.Debug("ApplyProfileSearchGlow: {E}", ex.Message); }
        }
    }
}
