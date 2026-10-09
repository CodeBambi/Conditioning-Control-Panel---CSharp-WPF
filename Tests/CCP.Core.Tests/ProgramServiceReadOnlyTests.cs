using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// programs.json against a golden file in the shape WPF's ProgramService writes (default
/// System.Text.Json options, WriteIndented, Local-kind dates under TZ=Europe/Berlin), and the
/// load-only mode the Avalonia head runs until it can run a program's days
/// (docs/avalonia-decisions.md 2026-10-09, programs CHECKPOINT A). The golden was written by the
/// shared Core Save (the code WPF runs) under TZ=Europe/Berlin, not captured on Windows; regenerate
/// it the same way when a persisted field is added on purpose.
/// </summary>
public sealed class ProgramServiceReadOnlyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-programs-").FullName;
    private string StatePath => Path.Combine(_dir, "programs.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    private string[] Snapshot() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Fact]
    public void WpfFileRoundTripsByteForByte() => QuestPersistenceTests.InBerlin(() =>
    {
        var golden = File.ReadAllBytes(Fixture("programs_wpf.json"));
        File.WriteAllBytes(StatePath, golden);

        using (var svc = new ProgramService(StatePath, readOnly: false))
        {
            Assert.Equal(3, svc.State.Active!.CurrentDay);
            svc.Save();
        }

        Assert.Equal(golden, File.ReadAllBytes(StatePath));
    });

    [Fact]
    public void ReadOnlyNeverWritesEvenOnSaveAndDispose() => QuestPersistenceTests.InBerlin(() =>
    {
        var golden = File.ReadAllBytes(Fixture("programs_wpf.json"));
        File.WriteAllBytes(StatePath, golden);
        var stamp = File.GetLastWriteTimeUtc(StatePath);

        var svc = new ProgramService(StatePath, readOnly: true);
        Assert.Equal("first_week", svc.State.Active!.ProgramId);
        svc.State.Active.CurrentDay = 99;
        svc.MarkDirty();
        svc.Save();
        svc.Dispose();

        Assert.Equal(golden, File.ReadAllBytes(StatePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(StatePath));
        Assert.Equal(new[] { "programs.json" }, Snapshot());
    });

    [Fact]
    public void ReadOnlyRecoversFromTempWithoutMovingIt()
    {
        var tmp = StatePath + ".0123abcd.tmp";
        File.Copy(Fixture("programs_wpf.json"), tmp);

        using (var svc = new ProgramService(StatePath, readOnly: true))
            Assert.Equal("first_week", svc.State.Active!.ProgramId);

        Assert.False(File.Exists(StatePath));
        Assert.Equal(new[] { Path.GetFileName(tmp) }, Snapshot());
    }

    [Fact]
    public void ReadOnlyLeavesCorruptFileAndTempUntouched()
    {
        File.WriteAllText(StatePath, "{ \"Active\": { not json");
        var tmp = StatePath + ".feedface.tmp";
        File.Copy(Fixture("programs_wpf.json"), tmp);
        var corrupt = File.ReadAllBytes(StatePath);

        var svc = new ProgramService(StatePath, readOnly: true);
        // Same recovery WPF's LoadState makes: the temp's state is what loads.
        Assert.Equal("first_week", svc.State.Active!.ProgramId);
        svc.MarkDirty();
        svc.Dispose();

        Assert.Equal(corrupt, File.ReadAllBytes(StatePath));
        Assert.Equal(new[] { "programs.json", Path.GetFileName(tmp) }, Snapshot());
    }

    [Fact]
    public void ReadOnlyNeverLapsesARunLeftTenDays()
    {
        var today = ProgramClock.ProgramDate(DateTime.Now, 4);
        var state = new ProgramState
        {
            Active = new ProgramEnrollment
            {
                ProgramId = "first_week",
                StartedAt = today.AddDays(-12),
                CurrentDay = 2,
                CurrentDayDate = today.AddDays(-10),
                DaysOffRemaining = 1,
                State = ProgramEnrollmentState.Active,
            },
        };
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        var before = File.ReadAllBytes(StatePath);

        using (var svc = new ProgramService(StatePath, readOnly: true))
        {
            svc.EvaluateRollover();   // what slice 3's one-minute clock will call
            Assert.Equal(ProgramEnrollmentState.Active, svc.State.Active!.State);
            Assert.Equal(2, svc.State.Active.CurrentDay);
            Assert.Equal(1, svc.State.Active.DaysOffRemaining);
        }

        Assert.Equal(before, File.ReadAllBytes(StatePath));
    }
}
