using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Decision logic for "did the silent update actually land?" (#849).
///
/// Inno Setup is run with /SILENT /SUPPRESSMSGBOXES, so a failed install rolls back and exits
/// non-zero with nothing on screen; the helper then relaunches the OLD build. Before the fix the
/// exit code was logged and ignored, so a rollback was indistinguishable from success and the app
/// re-attempted the same broken update on every launch.
///
/// Only the pure evaluation is covered here - the marker files themselves are plain writes into
/// App.UserDataPath and would need a live App static to exercise.
/// </summary>
public class UpdateAttemptOutcomeTests
{
    [Fact]
    public void SuccessRequiresBothZeroExitAndTheNewVersionRunning()
    {
        Assert.True(UpdateService.DidUpdateSucceed("6.7.4", "6.7.4", 0));
    }

    [Fact]
    public void ZeroExitButStillOnTheOldVersionIsAFailure()
    {
        // The installer can report success while the app it replaced is still the old build
        // (wrong /DIR, partial rollback). Trusting the exit code alone re-creates the silent loop.
        Assert.False(UpdateService.DidUpdateSucceed("6.7.4", "6.7.3", 0));
    }

    [Theory]
    [InlineData(1)]   // setup failed
    [InlineData(2)]   // user cancelled
    [InlineData(5)]   // rollback / other Inno failure
    public void AnyNonZeroExitCodeIsAFailure(int exitCode)
    {
        Assert.False(UpdateService.DidUpdateSucceed("6.7.4", "6.7.4", exitCode));
    }

    [Fact]
    public void MissingExitCodeFallsBackToTheVersionComparison()
    {
        // The helper writes its result file into the user's LOCALAPPDATA; if that write was lost
        // (e.g. the helper ran elevated as a different account) the running version is all we have.
        Assert.True(UpdateService.DidUpdateSucceed("6.7.4", "6.7.4", UpdateService.UnknownExitCode));
        Assert.False(UpdateService.DidUpdateSucceed("6.7.4", "6.7.3", UpdateService.UnknownExitCode));
    }

    [Fact]
    public void OvershootingTheAttemptedVersionStillCountsAsSuccess()
    {
        // A user who manually installed something newer must not be nagged about the old attempt.
        Assert.True(UpdateService.DidUpdateSucceed("6.7.4", "6.8.0", 0));
    }

    [Fact]
    public void UnparseableVersionsFallBackToTheExitCode()
    {
        Assert.True(UpdateService.DidUpdateSucceed("not-a-version", "6.7.3", 0));
        Assert.False(UpdateService.DidUpdateSucceed("not-a-version", "6.7.3", 1));
    }
}
