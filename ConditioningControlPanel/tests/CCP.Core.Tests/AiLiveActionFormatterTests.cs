using System;
using ConditioningControlPanel.Core.Services.Commands;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Unit tests for <see cref="AiLiveActionFormatter"/>, the pure port of WPF
/// <c>FormatLiveAction</c> (<c>ConditioningControlPanel/Services/Commands/AiCommandService.cs:140-180</c>).
/// Verifies the per-type strings, the <see cref="System.Math.Clamp"/> bounds, the text truncation,
/// and the privacy contract (the feed line describes the ACTION / effect content only, never the
/// AI prompt or raw command JSON).
/// </summary>
public class AiLiveActionFormatterTests
{
    private static AiCommandData Cmd(AICommandType type, IAiCommandData? data) => new() { Command = type, Data = data };

    [Fact]
    public void Format_FlashImage_UsesAmountAndDuration()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.flash_image, new FlashImage(5, 10, 100, 80)));
        Assert.Equal("💥 Flash · 5 images for 10s", line);
    }

    [Theory]
    [InlineData(20, 10, "💥 Flash · 8 images for 10s")]   // Amount clamped to 8
    [InlineData(4, 99, "💥 Flash · 4 images for 10s")]    // Duration clamped to 10
    [InlineData(-3, -1, "💥 Flash · 0 images for 0s")]    // Negative clamped to 0
    public void Format_FlashImage_ClampsBounds(int amount, int duration, string expected)
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.flash_image, new FlashImage(amount, duration, 50, 80)));
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Format_Bubbles_OnWithFrequency_UsesFrequency()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.bubbles, new Bubbles(true, 3)));
        Assert.Equal("🫧 Bubbles started (3/min)", line);
    }

    [Fact]
    public void Format_Bubbles_OnWithZeroFrequency_DefaultsToFive()
    {
        // WPF AiCommandService.cs:149-150 — On||freq>0 with freq==0 falls back to "(5/min)".
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.bubbles, new Bubbles(true, 0)));
        Assert.Equal("🫧 Bubbles started (5/min)", line);
    }

    [Fact]
    public void Format_Bubbles_OffAndZeroFrequency_Stops()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.bubbles, new Bubbles(false, 0)));
        Assert.Equal("🫧 Bubbles stopped", line);
    }

    [Fact]
    public void Format_Bubbles_OffButPositiveFrequency_Starts()
    {
        // freq>0 alone implies start even when On is false (WPF parity).
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.bubbles, new Bubbles(false, 7)));
        Assert.Equal("🫧 Bubbles started (7/min)", line);
    }

    [Fact]
    public void Format_Subliminal_ShowsQuotedText()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.subliminal, new Subliminal("OBEY", 60)));
        Assert.Equal("👁️ Subliminal · \"OBEY\"", line);
    }

    [Fact]
    public void Format_Subliminal_TruncatesLongText()
    {
        // WPF AiCommandService.cs:154 — cap at 40 chars + "…".
        var longText = new string('X', 60);
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.subliminal, new Subliminal(longText, 60)));
        Assert.Equal("👁️ Subliminal · \"" + new string('X', 40) + "…\"", line);
    }

    [Fact]
    public void Format_Subliminal_NullText_BecomesEmpty()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.subliminal, new Subliminal(null!, 60)));
        Assert.Equal("👁️ Subliminal · \"\"", line);
    }

    [Fact]
    public void Format_MantraLockscreen_ShowsQuotedMantraAndAmount()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.mantra_lockscreen, new MantraLockscreen("Good girl", 3)));
        Assert.Equal("🔒 Lock card · \"Good girl\" ×3", line);
    }

    [Fact]
    public void Format_MantraLockscreen_TruncatesLongMantraAndClampsAmount()
    {
        var longMantra = new string('M', 50);
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.mantra_lockscreen, new MantraLockscreen(longMantra, 99)));
        Assert.Equal("🔒 Lock card · \"" + new string('M', 30) + "…\" ×5", line);
    }

    [Fact]
    public void Format_Spiral_OnShowsIntensity()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.spiral, new SpiralPinkFiler(true, 20)));
        Assert.Equal("🌀 Spiral on (20%)", line);
    }

    [Fact]
    public void Format_Spiral_OffIsPlain()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.spiral, new SpiralPinkFiler(false, 0)));
        Assert.Equal("🌀 Spiral off", line);
    }

    [Fact]
    public void Format_Spiral_ClampsIntensityTo30()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.spiral, new SpiralPinkFiler(true, 99)));
        Assert.Equal("🌀 Spiral on (30%)", line);
    }

    [Fact]
    public void Format_Pink_OnOffAndClamp()
    {
        Assert.Equal("🩷 Pink filter on (15%)",
            AiLiveActionFormatter.Format(Cmd(AICommandType.pink, new SpiralPinkFiler(true, 15))));
        Assert.Equal("🩷 Pink filter off",
            AiLiveActionFormatter.Format(Cmd(AICommandType.pink, new SpiralPinkFiler(false, 0))));
        Assert.Equal("🩷 Pink filter on (30%)",
            AiLiveActionFormatter.Format(Cmd(AICommandType.pink, new SpiralPinkFiler(true, 40))));
    }

    [Fact]
    public void Format_Bounce_OnOff()
    {
        Assert.Equal("💃 Bouncing text on",
            AiLiveActionFormatter.Format(Cmd(AICommandType.bounce, new Bounce(new System.Collections.Generic.List<string>(), true))));
        Assert.Equal("💃 Bouncing text off",
            AiLiveActionFormatter.Format(Cmd(AICommandType.bounce, new Bounce(new System.Collections.Generic.List<string>(), false))));
    }

    [Fact]
    public void Format_Haptic_PercentAndDuration()
    {
        // WPF AiCommandService.cs:167 — intensity 0-1 rounded to a percentage.
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.haptic, new HapticCommandData(0.75, 8)));
        Assert.Equal("📳 Vibrate · 75% for 8s", line);
    }

    [Fact]
    public void Format_Haptic_ClampsIntensityAndDuration()
    {
        // Intensity > 1 clamps to 100%; Duration > 10 clamps to 10s.
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.haptic, new HapticCommandData(2.5, 99)));
        Assert.Equal("📳 Vibrate · 100% for 10s", line);
    }

    [Fact]
    public void Format_Video_PrefersTitle()
    {
        Assert.Equal("🎬 Video · Hypno Loop",
            AiLiveActionFormatter.Format(Cmd(AICommandType.video, new Media("Hypno Loop", "loop.mp4"))));
    }

    [Fact]
    public void Format_Video_FallsBackToPathThenDefault()
    {
        // WPF AiCommandService.cs:170 uses `vm.Path ?? "video"` — null-coalescing, so the
        // "video" default fires only when Path is NULL (not when it is empty).
        Assert.Equal("🎬 Video · loop.mp4",
            AiLiveActionFormatter.Format(Cmd(AICommandType.video, new Media("", "loop.mp4"))));
        Assert.Equal("🎬 Video · video",
            AiLiveActionFormatter.Format(Cmd(AICommandType.video, new Media("", null!))));
    }

    [Fact]
    public void Format_Audio_PrefersTitleThenPathThenDefault()
    {
        Assert.Equal("🔊 Audio · Drone",
            AiLiveActionFormatter.Format(Cmd(AICommandType.audio, new Media("Drone", "drone.mp3"))));
        Assert.Equal("🔊 Audio · drone.mp3",
            AiLiveActionFormatter.Format(Cmd(AICommandType.audio, new Media("", "drone.mp3"))));
        Assert.Equal("🔊 Audio · audio",
            AiLiveActionFormatter.Format(Cmd(AICommandType.audio, new Media("", null!))));
    }

    [Fact]
    public void Format_GetBackToMe_ShowsDelay()
    {
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.getbacktome,
            new GetBackToMe(30, "abc", null, null, true)));
        Assert.Equal("⏱️ Follow-up in 30s", line);
    }

    [Fact]
    public void Format_GetBackToMe_ClampsDelayToOneAndSixHundred()
    {
        Assert.Equal("⏱️ Follow-up in 1s",
            AiLiveActionFormatter.Format(Cmd(AICommandType.getbacktome, new GetBackToMe(0, "abc", null, null, true))));
        Assert.Equal("⏱️ Follow-up in 600s",
            AiLiveActionFormatter.Format(Cmd(AICommandType.getbacktome, new GetBackToMe(9999, "abc", null, null, true))));
    }

    [Fact]
    public void Format_UnknownCommand_ReturnsGearFallback()
    {
        // WPF AiCommandService.cs:177-178 default arm.
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.none, null));
        Assert.Equal("⚙️ none", line);
    }

    [Fact]
    public void Format_KnownCommandWithMismatchedData_ReturnsGearFallback()
    {
        // flash_image command but data is a Bubbles instance -> no case matches -> default arm.
        var line = AiLiveActionFormatter.Format(Cmd(AICommandType.flash_image, new Bubbles(false, 0)));
        Assert.Equal("⚙️ flash_image", line);
    }

    [Fact]
    public void Format_NeverLeaksRawJsonOrPrompt()
    {
        // Privacy contract (WPF FormatLiveAction): the feed line is a short human description of
        // the ACTION, never the AI's chat prompt or a raw JSON dump. Every output is a fixed
        // template filled from the parsed record fields and starts with its known emoji prefix.
        var samples = new[]
        {
            AiLiveActionFormatter.Format(Cmd(AICommandType.flash_image, new FlashImage(1, 2, 50, 80))),
            AiLiveActionFormatter.Format(Cmd(AICommandType.video, new Media("Title", "p.mp4"))),
            AiLiveActionFormatter.Format(Cmd(AICommandType.haptic, new HapticCommandData(0.5, 3))),
            AiLiveActionFormatter.Format(Cmd(AICommandType.getbacktome, new GetBackToMe(5, "tok", null, null, false))),
        };
        foreach (var line in samples)
        {
            Assert.DoesNotContain("\"command\"", line);
            Assert.DoesNotContain("\"data\"", line);
            Assert.DoesNotContain("prompt", line, StringComparison.OrdinalIgnoreCase);
        }
    }
}
