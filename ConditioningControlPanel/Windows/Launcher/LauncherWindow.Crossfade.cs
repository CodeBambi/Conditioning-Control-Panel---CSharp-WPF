using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The crossfade with the panel. Window.Opacity is dead with AllowsTransparency off, so both
/// fades run on RootGrid: up from nothing whenever the window comes on screen (first boot, back
/// from the panel, back from a game), down to nothing before the host hides it for the panel or a
/// game. The host asks for the fade-out through <see cref="LauncherHost.FadeOut"/> and hides at
/// once when this window declines (reduced motion, not on screen).
///
/// <para>The one rule: RootGrid is never left under 1 while the window is visible. Every fade
/// restores it in Completed and again from a guard timer, and OnHidden restores it on the way
/// out, so a Show() that skips the fade always shows a whole window.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int FadeInMs = 260;
    private const int FadeGuardMs = 150;

    private DispatcherTimer? _fadeGuard;
    private Action? _fadeOutThen;

    /// <summary>Called from OnShown, before the tiles stagger in over it.</summary>
    private void FadeIn()
    {
        _fadeOutThen = null;
        if (!MotionFx.AllowTransitions) { RestoreRootOpacity(); return; }
        RootGrid.Opacity = 0;
        RunFade(0, 1, FadeInMs, null);
    }

    /// <summary>
    /// The host's fade-out hook. False when there is nothing to animate; the host then hides at
    /// once. A second request while a fade is in flight joins the first one's continuation.
    /// </summary>
    private bool FadeOutThen(Action then)
    {
        if (!MotionFx.AllowTransitions || !IsVisible) return false;
        if (_fadeOutThen != null) { _fadeOutThen += then; return true; }
        _fadeOutThen = then;
        RunFade(RootGrid.Opacity, 0, LauncherHost.FadeOutMs, () =>
        {
            var pending = _fadeOutThen;
            _fadeOutThen = null;
            pending?.Invoke();
        });
        return true;
    }

    private void RunFade(double from, double to, int ms, Action? then)
    {
        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            _fadeGuard?.Stop();
            try { then?.Invoke(); }
            catch (Exception ex) { Log.Error(ex, "[Launcher] step after fade failed"); }
            finally { RestoreRootOpacity(); }
        }

        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = to > from ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        anim.Completed += (_, _) => Finish();

        _fadeGuard?.Stop();
        _fadeGuard = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms + FadeGuardMs) };
        _fadeGuard.Tick += (_, _) => Finish();
        _fadeGuard.Start();

        RootGrid.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Drops any fade clock and parks RootGrid at 1. Safe while hidden.</summary>
    private void RestoreRootOpacity()
    {
        try
        {
            RootGrid.BeginAnimation(UIElement.OpacityProperty, null);
            RootGrid.Opacity = 1;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] RootGrid opacity restore failed"); }
    }
}
