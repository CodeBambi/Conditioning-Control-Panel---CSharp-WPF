using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// programs.json schema skew (docs/avalonia-decisions.md 2026-10-09, programs CHECKPOINT B, slice 3-0):
/// fields a newer build wrote survive this build's save, a file stamped with a newer SchemaVersion
/// loads read-only, and the service's clock is a seam tests can step.
/// </summary>
[Collection(ProgramStatics.Name)]   // Enroll reads CoreProgram.TaskAvailableProvider
public sealed class ProgramServiceSchemaTests : IDisposable
{
    private static readonly DateTime FixtureNow = new(2026, 3, 12, 23, 0, 0, DateTimeKind.Local);

    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-programs-schema-").FullName;
    private string StatePath => Path.Combine(_dir, "programs.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name);

    private string[] Snapshot() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    private static JsonNode FixtureWithUnknownFields()
    {
        var root = JsonNode.Parse(File.ReadAllText(Fixture("programs_wpf.json")))!;
        root["FutureRoot"] = new JsonObject { ["a"] = 1, ["b"] = "two" };
        root["History"]![0]!["FutureEnrollment"] = new JsonArray(1, 2, 3);
        root["Active"]!["FutureEnrollment"] = "active-extra";
        root["Active"]!["Records"]!["1"]!["FutureDay"] = new JsonObject { ["nested"] = true };
        return root;
    }

    [Fact]
    public void UnknownFieldsAtEveryLevelSurviveLoadMutateSave() => QuestPersistenceTests.InBerlin(() =>
    {
        var input = FixtureWithUnknownFields();
        File.WriteAllText(StatePath, input.ToJsonString());

        using (var svc = new ProgramService(StatePath, readOnly: false, now: () => FixtureNow))
        {
            Assert.False(svc.IsReadOnly);
            svc.State.Active!.NudgeHour = 21;
            svc.MarkDirty();
            svc.Save();
        }

        input["Active"]!["NudgeHour"] = 21;
        var saved = JsonNode.Parse(File.ReadAllText(StatePath))!;
        // As JSON, not bytes: extension data is written at the end of each object.
        Assert.True(JsonNode.DeepEquals(input, saved), saved.ToJsonString());
    });

    [Fact]
    public void NewerSchemaStampLoadsReadOnlyAndNeverWrites() => QuestPersistenceTests.InBerlin(() =>
    {
        var root = JsonNode.Parse(File.ReadAllText(Fixture("programs_wpf.json")))!;
        root["SchemaVersion"] = ProgramState.CurrentSchemaVersion + 1;
        File.WriteAllText(StatePath, root.ToJsonString());
        var before = File.ReadAllBytes(StatePath);
        var stamp = File.GetLastWriteTimeUtc(StatePath);

        var svc = new ProgramService(StatePath, readOnly: false, now: () => FixtureNow.AddDays(10));
        Assert.True(svc.IsReadOnly);
        svc.State.Active!.CurrentDay = 99;
        svc.MarkDirty();
        svc.Save();
        svc.EvaluateRollover();
        svc.Dispose();

        Assert.Equal(before, File.ReadAllBytes(StatePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(StatePath));
        Assert.Equal(new[] { "programs.json" }, Snapshot());
    });

    [Fact]
    public void NewerSchemaStampInATempIsRecoveredWithoutMovingIt()
    {
        var root = JsonNode.Parse(File.ReadAllText(Fixture("programs_wpf.json")))!;
        root["SchemaVersion"] = ProgramState.CurrentSchemaVersion + 1;
        var tmp = StatePath + ".0123abcd.tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        var before = File.ReadAllBytes(tmp);

        using (var svc = new ProgramService(StatePath, readOnly: false, now: () => FixtureNow))
        {
            Assert.True(svc.IsReadOnly);
            Assert.Equal("first_week", svc.State.Active!.ProgramId);
            svc.MarkDirty();
            svc.Save();
        }

        Assert.False(File.Exists(StatePath));
        Assert.Equal(new[] { Path.GetFileName(tmp) }, Snapshot());
        Assert.Equal(before, File.ReadAllBytes(tmp));
    }

    [Fact]
    public void SteppedClockDrivesEnrollAndRollover()
    {
        var now = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Local);
        using var svc = new ProgramService(StatePath, readOnly: false, now: () => now);
        var program = svc.Library.First(p => p.Id == "first_week");

        var enrollment = svc.Enroll(program)!;
        Assert.Equal(now, enrollment.StartedAt);
        Assert.Equal(now.Date, enrollment.CurrentDayDate);

        now = now.AddDays(1);
        svc.EvaluateRollover();
        Assert.Equal(ProgramEnrollmentState.Active, enrollment.State);
        Assert.Equal(2, enrollment.CurrentDay);
        Assert.Equal(now.Date, enrollment.CurrentDayDate);
        Assert.True(enrollment.Records[1].Missed);

        now = now.AddDays(3);
        svc.EvaluateRollover();
        Assert.Equal(ProgramEnrollmentState.Lapsed, enrollment.State);
        Assert.Equal(now, enrollment.LapsedAt);
    }
}
