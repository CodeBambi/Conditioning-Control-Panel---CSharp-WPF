using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// How long a leash video may run (owner, 2026-09-26). A video punishment ends when the video
/// does or when this cap of real watching is reached, whichever is first. The holder picks the
/// cap when sending (the punishment's <c>size</c>, minutes) and the leashed side sets the longest
/// it will take (<c>video_max</c>); the server clamps the holder's pick to it and the runner
/// applies the lower of the two again in case it was lowered since. No minimum beyond one
/// minute: a short video simply ends first. The slider paints blue to 30, yellow to 60, red to
/// 90. Pure.
/// </summary>
public static class LeashVideoCap
{
    public const int Min = 1;
    public const int Max = 90;
    public const int Default = 30;

    public enum Band { Blue, Yellow, Red }

    public static int Clamp(int minutes) => Math.Clamp(minutes, Min, Max);

    public static Band BandOf(int minutes) => minutes <= 30 ? Band.Blue : minutes <= 60 ? Band.Yellow : Band.Red;

    /// <summary>The cap that applies to a punishment: its own size, never above the leashed
    /// side's current maximum.</summary>
    public static int Effective(int punishSize, int videoMax) => Math.Min(Clamp(punishSize), Clamp(videoMax));
}
