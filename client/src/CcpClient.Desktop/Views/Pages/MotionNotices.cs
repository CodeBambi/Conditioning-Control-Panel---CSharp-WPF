using CcpClient.Desktop.Motion;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Every sentence the <b>Motion</b> module on the System door says, and the one thing they are all
/// careful about: <b>they describe what this build actually does, not what the enum means
/// upstream.</b>
///
/// <para><b>Why the text lives here rather than in the page.</b> The <c>*Notices.cs</c> convention
/// this port already follows (<see cref="PhraseBackupNotices"/>): a sentence in AXAML or in a
/// code-behind can only be checked by a headless test that mounts a window, while a sentence here
/// is checked by a unit fact. <see cref="Describe"/> takes the OS answer as an argument for the
/// same reason <see cref="HostedMotion.BrowserArgument"/> does — the override sentence is otherwise
/// unprovable on any machine whose animation effects happen to be on.</para>
///
/// <para><b>THE SCOPE CORRECTION, which is why this file exists at all.</b> The module's first
/// sentence used to be "How much movement this app is allowed to show", which is upstream's claim
/// (<c>Localization/Languages/en.json:4106</c> — "How much the interface is allowed to move") and
/// is TRUE upstream, where <c>MotionFx</c> gates the whole interface: hover pops, card breaths,
/// ambient loops, particles, leaderboard glides (<c>Services/MotionFx.cs:12-68</c>). None of that
/// cluster is ported: the port-completeness census measures it at 2,785 lines across 9 files, ZERO
/// animation across every client <c>.axaml</c>, and no consumer for a single one of those helpers.
/// So in THIS build the setting reaches hosted pages and nothing else, and saying otherwise
/// promised a user a calmer interface that no level here can deliver.</para>
///
/// <para><b>And <see cref="MotionLevel.Reduced"/> is said plainly.</b> It is kept, because it is
/// upstream's enum and its ORDINAL is the persisted value, but it currently produces exactly what
/// <see cref="MotionLevel.Full"/> produces: both write the same Chromium switch
/// (<c>Chaos/ChaosWebViewHost.cs:814-817</c>), and the chrome motion Reduced exists to calm is not
/// in this build to calm. A picker that ships a level and says nothing about it lets a user believe
/// they changed something.</para>
/// </summary>
public static class MotionNotices
{
    /// <summary>
    /// The module blurb. Sentence one is SCOPED to hosted pages (see the type remarks); sentence
    /// two says what is untouched; sentence three is the Full/Reduced/Off outcome, which is the
    /// only one of the three a user can act on.
    /// </summary>
    public const string Blurb =
        "How much movement the pages this app hosts are allowed to show. "
        + "It reaches those pages only - this app's own windows and effects are not changed by it. "
        + "Full and Reduced both keep hosted pages in motion; Off is the only setting that stops them.";

    /// <summary>What the page says when the composition root built no motion store, so the choice
    /// cannot be persisted and every hosted surface falls back to
    /// <see cref="MotionLevel.Full"/> (<see cref="HostedMotion.LevelOf"/>).</summary>
    public const string NoStore =
        "This build has no motion settings store, so the choice cannot be saved and hosted "
        + "pages run at Full.";

    /// <summary>
    /// The state line under the picker: what the CURRENT level does, said in the order a user
    /// needs it.
    ///
    /// <para>"The next page you open" is not hedging: the switch is a Chromium command-line
    /// argument fixed when a hosted surface's WebView2 environment is created
    /// (<c>Chaos/ChaosWebViewHost.cs:832-836</c>), so a change cannot reach a window that is
    /// already up.</para>
    /// </summary>
    /// <param name="osClientAreaAnimation">Windows' animation-effects flag, or null where it is
    /// unknown (non-Windows, or a failed GET). Only <c>false</c> can add the override sentence,
    /// and only for a level that is not <see cref="MotionLevel.Off"/>
    /// (<see cref="HostedMotion.OverridesOsPreference"/>).</param>
    public static string Describe(MotionLevel level, bool? osClientAreaAnimation)
    {
        var line = level == MotionLevel.Off
            ? "Pages this app hosts are told to stop moving (" + HostedMotion.ReducedArgument + ")."
            : "Pages this app hosts are told to keep moving (" + HostedMotion.NoReducedArgument + ").";

        if (level == MotionLevel.Reduced)
        {
            // The truth about the level the user just picked, said where they picked it.
            line += " Reduced asks for calmer app chrome, which this build does not animate, "
                + "so today it does exactly what Full does.";
        }

        line += " A change reaches the NEXT hosted page you open, not one already on screen.";

        // The OS disagreement, said where the user can act on it — the same condition the hosted
        // surfaces log (Chaos/ChaosWebViewHost.cs:782-800).
        if (HostedMotion.OverridesOsPreference(level, osClientAreaAnimation))
        {
            line += " Windows animation effects are off on this machine; this setting overrides "
                + "that for hosted pages, which is what stops a session playing as a set of stills.";
        }

        return line;
    }
}
