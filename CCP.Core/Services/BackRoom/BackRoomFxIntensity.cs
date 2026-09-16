namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>Setting <c>AppSettings.BackRoomFxIntensity</c> (CONTRACT section 4). Default
/// <see cref="Normal"/>. <see cref="Calm"/> is also forced whenever MotionLevel is not Full.
/// Full never breaks the Brake: no strobe over 6 Hz, one hero at a time, toggles still win.
///
/// <para>Split out of <c>BackRoomContracts.cs</c> (which stays in the WPF head) because
/// <see cref="Models.AppSettings"/> persists this value and settings live in Core. The rest of
/// the Back Room contract surface reaches head-only types (BackRoomFxArgs/BackRoomFxPlan/Diag),
/// so only the stored enum crosses; the namespace is unchanged, so no caller moves.</para>
/// </summary>
public enum BackRoomFxIntensity
{
    Calm,
    Normal,
    Full,
}
