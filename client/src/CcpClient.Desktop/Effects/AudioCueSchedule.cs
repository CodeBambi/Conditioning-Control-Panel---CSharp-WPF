namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Mind Wipe pacing law — and it is a DIFFERENT SHAPE from every module the port has paced so
/// far, which is the reason it has its own file rather than a third facade over
/// <see cref="EffectSchedule"/>.
///
/// <para>Flash Images and Subliminals both compute an INTERVAL from their dial and schedule the next
/// firing that far out (<c>Services/Flash/FlashService.cs:538-563</c>,
/// <c>Services/Subliminal/SubliminalService.cs:172-187</c>). Mind Wipe does not: it ticks on a
/// <b>fixed 10-second window</b> and ROLLS a probability each time
/// (<c>Services/LockCard/MindWipeService.cs:127-129</c> for the window,
/// <c>:741-742</c> for the roll). The dial changes the odds, never the spacing. That difference is
/// visible to a user — a paced module at 6/hour arrives on a jittered ten-minute rhythm, a rolled
/// one arrives in clumps and gaps — so it is kept exactly rather than normalised into the interval
/// law the other two share.</para>
///
/// <para><b>The divisor is 360 and the code comment says otherwise.</b>
/// <c>MindWipeService.cs:710</c> says "probability of triggering in this 30-second window" and
/// <c>:733</c> says "At 180/hour, probability = 0.5 = 50% chance per interval", but the timer's
/// interval is <b>10</b> seconds (<c>:127-129</c>) and the arithmetic at <c>:734</c> is
/// <c>_frequencyPerHour / 360.0</c> — which is 3600/10, the number of TEN-second windows in an hour,
/// and which makes 180/hour give 0.5 exactly as <c>:733</c> claims. <b>The arithmetic is the
/// behaviour and the "30-second" comment is stale.</b> Recorded as a discrepancy rather than
/// silently followed either way.</para>
/// </summary>
public static class MindWipeSchedule
{
    /// <summary>WPF's tick window — <c>TimeSpan.FromSeconds(10)</c>, whose own comment is "Check
    /// every 10 seconds for better high-frequency support" (<c>MindWipeService.cs:127-129</c>).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    /// <summary>Ten-second windows in an hour: the divisor at <c>MindWipeService.cs:734</c>.</summary>
    public const double WindowsPerHour = 360.0;

    /// <summary>WPF default (<c>MindWipeService.cs:25</c>): 6 per hour.</summary>
    public const int DefaultPerHour = 6;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 180)</c> (<c>MindWipeService.cs:100</c>).</summary>
    public const int MinPerHour = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 180)</c> (<c>MindWipeService.cs:100</c>).</summary>
    public const int MaxPerHour = 180;

    /// <summary>The chance that one window fires — <c>_frequencyPerHour / 360.0</c>
    /// (<c>MindWipeService.cs:734</c>). At the dial's ceiling of 180 this is exactly 0.5, which is
    /// upstream's own stated expectation (<c>:733</c>).</summary>
    public static double TriggerProbability(double perHour) => perHour / WindowsPerHour;
}

/// <summary>
/// The Brain Drain pacing law — the same ROLLED shape as <see cref="MindWipeSchedule"/> with two
/// differences a user can hear, and one they can change.
///
/// <para><b>The window is a dial.</b> Brain Drain ticks every 500 ms in high-refresh mode and every
/// 5 s otherwise (<c>Services/LockCard/BrainDrainService.cs:60-69</c>). Mind Wipe's window is
/// fixed.</para>
///
/// <para><b>The probability is normalised per MINUTE, not per hour.</b>
/// <c>_intensity / 100.0 / (60.0 / _timer.Interval.TotalSeconds)</c>
/// (<c>BrainDrainService.cs:183</c>) — the divisor is the number of ticks in a minute, so the dial
/// reads as "roughly this many percent chance of a clip per minute" and moving the high-refresh
/// switch changes the GRAIN without changing the rate. That the two halves of that expression cancel
/// is the point: it is why the same intensity sounds the same in both modes.</para>
/// </summary>
public static class BrainDrainSchedule
{
    /// <summary>High-refresh tick — <c>TimeSpan.FromMilliseconds(500)</c>
    /// (<c>BrainDrainService.cs:64-65</c>).</summary>
    public static readonly TimeSpan HighRefreshWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>Normal tick — <c>TimeSpan.FromSeconds(5)</c> (<c>BrainDrainService.cs:64-66</c>).</summary>
    public static readonly TimeSpan NormalWindow = TimeSpan.FromSeconds(5);

    /// <summary>Seconds in the MINUTE the intensity dial is normalised over
    /// (<c>BrainDrainService.cs:183</c>).</summary>
    public const double SecondsPerMinute = 60.0;

    /// <summary>WPF default (<c>BrainDrainService.cs:20</c>): 50 %.</summary>
    public const int DefaultIntensity = 50;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 100)</c> (<c>BrainDrainService.cs:49</c>).</summary>
    public const int MinIntensity = 1;

    /// <summary>WPF clamp <c>Math.Clamp(value, 1, 100)</c> (<c>BrainDrainService.cs:49</c>).</summary>
    public const int MaxIntensity = 100;

    /// <summary>The tick for a high-refresh setting (<c>BrainDrainService.cs:60-69</c>).</summary>
    public static TimeSpan Window(bool highRefresh) => highRefresh ? HighRefreshWindow : NormalWindow;

    /// <summary>The chance that one window fires (<c>BrainDrainService.cs:183</c>).</summary>
    public static double TriggerProbability(double intensity, TimeSpan window) =>
        intensity / 100.0 / (SecondsPerMinute / window.TotalSeconds);
}
