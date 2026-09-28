using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Audio (ducking): your music plays at full volume, a video comes on and the music dips under
/// it, the video ends and the music comes back up. The app never lowers its own sound.
/// </summary>
internal sealed class AudioLoop : HelpLoopScene
{
    private static readonly Rect Mixer = new(18, 150, 196, 84);
    private static readonly Rect VideoRect = new(236, 34, 226, 132);

    private static readonly (double T, double V)[] Music =
        { (0, 1), (2300, 1), (2900, .3), (4700, .3), (5400, 1) };
    private static readonly (double T, double V)[] Ccp =
        { (0, 0), (2100, 0), (2600, .85), (4500, .85), (4900, 0) };

    private static readonly GradientStopCollection Stops = LoopPalette.Freeze(new GradientStopCollection
    {
        new GradientStop(LoopPalette.Css("#3a1450"), 0),
        new GradientStop(LoopPalette.Css("#ff6fb5"), 1 / 3.0),
        new GradientStop(LoopPalette.Css("#6b3cff"), 2 / 3.0),
        new GradientStop(LoopPalette.Css("#3a1450"), 1),
    });
    private static readonly Brush BarTrack = LoopPalette.Solid("#ffffff33");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_audio_1", 0, 2000),
        new HelpLoopStep("help_loop_audio_2", 2000, 4600),
        new HelpLoopStep("help_loop_audio_3", 4600, 6600),
    };

    public override string Id => "Audio";
    public override double DurationMs => 6800;
    public override double StillMs => 3400;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        f.Desktop();

        double music = Schedule(Music, t);
        double ccp = Schedule(Ccp, t);

        // --- the video window ---
        double vk = EaseOut(Seg(t, 2000, 2450)) * (1 - EaseInOut(Seg(t, 4400, 4800)));
        if (vk > 0)
        {
            var c = new Point(VideoRect.X + VideoRect.Width / 2, VideoRect.Y + VideoRect.Height / 2);
            using (f.At(dc, c.X, c.Y, Lerp(.4, 1, vk), Seg(vk, 0, .3)))
            {
                LoopFrame.SoftShadow(dc, VideoRect, 6, 8, 22, 0x99);
                var span = VideoRect.Width * 3;
                var shift = -2 * VideoRect.Width * ((t / 40) % 300) / 100.0;
                var dir = new Vector(Math.Sin(120 * Math.PI / 180), -Math.Cos(120 * Math.PI / 180));
                var start = new Point(VideoRect.X + shift, VideoRect.Y);
                var brush = new LinearGradientBrush(Stops, 0)
                {
                    MappingMode = BrushMappingMode.Absolute,
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    StartPoint = start,
                    EndPoint = start + dir * span,
                };
                dc.DrawRoundedRectangle(brush, null, VideoRect, 6, 6);
                var track = new Rect(VideoRect.X + 10, VideoRect.Bottom - 14, VideoRect.Width - 20, 4);
                dc.DrawRoundedRectangle(BarTrack, null, track, 2, 2);
                double fill = track.Width * Seg(t, 2300, 4400);
                if (fill > 0) dc.DrawRoundedRectangle(p.White, null, new Rect(track.X, track.Y, fill, 4), 2, 2);
            }
        }

        // --- the mixer ---
        f.Card(Mixer, p.Border);
        double wave = t % 900 / 900;
        using (f.Fade(dc, Lerp(.35, 1, music)))
            f.Speaker(Mixer.X + 10, Mixer.Y + 29, wave);
        f.Meter(new Rect(Mixer.X + 74, Mixer.Y + 26, 108, 8), music, p.Lilac, "MUSIC");
        f.Meter(new Rect(Mixer.X + 74, Mixer.Y + 62, 108, 8), ccp, p.Accent, "CCP");

        double duck = Seg(t, 2600, 2900) * (1 - Seg(t, 4700, 5000));
        f.Chip(Mixer.X, Mixer.Y - 30, "DUCK 70%", hot: true, opacity: duck);
    }
}
