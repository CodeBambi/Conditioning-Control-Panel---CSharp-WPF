using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops.Scenes;

/// <summary>
/// Remote Control: a code travels from the desktop to a phone, a tap on the phone lands a flash
/// and a few bubbles on the desktop, then the panic key clears everything and the phone ends.
/// </summary>
internal sealed class RemoteControlLoop : HelpLoopScene
{
    private const string Code = "KX4-219";
    private static readonly Rect Win = new(22, 24, 292, 178);
    private static readonly Rect Phone = new(354, 26, 100, 190);
    private static readonly Rect Screen = LoopFrame.Inset(Phone, 6);
    private static readonly Rect PanicKey = new(118, 214, 84, 26);

    private static readonly string[] Buttons = { "Flash", "Bubbles", "Spiral" };
    private static readonly double[] BubbleXs = { 70, 150, 230, 110, 270 };

    // The finger: in from the bottom right, taps Flash at 2700 and Bubbles at 3500, leaves.
    private static readonly (double, double, double)[] FingerPath =
    {
        (2000, 470, 260), (2600, 404, 118), (2900, 404, 118), (3400, 404, 146), (3800, 404, 146), (4400, 470, 262),
    };

    private static readonly Brush PhoneBody = LoopPalette.Solid("#0b0816");
    private static readonly Brush PhoneScreen = LoopPalette.Solid("#1b1530");
    private static readonly Brush Finger = LoopPalette.Solid("#ffffff66");
    private static readonly Brush KeyFill = LoopPalette.Solid("#3a1220");
    private static readonly Brush KeyGlow = LoopPalette.Solid("#ff5a6e55");

    private static readonly IReadOnlyList<HelpLoopStep> StepList = new[]
    {
        new HelpLoopStep("help_loop_remotecontrol_1", 0, 2000),
        new HelpLoopStep("help_loop_remotecontrol_2", 2000, 4600),
        new HelpLoopStep("help_loop_remotecontrol_3", 4600, 7200),
    };

    public override string Id => "RemoteControl";
    public override double DurationMs => 7400;
    public override double StillMs => 3600;
    public override IReadOnlyList<HelpLoopStep> Steps => StepList;

    public override void Draw(LoopFrame f, double t)
    {
        var p = f.P;
        var dc = f.Front;
        f.Desktop(Win);

        double panic = Seg(t, 5000, 5350);        // everything clears
        double reset = Seg(t, 6900, 7350);        // the phone goes back to blank for the wrap

        // --- the code, shown on the desktop, then flown to the phone ---
        double codeIn = EaseOut(Seg(t, 250, 600));
        double fly = EaseInOut(Seg(t, 1000, 1700));
        double codeO = codeIn * (1 - reset);
        var from = new Point(Win.X + 30, Win.Y + 70);
        var to = new Point(Screen.X + 8, Screen.Y + 30);
        if (codeO > 0)
        {
            // a small "share code" card inside the window stays behind as the chip leaves
            using (f.Fade(dc, codeIn * (1 - Seg(t, 1900, 2300))))
            {
                dc.DrawRoundedRectangle(p.Panel2, new Pen(p.Border, 1), new Rect(Win.X + 12, Win.Y + 30, 170, 74), 8, 8);
                f.DrawText(dc, "SHARE CODE", Win.X + 30, Win.Y + 42, 9, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);
            }
        }

        // --- the desktop reacts: a photo, then bubbles; the panic key clears both ---
        double photoK = Seg(t, 2780, 3080);
        double photoO = photoK * (1 - panic);
        if (photoO > 0)
        {
            var r = new Rect(196, 64, 104, 76);
            using (f.At(dc, r.X + r.Width / 2, r.Y + r.Height / 2, Lerp(.6, 1, Back(photoK)), photoO))
                f.Photo(dc, r, PhotoLook.Pink);
        }
        for (int i = 0; i < BubbleXs.Length; i++)
        {
            double born = 3560 + i * 120;
            if (t < born) continue;
            double age = t - born;
            double o = Seg(age, 0, 200) * (1 - panic);
            if (o <= 0) continue;
            double y = 200 - age * .045 - i * 6;
            f.Bubble(BubbleXs[i] + 8 * Math.Sin(age / 380 + i), y, .62, o);
        }

        // --- the panic key ---
        double keyIn = Seg(t, 4600, 4850) * (1 - Seg(t, 6400, 6800));
        if (keyIn > 0)
        {
            bool down = t > 5000 && t < 5220;
            double dy = down ? 3 : 0;
            using (f.Fade(dc, keyIn))
            {
                if (t > 5000 && t < 5700)
                    using (f.Fade(dc, 1 - Seg(t, 5000, 5700)))
                        dc.DrawRoundedRectangle(KeyGlow, null, LoopFrame.Inset(PanicKey, -6), 10, 10);
                if (!down) dc.DrawRoundedRectangle(p.Red, null, new Rect(PanicKey.X, PanicKey.Y + 3, PanicKey.Width, PanicKey.Height), 6, 6);
                var kr = new Rect(PanicKey.X, PanicKey.Y + dy, PanicKey.Width, PanicKey.Height);
                dc.DrawRoundedRectangle(KeyFill, new Pen(p.Red, 1.5), LoopFrame.Inset(kr, .75), 6, 6);
                f.DrawText(dc, "PANIC", kr.X + kr.Width / 2, kr.Y + 6, 11, p.Text, LoopFrame.Mono, FontWeights.SemiBold, TextAlignment.Center);
            }
        }

        DrawPhone(f, t, panic, reset, fly);

        // the code chip flies over the phone, so it is drawn after it
        if (codeO > 0)
        {
            double arc = -46 * Math.Sin(fly * Math.PI);
            double x = Lerp(from.X, to.X, fly), y = Lerp(from.Y, to.Y, fly) + arc;
            double sc = Lerp(1, .82, fly);
            using (f.At(dc, x, y + 11, sc, codeO * (1 - panic * .6)))
                f.Chip(x, y, Code, hot: t < 1900);
        }

        // --- the finger on the phone ---
        var fp = Path(FingerPath, t);
        bool press = (t > 2680 && t < 2800) || (t > 3480 && t < 3600);
        f.Ripple(404, 118, Seg(t, 2700, 3100));
        f.Ripple(404, 146, Seg(t, 3500, 3900));
        if (t > 2000 && t < 4400)
            dc.DrawEllipse(Finger, new Pen(p.White, 1.2), fp, press ? 6.5 : 8, press ? 6.5 : 8);
    }

