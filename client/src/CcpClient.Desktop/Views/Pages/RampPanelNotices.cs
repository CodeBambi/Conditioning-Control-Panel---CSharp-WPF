using System.Globalization;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>What the Intensity Ramp is holding for one dial: whose it is, what it was when the ramp
/// took it, and what it is now. The middle number is the one the user gets back.</summary>
/// <param name="Label">The dial's display name, e.g. "Spiral Overlay opacity".</param>
/// <param name="BaseValue">What the ramp took custody of and will restore.</param>
/// <param name="CurrentValue">Where the ramp has driven it to.</param>
public sealed record RampDialHold(string Label, int BaseValue, int CurrentValue);

/// <summary>
/// The Intensity Ramp panel's two sentences.
///
/// <para><b>Why they are in their own file (SP-108).</b> SP-105 deferred extracting
/// <c>StudioPage.axaml.cs</c>'s <c>Describe*</c> families and SP-106 §1.4 said four real bodies had
/// made the extraction justified. Refactoring the file that carries all four landed modules' rendered
/// claims is not this packet's risk to take, so the fifth module's text starts where the extraction
/// would put it instead of adding a fifth family to the file that will not scale.</para>
///
/// <para><b>There is no <c>DescribeSurface</c> here and there never will be.</b> Every other module's
/// panel ends with a line about where its pixels went. This module has no pixels: what it reports
/// instead is <b>custody</b> — which dials it is holding, what they were, and that they are going
/// back.</para>
/// </summary>
public static class RampPanelNotices
{
    /// <summary>
    /// The live-state line, off the row's own dot plus the numbers behind it.
    ///
    /// <para><b>Every arm here describes a state the module can really be in</b>, and the two that
    /// look alike are deliberately not: a ramp that is <see cref="EffectDotState.Armed"/> during a
    /// running session is holding nothing (nothing is linked), while a ramp that is
    /// <see cref="EffectDotState.Live"/> at 1.0× is holding real dials at neutral gain. The first
    /// will never do anything; the second starts climbing the moment the multiplier moves.</para>
    /// </summary>
    public static string DescribeRampState(
        EffectDotState dot,
        IntensityRampPresetDocument preset,
        double progress,
        double currentMultiplier,
        int heldCount,
        bool sessionRunning)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var anyLink = preset.LinkSpiralOpacity || preset.LinkPinkFilterOpacity;

        if (dot == EffectDotState.Off)
        {
            return "Switched off. Nothing is ramped, session or no session.";
        }

        if (dot == EffectDotState.Armed && !sessionRunning)
        {
            return anyLink
                ? $"Ready. When a session starts, the effects you have linked climb to "
                    + $"{Fixed(preset.Multiplier)}× over {preset.DurationMinutes} minutes."
                : "Ready — but nothing is linked to it yet, so it would run for the whole session and "
                    + "change nothing. Tick an effect under “Link to ramp”.";
        }

        if (dot == EffectDotState.Armed)
        {
            return "Running, and holding nothing: no effect is linked to the ramp, so nothing will "
                + "climb and nothing has to be put back.";
        }

        if (preset.Multiplier <= IntensityRampPresetDocument.MinMultiplier)
        {
            return $"Running: it is holding {Dials(heldCount)}, but the multiplier is at "
                + $"{Fixed(preset.Multiplier)}×, so nothing climbs until you move it. What it holds "
                + "still goes back when the session stops.";
        }

        if (progress >= 1.0)
        {
            return $"Finished: the climb reached {Fixed(preset.Multiplier)}× and the "
                + $"{Dials(heldCount)} it holds stay there until you stop. They go back to where you "
                + "left them then.";
        }

        return $"Running: {Percent(progress)} through the climb, currently at "
            + $"{Fixed(currentMultiplier)}× of {Fixed(preset.Multiplier)}×, holding {Dials(heldCount)}.";
    }

    /// <summary>
    /// The custody line — the one this module has instead of a surface line.
    ///
    /// <para>It names each dial it has borrowed, what that dial was, and where it has been driven to,
    /// because "your spiral opacity says 27 and you set it to 10" is exactly the question a user
    /// looking at another module's panel mid-session will have, and the answer must not be "the ramp,
    /// probably".</para>
    /// </summary>
    public static string DescribeRampCustody(int dialsInBuild, IReadOnlyList<RampDialHold> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        if (dialsInBuild == 0)
        {
            return "This build gives the ramp no dials to drive, so there is nothing for it to do.";
        }

        if (held.Count == 0)
        {
            return "Holding nothing right now. The ramp only borrows a dial once a session has "
                + "started with that effect linked.";
        }

        var parts = held.Select(h => $"{h.Label} {h.BaseValue}% → {h.CurrentValue}%");
        return $"Holding {string.Join("; ", parts)}. Every one of them goes back to the first number "
            + "when the session stops.";
    }

    private static string Dials(int count) =>
        count == 1 ? "1 dial" : $"{count.ToString(CultureInfo.CurrentCulture)} dials";

    private static string Fixed(double value) => value.ToString("0.0", CultureInfo.CurrentCulture);

    private static string Percent(double progress) =>
        ((int)Math.Round(Math.Clamp(progress, 0.0, 1.0) * 100)).ToString(CultureInfo.CurrentCulture) + "%";
}
