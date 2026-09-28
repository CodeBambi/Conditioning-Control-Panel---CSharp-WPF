using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The window's pink edge and its corners (owner, 2026-09-27: "rounded corners, rn those are
/// squared", "make it pop and add sfx on it, and slightly thicker").
///
/// <para>Corners: the Windows 11 DWM rounds the frame (the same call MainWindow makes); Windows 10
/// ignores it and keeps square corners. The edge's CornerRadius in the XAML follows the DWM curve.</para>
///
/// <para>Pop: on open the edge flares (glow up, then back to rest) while a glint runs round it with
/// a small shimmer. Focus coming back runs the glint and the shimmer again, at most every few
/// seconds. At rest the glow breathes and the glint passes by itself now and then, silently.
/// Motion off keeps the edge still and only the sound plays.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;
    private const double GlowRest = 0.3, GlowFlare = 0.85;
    private static readonly TimeSpan IdleGlintEvery = TimeSpan.FromSeconds(14);
    private static readonly TimeSpan FocusCueGap = TimeSpan.FromSeconds(4);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private DispatcherTimer? _edgeIdle;
    private DateTime _lastEdgeCue = DateTime.MinValue;
    private bool _edgeHooked;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = DwmCornerRound;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] rounded corners unavailable"); }
        if (!_edgeHooked) { _edgeHooked = true; Activated += OnEdgeActivated; }
    }

    /// <summary>Called from OnShown, after the open sting.</summary>
    private void EdgeOnShown()
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastEdgeCue > TimeSpan.FromSeconds(1)) LauncherSfx.Edge();   // activation may have just played it
            _lastEdgeCue = now;
            if (!MotionFx.AllowTransitions) return;
            FlareEdge();
            RunGlint();
            StartEdgeBreath();
            _edgeIdle ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = IdleGlintEvery };
            _edgeIdle.Tick -= OnEdgeIdle;
            _edgeIdle.Tick += OnEdgeIdle;
            _edgeIdle.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] edge show failed"); }
    }

    private void EdgeOnHidden()
    {
        try
        {
            _edgeIdle?.Stop();
            EdgeGlow.BeginAnimation(OpacityProperty, null);
            EdgeGlint.BeginAnimation(OpacityProperty, null);
            EdgeGlintSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            EdgeGlow.Opacity = GlowRest;
            EdgeGlint.Opacity = 0;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] edge park failed"); }
    }

    private void EdgeOnClosed()
    {
        EdgeOnHidden();
        if (_edgeHooked) { Activated -= OnEdgeActivated; _edgeHooked = false; }
    }

    private void OnEdgeActivated(object? sender, EventArgs e)
    {
        if (!IsVisible || _firstShow) return;
        var now = DateTime.UtcNow;
        if (now - _lastEdgeCue < FocusCueGap) return;   // the open itself activates the window
        _lastEdgeCue = now;
        LauncherSfx.Edge();
        if (MotionFx.AllowTransitions) RunGlint();
    }

    private void OnEdgeIdle(object? sender, EventArgs e)
    {
        if (!IsVisible || !MotionFx.AllowTransitions) return;
        RunGlint();
    }

    /// <summary>The glow jumps up and settles back to its breath.</summary>
    private void FlareEdge()
    {
        var flare = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        flare.KeyFrames.Add(new EasingDoubleKeyFrame(GlowFlare, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        flare.KeyFrames.Add(new EasingDoubleKeyFrame(GlowRest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        flare.Completed += (_, _) => StartEdgeBreath();
        EdgeGlow.BeginAnimation(OpacityProperty, flare);
    }

    private void StartEdgeBreath()
    {
        if (!IsVisible || !MotionFx.AllowTransitions) return;
        var breath = new DoubleAnimation(GlowRest, GlowRest + 0.14, TimeSpan.FromSeconds(3.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        EdgeGlow.BeginAnimation(OpacityProperty, breath);
    }

    /// <summary>A white glint runs once round the edge: the band turns a full circle while it
    /// fades in and out, so it enters and leaves soft.</summary>
    private void RunGlint()
    {
        var turn = new DoubleAnimation(-90, 270, TimeSpan.FromMilliseconds(1500))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var shine = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1500) };
        shine.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        shine.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1150))));
        shine.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        EdgeGlintSpin.BeginAnimation(RotateTransform.AngleProperty, turn);
        EdgeGlint.BeginAnimation(OpacityProperty, shine);
    }
}
