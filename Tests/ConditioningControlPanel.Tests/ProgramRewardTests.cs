using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Chapter rewards, from banked id to a thing the user has.
///
/// The bug these exist to keep closed: <c>CompleteChapter</c> appended the chapter's RewardId to
/// <see cref="ProgramEnrollment.BankedRewards"/> and nothing anywhere read the list back, so all
/// thirteen authored reward lines - a saved preset, installed phrases, a permanently replayable day
/// 28 - promised possessions that were never created. Every assertion below is about a promise that
/// used to be a string and now has to be a file or a pool entry.
///
/// Four things are load-bearing.
///
/// First, IDEMPOTENCE. Rewards deliberately survive <c>RestartForNewAttempt</c>, and the service
/// re-materialises everything banked on every launch, so a grant that is not idempotent hands out a
/// duplicate session per launch and per attempt.
///
/// Second, the FIRST-GRANT distinction. The reward line promises the phrases go live, which is true
/// exactly once; a later pass may only re-add a phrase that is missing entirely, or every launch
/// would switch back on the phrases the user had deliberately silenced.
///
/// Third, ALREADY-BANKED. A second attempt re-finishing chapter 1 raises ChapterCompleted for a
/// reward the enrollment already holds. Granting there would re-give it once per attempt.
///
/// Fourth, the SESSION ID. The filed keepsake must NOT carry the transient program session's id -
/// ProgramService pins that in <c>_expectedSessionId</c> to decide whether a finishing session ticks
/// today's slot, so a keepsake wearing it could credit a program day just for being replayed.
///
/// Everything runs through <see cref="IProgramRewardSurfaces"/>. The real surfaces write the user's
/// real CustomSessions folder and the live AppSettings (null in a test host), so the rules are tested
/// against a fake and the real implementation is exercised by play-test.
/// </summary>
public class ProgramRewardTests
{
    // ---- fake surfaces ----

    private sealed class FakeSurfaces : IProgramRewardSurfaces
    {
        public readonly List<Session> Filed = new();
        public readonly Dictionary<string, bool> Pool = new();
        public readonly List<string> Celebrations = new();

        /// <summary>Every InstallPhrases call, so re-enable behaviour is visible, not inferred.</summary>
        public readonly List<(IReadOnlyList<string> Phrases, bool EnableExisting)> Installs = new();

        public bool SessionIsFiled(string sessionId) =>
            Filed.Any(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal));

        public void FileSession(Session session) => Filed.Add(session);

        public bool PhraseIsInstalled(string phrase) => Pool.ContainsKey(phrase);

        public void InstallPhrases(IReadOnlyList<string> phrases, bool enableExisting)
        {
            Installs.Add((phrases.ToList(), enableExisting));
            foreach (var phrase in phrases)
            {
                if (!Pool.TryGetValue(phrase, out var enabled)) Pool[phrase] = true;
                else if (enableExisting && !enabled) Pool[phrase] = true;
            }
        }

