using System;

namespace ConditioningControlPanel.Motion
{
    /// <summary>
    /// The timing table of WPF 7.1.5: the MotionFx helpers' numbers (hover lift, press squish,
    /// entrance stagger, odometer, bar fill, glow breath), the launcher choreography the playbook
    /// names as the reference (pop, shockwave, exit beat) and the house beats from
    /// docs/JUICE-PLAYBOOK.md section 2. Numbers only; each head runs its own clock.
    /// </summary>
    public static class MotionTimings
    {
        // ---- MotionFx interaction helpers (Services/MotionFx.cs) -------------------------------

        /// <summary>Hover lift: scale to 1.02 over 150 ms (quad out), and back.</summary>
        public const double HoverLiftScale = 1.02;
        public const int HoverMs = 150;

        /// <summary>Press squish: scale to 0.97 over 80 ms (quad out), and back.</summary>
        public const double PressSquishScale = 0.97;
        public const int PressMs = 80;

        /// <summary>Entrance stagger: 40 ms apart, capped at 6 slots; each item fades 0 -> 1 over
        /// 220 ms and rises 10 px -> 0 over 260 ms, both quad out.</summary>
        public const int StaggerMs = 40;
        public const int StaggerCap = 6;
        public const int StaggerFadeMs = 220;
        public const int StaggerRiseMs = 260;
        public const double StaggerRisePx = 10;

        /// <summary>The stagger delay for the item at <paramref name="index"/>: anything past the cap
        /// enters on the last slot's clock so a long list never crawls in.</summary>
        public static int StaggerDelayMs(int index) => StaggerMs * Math.Min(Math.Max(0, index), StaggerCap);

        /// <summary>Odometer tween default: 0.7 s, quad out; snaps under Off or a step under 0.5.</summary>
        public const double OdometerSeconds = 0.7;
        public const double OdometerMinStep = 0.5;

        /// <summary>Bar fill default: 0.6 s, quad out. The cap bloom runs fill + 0.45 s: dark until
        /// 80% of the fill, full at the fill's end, gone 0.45 s later (all linear).</summary>
        public const double BarFillSeconds = 0.6;
        public const double BarBloomTailSeconds = 0.45;
        public const double BarBloomStartShare = 0.8;

        /// <summary>The bar bloom's keyframes for a fill of <paramref name="seconds"/>, in ms.</summary>
        public static Keyframe[] BarBloom(double seconds = BarFillSeconds) => new[]
        {
            new Keyframe(0, 0, EaseKind.Linear),
            new Keyframe(seconds * BarBloomStartShare * 1000, 0, EaseKind.Linear),
            new Keyframe(seconds * 1000, 1, EaseKind.Linear),
            new Keyframe((seconds + BarBloomTailSeconds) * 1000, 0, EaseKind.Linear),
        };

        /// <summary>Glow breath (ambient only): opacity 0.6 -> 1.0 over 3.4 s, sine in-out,
        /// auto-reverse, forever, at the ambient frame rate. Parks at the max when refused.</summary>
        public const double GlowBreathMin = 0.6;
        public const double GlowBreathMax = 1.0;
        public const double GlowBreathSeconds = 3.4;

        /// <summary>Every ambient timeline is capped at this frame rate (never 24: judders on 60 Hz).</summary>
        public const int AmbientFrameRate = 30;

        // ---- launcher choreography (Windows/Launcher/LauncherWindow.Choreo.cs) -----------------

        public const double TilePopScale = 1.06;
        public const double CardLiftScale = 1.03;
        public const int PopMs = 380;
        /// <summary>The pop peaks at 35% of its length.</summary>
        public const double PopPeakAt = 0.35;
        public const int ShockwaveMs = 480;
        public const double ShockwaveFromPx = 40;
        public const double ShockwaveToPx = 700;
        public const int ExitBeatMs = 450;
        public const int PlayBurstMain = 250;
        public const int PlayBurstEcho = 120;
        public const int EchoDelayMs = 120;
        public const double DimOthersTo = 0.55;
        public const int DimMs = 300;
        public const int ShakeMs = 320;
        public const double ShakeAmp = 5;

        // ---- house beats (JUICE-PLAYBOOK "House timings") --------------------------------------

        /// <summary>THUD landing entrance: 340 ms on the THUD curve.</summary>
        public const int ThudMs = 340;
        /// <summary>SHIVER small reaction.</summary>
        public const int ShiverMs = 250;
        /// <summary>REVEAL big unveil.</summary>
        public const int RevealMs = 620;
    }
}
