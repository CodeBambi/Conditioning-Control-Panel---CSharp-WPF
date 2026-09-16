using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rules behind the one sheet a returning user meets on a new PC.
///
/// <para><b>Why these are pure tests.</b> The population this decides for - fresh settings file,
/// real account, backup in the cloud - is the single hardest thing in this codebase to reach by
/// hand: it needs a second machine, a wiped profile directory and a live sign-in. Every one of
/// those combinations is a plain truth table here, and the sheet itself only paints what this
/// returns.</para>
///
/// <para>Two of the cases are the ones that used to hurt. A factory reset leaves the same missing
/// settings file a new PC does, and the old MessageBox happily offered to undo the wipe the user
/// had just asked for. And an account with no backup and nothing accomplished is, for this
/// purpose, a brand-new install - the wizard already owns that launch, and a second modal saying
/// only hello is exactly the pile-up this redesign deletes.</para>
/// </summary>
public class WelcomeBackDecisionTests
{
    private const string Pack = "mod-locked";

    private static WelcomeBackPlan Decide(
        bool freshFile = true,
        bool factoryReset = false,
        bool identity = true,
        bool backup = true,
        int level = 12,
        string? packId = null,
        bool packInstalled = false)
        => WelcomeBackDecision.Decide(freshFile, factoryReset, identity, backup, level, packId, packInstalled);

    // =====================================================================================
    //  when there is no sheet at all
    // =====================================================================================

    [Fact]
    public void AnExistingInstallIsNeverWelcomedBack()
    {
        // The settings file is right where they left it. Nothing about this launch is a return.
        Assert.False(Decide(freshFile: false).ShowSheet);
    }

    [Fact]
    public void AFactoryResetIsNeverOfferedItsOwnUndo()
    {
        // Settings, Data leaves exactly the same missing settings file a new PC does. The old
        // cloud-restore MessageBox could not tell them apart and offered to put the wiped
        // settings straight back under fresh-install copy.
        Assert.False(Decide(factoryReset: true).ShowSheet);
    }

    [Fact]
    public void NoAccountMeansNobodyToWelcomeBack()
    {
        Assert.False(Decide(identity: false).ShowSheet);
    }

    [Fact]
    public void AnAccountWithNoBackupAndNoProgressIsJustANewInstall()
    {
        // The wizard owns this launch. A second modal that says only hello is the pile-up.
        Assert.False(Decide(backup: false, level: 1).ShowSheet);
    }

    [Fact]
    public void AnAccountWithNoBackupButRealProgressStillGetsAGreeting()
    {
        // Someone who reached level 12 and lost their settings file has something to be welcomed
        // back to, even though there is nothing to restore.
        var plan = Decide(backup: false, level: 12);

        Assert.True(plan.ShowSheet);
        Assert.False(plan.ShowRestoreRow);
        Assert.False(plan.ShowFlavourRow);
    }

    [Fact]
    public void TheGreetingThresholdIsTheOneTheRecapUses()
    {
        // Level 2 is "has ever levelled up" everywhere else in the app (the season recap's own
        // highestLevel gate). Drifting from it here would greet a population nothing else does.
        Assert.Equal(2, WelcomeBackDecision.GreetWithoutBackupFromLevel);
        Assert.False(Decide(backup: false, level: 1).ShowSheet);
        Assert.True(Decide(backup: false, level: 2).ShowSheet);
    }

    // =====================================================================================
    //  the restore row
    // =====================================================================================

    [Fact]
    public void ABackupMeansASheetWithARestoreRow()
    {
        var plan = Decide(backup: true, level: 1);

        Assert.True(plan.ShowSheet);
        Assert.True(plan.ShowRestoreRow);
    }

    // =====================================================================================
    //  the flavour row
    // =====================================================================================

    [Fact]
    public void TheFlavourRowNeedsAPackThatIsNotHereYet()
    {
        var plan = Decide(packId: Pack, packInstalled: false);

        Assert.True(plan.ShowFlavourRow);
        Assert.Equal(Pack, plan.FlavourPackId);
    }

    [Fact]
    public void APackAlreadyOnDiskIsNotOffered()
    {
        var plan = Decide(packId: Pack, packInstalled: true);

        Assert.False(plan.ShowFlavourRow);
        Assert.Null(plan.FlavourPackId);
    }

    [Fact]
    public void AModThatMapsToNoPackIsNotOffered()
    {
        // CCP Default and every user mod. There is nothing to download, so there is nothing to
        // tick - and a row whose toggle does nothing is worse than no row.
        var plan = Decide(packId: null);

        Assert.False(plan.ShowFlavourRow);
        Assert.Null(plan.FlavourPackId);
    }

    [Fact]
    public void ThereIsNoFlavourToBringWithoutABackupToReadItFrom()
    {
        // The mod comes out of the backup's ActiveModId. No backup, no answer.
        var plan = Decide(backup: false, level: 12, packId: Pack, packInstalled: false);

        Assert.True(plan.ShowSheet);
        Assert.False(plan.ShowFlavourRow);
    }

    // =====================================================================================
    //  the season line
    // =====================================================================================

    [Fact]
    public void OnlyTheServerMaySayASeasonEnded()
    {
        // CurrentSeasonKey falls back to the wall-clock month when nothing has ever synced, and
        // that fallback rolls itself over on the 1st for every never-synced install. Announcing
        // off it is how a machine invents a rotation it never witnessed.
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("2026-09", "2026-08", serverConfirmed: false));
        Assert.True(WelcomeBackDecision.ShouldShowSeasonLine("2026-09", "2026-08", serverConfirmed: true));
    }

    [Fact]
    public void TheSeasonHasToHaveActuallyMoved()
    {
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("2026-09", "2026-09", serverConfirmed: true));
    }

    [Fact]
    public void ABackwardSeasonKeyIsADesyncNotNews()
    {
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("2026-08", "2026-09", serverConfirmed: true));
    }

    [Fact]
    public void NothingKnownMeansNothingSaid()
    {
        // A fresh settings file holds no season at all. With nothing to have moved on FROM, the
        // conservative answer is silence - which is the whole point of the line being a line.
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("2026-09", "", serverConfirmed: true));
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("2026-09", null, serverConfirmed: true));
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine("", "2026-08", serverConfirmed: true));
        Assert.False(WelcomeBackDecision.ShouldShowSeasonLine(null, "2026-08", serverConfirmed: true));
    }
}
