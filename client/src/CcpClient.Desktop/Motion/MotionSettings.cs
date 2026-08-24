using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Motion;

/// <summary>
/// How much motion the user has asked for. Upstream's enum, in upstream's order
/// (<c>ConditioningControlPanel/Models/AppSettings.cs:70-75</c>), and the order is load-bearing
/// twice over: it is the persisted ordinal (upstream persists the same enum by value,
/// <c>AppSettings.cs:3961-3966</c>) and it is the picker's item order
/// (<c>Views/Controls/AppSettings/PerformanceSettingsSection.xaml:164</c> — "The three items ARE
/// the enum ordinals — never reorder").
/// </summary>
public enum MotionLevel
{
    /// <summary>Everything moves. Upstream's default (<c>AppSettings.cs:3954</c>).</summary>
    Full = 0,

    /// <summary>Calmer CHROME. Upstream keeps crossfades and state transitions and drops ambient
    /// loops, particles and parallax (<c>AppSettings.cs:64-66</c>). It is NOT "stop": a user who
    /// asked for calmer chrome did not ask for a session that never moves
    /// (<c>Chaos/ChaosWebViewHost.cs:809-811</c>).
    ///
    /// <para><b>In THIS build it is Full's twin, and the page says so</b>
    /// (<see cref="Views.Pages.MotionNotices"/>): the chrome it exists to calm is upstream's <c>MotionFx</c> cluster — ambient loops,
    /// particles, hover pops, card breaths (<c>Services/MotionFx.cs:12-68</c>) — and none of that
    /// is ported. The level is kept anyway because its ORDINAL is the persisted value and dropping
    /// it would renumber Off.</para></summary>
    Reduced = 1,

    /// <summary>No animation at all — the explicit, in-app kill switch, and the ONE level that is
    /// forwarded to a hosted page (<c>Chaos/ChaosWebViewHost.cs:777-780</c>).</summary>
    Off = 2,
}

/// <summary>
/// The motion preference's persisted document — upstream's <c>AppSettings.MotionLevel</c>
/// (<c>ConditioningControlPanel/Models/AppSettings.cs:3953-3966</c>) in this port's own
/// persistence shape (schema version + unknown-member preservation, persistence contract §1/§6;
/// the shape is <c>Haptics/HapticSettingsDocument.cs</c>'s).
///
/// <para><b>Its own file rather than a member of the demo settings</b>, on the per-module
/// precedent the preset documents already set (<c>Session/IntensityRampPresetDocument.cs:10-14</c>):
/// the store's Degraded load path takes the WHOLE document to defaults, so one hand-broken value
/// elsewhere must not silently restore motion for a user who switched it off. <c>DemoSettings</c>
/// was not an option either way — it says in its own summary that it is not a feature model.</para>
///
/// <para><b>It holds one member because upstream's motion preference is one member.</b> Upstream's
/// neighbouring performance dials (performance tier, auto-performance) are a different setting with
/// a different consumer and are not in this row's scope.</para>
/// </summary>
public sealed class MotionSettingsDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "motion.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary><b>Full on a fresh install</b>, which is upstream's default
    /// (<c>AppSettings.cs:3954</c>) and the owner decision for this row: the app's own setting
    /// governs a hosted page from first run, so no user silently loses motion to an OS checkbox
    /// they ticked for Explorer.</summary>
    public MotionLevel Level { get; set; } = MotionLevel.Full;

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// WHO ANSWERS <c>prefers-reduced-motion</c> FOR A PAGE THIS APP HOSTS — the single place the
/// answer is decided, and the reason it is single.
///
/// <para><b>What breaks without it.</b> Blink maps that media query on Windows to
/// <c>SPI_GETCLIENTAREAANIMATION</c> — the "Animation effects" checkbox in Windows Settings — so a
/// user with that checkbox off gets every hosted page's reduced-motion path: frozen canvas clocks,
/// posters that never advance, a session that plays as a set of stills. Upstream diagnosed this
/// and wrote the fix down (<c>ConditioningControlPanel/Chaos/ChaosWebViewHost.cs:766-813</c>,
/// ccp-bugs #980).</para>
///
/// <para><b>Why the OS answer is the wrong one here.</b> In this app the reduced-motion gate
/// governs CHROME, never CONTENT — a page in one of these hosts is the content the user asked for,
/// and a browser engine cannot tell the two apart, so the host answers for it
/// (<c>ChaosWebViewHost.cs:781-791</c>). The one preference that IS forwarded is the user's own
/// explicit <see cref="MotionLevel.Off"/> (<c>:793-796</c>).</para>
///
/// <para><b>Five hosted surfaces, one answer.</b> The port has five WebView2 environment sites
/// (DTRH, the Loom, Goon, Intake, the Chaos tunnel) where upstream had one host class; five copies
/// of this rule would be five chances to drift. <c>MotionArgumentChokePointGuardTests</c> keeps the
/// two switch literals inside this file mechanically.</para>
///
/// <para><b>Windows only, and named as such.</b> The switch is a Chromium command-line argument and
/// reaches only the WebView2 branch of each site's environment-requested handler; WebKitGTK (the
/// Linux surface) takes base directories and no Chromium switches
/// (<c>Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs</c>). Linux hosted motion is
/// unproven and is not claimed here.</para>
///
/// <para>An older Chromium that does not know the switch ignores it, which is the pre-fix
/// behaviour — there is no version floor to guard (<c>ChaosWebViewHost.cs:800-801</c>).</para>
/// </summary>
public static class HostedMotion
{
    /// <summary>Chromium's force-on switch. Written ONLY for <see cref="MotionLevel.Off"/>.</summary>
    public const string ReducedArgument = "--force-prefers-reduced-motion";

