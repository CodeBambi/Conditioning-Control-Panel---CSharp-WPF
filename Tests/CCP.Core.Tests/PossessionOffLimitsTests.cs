using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PORTED from the WPF PossessionOffLimitsTests (7.1.5), the name half: the rooms the user
/// must always be able to leave are never possessable. The visual-tree half needs a WPF tree.</summary>
public sealed class PossessionOffLimitsTests
{
    [Theory]
    [InlineData("LockdownCardBorder", true)]
    [InlineData("BtnEmergencyExit", true)]
    [InlineData("TxtSecretExit", true)]
    [InlineData("txtlockdownexit", true)]
    [InlineData("BtnStart", false)]
    [InlineData("TxtPossessionRung", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReservedNamesAreOffLimits(string? name, bool reserved) =>
        Assert.Equal(reserved, PossessionOffLimits.IsReservedName(name));

    /// <summary>The owner's hard limits (10 Oct 2026): each SAFETY control is named here and is never
    /// possessable, tagged or not. The controls WPF tags that are not safety controls stay reachable.</summary>
    [Theory]
    [InlineData("BtnEmergencyExit", true)]
    [InlineData("TxtEmergencyExitNotice", true)]
    [InlineData("TxtLockdownExit", true)]
    [InlineData("TxtSecretExit", true)]
    [InlineData("BtnPanic", true)]
    [InlineData("ChkLockdownNoPanic", true)]
    [InlineData("ChkLockdownStrict", true)]
    [InlineData("BtnStopAll", true)]
    [InlineData("BtnPauseSession", true)]
    [InlineData("BtnLeashCut", true)]
    [InlineData("TrayStopEverything", true)]
    [InlineData("BtnCancel", true)]
    [InlineData("BtnClose", true)]
    [InlineData("ConfirmDialogYes", true)]
    [InlineData("BtnSafeword", true)]
    [InlineData("ChkPossessionEnabled", true)]
    [InlineData("ChkPossPhotosafe", true)]
    [InlineData("DoorHome", false)]
    [InlineData("BtnStart", false)]
    [InlineData("ChkLockdownHideTimer", false)]
    [InlineData("ChkLockdownDose", false)]
    [InlineData("TxtLockdownTimer", false)]
    [InlineData("LockdownCardBorder", false)]
    [InlineData(null, false)]
    public void SafetyNamesAreNeverPossessable(string? name, bool safety) =>
        Assert.Equal(safety, PossessionOffLimits.IsSafetyName(name));

    [Theory]
    [InlineData("BtnStart", true)]
    [InlineData("btnstartsession", true)]
    [InlineData("DoorHome", false)]
    [InlineData(null, false)]
    public void AStartControlIsNamedSoItCanBecomeAStop(string? name, bool startStop) =>
        Assert.Equal(startStop, PossessionOffLimits.IsStartStopName(name));
}
