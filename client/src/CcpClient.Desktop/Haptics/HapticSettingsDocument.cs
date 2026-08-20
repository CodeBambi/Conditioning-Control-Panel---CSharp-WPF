using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// SP-119: the haptic sink's persisted setting — WPF's <c>HapticSettings.Enabled</c>, the master
/// toggle the premium gate guards (<c>MainWindow/MainWindow.Haptics.cs:500</c>, written only after
/// the gate has let the handler past <c>:489-497</c>).
///
/// <para><b>It holds ONE field, and the count is a decision rather than an omission.</b> WPF's
/// haptics object carries an auto-connect flag, two provider URLs, per-provider enables, a
/// twelve-row routing matrix, a master cap, a master intensity, a temperament and a DSP block
/// (<c>Views/Tabs/HapticsTabView.xaml</c>, 1640 lines of surface over
/// <c>Services/Haptics/**</c>'s 9193). <b>Every one of them configures a provider or a mixer, and
/// this build admits neither.</b> Persisting them would write settings nothing reads — the exact
/// debt <c>Persistence/SessionPresetDocument.cs:17-23</c> recorded and SP-117 closed by waiting for
/// the surface that honours them. They arrive with the provider that honours them.</para>
///
/// <para><b>Auto-connect in particular is ABSENT rather than present-and-inert</b> (D93's rule).
/// Upstream's is a real predicate — <c>Settings.Current.Haptics.AutoConnect &amp;&amp;
/// HasRealHapticProviderEnabled()</c> (<c>App.xaml.cs:2103</c>) — and in this port the second
/// conjunct is false for every user for a reason no setting can change, so a checkbox for the first
/// would be a control that decides nothing. The participant's connect predicate is the admitted
/// route list and nothing else.</para>
///
/// <para><b>This document lives in <c>Haptics/</c>, not in <c>Session/</c>, and that is the
/// ownership point made structural.</b> Haptics is APP-scoped upstream — constructed at startup
/// (<c>App.xaml.cs:2060</c>), stopped at exit (<c>:4406</c>), and never engine-started (zero hits
/// for <c>App.Haptics</c> in <c>MainWindow/MainWindow.StartStop.cs</c>) — so its document belongs
/// beside its owner, the way <c>Scheduling/SchedulerPresetDocument.cs</c> does.</para>
/// </summary>
public sealed class HapticSettingsDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "haptics.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The master toggle. <b>False in every file this build writes</b>, because the only path that
    /// sets it runs after <see cref="HapticGate"/> allows and this build's entitlement authority is
    /// unconfigured — but it is persisted rather than computed, because upstream persists it and
    /// because the day the gate opens the user's answer must survive a restart.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model). It matters more here than usual: the settings this document deliberately does not
    /// carry are the ones a later provider packet will add, and a round trip through this build must
    /// not eat them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
