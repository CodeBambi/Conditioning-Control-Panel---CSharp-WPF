using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Scheduling;

/// <summary>
/// SP-118: the <b>Scheduler</b> row's persisted settings — WPF's nine, and exactly its nine.
///
/// <para>The shipping editor is <c>Features/SchedulerFeatureControl.xaml</c>, hosted by the Studio
/// rack at <c>Views/Controls/Studio/SchedulerRackPanel.xaml:92</c>, and it carries <b>an enable, a
/// start time, an end time and seven day boxes</b> — nine controls, no more
/// (<c>SchedulerFeatureControl.xaml.cs:41-51</c> loads exactly those nine). The other four
/// <c>Scheduler*</c> members on <c>AppSettings</c> (<c>SchedulerDurationMinutes</c>,
/// <c>SchedulerMultiplier</c>, <c>SchedulerLinkAlpha</c>,
/// <c>CCP.Core/Models/AppSettings.cs:2461-2478</c>) belong to the intensity RAMP and appear on no
/// scheduler surface at all, so they are not here.</para>
///
/// <para><b>The defaults are the MODEL's, not the markup's.</b> A fresh install has
/// <c>SchedulerStartTime = "00:00"</c> and <c>SchedulerEndTime = "22:00"</c>
/// (<c>AppSettings.cs:2510-2521</c>) with every day ticked (<c>:2525-2569</c>) and the feature OFF
/// (<c>:2453</c>). The <c>16:00</c>/<c>22:00</c> literals in <c>SchedulerFeatureControl.xaml:47,56</c>
/// are design-time text that <c>LoadFromSettings</c> overwrites from settings on <c>Loaded</c>
/// (<c>.xaml.cs:42-43</c>), so no user ever sees them as values.</para>
///
/// <para><b>The times are STRINGS, deliberately, and that is behaviour rather than laziness.</b>
/// Upstream stores what the user typed and parses it at every evaluation
/// (<c>MainWindow/MainWindow.StartStop.cs:667-677</c>), with a fallback when the parse fails. A
/// document that stored a <see cref="TimeSpan"/> would have to reject bad text at the point of
/// entry, which would delete the fallback branch and with it one of this module's refusals —
/// the one where a user's typo silently moves the window to 16:00-22:00.</para>
///
/// <para>One document per module, on the precedent SP-101 set and SP-105 restated (divergence
/// D71): <c>Persistence/**</c> is outside this packet's File Scope, and the substantive half of
/// that argument is unchanged — the store's Degraded load path takes the WHOLE document to
/// defaults, so one hand-broken value in a shared file would reset every other module's dials.</para>
/// </summary>
public sealed class SchedulerPresetDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "session_scheduler.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF default (<c>CCP.Core/Models/AppSettings.cs:2453</c>): the scheduler ships OFF.
    /// A feature that can start a conditioning session by itself does not ship armed.</summary>
    public const bool DefaultEnabled = false;

    /// <summary>WPF's <c>_schedulerStartTime</c> default AND its setter's null-coalesce
    /// (<c>AppSettings.cs:2510,2516</c>).</summary>
    public const string DefaultStartTime = "00:00";

    /// <summary>WPF's <c>_schedulerEndTime</c> default AND its setter's null-coalesce
    /// (<c>AppSettings.cs:2517,2523</c>).</summary>
    public const string DefaultEndTime = "22:00";

    private string _startTime = DefaultStartTime;
    private string _endTime = DefaultEndTime;

    /// <summary>WPF's <c>SchedulerEnabled</c> (<c>AppSettings.cs:2454-2458</c>) — the first thing
    /// the tick reads and the reason it usually does nothing
    /// (<c>MainWindow.StartStop.cs:604</c>).</summary>
    public bool Enabled { get; set; } = DefaultEnabled;

    /// <summary>
    /// WPF's <c>SchedulerStartTime</c> (<c>AppSettings.cs:2511-2516</c>), kept as typed text. The
    /// null-coalesce is WPF's own (<c>:2515</c>) and it is the reason a <c>null</c> can never
    /// reach <see cref="TimeSpan.TryParse(string, out TimeSpan)"/>: only a non-null unparseable
    /// string can, which is the only way the 16:00 fallback is ever reachable.
    /// </summary>
    [AllowNull]
    public string StartTime
    {
        get => _startTime;
        set => _startTime = value ?? DefaultStartTime;
    }

    /// <summary>WPF's <c>SchedulerEndTime</c> (<c>AppSettings.cs:2518-2523</c>), same shape, same
    /// null-coalesce (<c>:2522</c>).</summary>
    [AllowNull]
    public string EndTime
    {
        get => _endTime;
        set => _endTime = value ?? DefaultEndTime;
    }

    /// <summary>WPF's <c>SchedulerMonday</c> (<c>AppSettings.cs:2526-2531</c>), default true.</summary>
    public bool Monday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerTuesday</c> (<c>AppSettings.cs:2533-2538</c>), default true.</summary>
    public bool Tuesday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerWednesday</c> (<c>AppSettings.cs:2540-2545</c>), default true.</summary>
    public bool Wednesday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerThursday</c> (<c>AppSettings.cs:2547-2552</c>), default true.</summary>
    public bool Thursday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerFriday</c> (<c>AppSettings.cs:2554-2559</c>), default true.</summary>
    public bool Friday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerSaturday</c> (<c>AppSettings.cs:2561-2566</c>), default true.</summary>
    public bool Saturday { get; set; } = true;

    /// <summary>WPF's <c>SchedulerSunday</c> (<c>AppSettings.cs:2568-2573</c>), default true.</summary>
    public bool Sunday { get; set; } = true;

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
