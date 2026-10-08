using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core DescentMigrationAck on fixture replies: settles and heals, never touches the ledger, and ignores
/// anything that is not an explicit <c>completed: true</c>.</summary>
public sealed class DescentMigrationAckTests
{
    private static AppSettings Fixture() => new()
    {
        PlayerLevel = 40, PlayerXP = 1234.5, HighestLevelEver = 41, DescentEpoch = 1, DescentCycleXpBonus = 1.0,
    };

    private static void AssertLedgerUntouched(AppSettings s) =>
        Assert.Equal((40, 1234.5, 41, 1), (s.PlayerLevel, s.PlayerXP, s.HighestLevelEver, s.DescentEpoch));

    [Theory]
    [InlineData("""{"success":true}""")]
    [InlineData("""{"descent_migration":null}""")]
    [InlineData("""{"descent_migration":"completed"}""")]
    [InlineData("""{"descent_migration":{"completed":"true","choice":"restore"}}""")]
    [InlineData("""{"descent_migration":{"completed":false,"choice":"cycle"}}""")]
    [InlineData("""{"descent_migration":{"required":true,"total_xp_earned":999999}}""")]
    public void Malformed_or_absent_ack_changes_nothing(string reply)
    {
        var s = Fixture();
        Assert.False(DescentMigrationAck.Apply(s, JObject.Parse(reply)));
        Assert.False(s.DescentMigrationCompleted);
        Assert.Equal(1.0, s.DescentCycleXpBonus);
        Assert.Equal(1.0, DescentCycleXp.XpBonusFor(s));
        AssertLedgerUntouched(s);
    }

    [Fact]
    public void Restore_ack_settles_heals_the_bonus_but_no_cycle_mark()
    {
        var s = Fixture();
        s.DescentMigrationOffered = true;
        Assert.True(DescentMigrationAck.Apply(s, JObject.Parse("""{"descent_migration":{"completed":true,"choice":"restore"}}""")));
        Assert.True(s.DescentMigrationCompleted);
        Assert.Equal(DescentMigrationChoices.Restore, s.DescentMigrationChoice);
        Assert.Equal(DescentCycleXp.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.Equal(0, s.DescentCycle);
        Assert.False(s.DescentMigrationOffered);
        AssertLedgerUntouched(s);
        Assert.False(DescentMigrationAck.Apply(s, JObject.Parse("""{"descent_migration":{"completed":true,"choice":"restore"}}""")));
        AssertLedgerUntouched(s);
    }

    [Fact]
    public void Ack_without_a_choice_falls_back_to_the_pending_choice()
    {
        var s = Fixture();
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Cycle;
        Assert.True(DescentMigrationAck.Apply(s, JObject.Parse("""{"descent_migration":{"completed":true}}""")));
        Assert.True(s.DescentMigrationCompleted);
        Assert.Equal(DescentMigrationChoices.Cycle, s.DescentMigrationChoice);
        Assert.Null(s.PendingDescentMigrationChoice);
        Assert.Equal(0, s.DescentCycle);   // Cycle I only from the server's own record, as WPF
        AssertLedgerUntouched(s);
    }
}