    private static void DrawPhone(LoopFrame f, double t, double panic, double reset, double fly)
    {
        var p = f.P;
        var dc = f.Front;
        LoopFrame.SoftShadow(dc, Phone, 16, 10, 26, 0xAA);
        dc.DrawRoundedRectangle(PhoneBody, new Pen(p.WindowBorder, 1.5), Phone, 16, 16);
        dc.DrawRoundedRectangle(PhoneScreen, null, Screen, 11, 11);
        dc.DrawRoundedRectangle(p.Track, null, new Rect(Phone.X + Phone.Width / 2 - 14, Phone.Y + 3, 28, 3), 1.5, 1.5);

        f.DrawText(dc, "REMOTE", Screen.X + 8, Screen.Y + 8, 8, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Left);

        // linked dot, once the code arrived
        double linked = Seg(t, 1700, 1900) * (1 - panic) * (1 - reset);
        if (linked > 0)
            using (f.Fade(dc, linked))
                dc.DrawEllipse(p.Mint, null, new Point(Screen.Right - 10, Screen.Y + 13), 3, 3);

        // the three buttons
        double btnO = Seg(t, 1800, 2100) * (1 - panic);
        if (btnO > 0)
            using (f.Fade(dc, btnO))
                for (int i = 0; i < Buttons.Length; i++)
                {
                    var r = new Rect(Screen.X + 8, Screen.Y + 72 + i * 28, Screen.Width - 16, 22);
                    double hit = i == 0 ? Seg(t, 2700, 3000) : i == 1 ? Seg(t, 3500, 3800) : 0;
                    bool lit = hit > 0 && hit < 1;
                    dc.DrawRoundedRectangle(lit ? p.Accent : p.Panel2, new Pen(lit ? p.Accent : p.Border, 1), LoopFrame.Inset(r, .5), 7, 7);
                    f.DrawText(dc, Buttons[i], r.X + r.Width / 2, r.Y + 4, 10, lit ? p.Ink : p.Text, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Center);
                }

        // after panic: the session has ended on the phone too
        double ended = Seg(t, 5250, 5550) * (1 - reset);
        if (ended > 0)
            using (f.Fade(dc, ended))
            {
                f.DrawText(dc, "ended", Screen.X + Screen.Width / 2, Screen.Y + 90, 14, p.Red, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Center);
                f.DrawText(dc, "by panic", Screen.X + Screen.Width / 2, Screen.Y + 110, 9, p.Dim, LoopFrame.Mono, FontWeights.Medium, TextAlignment.Center);
            }
    }
}
