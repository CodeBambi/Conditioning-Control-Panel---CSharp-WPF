using System.Globalization;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>Visuals</b> panel's sentences.
///
/// <para><b>This panel has no live-state line and no dot, and that is the row's whole finding.</b>
/// Every other module's panel opens with what its dot means right now. Visuals has no dot — its
/// rack entry passes <c>null</c> where every other row passes a dot predicate
/// (<c>Views/Tabs/StudioTabView.xaml.cs:496</c>), because it has no master toggle to read and
/// upstream's own comment says a dot that cannot be wired honestly is omitted (<c>:494-495</c>).
/// Inventing one here would mean either duplicating the Flash Images row's dot two inches to its
/// left, or lighting a state for a row that has no state.</para>
///
/// <para>What it says instead is <b>whose</b> dials these are, <b>when</b> a change takes effect,
/// and <b>which two upstream controls are missing and why</b> — the D93 declaration, made loudly on
/// the page rather than buried in a record.</para>
/// </summary>
public static class VisualsPanelNotices
{
    /// <summary>
    /// The lead line: what this page is. It names Flash Images because the numbers belong to that
    /// module and a user who switched Flash Images off must not be left wondering why moving these
    /// sliders changes nothing.
    /// </summary>
    /// <param name="flashEnabled">Whether the Flash Images module's own dial is on.</param>
    public static string DescribeOwnership(bool flashEnabled) =>
        flashEnabled
            ? "These are the Flash Images module's own dials: how big its pictures are, how solid "
                + "they look, and how long they stay. They have no on/off of their own."
            : "These are the Flash Images module's own dials — and Flash Images is switched off, so "
                + "nothing here will change anything until you switch it back on.";

    /// <summary>
    /// The three dials, read back as a sentence, plus when a change lands.
    ///
    /// <para><b>"The next flash", not "now", and it is a real difference.</b> Upstream re-reads its
    /// opacity on every composition frame and re-tints pictures already on screen
    /// (<c>Services/Flash/FlashService.cs:2072</c>, applied <c>:2108-2117</c>). This port sets a
    /// layered window's alpha once, at placement (D174). Saying "now" would be false for the six
    /// seconds a flash is already up.</para>
    /// </summary>
    public static string DescribeDials(FlashDraw draw) =>
        $"Size {Percent(draw.ScalePercent)}, opacity {Percent(draw.OpacityPercent)}, on screen for "
        + $"{Seconds(draw.DurationSeconds)}. A change here reaches the NEXT flash, not one that is "
        + "already up.";

    /// <summary>
    /// The absence line — the two controls upstream's page has that this one does not, each with
    /// the reason a user can act on.
    ///
    /// <para>It is on the PAGE rather than only in a record, on the precedent SP-111, SP-113 and
    /// SP-115 set for a half-ported row: an absence the user can see is an absence the user was
    /// told about.</para>
    /// </summary>
    public static string DescribeAbsences() =>
        "Two controls the Windows app shows here are deliberately not here. Its “Fade” slider is "
        + "wired to nothing in that build either — the fade speed is a fixed constant — so a slider "
        + "for it would move nothing. Its “Link to audio” switch makes a flash last as long as its "
        + "sound, and flashes are silent in this build.";

    /// <summary>
    /// Where the pictures these dials describe actually went — the surface line every drawing
    /// module's panel ends with, quoting the overlay capability's own last answer verbatim.
    ///
    /// <para>Null means no flash has been drawn yet in this run, which is not a refusal: it is the
    /// honest "nothing has been attempted" of <see cref="Effects.IFlashSurface.LastPlacement"/>.</para>
    /// </summary>
    public static string DescribeSurface(CapabilityState? placement) => placement switch
    {
        null => "No flash has been drawn yet this run, so the screen has not been asked for "
            + "anything and there is nothing to report.",
        CapabilityState.Available => "The operating system confirmed the last flash was placed at "
            + "the size and opacity above.",
        CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}",
        CapabilityState.Unavailable u =>
            $"These dials describe pictures this build cannot put on a screen: {u.Reason.Detail}",
        CapabilityState.PermissionRequired p =>
            $"These dials describe pictures this build cannot put on a screen: {p.Reason.Detail}",
        CapabilityState.DependencyMissing m =>
            $"These dials describe pictures this build cannot put on a screen: {m.Reason.Detail}",
        CapabilityState.Faulted f =>
            $"These dials describe pictures this build cannot put on a screen: {f.Reason.Detail}",
        // The hierarchy is closed (CapabilityState's constructor is private), so this arm is
        // unreachable today; it is the codebase's own convention for these switches and it prints
        // the state rather than inventing a sentence.
        _ => placement.ToString() ?? string.Empty,
    };

    private static string Percent(int value) =>
        value.ToString(CultureInfo.CurrentCulture) + "%";

    private static string Seconds(int value) =>
        value == 1 ? "1 second" : value.ToString(CultureInfo.CurrentCulture) + " seconds";
}
