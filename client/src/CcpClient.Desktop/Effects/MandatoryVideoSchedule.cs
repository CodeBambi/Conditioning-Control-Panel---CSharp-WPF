namespace CcpClient.Desktop.Effects;

/// <summary>
/// The Mandatory Video pacing law — an INTERVAL law, like Flash Images and Subliminals and unlike
/// the two rolled audio modules.
///
/// <para><c>3600.0 / perHour * (0.8 + roll * 0.4)</c>, floored at sixty seconds
/// (<c>Services/Video/VideoService.cs:2225-2229</c>). The jitter is ±20 % of the base interval and
/// the floor is upstream's own <c>Math.Max(60, secs)</c>, which is what stops a dial at its ceiling
/// from stacking clips: at 20 an hour the base interval is 180 s and the floor never binds, but a
/// preset written by hand could ask for more.</para>
///
/// <para><b>The floor is not cosmetic and it is not the same as the dial's clamp.</b> A video FIRING
/// occupies the screen for the length of the clip; two firings a minute apart on a two-minute clip
/// would produce a module that is always busy and drops every other firing. Upstream's floor is the
/// only thing between the schedule and that state, which is why it is ported as arithmetic rather
/// than as a comment.</para>
/// </summary>
public static class MandatoryVideoSchedule
{
    /// <summary>
    /// WPF default (<c>Models/AppSettings.cs:992</c>, <c>private int _videosPerHour = 6;</c>): six
    /// clips an hour.
    ///
    /// <para><b>This constant used to be 2, and 2 came from the slider's markup</b>
    /// (<c>Features/VideoFeatureControl.xaml:79</c>, <c>Value="2"</c>). That literal is overwritten
    /// the moment the control loads — <c>SliderPerHour.Value = s.VideosPerHour;</c>
    /// (<c>Features/VideoFeatureControl.xaml.cs:54</c>) — so no fresh upstream install has ever
    /// shown it. <b>A XAML literal is a design-time placeholder, not a default</b>, and that lesson
    /// is general rather than this constant's alone: the settings model is the only thing that
    /// decides what a fresh install runs at. Pinned against upstream's own field initializer by
    /// <c>PresetDefaultParityTests</c>, which reads it out of this checkout rather than retyping it.</para>
    /// </summary>
    public const int DefaultPerHour = 6;

    /// <summary>WPF's slider minimum (<c>Features/VideoFeatureControl.xaml:79</c>,
    /// <c>Minimum="1"</c>), which is also <c>Math.Max(1, …)</c> at
    /// <c>Services/Video/VideoService.cs:2225</c>.</summary>
    public const int MinPerHour = 1;

    /// <summary>WPF's slider maximum (<c>Features/VideoFeatureControl.xaml:79</c>,
    /// <c>Maximum="20"</c>) and its own named constant
    /// (<c>Models/Program/ProgramDefinition.cs:442</c>, <c>MaxVideosPerHour = 20</c>).</summary>
    public const int MaxPerHour = 20;

    /// <summary>Upstream's <c>Math.Max(60, secs)</c> (<c>VideoService.cs:2229</c>).</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(60);

    /// <summary>Seconds in the hour the dial is denominated in (<c>VideoService.cs:2226</c>).</summary>
    public const double SecondsPerHour = 3600.0;

    /// <summary>The jitter's lower factor: <c>0.8</c> (<c>VideoService.cs:2227</c>).</summary>
    public const double JitterFloor = 0.8;

    /// <summary>The jitter's span: <c>0.4</c>, so the factor runs 0.8..1.2 (<c>:2227</c>).</summary>
    public const double JitterSpan = 0.4;

    /// <summary>
    /// The interval until the next firing.
    /// </summary>
    /// <param name="perHour">The dial. Floored at <see cref="MinPerHour"/> here as well as in the
    /// document's clamp, because upstream floors it at the point of use too
    /// (<c>VideoService.cs:2225</c>) and a preset file is not the only way in.</param>
    /// <param name="roll">A uniform draw in [0,1) — upstream's <c>_random.NextDouble()</c>.</param>
    public static TimeSpan Interval(int perHour, double roll)
    {
        var effective = Math.Max(MinPerHour, perHour);
        var seconds = SecondsPerHour / effective * (JitterFloor + (roll * JitterSpan));
        return TimeSpan.FromSeconds(Math.Max(MinimumInterval.TotalSeconds, seconds));
    }
}
