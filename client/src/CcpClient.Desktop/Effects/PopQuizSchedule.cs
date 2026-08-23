namespace CcpClient.Desktop.Effects;

/// <summary>
/// Pop Quiz's pacing arithmetic, ported from <c>Services/Quiz/PopQuizService.cs</c> and kept pure so
/// every constant and every clamp is pinnable without a clock, a window or a session.
///
/// <para><b>It is Lock Card's law with a different dial, and upstream says so itself.</b>
/// <c>PopQuizService.cs:11</c>: <i>"Follows the same scheduling pattern as LockCardService."</i> The
/// two bodies are the same four lines — <c>60.0/perHour</c>, <c>min = x0.7</c>, <c>max = x1.3</c>,
/// <c>roll * (max - min) + min</c> (<c>:113-122</c>, recomputed every tick at <c>:163-171</c>).</para>
///
/// <para><b>So why is this not a call into <see cref="LockCardSchedule"/>?</b> For the reason
/// <see cref="BubbleCountSchedule"/> already recorded for the same shape: they are two upstream
/// sites with two dials and two clamps, and a shared helper would make a later change to one
/// silently change the other. The NUMBERS are the behaviour, and this module's numbers are not the
/// Lock Card's: its dial clamps 1..100 rather than 1..10, and it has <b>no first-card offset</b> —
/// Pop Quiz spaces its very first question like every other one (<c>:120-123</c>), where the Lock
/// Card deliberately does not (<c>Services/LockCard/LockCardService.cs:159-168</c>).</para>
/// </summary>
public static class PopQuizSchedule
{
    /// <summary>WPF's default (<c>Models/AppSettings.cs:3582</c>, <c>_popQuizFrequency = 2</c>).</summary>
    public const int DefaultPerHour = 2;

    /// <summary>WPF's clamp (<c>AppSettings.cs:3586</c>, <c>Math.Clamp(value, 1, 100)</c>).</summary>
    public const int MinPerHour = 1;

    /// <summary>
    /// WPF's clamp (<c>AppSettings.cs:3586</c>) — <b>one hundred, not ten</b>.
    ///
    /// <para><b>A spec-versus-code disagreement resolved in favour of the code, and the panel agrees
    /// with the code.</b> The comment one line above the clamp says <i>"Per hour (1-10)"</i>
    /// (<c>:3582</c>), which would make this ten. It is stale: the slider the user actually drags is
    /// <c>Minimum="1" Maximum="100"</c> (<c>Views/Tabs/GradedIntakeTabView.xaml:286</c>), so a dial
    /// of 60 is reachable in the shipping product and a port that capped at ten would refuse a
    /// setting a user already has.</para>
    /// </summary>
    public const int MaxPerHour = 100;

    /// <summary>The bottom of the ±30 % band (<c>PopQuizService.cs:117</c>,
    /// <c>minInterval = intervalMinutes * 0.7</c>, and again at <c>:165</c>).</summary>
    public const double JitterLow = 0.7;

    /// <summary>The top of it (<c>PopQuizService.cs:118</c>,
    /// <c>maxInterval = intervalMinutes * 1.3</c>, and again at <c>:166</c>).</summary>
    public const double JitterHigh = 1.3;

    /// <summary>
    /// The interval until the next question — <c>Timer_Tick</c>'s own arithmetic
    /// (<c>PopQuizService.cs:163-171</c>), verbatim including the <c>roll * (max - min) + min</c>
    /// shape.
    /// </summary>
    /// <param name="perHour">Questions per hour. <b>The <c>Math.Max(1, …)</c> below is the PORT's,
    /// not upstream's</b>: upstream divides by the dial unguarded (<c>:114</c>, <c>:164</c>) and is
    /// safe only because <c>AppSettings</c> clamped it first, so a zero here would be a division by
    /// zero producing an infinite <see cref="TimeSpan"/> that throws at the clock. It is the guard
    /// <see cref="EffectSchedule.BaseIntervalSeconds"/> already states the case for — it stops this
    /// function depending on its caller's clamp.</param>
    /// <param name="roll">A uniform draw in [0,1) — upstream's <c>_random.NextDouble()</c>.</param>
    public static TimeSpan Interval(int perHour, double roll)
    {
        var intervalMinutes = 60.0 / Math.Max(1, perHour);
        var minInterval = intervalMinutes * JitterLow;
        var maxInterval = intervalMinutes * JitterHigh;
        return TimeSpan.FromMinutes((roll * (maxInterval - minInterval)) + minInterval);
    }
}
