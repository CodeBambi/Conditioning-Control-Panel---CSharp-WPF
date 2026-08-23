using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The haptic sink's persisted setting — WPF's <c>HapticSettings.Enabled</c>, the master
/// toggle the premium gate guards (<c>MainWindow/MainWindow.Haptics.cs:500</c>, written only after
/// the gate has let the handler past <c>:489-497</c>).
///
/// <para><b>It used to hold ONE field, and the count was a decision rather than an omission.</b>
/// WPF's haptics object carries an auto-connect flag, two provider URLs, per-provider enables, a
/// twelve-row routing matrix, a master cap, a master intensity, a temperament and a DSP block
/// (<c>Views/Tabs/HapticsTabView.xaml</c>, 1640 lines of surface over
/// <c>Services/Haptics/**</c>'s 9193). Every one of them configures a provider or a mixer, and this
/// build admitted neither, so persisting them would have written settings nothing reads — the exact
/// debt <c>Persistence/SessionPresetDocument.cs:17-23</c> recorded. The rule was that they arrive
/// with the provider that honours them, and <b>the PER-PROVIDER ENABLES have now arrived</b>: both
/// upstream routes have a client here, and <see cref="HapticSinkFactory.AdmittedRoutes"/> carries
/// both. The rest still have no honouring implementation and are still absent.</para>
///
/// <para><b>Auto-connect is still ABSENT rather than present-and-inert</b> (D93's rule).
/// Upstream's is a real predicate — <c>Settings.Current.Haptics.AutoConnect &amp;&amp;
/// HasRealHapticProviderEnabled()</c> (<c>App.xaml.cs:2176</c>) — and this port has no second
/// checkbox to spend on the first conjunct: <see cref="Enabled"/> already IS the switch a user
/// throws, and the participant connects on it. Adding an auto-connect flag would give one behaviour
/// two controls that can disagree.</para>
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

    /// <summary>
    /// Whether the Lovense route participates. <b>One flag per route, because upstream's providers
    /// are a SET and not a choice</b> — its own words, at
    /// <c>Models/HapticSettings.cs:608-609</c>: <i>"the v2 device manager connects every enabled
    /// provider concurrently, so the flags are a SET, not a choice"</i>. The user's surface upstream
    /// is three checkboxes headed <i>"PROVIDERS - enable as many as you like, they connect
    /// together"</i> (<c>Views/Tabs/HapticsTabView.xaml:726</c>,
    /// <c>Localization/Languages/en.json:4113</c>), never a dropdown, and the legacy
    /// <c>HapticProviderType</c> enum (<c>Services/Haptics/IHapticProvider.cs:7-13</c>) survives
    /// only as a back-compat MIRROR of these flags (<c>HapticSettings.cs:604-622</c>).
    /// </summary>
    /// <remarks>
    /// <b>Default FALSE, which is upstream's own stored default</b>: <c>HapticProviderConfig.Enabled</c>
    /// carries no initializer (<c>Models/HapticSettings.cs:769</c>), so a route participates only
    /// because a person ticked it.
    ///
    /// <para><b>An earlier draft defaulted this TRUE and that was the wrong trade.</b> The argument
    /// was that a <c>false</c> default leaves a user with nothing ticked and no surface on which to
    /// tick it — but the repair for a missing checkbox is the checkbox, not a flag switched on for
    /// them. A default-true route flag means a fresh install has consented to a route it was never
    /// shown, and the moment the master switch goes on, a socket opens to a server the user never
    /// named. Both boxes now exist on the haptics panel
    /// (<c>Views/Pages/StudioPage.axaml</c>, <c>HapticsLovenseToggle</c> and
    /// <c>HapticsButtplugToggle</c>), so the nothing-ticked refusal names something a user can
    /// actually do.</para>
    /// </remarks>
    public bool LovenseEnabled { get; set; }

    /// <summary>Whether the Buttplug (Intiface) route participates. See
    /// <see cref="LovenseEnabled"/> for why there is one flag per route and why both default false,
    /// which is upstream's default too (<c>Models/HapticSettings.cs:769</c>).</summary>
    public bool ButtplugEnabled { get; set; }

    /// <summary>
    /// The routes this document has switched on, in upstream's own preference order
    /// (<c>Services/Haptics/Core/HapticDeviceManager.cs:21</c>:
    /// <c>{ "lovense", "buttplug", "mock" }</c>). This is the port of
    /// <c>HapticDeviceManager.EnabledProviders()</c> (<c>:91-98</c>) — the registered routes
    /// filtered by the user's per-route flags — and it is the ONLY thing that decides which routes
    /// a connect touches.
    /// </summary>
    public IReadOnlyList<HapticProviderRoute> EnabledRoutes()
    {
        var routes = new List<HapticProviderRoute>(2);
        if (LovenseEnabled)
        {
            routes.Add(HapticProviderRoute.Lovense);
        }

        if (ButtplugEnabled)
        {
            routes.Add(HapticProviderRoute.Buttplug);
        }

        return routes;
    }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model). It matters more here than usual: the settings this document deliberately does not
    /// carry are the ones a later provider packet will add, and a round trip through this build must
    /// not eat them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
