using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ProgramStatics { public const string Name = "ProgramStatics"; }

/// <summary>
/// programs-3a decision (docs/avalonia-decisions.md, evidence/oracle/programs-3a-canenroll.md tests 1-2):
/// CanEnroll refuses a program whose required tasks the head can never raise. Unseeded = WPF, unchanged.
/// </summary>
[Collection(ProgramStatics.Name)]
public sealed class ProgramCanEnrollCapabilityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-programs-cap-").FullName;
    private readonly Func<ProgramTask, bool>? _savedTask = CoreProgram.TaskAvailableProvider;
    private readonly Func<bool>? _savedPremium = CoreProgram.HasPremiumProvider;

    public void Dispose()
    {
        CoreProgram.TaskAvailableProvider = _savedTask;
        CoreProgram.HasPremiumProvider = _savedPremium;
        Directory.Delete(_dir, recursive: true);
    }

    private ProgramService NewService() => new(Path.Combine(_dir, "programs.json"), readOnly: true);

    [Fact]
    public void NullProviderKeepsEveryProgramsAnswer()
    {
        CoreProgram.TaskAvailableProvider = null;
        using var svc = NewService();
        Assert.Equal(5, svc.Library.Count);

        CoreProgram.HasPremiumProvider = null;
        foreach (var p in svc.Library)
        {
            var ok = svc.CanEnroll(p, out var reason);
            var premium = p.Tier == ProgramTier.Premium;
            Assert.Equal(!premium, ok);
            Assert.Equal(premium ? "This program is Patreon-exclusive." : "", reason);
        }

        CoreProgram.HasPremiumProvider = () => true;
        foreach (var p in svc.Library)
        {
            Assert.True(svc.CanEnroll(p, out var reason), p.Id);
            Assert.Equal("", reason);
        }
    }

    [Fact]
    public void RefusedLockCardRefusesFirstWeekAndNamesIt()
    {
        CoreProgram.HasPremiumProvider = null;
        CoreProgram.TaskAvailableProvider = t => t.Verifier != QuestCategory.LockCard;
        using var svc = NewService();
        var firstWeek = svc.Library.First(p => p.Id == "first_week");

        Assert.False(svc.CanEnroll(firstWeek, out var reason));
        // Core tests never load a language (Loc answers raw keys here), so the feature is pinned on the
        // argument list and the shipped English template; the Avalonia render test shows the real text.
        Assert.Equal(Loc.GetF("programs_needs_feature", "LockCard"), reason);
        Assert.Equal(new QuestCategory?[] { QuestCategory.LockCard },
            ProgramService.UnavailableTasks(firstWeek, CoreProgram.IsTaskAvailable).Select(t => t.Verifier).Distinct());
        var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", "en.json")))!;
        Assert.Equal("Not available on this build yet: needs LockCard.",
            string.Format(en["programs_needs_feature"], "LockCard"));

        // Refusing every OPTIONAL task refuses nothing: first_week's optional ritual (day 6) does not count.
        CoreProgram.TaskAvailableProvider = t => !t.Optional;
        Assert.True(svc.CanEnroll(firstWeek, out _));
    }

    [Fact]
    public void OptionalTasksAndAmbientNeverRefuse()
    {
        var day = new ProgramDay
        {
            DayIndex = 1,
            Tasks =
            {
                new ProgramTask { Id = "opt", Verifier = QuestCategory.LockCard, Optional = true },
                new ProgramTask { Id = "req", Verifier = QuestCategory.Flash },
            },
            Ambient = new ProgramAmbient { RequiredMinutes = 30, Verifier = QuestCategory.LockCard },
        };
        var program = new ProgramDefinition { Id = "synthetic", Chapters = { new ProgramChapter { Days = { day } } } };

        Func<ProgramTask, bool> noLockCard = t => t.Verifier != QuestCategory.LockCard;
        Assert.Empty(ProgramService.UnavailableTasks(program, noLockCard));
        Assert.Null(ProgramService.UnavailableReason(program, noLockCard));

        Func<ProgramTask, bool> noFlash = t => t.Verifier != QuestCategory.Flash;
        Assert.Equal(new[] { "req" }, ProgramService.UnavailableTasks(program, noFlash).Select(t => t.Id));
    }
}
