using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Animation.RunAsync on a Transform object throws (TransformAnimator casts to Visual);
/// hovering the header wallet killed the app with it (2026-10-09). TransformTween is the road.</summary>
public sealed class TransformTweenTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task AnimationRunAsyncOnATransformStillThrows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var anim = new Animation { Duration = TimeSpan.FromMilliseconds(100) };
        anim.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 10.0) } });
        // If Avalonia ever learns this, TransformTween can retire.
        Assert.ThrowsAny<InvalidCastException>(() => { _ = anim.RunAsync(new RotateTransform()); });
        return Task.CompletedTask;
    });

    [Fact]
    public Task TweenWritesTheTransformAndEndsOnTheLastKey() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var turn = new RotateTransform();
        var timer = TransformTween.Run(turn, TimeSpan.FromMilliseconds(120),
            new (double, AvaloniaProperty, double)[] { (0, RotateTransform.AngleProperty, 0.0), (0.5, RotateTransform.AngleProperty, -17.0), (1, RotateTransform.AngleProperty, 5.0) });
        var until = DateTime.UtcNow.AddSeconds(3);
        while (timer.IsEnabled && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(timer.IsEnabled);
        Assert.Equal(5.0, turn.Angle, 3);
    });
}