        public void Celebrate(string message) => Celebrations.Add(message);
    }

    // ---- fixtures ----

    private static ProgramSessionTemplate Template() => new()
    {
        Id = "T",
        Name = "Template",
        Floor = new SessionSettings { FlashEnabled = true, FlashPerHour = 10 },
        Ceiling = new SessionSettings { FlashEnabled = true, FlashPerHour = 30 }
    };

    private static ProgramDay Day(int index) => new()
    {
        DayIndex = index,
        Title = $"Day {index}",
        SessionTemplateId = "T",
        SessionMinutes = 30,
        Intensity = index / 10.0
    };

    /// <summary>A two-chapter program: chapter 1 saves its final session, chapter 2 installs phrases.</summary>
    private static ProgramDefinition Program() => new()
    {
        Id = "test_program",
        Title = "Test Program",
        LengthDays = 4,
        Templates = new List<ProgramSessionTemplate> { Template() },
        Chapters = new List<ProgramChapter>
        {
            new ProgramChapter
            {
                Id = "ch1",
                Name = "Chapter One",
                RewardId = "test_ch1",
                RewardDescription = "Day two, saved.",
                RewardSavesFinalSession = true,
                Days = new List<ProgramDay> { Day(1), Day(2) }
            },
            new ProgramChapter
            {
                Id = "ch2",
                Name = "Chapter Two",
                RewardId = "test_ch2",
                RewardDescription = "Two phrases, installed.",
                RewardPhrases = new List<string> { "ALPHA", "BETA" },
                Days = new List<ProgramDay> { Day(3), Day(4) }
            }
        }
    };

    private static ProgramEnrollment Enrollment(params string[] banked)
    {
        var enrollment = new ProgramEnrollment { ProgramId = "test_program", CurrentDay = 1 };
        enrollment.BankedRewards.AddRange(banked);
        return enrollment;
    }

    private static (ProgramRewardService service, FakeSurfaces surfaces) Build()
    {
        var surfaces = new FakeSurfaces();
        // programs: null. The constructor then subscribes to nothing and materialises nothing, which
        // is what lets these tests drive the rules without a live ProgramService reading and
        // rewriting the real %LOCALAPPDATA%\ConditioningControlPanel\programs.json.
        return (new ProgramRewardService(null, surfaces), surfaces);
    }

    private static Func<string, ProgramDefinition?> Lookup(ProgramDefinition program) =>
        id => string.Equals(id, program.Id, StringComparison.OrdinalIgnoreCase) ? program : null;

    // ---- the preset actually lands in the sessions store ----

    [Fact]
    public void AChapterThatPromisesASessionFilesOne()
    {
        var (service, surfaces) = Build();
        var program = Program();

        var grant = service.Grant(program, program.Chapters[0], Enrollment(), firstGrant: true);

        Assert.True(grant.Materialised);
        var filed = Assert.Single(surfaces.Filed);
        Assert.Equal("Test Program · Day 2", filed.Name);
        Assert.Equal("Test Program · Day 2", grant.SessionName);
        Assert.Equal(SessionSource.Custom, filed.Source);

        // Built from the chapter's LAST day at that day's authored intensity, not day one's.
        Assert.Equal(30, filed.DurationMinutes);
        Assert.Equal(14, filed.Settings.FlashPerHour); // 10 -> 30 at intensity 0.2
    }

    /// <summary>
    /// The keepsake must not wear the id ProgramService pins as the day's expected session, or
    /// replaying it could tick a program day.
    /// </summary>
    [Fact]
    public void TheFiledSessionDoesNotCarryTheTransientProgramSessionId()
    {
        var (service, surfaces) = Build();
        var program = Program();

        service.Grant(program, program.Chapters[0], Enrollment(), firstGrant: true);

        var filed = Assert.Single(surfaces.Filed);
        Assert.Equal(ProgramRewardService.SessionIdPrefix + "test_ch1", filed.Id);

        var transient = ProgramSessionBuilder.Build(program, program.Chapters[0].Days.Last());
        Assert.NotEqual(transient.Id, filed.Id);
    }

    [Fact]
    public void FilingTheSameRewardTwiceDoesNotDuplicateTheSession()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var enrollment = Enrollment();

        service.Grant(program, program.Chapters[0], enrollment, firstGrant: true);
        service.Grant(program, program.Chapters[0], enrollment, firstGrant: false);
        service.Grant(program, program.Chapters[0], enrollment, firstGrant: false);

        Assert.Single(surfaces.Filed);
    }

    // ---- phrases actually land in the pool ----

    [Fact]
    public void AChapterThatPromisesPhrasesInstallsThem()
    {
        var (service, surfaces) = Build();
        var program = Program();

        var grant = service.Grant(program, program.Chapters[1], Enrollment(), firstGrant: true);

        Assert.True(grant.Materialised);
        Assert.True(surfaces.Pool["ALPHA"]);
        Assert.True(surfaces.Pool["BETA"]);
        Assert.Equal(new[] { "ALPHA", "BETA" }, grant.InstalledPhrases);
    }

    [Fact]
    public void InstallingTheSamePhrasesAgainWritesNothingNew()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var enrollment = Enrollment();

        service.Grant(program, program.Chapters[1], enrollment, firstGrant: true);
        var second = service.Grant(program, program.Chapters[1], enrollment, firstGrant: false);

        Assert.Single(surfaces.Installs);
        Assert.Empty(second.InstalledPhrases);
        Assert.False(second.Materialised);

        // Still reported as owned, so the possessions list and any toast can name them.
        Assert.Equal(new[] { "ALPHA", "BETA" }, second.Phrases);
    }

    /// <summary>
    /// The reward line promises the phrases go live, so the first grant may re-enable one the user
    /// had switched off. Every later pass may not - that is the user's own setting by then.
    /// </summary>
    [Fact]
    public void OnlyTheFirstGrantReEnablesAPhraseTheUserSwitchedOff()
    {
        var (service, surfaces) = Build();
        var program = Program();

        surfaces.Pool["ALPHA"] = false;
        service.Grant(program, program.Chapters[1], Enrollment(), firstGrant: true);
        Assert.True(surfaces.Pool["ALPHA"]);

        surfaces.Pool["ALPHA"] = false;
        service.Grant(program, program.Chapters[1], Enrollment(), firstGrant: false);
        Assert.False(surfaces.Pool["ALPHA"]);
    }

    /// <summary>A phrase the user DELETED is still theirs - the reward line says a restart cannot take it back.</summary>
    [Fact]
    public void ALaterPassReAddsAPhraseThatWasDeletedEntirely()
    {
        var (service, surfaces) = Build();
        var program = Program();

        service.Grant(program, program.Chapters[1], Enrollment(), firstGrant: true);
        surfaces.Pool.Remove("BETA");

        var again = service.Grant(program, program.Chapters[1], Enrollment(), firstGrant: false);

        Assert.True(surfaces.Pool["BETA"]);
        Assert.Equal(new[] { "BETA" }, again.InstalledPhrases);
        Assert.False(surfaces.Installs.Last().EnableExisting);
    }

    // ---- the grant stamp ----

    [Fact]
    public void TheFirstGrantStampsTheEnrollmentAndLaterOnesDoNotMoveIt()
    {
        var (service, _) = Build();
        var program = Program();
        var enrollment = Enrollment();

        service.Grant(program, program.Chapters[0], enrollment, firstGrant: true);
        Assert.True(enrollment.RewardGrantedAt.ContainsKey("test_ch1"));
        var stamped = enrollment.RewardGrantedAt["test_ch1"];

        service.Grant(program, program.Chapters[0], enrollment, firstGrant: false);
        Assert.Equal(stamped, enrollment.RewardGrantedAt["test_ch1"]);
    }

    // ---- the startup catch-up ----

    [Fact]
    public void MaterialiseBankedGrantsEverythingTheEnrollmentAlreadyHolds()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var state = new ProgramState { Active = Enrollment("test_ch1", "test_ch2") };

        var count = service.MaterialiseBanked(state, Lookup(program));

        Assert.Equal(2, count);
        Assert.Single(surfaces.Filed);
        Assert.True(surfaces.Pool["ALPHA"]);
        Assert.True(surfaces.Pool["BETA"]);

        // Never toasts: this runs at startup, for possessions the user has had for weeks.
        Assert.Empty(surfaces.Celebrations);
    }

    [Fact]
    public void MaterialiseBankedIsIdempotentAcrossLaunches()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var state = new ProgramState { Active = Enrollment("test_ch1", "test_ch2") };

        Assert.Equal(2, service.MaterialiseBanked(state, Lookup(program)));
        Assert.Equal(0, service.MaterialiseBanked(state, Lookup(program)));
        Assert.Equal(0, service.MaterialiseBanked(state, Lookup(program)));

        Assert.Single(surfaces.Filed);
        Assert.Single(surfaces.Installs);
    }

    /// <summary>
    /// A programs.json restored onto a machine that has never held the session file. The stamp says
    /// "granted", so the phrase half must stay hands-off, but the file is genuinely missing and the
    /// possession is not allowed to be lost with it.
    /// </summary>
    [Fact]
    public void MaterialiseBankedRefilesASessionThatIsNoLongerOnDisk()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var state = new ProgramState { Active = Enrollment("test_ch1") };

        service.MaterialiseBanked(state, Lookup(program));
        surfaces.Filed.Clear();

        Assert.Equal(1, service.MaterialiseBanked(state, Lookup(program)));
        Assert.Single(surfaces.Filed);
    }

    [Fact]
    public void MaterialiseBankedWalksHistoryAsWellAsTheActiveRun()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var state = new ProgramState { History = { Enrollment("test_ch1", "test_ch2") } };

        Assert.Equal(2, service.MaterialiseBanked(state, Lookup(program)));
        Assert.Single(surfaces.Filed);
        Assert.True(surfaces.Pool["ALPHA"]);
    }

    [Fact]
    public void MaterialiseBankedIgnoresARewardWhoseChapterIsGone()
    {
        var (service, surfaces) = Build();
        var program = Program();
        var state = new ProgramState { Active = Enrollment("a_reward_from_a_program_that_changed") };

        Assert.Equal(0, service.MaterialiseBanked(state, Lookup(program)));
        Assert.Empty(surfaces.Filed);
    }

    // ---- the read model ----

    [Fact]
    public void GetBankedRewardsReportsWhatTheUserOwns()
    {
        var (service, _) = Build();
        var program = Program();
        var enrollment = Enrollment("test_ch1", "test_ch2");
        var state = new ProgramState { Active = enrollment };

        service.MaterialiseBanked(state, Lookup(program));
        var owned = ProgramRewardService.GetBankedRewards(state, Lookup(program));

        Assert.Equal(2, owned.Count);

        var first = owned.Single(r => r.RewardId == "test_ch1");
        Assert.Equal("Test Program", first.ProgramTitle);
        Assert.Equal("Chapter One", first.ChapterName);
        Assert.Equal("Day two, saved.", first.Description);
        Assert.Equal("Test Program · Day 2", first.SessionName);
        Assert.NotNull(first.GrantedAt);

        var second = owned.Single(r => r.RewardId == "test_ch2");
        Assert.Null(second.SessionName);
        Assert.Equal(new[] { "ALPHA", "BETA" }, second.Phrases);
    }

    /// <summary>
    /// Two runs of the same program bank the same id. The list is a set of possessions, not a log.
    /// </summary>
    [Fact]
    public void GetBankedRewardsDeduplicatesAcrossRuns()
    {
        var program = Program();
        var state = new ProgramState
        {
            Active = Enrollment("test_ch1"),
            History = { Enrollment("test_ch1") }
        };

        var owned = ProgramRewardService.GetBankedRewards(state, Lookup(program));
        Assert.Single(owned);
    }

    [Fact]
    public void GetBankedRewardsIsEmptyWithNoState()
    {
        Assert.Empty(ProgramRewardService.GetBankedRewards(null, _ => null));
    }

    // ---- the built-in library ----

    /// <summary>
    /// Every reward id in the shipped library must resolve to a chapter the granter can read, and
    /// every reward that promises a session must have a day to build it from. Without this a typo in
    /// a RewardId is a silently ungrantable promise - exactly the class of bug this whole file exists
    /// to close.
    /// </summary>
    [Fact]
    public void EveryBuiltInRewardIsGrantable()
    {
        foreach (var program in BuiltInPrograms.All())
        {
            foreach (var chapter in program.Chapters)
            {
                if (string.IsNullOrWhiteSpace(chapter.RewardId)) continue;

                if (chapter.RewardSavesFinalSession)
                {
                    Assert.True(chapter.Days.Count > 0,
                        $"{program.Id}/{chapter.Id} promises a saved session but has no days.");

                    var day = chapter.Days.OrderByDescending(d => d.DayIndex).First();
                    Assert.NotNull(program.GetTemplate(day.SessionTemplateId));

                    // The name a user will see in their Sessions list, screened the same way the
                    // transient session's is - a title containing "good girls" would falsely unlock.
                    var name = ProgramRewardService.BankedSessionName(program, day);
                    Assert.False(ProgramSessionBuilder.ContainsReserved(name),
                        $"{program.Id}/{chapter.Id} would file a session named '{name}'.");
                }

                Assert.All(chapter.RewardPhrases, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            }
        }
    }

    /// <summary>
    /// The thirteen authored reward lines, pinned to what each one now actually hands over. A reward
    /// line that names a surface ("saved", "installed", "a phrase pack") and grants nothing is the
    /// original bug; a line that grants something it never promised is the same bug wearing a hat.
    /// </summary>
    [Fact]
    public void TheBuiltInRewardsGrantWhatTheirCopyPromises()
    {
        var byId = BuiltInPrograms.All()
            .SelectMany(p => p.Chapters, (p, c) => (Program: p, Chapter: c))
            .Where(x => !string.IsNullOrWhiteSpace(x.Chapter.RewardId))
            .ToDictionary(x => x.Chapter.RewardId!, StringComparer.Ordinal);

        Assert.Equal(13, byId.Count);

        // Rewards that file the chapter's final day as a permanent, replayable session.
        foreach (var id in new[]
                 {
                     "first_week_preset", "firmware_module2", "kept_ch1_offering", "kept_ch2_ache",
                     "kept_ch3_habit", "kept_ch4_decision", "tk_ch4_banked"
                 })
        {
            Assert.True(byId[id].Chapter.RewardSavesFinalSession, $"'{id}' should save its final session.");
        }

        // Rewards that install phrases, and exactly which ones. The Takeover lines name their
        // phrases in the copy the user reads, so the pack has to be those and only those.
        Assert.Equal(new[] { "GOOD GIRL", "BAMBI SLEEP" }, byId["tk_ch1_banked"].Chapter.RewardPhrases);
        Assert.Equal(new[] { "BIMBO DOLL", "SNAP AND FORGET", "PRIMPED AND PAMPERED" },
            byId["tk_ch2_banked"].Chapter.RewardPhrases);
        Assert.Equal(new[] { "BAMBI FREEZE", "BAMBI UNIFORM LOCK" }, byId["tk_ch3_banked"].Chapter.RewardPhrases);

        Assert.NotEmpty(byId["firmware_module1"].Chapter.RewardPhrases);
        Assert.NotEmpty(byId["kept_ch2_ache"].Chapter.RewardPhrases);
        Assert.NotEmpty(byId["pr_ch2_banked"].Chapter.RewardPhrases);

        // Presentation's ledger reward is the one line with no surface behind it: the pages are the
        // ritual photos, already written as the days were done. It grants the ledger entry only.
        Assert.False(byId["pr_ch1_banked"].Chapter.RewardSavesFinalSession);
        Assert.Empty(byId["pr_ch1_banked"].Chapter.RewardPhrases);
    }

    /// <summary>
    /// Phrase packs must be keys of the pool the program's OWN mod ships. The Sissy manifest is a
    /// near-clone of the Bambi one with the Bambi-prefixed entries renamed (BAMBI SLEEP -> DEEP
    /// SLEEP, BAMBI FREEZE -> FREEZE), so a pack copy-pasted between programs compiles, installs, and
    /// is a set of phrases that mod has never heard of - no linked whisper audio, no haptic pattern.
    /// </summary>
    [Fact]
    public void EveryBuiltInRewardPhraseIsAKeyOfItsOwnModsPool()
    {
        var mods = new[]
        {
            BuiltInMods.CCPDefault, BuiltInMods.BambiSleep, BuiltInMods.SissyHypno,
            BuiltInMods.Dronification, BuiltInMods.Locked
        };

        foreach (var program in BuiltInPrograms.All())
        {
            var mod = mods.FirstOrDefault(m =>
                string.Equals(m.Id, program.ModId, StringComparison.OrdinalIgnoreCase));

            Assert.True(mod != null, $"Program {program.Id} names unknown mod '{program.ModId}'.");
            if (mod!.SubliminalPool == null) continue;

            foreach (var chapter in program.Chapters)
            {
                foreach (var phrase in chapter.RewardPhrases)
                {
                    Assert.True(mod.SubliminalPool.ContainsKey(phrase),
                        $"{program.Id}/{chapter.Id} installs '{phrase}', which is not a key of " +
                        $"{program.ModId}'s subliminal pool.");
                }
            }
        }
    }
}
