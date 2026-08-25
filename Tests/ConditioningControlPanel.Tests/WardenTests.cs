using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The warden (Services/Possession/Warden.cs) - the companion's half of Possession. Every verb moves a
/// layered window on its own thread, so the choreography itself is play-test territory; what is pinned
/// here is the bookkeeping that made three bugs possible: where "off-frame" is, who owns an in-flight
/// leave, and who owns the tube when a non-warden effect wants to move it.
/// </summary>
public class WardenTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string WardenSource() => Source("Services", "Possession", "Warden.cs");

    [Fact]
    public void Leave_GoesOffTheSCREEN_NotOffTheWorkingArea()
    {
        // WorkingArea EXCLUDES the taskbar, so parking the tube's top-left there left a taskbar-height
        // slice of the companion visible - and topmost - through the whole R4 "she's gone" beat.
        var src = WardenSource();
        var start = src.IndexOf("private async Task LeaveCoreAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "LeaveCoreAsync not found");
        var end = src.IndexOf("//  RETURN", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var body = src[start..end];

        Assert.Contains("screen.Bounds.Bottom", body, StringComparison.Ordinal);
        Assert.DoesNotContain("screen.WorkingArea", body, StringComparison.Ordinal);
        Assert.DoesNotContain("wa.Bottom", body, StringComparison.Ordinal);
        Assert.Contains("clampToWorkArea: false", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Return_CancelsAndDrainsAnInFlightLeave_BeforeItTouchesTheTube()
    {
        // A lockdown ending mid-leave used to strand the tube: ReturnHomeAsync cleared the tube's
        // captured home, then the leave finished and pinned its note in a frame nobody was coming
        // back to, with _hasLeft still true for the next lockdown to bark a phantom return.
        var src = WardenSource();
        var start = src.IndexOf("public async Task ReturnAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "ReturnAsync not found");
        var end = src.IndexOf("//  helpers", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var body = src[start..end];

        var cancel = body.IndexOf("CancelInFlightLeaveAsync", StringComparison.Ordinal);
        var clear = body.IndexOf("ClearNote()", StringComparison.Ordinal);
        var home = body.IndexOf("tube.ReturnHomeAsync(", StringComparison.Ordinal);
        Assert.True(cancel > 0, "ReturnAsync must cancel an in-flight leave");
        Assert.True(cancel < clear && cancel < home,
            "the leave must be drained before the note is cleared and before the homeward glide");

        // Every exit path leaves _hasLeft false.
        Assert.Equal(4, body.Split("_hasLeft = false").Length - 1);
    }

    [Fact]
    public void Leave_RefusesToFinish_OnceAReturnBegan()
    {
        var src = WardenSource();
        var start = src.IndexOf("private async Task LeaveCoreAsync", StringComparison.Ordinal);
        var end = src.IndexOf("//  RETURN", start, StringComparison.Ordinal);
        var body = src[start..end];

        // The epoch check must sit between the glide and the note, and _hasLeft is only true once she
        // is actually off-frame.
        var glide = body.IndexOf("GlideToScreenPointAsync", StringComparison.Ordinal);
        var epoch = body.IndexOf("_returnEpoch) != epoch", StringComparison.Ordinal);
        var hasLeft = body.IndexOf("_hasLeft = true", StringComparison.Ordinal);
        var note = body.IndexOf("LeaveNote()", StringComparison.Ordinal);
        Assert.True(glide > 0 && epoch > glide, "the epoch check must follow the glide");
        Assert.True(hasLeft > epoch && note > epoch, "no note and no _hasLeft once a return began");
    }

    [Fact]
    public void ANewLockdown_ClearsTheCooldownStampsAndTheLeftFlag()
    {
        var src = WardenSource();
        var start = src.IndexOf("public void Reset()", StringComparison.Ordinal);
        Assert.True(start > 0, "Reset() not found");
        var end = src.IndexOf("private void EnsureHooked", start, StringComparison.Ordinal);
        var body = src[start..end];

        Assert.Contains("_lastAppearance = DateTime.MinValue;", body, StringComparison.Ordinal);
        Assert.Contains("_lastStare = DateTime.MinValue;", body, StringComparison.Ordinal);
        Assert.Contains("_hasLeft = false;", body, StringComparison.Ordinal);

        // and it is actually wired to the start of a lockdown
        Assert.Contains("lockdown.LockdownActivated += Reset;", src, StringComparison.Ordinal);
    }

    [Fact]
    public void StealCard_MovesTheTubeOnlyUnderTheWardensLease()
    {
        // The steal card moved the tube with no busy check and no coordination, and its homeward leg
        // (ReturnHomeAsync) clears the tube's captured home - so an undo landing during a knock left a
        // detached tube stranded beside the card.
        var steal = Source("Services", "Possession", "Effects", "StealCardEffect.cs");

        Assert.DoesNotContain("AvailableTube()", steal, StringComparison.Ordinal);
        Assert.Contains("warden.TryTakeTube()", steal, StringComparison.Ordinal);
        Assert.Contains("ReleaseTube()", steal, StringComparison.Ordinal);

        // The lease is given back on every path out of the apply, or the warden never knocks again.
        var apply = steal.IndexOf("protected override async Task ApplyCoreAsync", StringComparison.Ordinal);
        var undo = steal.IndexOf("protected override async Task UndoCoreAsync", StringComparison.Ordinal);
        Assert.True(apply > 0 && undo > apply);
        Assert.Contains("finally", steal[apply..undo], StringComparison.Ordinal);

        // The undo only goes home when the tube is actually free.
        Assert.Contains("if (_flew && TakeTube() != null) SendTubeHome();", steal, StringComparison.Ordinal);

        // ...and the lease the warden hands out is the same flag its own verbs take.
        var warden = WardenSource();
        Assert.Contains("public bool TryTakeTube() => Interlocked.Exchange(ref _busy, 1) == 0;", warden, StringComparison.Ordinal);
        Assert.Contains("public void ReleaseTube() => Interlocked.Exchange(ref _busy, 0);", warden, StringComparison.Ordinal);
    }
}
