using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake settings document on SP-005 machinery (`intake_settings.json`).
/// The row's "settings keys on SP-005" — a DEDICATED document, not DemoSettings (a
/// self-declared non-feature model, persistence-migration-contract §1 rule 4) and not the
/// Persistence/ folder (out of File Scope). Deviation from WPF (which keeps these keys in
/// settings.json — AppSettings.cs:3040-3106): recorded in record.md; a future settings row
/// can fold the document in. Extension data round-trips unknown members verbatim (§6).
/// </summary>
public sealed class IntakeSettingsDocument
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>WPF AppSettings.IntakeFullscreen (:3040-3050) — the remembered window mode;
    /// the intake window is BUILT in this mode (recovery relaunch is always windowed).</summary>
    public bool Fullscreen { get; set; }

    /// <summary>WPF AppSettings.IntakePassSpentWeek (:3058-3069) — the ISO week key the
    /// pass was spent in ("" = unspent). The STRING is the authority, never a 7-day delta.</summary>
    public string PassSpentWeek { get; set; } = string.Empty;

    /// <summary>WPF AppSettings.IntakePassSpentUtc — when the spend landed (rollback guard).</summary>
    public DateTimeOffset? PassSpentUtc { get; set; }

    /// <summary>WPF AppSettings intake nudge dismissal slot (:3098-3106 family) — the
    /// dashboard cadence is the BLOCKED inventory's item; the KEY rides this document so
    /// the cadence row lands on a real store, not a migration.</summary>
    public string NudgeDismissedWeek { get; set; } = string.Empty;

    /// <summary>WPF default ON (:3098-3106, MainWindow.Settings.cs:452-454).</summary>
    public bool NudgeEnabled { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