    /// <summary>Chromium's force-off switch. Written for Full AND Reduced — see
    /// <see cref="MotionLevel.Reduced"/>.</summary>
    public const string NoReducedArgument = "--force-prefers-no-reduced-motion";

    /// <summary>
    /// The pure rule (<c>ChaosWebViewHost.cs:807-817</c>). <b>Note what is NOT a parameter: the OS
    /// animation flag.</b> The argument is chosen by the user's setting and by nothing else; the OS
    /// can only ever produce a LOG LINE here, never a different answer.
    /// </summary>
    public static string PrefersReducedMotionArgument(MotionLevel level) =>
        level == MotionLevel.Off ? ReducedArgument : NoReducedArgument;

    /// <summary>
    /// The user's current level. Null host, or a host whose participant list carries no motion
    /// store, resolves to <see cref="MotionLevel.Full"/> — the same default upstream falls back to
    /// when settings are not up yet (<c>ChaosWebViewHost.cs:768-770</c>), and the same value a
    /// fresh <see cref="MotionSettingsDocument"/> holds.
    /// </summary>
    public static MotionLevel LevelOf(ApplicationHost? host) =>
        StoreOf(host)?.Current.Level ?? MotionLevel.Full;

    /// <summary>The motion store the composition root registered, or null in a host built without
    /// one (owner-less test hosts, custom participant factories).</summary>
    public static PersistenceStore<MotionSettingsDocument>? StoreOf(ApplicationHost? host) =>
        host?.Participants.OfType<PersistenceStore<MotionSettingsDocument>>().FirstOrDefault();

    /// <summary>
    /// The pure half of the breadcrumb rule, split out the same way upstream splits the argument
    /// itself so it can be tested without a machine in a particular state
    /// (<c>ChaosWebViewHost.cs:806-808</c>). True only when this app has actually overruled the OS:
    /// the animation flag reads OFF and the user did not ask for Off. An unknown OS state
    /// (non-Windows, or a failed GET — <see cref="DtrhMotionPreference.ReadOsClientAreaAnimation"/>
    /// returns null there) is NOT a disagreement.
    /// </summary>
    public static bool OverridesOsPreference(MotionLevel level, bool? osClientAreaAnimation) =>
        osClientAreaAnimation == false && level != MotionLevel.Off;

    /// <summary>
    /// The one call every hosted surface makes: resolve the user's level, leave the breadcrumb when
    /// this app has just overruled the OS, and return the switch to append to that surface's
    /// browser arguments.
    ///
    /// <para>The breadcrumb is written ONLY when the override actually changed something — the OS
    /// flag was off and the user's setting overruled it — so it is silent for everyone whose Windows
    /// animations are on, and its presence in a diagnostic log is the confirmation that this machine
    /// is one of the ones the report was about (<c>ChaosWebViewHost.cs:782-800</c>). The OS read is
    /// this port's existing one and can answer "unknown" (non-Windows, or a failed GET), which is
    /// not a disagreement and logs nothing.</para>
    /// </summary>
    /// <param name="readOsClientAreaAnimation">
    /// The OS animation read, injectable and defaulting to the real one. <b>It is a seam because
    /// this method's rule is otherwise only half-checkable</b>: on a machine whose animation
    /// effects are ON the real read answers <c>true</c>, so an edit that let the OS choose the
    /// argument (the exact defect this row exists to close) would change nothing that any test on
    /// that machine could see. With the read supplied, the argument is pinned against every OS
    /// answer on any machine. The OS still cannot reach the returned argument through it — it is
    /// passed to <see cref="OverridesOsPreference"/>, which decides only whether to LOG.
    /// </param>
    public static string BrowserArgument(
        ApplicationHost? host,
        string logTag,
        Action<string> log,
        Func<bool?>? readOsClientAreaAnimation = null)
    {
        var level = LevelOf(host);
        var argument = PrefersReducedMotionArgument(level);

        var os = (readOsClientAreaAnimation ?? DtrhMotionPreference.ReadOsClientAreaAnimation)();
        if (OverridesOsPreference(level, os))
        {
            log($"{logTag}: Windows animation effects are OFF; hosted page is kept in motion anyway "
                + $"({argument}) — the app's Motion setting ({level}) governs");
        }

        return argument;
    }
}
