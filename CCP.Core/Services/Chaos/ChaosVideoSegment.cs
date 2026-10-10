using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The descent's video tape is a random slice: WPF <c>VideoPayload.Fire</c>
/// (Services/Chaos/EffectPayload.cs:229) arms <c>VideoService.ArmRandomSegment(SEGMENT_SEC)</c>
/// before it triggers the video, and the player then jumps to a random start that leaves at
/// least that many seconds to play (VideoService.cs:517, :3542). The arm is one shot and goes
/// stale after 30 s, so a video the scheduler starts later plays from its beginning.
/// </summary>
public static class ChaosVideoSegment
{
    /// <summary>WPF VideoPayload.SEGMENT_SEC.</summary>
    public const double SEGMENT_SEC = 15;
    /// <summary>WPF VideoService.SegmentArmed: an arm older than this no longer counts.</summary>
    public const double ARM_LIFE_SEC = 30;
    /// <summary>WPF: a start this close to the beginning is not worth a seek.</summary>
    public const long MIN_SEEK_MS = 500;

    /// <summary>The start position for a clip of <paramref name="lengthMs"/>, or 0 for "play from
    /// the beginning" (the clip is no longer than the slice, or the start is within half a second).</summary>
    public static long StartMs(long lengthMs, double segmentSec, double fraction)
    {
        long segMs = (long)(Math.Max(1, segmentSec) * 1000);
        if (lengthMs <= segMs) return 0;
        long startMs = (long)((lengthMs - segMs) * Math.Clamp(fraction, 0, 1));
        return startMs > MIN_SEEK_MS ? startMs : 0;
    }

    public static bool StillArmed(DateTime armedAtUtc, DateTime nowUtc) =>
        (nowUtc - armedAtUtc).TotalSeconds < ARM_LIFE_SEC;
}
