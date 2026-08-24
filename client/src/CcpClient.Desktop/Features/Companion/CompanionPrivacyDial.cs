namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// The three stops of the awareness privacy dial (audit row A3, ADOPT — copy and presentation).
/// Upstream's "three privacy levels" are not an enum anywhere in its own tree: they are DERIVED
/// from a capability switch and a breadth switch, and this port derives them the same way from the
/// two states it has (awareness consent, and the F4 title allow-list's size).
/// </summary>
public enum CompanionPrivacyStop
{
    /// <summary>Awareness consent not given. Nothing is observed.</summary>
    Off,

    /// <summary>Consent given with an EMPTY title allow-list: category, app and duration, never a page title. The shipped default the moment consent is given.</summary>
    AppNamesOnly,

    /// <summary>Consent given with at least one app named. Only those apps' titles travel.</summary>
    PlusPageTitles,
}

/// <summary>
/// The one place that turns the port's landed privacy state into the sentence describing it
/// (audit row A3; the WPF file with the same job is Views/Controls/Companion/AwarenessDialCopy.cs,
/// whose own remark is "The one place that turns a dial stop into the sentence describing it …
/// so the state gallery cannot drift from the live card").
///
/// <para><b>Why the dial is derived and not stored.</b> Upstream has no `PrivacyLevel` type; the
/// stops fall out of the state (`AwarenessPrivacyRuntimeVm.cs:332-336`: off when awareness is off,
/// else Everything when the title allow-list has entries, else BroadStrokes). A stored level would
/// be a second dialect of the same fact and could disagree with what the filter actually does —
/// and a dial that says more than the code does is the failure this row exists to prevent.</para>
///
/// <para><b>The third stop is earned, not pressed.</b> Selecting "+ Page titles" enables awareness
/// and OPENS the per-app editor; it never widens anything by itself, and the dial keeps reporting
/// the middle stop until an app is actually named (WPF `AwarenessPrivacyRuntimeVm.cs:106-113` and
/// the reason at `:24-27`: "the dial only reports 'Everything' once an app is actually listed — a
/// stop that silently meant nothing would be the privacy failure that looks like a working
/// feature"). Selecting the MIDDLE stop empties the allow-list, because that stop is a promise
/// that no page title travels (`:97-101`).</para>
///
/// <para><b>Copy is upstream's, verbatim</b> (Localization/Languages/en.json — the audit cited
/// :4325-4327 for the hints and the file has since moved them to :4435-4437; the strings are
/// unchanged and the line numbers here are the ones in the tree). Labels :4432, :4433, :4450;
/// head :4434; hints :4435-4437. Lowercase and the ◔/◉ glyphs are upstream's own voice, not a
/// transcription slip.</para>
///
/// <para><b>Honest limit.</b> The mapping is presentational over the port's SESSION-scoped consent
/// (audit row A5 / owner question Q5 — whether consent persists across restarts and how granular
/// it is remains the owner's call). The dial reports exactly what the landed filters do today and
/// claims nothing about tomorrow's default.</para>
/// </summary>
public static class CompanionPrivacyDial
{
    /// <summary>The strip's heading (en.json:4434).</summary>
    public const string Head = "what leaves your PC";

    /// <summary>
    /// The stop the current state IS, derived exactly as upstream derives it
    /// (`AwarenessPrivacyRuntimeVm.cs:332-336`).
    /// </summary>
    public static CompanionPrivacyStop Derive(bool consentGranted, int namedAppCount) =>
        !consentGranted
            ? CompanionPrivacyStop.Off
            : namedAppCount > 0
                ? CompanionPrivacyStop.PlusPageTitles
                : CompanionPrivacyStop.AppNamesOnly;

    /// <summary>The segment label (en.json:4450, :4432, :4433).</summary>
    public static string LabelFor(CompanionPrivacyStop stop) => stop switch
    {
        CompanionPrivacyStop.Off => "─ Off",
        CompanionPrivacyStop.AppNamesOnly => "◔ App names only",
        _ => "◉ + Page titles",
    };

    /// <summary>
    /// What the selected stop does, in one line (en.json:4435-4437; WPF `AwarenessDialCopy.HintFor`,
    /// :29-34, which selects over the same three stops in the same order).
    /// </summary>
    public static string HintFor(CompanionPrivacyStop stop) => stop switch
    {
        CompanionPrivacyStop.Off => "her eyes are closed. nothing is watched, nothing is counted.",
        CompanionPrivacyStop.AppNamesOnly => "the category, the app name and a rounded time. never a page title.",
        _ => "app names, plus page titles for the apps you name yourself.",
    };
}
