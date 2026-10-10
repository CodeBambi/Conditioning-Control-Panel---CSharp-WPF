using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Programs 3b, ritual photos through the roadmap (oracle ~/ccp-port/evidence/oracle/programs-roadmap-seed.md
/// tests 1-4): read-only refuses before touching the roadmap; an active borrowed step goes to the roadmap and
/// the ledger keeps its bare file name; an inactive one only files the photo; no roadmap logs the dropped photo.
/// </summary>
[Collection(ProgramStatics.Name)]
public sealed class ProgramRitualTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-programs-ritual-").FullName;
    private readonly Func<RoadmapService?>? _savedRoadmap = CoreProgram.RoadmapProvider;
    private readonly Func<ProgramTask, bool>? _savedTask = CoreProgram.TaskAvailableProvider;
    private readonly Func<bool>? _savedPremium = CoreProgram.HasPremiumProvider;
    private readonly DateTime _now = new(2026, 5, 4, 12, 0, 0, DateTimeKind.Local);
    private RoadmapService? _roadmap;

    private string StatePath => Path.Combine(_dir, "programs.json");
    private string RoadmapPath => Path.Combine(_dir, "roadmap.json");
    private string DiaryPath => Path.Combine(_dir, "roadmap_diary");

    public ProgramRitualTests()
    {
        CoreProgram.TaskAvailableProvider = null;
        CoreProgram.HasPremiumProvider = () => true;
    }

    public void Dispose()
    {
        _roadmap?.Dispose();
        CoreProgram.RoadmapProvider = _savedRoadmap;
        CoreProgram.TaskAvailableProvider = _savedTask;
        CoreProgram.HasPremiumProvider = _savedPremium;
        Directory.Delete(_dir, recursive: true);
    }

    private RoadmapService SeedRoadmap()
    {
        _roadmap = new RoadmapService(RoadmapPath, DiaryPath);
        _roadmap.Save();
        CoreProgram.RoadmapProvider = () => _roadmap;
        return _roadmap;
    }

    /// <summary>Enrolls <paramref name="programId"/> and stands on <paramref name="day"/>; returns the photo to submit.</summary>
    private string EnrollOnDay(ProgramService svc, string programId, int day)
    {
        var enrollment = svc.Enroll(svc.Library.First(p => p.Id == programId))!;
        enrollment.CurrentDay = day;
        enrollment.GetOrCreateRecord(day, enrollment.CurrentDayDate);
        var photo = Path.Combine(_dir, "photo.png");
        File.WriteAllBytes(photo, new byte[] { 1, 2, 3 });
        return photo;
    }

    [Fact]
    public void ReadOnlyRefusesAndLeavesTheRoadmapAlone()
    {
        var roadmap = SeedRoadmap();
        using (var writer = new ProgramService(StatePath, readOnly: false, now: () => _now))
            EnrollOnDay(writer, "presentation", 1);
        var before = File.ReadAllBytes(RoadmapPath);
        using var svc = new ProgramService(StatePath, readOnly: true, now: () => _now);
        svc.ActiveEnrollment!.CurrentDay = 1;

        Assert.False(svc.SubmitRitualTask("d1_ritual_blank_slate", Path.Combine(_dir, "photo.png"), null));
        Assert.True(roadmap.IsStepActive("t1_step1"));
        Assert.Empty(Directory.GetFiles(DiaryPath));
        Assert.Equal(before, File.ReadAllBytes(RoadmapPath));
    }

    [Fact]
    public void ActiveBorrowedStepGoesToTheRoadmapAndTheLedgerKeepsABareName()
    {
        var roadmap = SeedRoadmap();
        using var svc = new ProgramService(StatePath, readOnly: false, now: () => _now);
        var photo = EnrollOnDay(svc, "presentation", 1);

        Assert.True(svc.SubmitRitualTask("d1_ritual_blank_slate", photo, null));
        Assert.True(roadmap.IsStepCompleted("t1_step1"));
        Assert.False(roadmap.IsStepActive("t1_step1"));
        var filed = svc.TodayRecord!.RitualPhotos["d1_ritual_blank_slate"];
        Assert.Equal(roadmap.GetStepProgress("t1_step1")!.PhotoPath, filed);
        Assert.False(Path.IsPathRooted(filed) || filed.Contains('/') || filed.Contains('\\'));
        Assert.True(File.Exists(roadmap.GetFullPhotoPath(filed)));
    }

    [Fact]
    public void InactiveBorrowedStepOnlyFilesThePhoto()
    {
        var roadmap = SeedRoadmap();
        using var svc = new ProgramService(StatePath, readOnly: false, now: () => _now);
        var photo = EnrollOnDay(svc, "first_week", 6);   // d6_ritual_pink borrows t1_step3, not active
        var before = File.ReadAllBytes(RoadmapPath);

        Assert.True(svc.SubmitRitualTask("d6_ritual_pink", photo, null));
        var filed = svc.TodayRecord!.RitualPhotos["d6_ritual_pink"];
        Assert.StartsWith("first_week_d6_ritual_pink_", filed);
        Assert.True(File.Exists(Path.Combine(DiaryPath, filed)));
        Assert.True(roadmap.IsStepActive("t1_step1"));
        Assert.False(roadmap.IsStepCompleted("t1_step3"));
        roadmap.Save();
        Assert.Equal(before, File.ReadAllBytes(RoadmapPath));
    }

    [Fact]
    public void NoRoadmapStillCreditsTheRitualAndLogsTheDroppedPhoto()
    {
        CoreProgram.RoadmapProvider = null;
        var sink = new ListSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            using var svc = new ProgramService(StatePath, readOnly: false, now: () => _now);
            var photo = EnrollOnDay(svc, "first_week", 6);
            Assert.True(svc.SubmitRitualTask("d6_ritual_pink", photo, null));
            Assert.Contains("d6_ritual_pink", svc.TodayRecord!.CompletedTaskIds);
            Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Warning
                                              && e.MessageTemplate.Text.Contains("photo not filed"));
        }
        finally { Log.Logger = previous; }
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }
}
