using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The migration in its retired form (owner, 2026-10-06): no ceremony, the offer is taken as "restore"
/// silently, the build says <c>descent_auto: true</c>. Core twin of WPF DescentAutoRestoreTests.</summary>
public class DescentAutoRestoreTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static AppSettings Veteran() => new()
    {
        UnifiedId = "u_1", PlayerLevel = 40, PlayerXP = 120, HighestLevelEver = 40,
        LastConfirmedServerXp = 50000, LastConfirmedServerXpAccount = "u_1",
    };

    private static DescentMigrationOffer Offer(double basis = 90000) =>
        new() { TotalXpEarned = 80000, DevotionDays = 12, RestoreBasisXp = basis };

    [Fact]
    public void EverySyncSaysThisBuildTakesTheOfferSilently()
    {
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(new AppSettings { UnifiedId = "u_1" }, null)));
        Assert.True((bool)body["descent_auto"]!);
    }

    [Fact]
    public void AnOfferIsRestoredOnCurveV2WithTheBonusAndAPendingSubmit()
    {
        var s = Veteran();
        Assert.True(DescentMigration.ApplyRestore(s, Offer(), null, Now));

        var (level, into) = XpCurve.DeriveLevelFromLifetimeXp(90000, DescentEpochs.AccountDescent);
        Assert.Equal(level, s.PlayerLevel);
        Assert.Equal(into, s.PlayerXP);
        Assert.Equal(DescentEpochs.AccountDescent, s.DescentEpoch);
        Assert.Equal(DescentMigrationChoices.Restore, s.PendingDescentMigrationChoice);
        Assert.Equal(DescentCycleXp.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.Equal(40, s.DescentPreMigrationLevel);
        Assert.False(s.DescentMigrationCompleted);          // only the ack may write that
        Assert.Equal(0, s.LastConfirmedServerXp);           // the watermark was in pre-migration terms
        Assert.Equal(0, s.DescentCycle);                    // restore is never the Cycle door
        Assert.Equal(Now, s.DescentAnchorUtc);
    }

    [Fact]
    public void ABasisTheServerDidNotSendFallsBackToLifetime()
    {
        var s = Veteran();
        DescentMigration.ApplyRestore(s, Offer(basis: 0), null, Now);
        Assert.Equal(XpCurve.DeriveLevelFromLifetimeXp(80000, DescentEpochs.AccountDescent).Level, s.PlayerLevel);
    }

    [Fact]
    public void AMigratedAccountOrAPendingChoiceIsNeverRewrittenTwice()
    {
        var done = Veteran();
        done.DescentMigrationCompleted = true;
        Assert.False(DescentMigration.ApplyRestore(done, Offer(), null, Now));
        Assert.Equal(40, done.PlayerLevel);

        var pending = Veteran();
        Assert.True(DescentMigration.ApplyRestore(pending, Offer(), null, Now));
        var level = pending.PlayerLevel;
        Assert.False(DescentMigration.ApplyRestore(pending, Offer(basis: 5), null, Now));
        Assert.Equal(level, pending.PlayerLevel);
    }

    [Fact]
    public void TheDripQueueIsTheStagesAlreadyReached()
    {
        var stage = new DescentStage { N = 1, Thresholds = new[] { 3, 10, 30 } };
        Assert.Equal(new[] { 1, 2 }, DescentMigration.BuildStageDripQueue(Offer(), stage));
        Assert.Empty(DescentMigration.BuildStageDripQueue(Offer(), null));
    }

    [Fact]
    public void OnlyARequiredBlockIsAnOfferAndOnlyCompletedIsAnAck()
    {
        var offer = JObject.Parse("{\"descent_migration\":{\"required\":true,\"total_xp_earned\":1200.5,\"devotion_days\":4,\"restore_basis_xp\":1500}}");
        var read = DescentMigration.ReadOffer(offer);
        Assert.NotNull(read);
        Assert.Equal(1200.5, read!.TotalXpEarned);
        Assert.Equal(4, read.DevotionDays);
        Assert.Equal(1500, read.RestoreBasisXp);
        Assert.False(DescentMigration.IsAck(offer));

        var ack = JObject.Parse("{\"descent_migration\":{\"completed\":true,\"choice\":\"restore\"}}");
        Assert.Null(DescentMigration.ReadOffer(ack));
        Assert.True(DescentMigration.IsAck(ack));
        Assert.Null(DescentMigration.ReadOffer(JObject.Parse("{}")));
    }

    [Fact]
    public void TheAckSettlesTheRestore()
    {
        var s = Veteran();
        DescentMigration.ApplyRestore(s, Offer(), null, Now);
        Assert.True(DescentMigrationAck.Apply(s, JObject.Parse("{\"descent_migration\":{\"completed\":true,\"choice\":\"restore\"}}")));
        Assert.True(s.DescentMigrationCompleted);
        Assert.Null(s.PendingDescentMigrationChoice);
        Assert.Equal(DescentMigrationChoices.Restore, s.DescentMigrationChoice);
    }
}
