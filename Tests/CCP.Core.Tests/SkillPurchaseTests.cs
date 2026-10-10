using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core SkillPurchase against WPF SkillTreeService.PurchaseSkillAsync + ProfileSyncService.PurchaseSkillAsync.
/// The wallet rule: only a debited answer lowers SkillPoints; the one exception is a balance refusal that
/// survives a sync.</summary>
public class SkillPurchaseTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Answers = new();
        public readonly List<string> Bodies = new();
        public readonly List<string?> Tokens = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            Tokens.Add(request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null);
            Assert.EndsWith("/v2/user/purchase-skill", request.RequestUri!.AbsolutePath);
            return Answers.Dequeue()();
        }
    }

    private static Func<HttpResponseMessage> Json(HttpStatusCode code, string json) =>
        () => new HttpResponseMessage(code) { Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json") };

    private static readonly SkillDefinition Skill = SkillDefinition.All.First(s => !s.IsSecret && string.IsNullOrEmpty(s.PrerequisiteId));

    private static AppSettings Settings(int points, string? id = "u_1") => new()
    {
        SkillPoints = points, UnifiedId = id, AuthToken = "tok", UnlockedSkills = new List<string>(),
    };

    private static (SkillPurchase service, Fake http, List<string> saves) Make(AppSettings s)
    {
        var http = new Fake();
        var saves = new List<string>();
        return (new SkillPurchase(() => s, http) { Save = () => saves.Add("save") }, http, saves);
    }

    [Fact]
    public async Task ADebitedAnswerLowersTheWalletAndUnlocksTheSkill()
    {
        var s = Settings(Skill.Cost + 5);
        var (service, http, saves) = Make(s);
        int spent = 0; long reconciled = 0; string? unlocked = null;
        service.PointsSpent = c => spent += c;
        service.LifetimeSpentReconciled = t => reconciled = t;
        service.SkillUnlocked += (_, id) => unlocked = id;
        http.Answers.Enqueue(Json(HttpStatusCode.OK,
            "{'success':true,'skill_points':5,'unlocked_skills':['" + Skill.Id + "'],'lifetime_points_spent':321}"));

        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(5, s.SkillPoints);
        Assert.Contains(Skill.Id, s.UnlockedSkills);
        Assert.Equal(Skill.Cost, spent);
        Assert.Equal(321, reconciled);
        Assert.Equal(Skill.Id, unlocked);
        Assert.NotEmpty(saves);
        Assert.Contains("skill_points", http.Bodies.Single());
        Assert.Contains((Skill.Cost + 5).ToString(), http.Bodies.Single());
    }

    [Fact]
    public async Task SignedOutNeverReachesTheServerAndKeepsTheWallet()
    {
        var s = Settings(Skill.Cost, id: null);
        var (service, http, _) = Make(s);
        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(http.Bodies);
        Assert.Equal(Skill.Cost, s.SkillPoints);
        Assert.Empty(s.UnlockedSkills);
    }

    [Fact]
    public async Task TooFewPointsIsRefusedLocally()
    {
        var s = Settings(Skill.Cost - 1);
        var (service, http, _) = Make(s);
        var (ok, _) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.Empty(http.Bodies);
    }

    [Fact]
    public async Task OfflineAndServerErrorsKeepTheWallet()
    {
        var s = Settings(Skill.Cost + 2);
        var (service, http, saves) = Make(s);
        http.Answers.Enqueue(() => throw new HttpRequestException("refused"));
        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.Equal("Connection failed. Please check your internet connection.", error);

        // An error body's skill_points (0 for a not-backfilled account) is never adopted.
        http.Answers.Enqueue(Json(HttpStatusCode.BadRequest, "{'success':false,'error':'Nope','skill_points':0}"));
        (ok, error) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.Equal("Nope", error);

        http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));
        (ok, error) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.StartsWith("Your session has expired.", error);

        Assert.Equal(Skill.Cost + 2, s.SkillPoints);
        Assert.Empty(s.UnlockedSkills);
        Assert.Empty(saves);
    }

    [Fact]
    public async Task ABalanceRefusalSyncsOnceAndAsksAgainBeforeLoweringAnything()
    {
        var s = Settings(Skill.Cost);
        var (service, http, _) = Make(s);
        int syncs = 0;
        service.SyncBeforeRetry = () => { syncs++; return Task.FromResult(true); };
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'error':'Not enough','skill_points':" + (Skill.Cost - 1) + "}"));
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':true,'skill_points':0,'unlocked_skills':['" + Skill.Id + "']}"));

        var (ok, _) = await service.PurchaseSkillAsync(Skill.Id);

        Assert.True(ok);
        Assert.Equal(1, syncs);
        Assert.Equal(2, http.Bodies.Count);
        Assert.Equal(0, s.SkillPoints);
    }

    [Fact]
    public async Task ARefusalThatSurvivesTheSyncAdoptsTheServerBalance()
    {
        var s = Settings(Skill.Cost);
        var (service, http, saves) = Make(s);
        service.SyncBeforeRetry = () => Task.FromResult(true);
        var refusal = "{'success':false,'error':'Not enough','skill_points':" + (Skill.Cost - 1) + "}";
        http.Answers.Enqueue(Json(HttpStatusCode.OK, refusal));
        http.Answers.Enqueue(Json(HttpStatusCode.OK, refusal));

        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);

        Assert.False(ok);
        Assert.Equal("Not enough", error);
        Assert.Equal(Skill.Cost - 1, s.SkillPoints);   // the one exception
        Assert.Empty(s.UnlockedSkills);
        Assert.Single(saves);
    }

    [Fact]
    public async Task ARefusalWithNoSyncKeepsTheWallet()
    {
        var s = Settings(Skill.Cost);
        var (service, http, _) = Make(s);
        service.SyncBeforeRetry = () => Task.FromResult(false);
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':false,'error':'Not enough','skill_points':0}"));
        var (ok, _) = await service.PurchaseSkillAsync(Skill.Id);
        Assert.False(ok);
        Assert.Equal(Skill.Cost, s.SkillPoints);
        Assert.Single(http.Bodies);
    }

    [Fact]
    public void LifetimeSpendOnlyRises()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "k2-ach-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir, "achievements.json"))) { SuppressPopups = true };
            engine.TrackSkillPointsSpent(40);
            engine.ReconcileLifetimePointsSpent(30);
            Assert.Equal(40, engine.Progress.LifetimeSkillPointsSpent);
            engine.ReconcileLifetimePointsSpent(120);
            Assert.Equal(120, engine.Progress.LifetimeSkillPointsSpent);
            Assert.True(engine.Progress.IsUnlocked("window_shopping"));
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
