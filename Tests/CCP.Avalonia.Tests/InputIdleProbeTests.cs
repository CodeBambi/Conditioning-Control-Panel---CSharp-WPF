using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner, 10 Oct 2026: an X11 input-idle probe, so Natasha's red flash can be dealt on Linux.
/// Everything here runs on the seam with a fake: no test loads libXss or opens a display. Unknown
/// (-1) keeps the old behaviour: never dealt.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class InputIdleProbeTests
{
    private static void WithSeams(Action body)
    {
        var (x11, linux, seeded) = (InputIdleProbe.X11IdleMilliseconds, InputIdleProbe.IsLinux, ActivityIdle.IdleSecondsProvider);
        try { body(); }
        finally
        {
            InputIdleProbe.X11IdleMilliseconds = x11;
            InputIdleProbe.IsLinux = linux;
            ActivityIdle.IdleSecondsProvider = seeded;
        }
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(999L, 0)]
    [InlineData(4200L, 4)]
    [InlineData(-1L, -1)]                         // no libXss, no display, Wayland without XWayland
    [InlineData(-500L, -1)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public void MillisecondsBecomeWholeSeconds_AndUnreadableIsUnknown(long ms, int want) =>
        Assert.Equal(want, InputIdleProbe.SecondsFrom(ms));

    [Fact]
    public void OnLinuxTheX11CounterAnswers_AndAFailedReadIsUnknown() => WithSeams(() =>
    {
        ActivityIdle.IdleSecondsProvider = null;      // nothing seeded: not Windows
        InputIdleProbe.IsLinux = () => true;
        InputIdleProbe.X11IdleMilliseconds = () => 7300;
        Assert.Equal(7, InputIdleProbe.DeskIdleSeconds());

        InputIdleProbe.X11IdleMilliseconds = () => -1;
        Assert.Equal(-1, InputIdleProbe.DeskIdleSeconds());

        InputIdleProbe.X11IdleMilliseconds = () => throw new DllNotFoundException("libXss.so.1");
        Assert.Equal(-1, InputIdleProbe.X11Seconds());
        Assert.Equal(-1, InputIdleProbe.DeskIdleSeconds());
    });

    [Fact]
    public void ASeededProviderWins_AndADeskNobodyCanReadIsUnknown() => WithSeams(() =>
    {
        int asked = 0;
        InputIdleProbe.X11IdleMilliseconds = () => { asked++; return 1000; };
        InputIdleProbe.IsLinux = () => true;
        ActivityIdle.IdleSecondsProvider = () => 42;  // Windows: GetLastInputInfo
        Assert.Equal(42, InputIdleProbe.DeskIdleSeconds());
        Assert.Equal(0, asked);

        ActivityIdle.IdleSecondsProvider = () => throw new InvalidOperationException();
        Assert.Equal(-1, InputIdleProbe.DeskIdleSeconds());
        ActivityIdle.IdleSecondsProvider = () => -3;
        Assert.Equal(-1, InputIdleProbe.DeskIdleSeconds());

        ActivityIdle.IdleSecondsProvider = null;
        InputIdleProbe.IsLinux = () => false;         // some other desk: nobody can tell
        Assert.Equal(-1, InputIdleProbe.DeskIdleSeconds());
        Assert.Equal(0, asked);
    });

    [Fact]
    public void TheRedFlashIsDealtOnlyToAProvenDesk() => WithSeams(() =>
    {
        ActivityIdle.IdleSecondsProvider = null;
        InputIdleProbe.IsLinux = () => true;

        InputIdleProbe.X11IdleMilliseconds = () => 5000;                                        // at the desk
        Assert.True(NatashasFavourite.FlashMayRoll(true, true, InputIdleProbe.DeskIdleSeconds()));
        InputIdleProbe.X11IdleMilliseconds = () => (NatashasFavourite.DodgeIdleSec + 1) * 1000L;  // walked away
        Assert.False(NatashasFavourite.FlashMayRoll(true, true, InputIdleProbe.DeskIdleSeconds()));
        InputIdleProbe.X11IdleMilliseconds = () => -1;                                          // unknown: as before
        Assert.False(NatashasFavourite.FlashMayRoll(true, true, InputIdleProbe.DeskIdleSeconds()));
    });

    [Fact]
    public void TheFlashAsksTheDeskProbe() =>
        Assert.Equal(nameof(InputIdleProbe.DeskIdleSeconds), FlashOverlay.IdleSeconds.Method.Name);
}
