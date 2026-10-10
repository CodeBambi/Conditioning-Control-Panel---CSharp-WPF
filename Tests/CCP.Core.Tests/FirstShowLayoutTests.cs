using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

// PORTED from Tests/ConditioningControlPanel.Tests/FirstShowLayoutTests.cs (WPF 7.1.5), cases verbatim.
public class FirstShowLayoutTests
{
    private static bool Contains(RectD outer, RectD r) =>
        r.Left >= outer.Left && r.Top >= outer.Top && r.Right <= outer.Right && r.Bottom <= outer.Bottom;

    // WPF Rect.IntersectsWith counts a shared edge as intersecting.
    private static bool Intersects(RectD a, RectD b) =>
        b.Left <= a.Right && b.Right >= a.Left && b.Top <= a.Bottom && b.Bottom >= a.Top;

    [Theory]
    [InlineData(1920, 1080, 220, 460)]
    [InlineData(1366, 768, 220, 460)]
    [InlineData(1280, 720, 420, 344)]
    public void Logo_remains_clear_with_a_resized_Emi_and_long_speech(int width, int height, int emi, int logoWidth)
    {
        var bounds = new RectD(16, 16, width - 32, height - 64);
        var logo = new RectD(width / 2 - logoWidth / 2, height * .43 - 150, logoWidth, 300);
        var speech = new ShowSize(390, 260);
        var body = FirstShowLayout.GuideBody(bounds, new ShowSize(emi, emi * 1.012), speech, logo);
        var card = FirstShowLayout.Speech(bounds, speech, body, logo, new Vec2(body.Left, body.Bottom + 24));
        Assert.True(Contains(bounds, body)); Assert.True(Contains(bounds, card));
        Assert.False(Intersects(logo, body)); Assert.False(Intersects(logo, card));
        Assert.False(Intersects(card, body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1920)]
    [InlineData(1920)]
    public void Asset_tree_and_speech_stay_clear_on_the_app_monitor(int monitorX)
    {
        var bounds = new RectD(monitorX + 16, 16, 1334, 704);
        var target = new RectD(monitorX + 160, 200, 280, 490);
        var speech = new ShowSize(390, 300);
        var body = FirstShowLayout.GuideBody(bounds, new ShowSize(300, 304), speech, target);
        var card = FirstShowLayout.Speech(bounds, speech, body, target, new Vec2(body.Left, body.Bottom + 24));
        Assert.True(Contains(bounds, body)); Assert.True(Contains(bounds, card));
        Assert.False(Intersects(target, body)); Assert.False(Intersects(target, card));
        Assert.False(Intersects(card, body));
    }

    [Fact]
    public void Right_edge_target_does_not_push_the_speech_off_screen()
    {
        var bounds = new RectD(16, 16, 990, 700);
        var target = new RectD(880, 220, 100, 36);
        var body = new RectD(720, 320, 220, 224);
        var card = FirstShowLayout.Speech(bounds, new ShowSize(390, 300), body, target, new Vec2(900, 700));
        Assert.True(Contains(bounds, card));
        Assert.False(Intersects(card, target)); Assert.False(Intersects(card, body));
    }

    [Fact]
    public void Outro_speech_stays_next_to_Emi_instead_of_at_screen_center()
    {
        var bounds = new RectD(16, 16, 1888, 1016);
        var body = new RectD(16, 812, 220, 220);
        var logo = new RectD(730, 314, 460, 300);
        var card = FirstShowLayout.Speech(bounds, new ShowSize(390, 180), body, logo, new Vec2(765, 852));
        Assert.InRange(card.Left - body.Right, 10, 24);
        Assert.False(Intersects(card, body)); Assert.False(Intersects(card, logo));
        Assert.True(Contains(bounds, card));
    }

    [Fact]
    public void Emi_stays_beside_the_logo_instead_of_in_a_screen_corner()
    {
        var logo = new RectD(730, 314, 460, 300);
        var body = FirstShowLayout.GuideBody(new RectD(16, 16, 1888, 1016), new ShowSize(220, 224), new ShowSize(390, 180), logo);
        Assert.False(Intersects(body, logo));
        double dx = System.Math.Max(0, System.Math.Max(body.Left - logo.Right, logo.Left - body.Right));
        double dy = System.Math.Max(0, System.Math.Max(body.Top - logo.Bottom, logo.Top - body.Bottom));
        Assert.InRange(System.Math.Sqrt(dx * dx + dy * dy), 18, 40);
    }
}

public class FirstShowScriptTests
{
    [Fact]
    public void Every_beat_has_a_cue_and_a_recorded_sound()
    {
        Assert.Equal(FirstShowScript.Beats.Length, FirstShowScript.Cues.Length);
        foreach (var cue in FirstShowScript.Cues) Assert.EndsWith(".mp3", FirstShowScript.CueAsset(cue));
        Assert.Equal("first_show_tada", FirstShowScript.LineKey(8));
        Assert.Equal("first_show_beat0", FirstShowScript.LineKey(0));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void The_show_never_opens_over_a_session_Lockdown_or_Strict_Lock(bool session, bool lockdown, bool strict, bool may) =>
        Assert.Equal(may, FirstShowScript.MayOpen(session, lockdown, strict));

    [Fact]
    public void Escape_reads_as_Esc_and_other_keys_keep_their_name()
    {
        Assert.Equal("Esc", FirstShowScript.PanicLabel("Escape"));
        Assert.Equal("Esc", FirstShowScript.PanicLabel(null));
        Assert.Equal("F9", FirstShowScript.PanicLabel("F9"));
    }

    [Fact]
    public void Bleepese_is_rendered_from_the_line_and_is_deterministic()
    {
        var a = ConditioningControlPanel.Services.EmiDesk.EmiVox.MakeScore("Hey. Want to see what CCP can do?", "celebration");
        var b = ConditioningControlPanel.Services.EmiDesk.EmiVox.MakeScore("Hey. Want to see what CCP can do?", "celebration");
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        var wav = ConditioningControlPanel.Services.EmiDesk.EmiVox.WriteWav(ConditioningControlPanel.Services.EmiDesk.EmiVox.RenderBurst(a));
        Assert.True(wav.Length > 44);
        Assert.Equal((byte)'R', wav[0]);
    }
}
