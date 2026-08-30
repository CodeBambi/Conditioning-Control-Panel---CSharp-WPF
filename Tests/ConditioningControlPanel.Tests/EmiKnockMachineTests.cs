using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ONBOARDING PROMPT THAT ONLY EVER HAPPENS ONCE.
///
/// <para>Ask EMI wave 1 gives the dock chip a single flash: three pink pulses on a settled first
/// launch, and if the user clicks, one offer to be walked round the app. It is the only discovery
/// mechanism a feature that ships switched on and silent has ever had - and it is also the single
/// easiest thing in this app to get wrong, because an onboarding prompt that comes back is the
/// fastest possible route to the whole widget being switched off. The owner's requirement was
/// "once, and if they shrug, once more, and then never".</para>
///
/// <para><see cref="EmiKnockMachine"/> is pure for exactly that reason - no timers, no dispatcher,
/// no <c>App</c>, an injectable clock and a world behind an interface - so the four brakes and the
/// population branch can be walked in a millisecond instead of across three fresh installs and a
/// pair of upgrades. What is checked here is that she knocks at all, and much more importantly
/// that she stops: at the answer, at the two-offer cap, at a tour already taken, and behind every
/// gate that says something else owns the screen.</para>
///
/// <para>The last class in the file covers the ONE line the knock adds to the offer cadence: a
/// <c>scripted</c> moment skipping the two cadence gates, and nothing else, for nobody else.</para>
/// </summary>
public class EmiKnockMachineTests
{
    private const string ThisBuild = "6.8.6";
    private const string OldBuild = "6.7.4";

    /// <summary>The world, as a bag of fields a test can set. Mirrors EmiState without touching disk.</summary>
    private sealed class FakeWorld : IEmiKnockWorld
    {
        public int KnockState { get; set; }
        public long KnockAtUtc { get; set; }
        public int KnockOffers { get; set; }

        public readonly HashSet<string> Tours = new(StringComparer.OrdinalIgnoreCase);
        public bool TourDone(string tour) => Tours.Contains(tour);

        public string? LastSeenVersion { get; set; } = string.Empty;
        public string? CurrentVersion { get; set; } = ThisBuild;
        public DateTime LaunchStartedUtc { get; set; } = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

        // Every gate starts in the state that ALLOWS a knock, so each test can switch exactly one
        // off and watch the knock die of that one thing.
        public bool DeskEnabled { get; set; } = true;
        public bool AlreadyOut { get; set; }
        public bool WizardUp { get; set; }
        public bool UpdateDialogUp { get; set; }
        public bool SessionRunning { get; set; }
        public bool TutorialOpen { get; set; }
        public bool WindowUsable { get; set; } = true;
    }

    /// <summary>A machine and a fresh-install world that is expected to knock.</summary>
    private static (EmiKnockMachine m, FakeWorld w) Fresh()
    {
        return (new EmiKnockMachine(() => new DateTime(2026, 8, 30, 12, 5, 0, DateTimeKind.Utc)),
                new FakeWorld());
    }

    /// <summary>An upgrader: they ran 6.7.4 and are now on 6.8.6.</summary>
    private static (EmiKnockMachine m, FakeWorld w) Upgrader()
    {
        var (m, w) = Fresh();
        w.LastSeenVersion = OldBuild;
        return (m, w);
    }

    // =========================================================================================
    //  she knocks at all
    // =========================================================================================

    [Fact]
    public void AFreshInstallIsKnockedAt()
    {
        var (m, w) = Fresh();
        Assert.True(m.MayKnock(w));
        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));
        Assert.Equal(EmiKnockMachine.FreshMoment, m.ContactMoment(w));
    }

    [Fact]
    public void AnUpgraderIsKnockedAtToo()
    {
        var (m, w) = Upgrader();
        Assert.True(m.MayKnock(w));
        Assert.Equal(EmiKnockPopulation.Upgrader, m.Population(w));
        Assert.Equal(EmiKnockMachine.UpgradeMoment, m.ContactMoment(w));
    }

    // =========================================================================================
    //  THE FOUR BRAKES, each on its own
    // =========================================================================================

    /// <summary>
    /// BRAKE 1. They said yes; the state latched to spent, and nothing here ever fires again -
    /// including the contact moment, so even a summon by some other route gets the ordinary hello.
    /// </summary>
    [Fact]
    public void Brake1_AnAnsweredKnockNeverFiresAgain()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Spent;

        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
        Assert.Null(m.ContactMoment(w));
    }

    /// <summary>
    /// BRAKE 2. The knock, plus one shrugged re-offer, and never a third. The cap is checked on its
    /// own, with the state still at "knocked", so it cannot be passing because of brake 1.
    /// </summary>
    [Fact]
    public void Brake2_TheOfferCapEndsIt()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;

        w.KnockOffers = EmiKnockMachine.OfferCap - 1;
        Assert.True(m.OfferOwed(w));

        w.KnockOffers = EmiKnockMachine.OfferCap;
        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
    }

    /// <summary>
    /// BRAKE 3 lives in the lines file (<c>limit: {per:"ever", max:1}</c>) and is enforced by the
    /// engine at draw time, deliberately NOT restated in the machine - so what is asserted here is
    /// that the machine does not quietly duplicate it. With brakes 1, 2 and 4 all clear, an offer
    /// is owed however many times it is asked; the content-side ceiling is the engine's job alone.
    /// </summary>
    [Fact]
    public void Brake3_IsTheLinesFilesJobAndNotDuplicatedHere()
    {
        var (m, w) = Fresh();

        Assert.True(m.OfferOwed(w));
        Assert.True(m.OfferOwed(w));
        Assert.True(m.OfferOwed(w));
    }

    /// <summary>
    /// BRAKE 4. Offering a walk somebody has already taken is the definition of not listening. A
    /// fresh install that took the walk inside the wizard is its own population, and gets nothing.
    /// </summary>
    [Fact]
    public void Brake4_ATourAlreadyTakenEndsIt()
    {
        var (m, w) = Fresh();
        w.Tours.Add(EmiKnockMachine.ShortWalkTour);

        Assert.Equal(EmiKnockPopulation.Walked, m.Population(w));
        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
        Assert.Null(m.ContactMoment(w));
    }

    /// <summary>...and the same brake, from the other population's side.</summary>
    [Fact]
    public void Brake4_AnUpgraderWhoTookTheUpgradeTourGetsNothing()
    {
        var (m, w) = Upgrader();
        w.Tours.Add(EmiKnockMachine.UpgradeTour);

        Assert.Equal(EmiKnockPopulation.Walked, m.Population(w));
        Assert.False(m.MayKnock(w));
    }

    /// <summary>
    /// The two tours are NOT interchangeable. A fresh install that somehow has the upgrade tour
    /// latched is still owed the short walk, and vice versa - brake 4 is per tour, not a single
    /// "has been shown something" flag, which is the shape of gate this codebase has been bitten by
    /// before.
    /// </summary>
    [Fact]
    public void Brake4_IsPerTourAndNotABareSeenFlag()
    {
        var (m, w) = Fresh();
        w.Tours.Add(EmiKnockMachine.UpgradeTour);
        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));
        Assert.True(m.MayKnock(w));

        var (m2, w2) = Upgrader();
        w2.Tours.Add(EmiKnockMachine.ShortWalkTour);
        Assert.Equal(EmiKnockPopulation.Upgrader, m2.Population(w2));
        Assert.True(m2.MayKnock(w2));
    }

    // =========================================================================================
    //  the population branch
    // =========================================================================================

    [Fact]
    public void AnEmptyVersionStampIsAFreshInstall()
    {
        var (m, w) = Fresh();
        w.LastSeenVersion = string.Empty;
        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));

        w.LastSeenVersion = null;
        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));

        w.LastSeenVersion = "   ";
        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));
    }

    /// <summary>
    /// THE BARE SEEN-FLAG TRAP. Somebody already running this build is not an upgrader and is owed
    /// nothing: "has a version stamp" is not the same question as "ran an older build". That
    /// conflation is what showed every fresh install a migration notice for a move it never
    /// witnessed.
    /// </summary>
    [Fact]
    public void SomebodyAlreadyOnThisBuildIsNobodysProblem()
    {
        var (m, w) = Fresh();
        w.LastSeenVersion = ThisBuild;

        Assert.Equal(EmiKnockPopulation.None, m.Population(w));
        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
        Assert.Null(m.ContactMoment(w));
    }

    /// <summary>A stamp from the FUTURE (a downgrade, a hand-edited settings file) is not an upgrade.</summary>
    [Fact]
    public void ADowngradeIsNotAnUpgrade()
    {
        var (m, w) = Fresh();
        w.LastSeenVersion = "6.9.9";
        Assert.Equal(EmiKnockPopulation.None, m.Population(w));
    }

    /// <summary>
    /// Versions are PARSED, not string-compared: "6.10.0" sorts before "6.9.0" as text, and the
    /// day this app reaches a tenth minor release a string compare would classify every upgrader
    /// backwards. This is the assertion a string compare cannot pass.
    /// </summary>
    [Fact]
    public void VersionsAreComparedNumericallyNotLexically()
    {
        Assert.True(EmiKnockMachine.IsOlder("6.9.0", "6.10.0"));
        Assert.False(EmiKnockMachine.IsOlder("6.10.0", "6.9.0"));

        Assert.True(EmiKnockMachine.IsOlder("6.7.4", "6.8.6"));
        Assert.False(EmiKnockMachine.IsOlder("6.8.6", "6.8.6"));
        Assert.False(EmiKnockMachine.IsOlder("", "6.8.6"));
        Assert.False(EmiKnockMachine.IsOlder("6.8.6", ""));
    }

    /// <summary>A leading v is cosmetic (git tags carry one, the setting does not).</summary>
    [Fact]
    public void ALeadingVIsIgnored()
    {
        Assert.False(EmiKnockMachine.IsOlder("v6.8.6", "6.8.6"));
        Assert.True(EmiKnockMachine.IsOlder("v6.7.4", "6.8.6"));
    }

    /// <summary>Each population is offered its own tour, and the two that are owed nothing get null.</summary>
    [Fact]
    public void EachPopulationIsOfferedItsOwnTour()
    {
        Assert.Equal(EmiKnockMachine.ShortWalkTour, EmiKnockMachine.TourFor(EmiKnockPopulation.Fresh));
        Assert.Equal(EmiKnockMachine.UpgradeTour, EmiKnockMachine.TourFor(EmiKnockPopulation.Upgrader));
        Assert.Null(EmiKnockMachine.TourFor(EmiKnockPopulation.Walked));
        Assert.Null(EmiKnockMachine.TourFor(EmiKnockPopulation.None));

        Assert.Equal("tour:shortwalk", EmiKnockMachine.EffectFor(EmiKnockPopulation.Fresh));
        Assert.Equal("tour:upgrade", EmiKnockMachine.EffectFor(EmiKnockPopulation.Upgrader));
        Assert.Null(EmiKnockMachine.EffectFor(EmiKnockPopulation.None));
    }

    // =========================================================================================
    //  the gates
    // =========================================================================================

    /// <summary>
    /// Every gate from the contract, each switched off ALONE against a world that is otherwise
    /// certain to knock. One test per gate would say the same thing eight times; what matters is
    /// that no gate is silently missing, and that none of them needs a second one's help.
    /// </summary>
    [Theory]
    [InlineData("deskOff")]
    [InlineData("alreadyOut")]
    [InlineData("wizardUp")]
    [InlineData("updateDialogUp")]
    [InlineData("sessionRunning")]
    [InlineData("tutorialOpen")]
    [InlineData("windowUnusable")]
    public void EveryGateStopsTheKnockOnItsOwn(string gate)
    {
        var (m, w) = Fresh();
        Assert.True(m.MayKnock(w));   // the control: this world knocks

        switch (gate)
        {
            case "deskOff": w.DeskEnabled = false; break;
            case "alreadyOut": w.AlreadyOut = true; break;
            case "wizardUp": w.WizardUp = true; break;
            case "updateDialogUp": w.UpdateDialogUp = true; break;
            case "sessionRunning": w.SessionRunning = true; break;
            case "tutorialOpen": w.TutorialOpen = true; break;
            case "windowUnusable": w.WindowUsable = false; break;
        }

        Assert.False(m.MayKnock(w));

        // A gate is about this instant, not about the feature: the offer is still owed, so the
        // next launch offers it properly rather than having burned it on a moment nobody saw.
        Assert.True(m.OfferOwed(w));
    }

    // =========================================================================================
    //  the re-offer
    // =========================================================================================

    /// <summary>
    /// The re-offer is a LATER-LAUNCH beat. A knock stamped inside this launch must not produce a
    /// second one: without this, a dismiss and a re-summon would read as a fresh sitting and she
    /// would knock twice inside a minute.
    /// </summary>
    [Fact]
    public void SheNeverKnocksTwiceInOneLaunch()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;
        w.KnockOffers = 1;
        w.KnockAtUtc = w.LaunchStartedUtc.AddMinutes(2).Ticks;

        Assert.True(m.OfferOwed(w));      // the offer survives...
        Assert.False(m.MayKnock(w));      // ...but not into this same sitting
    }

    /// <summary>...and on the NEXT launch the same shrugged offer comes back exactly once.</summary>
    [Fact]
    public void TheShruggedOfferComesBackOnceOnTheNextLaunch()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;
        w.KnockOffers = 1;
        w.KnockAtUtc = w.LaunchStartedUtc.AddDays(-1).Ticks;

        Assert.True(m.MayKnock(w));

        // The flash spends the second offer, and that is the end of it forever.
        w.KnockOffers = 2;
        w.KnockAtUtc = w.LaunchStartedUtc.AddMinutes(1).Ticks;
        Assert.False(m.MayKnock(w));
        Assert.False(m.OfferOwed(w));
    }

    /// <summary>
    /// The re-offer is the QUIETER beat. First contact carries the ask; the second time she just
    /// mentions it, which is why <c>firstContactLater</c> is a pool with no offer attached.
    /// </summary>
    [Fact]
    public void TheSecondContactIsTheQuieterOne()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;

        w.KnockOffers = 1;
        Assert.Equal(EmiKnockMachine.FreshMoment, m.ContactMoment(w));

        w.KnockOffers = 2;
        Assert.Equal(EmiKnockMachine.LaterMoment, m.ContactMoment(w));
    }

    /// <summary>An upgrader's second contact is the same quieter beat, not a second upgrade pitch.</summary>
    [Fact]
    public void TheSecondContactIsTheSameForBothPopulations()
    {
        var (m, w) = Upgrader();
        w.KnockState = EmiKnockMachine.Knocked;

        w.KnockOffers = 1;
        Assert.Equal(EmiKnockMachine.UpgradeMoment, m.ContactMoment(w));

        w.KnockOffers = 2;
        Assert.Equal(EmiKnockMachine.LaterMoment, m.ContactMoment(w));
    }

    /// <summary>A null world is not a reason to knock. Every entry point answers "no".</summary>
    [Fact]
    public void ANullWorldNeverKnocks()
    {
        var m = new EmiKnockMachine();
        Assert.False(m.MayKnock(null!));
        Assert.False(m.OfferOwed(null!));
        Assert.Null(m.ContactMoment(null!));
        Assert.Equal(EmiKnockPopulation.None, m.Population(null!));
    }
}

/// <summary>
/// THE ONE LINE THE KNOCK ADDS TO THE OFFER CADENCE, and the guarantee that it stays one line.
///
/// <para>The ambient offer cadence (BRIEF 7) says she never asks anything before the third summon
/// and never twice inside ten minutes. Both are correct for a companion volunteering things
/// unprompted, and both would make the knock's offer literally unreachable: it happens on the
/// FIRST summon, in direct answer to a chip the user just clicked.</para>
///
/// <para>So a moment may declare itself <c>scripted</c> and skip exactly those two gates - the
/// same two, and only those two, that the QA switch already skips. What is asserted here is the
/// leak: that an ordinary moment is unaffected, that the bypass does not stand in for the QA
/// switch or vice versa, and that the gap and the summon floor are both genuinely skipped rather
/// than one of them being forgotten.</para>
///
/// <para><c>AskGatesPass</c> itself cannot be reached from a headless test - it reads
/// <c>App.Settings</c> and <c>App.EmiDesk.AskSituationOk()</c>, neither of which exists without a
/// running WPF app, a session engine and a live widget - which is exactly why the arithmetic was
/// lifted into <c>ScriptedCadenceBypass</c> instead of being left inline.</para>
/// </summary>
public class EmiScriptedAskBypassTests
{
    private const double Recently = 1000;                              // an offer a second ago
    private const double LongAgo = EmiLineEngine.AskGapMs + 1;
    private const int TooFewSummons = EmiLineEngine.AskMinSummons - 1;
    private const int EnoughSummons = EmiLineEngine.AskMinSummons;

    // ---------------------------------------------------------------- the ordinary cadence

    [Fact]
    public void AnOrdinaryAskStillWaitsForTheThirdSummon()
    {
        Assert.False(EmiLineEngine.ScriptedCadenceBypass(
            scripted: false, qa: false, msSinceLastAsk: LongAgo, summonCount: TooFewSummons));
    }

    [Fact]
    public void AnOrdinaryAskStillWaitsOutTheTenMinuteGap()
    {
        Assert.False(EmiLineEngine.ScriptedCadenceBypass(
            scripted: false, qa: false, msSinceLastAsk: Recently, summonCount: EnoughSummons));
    }

    [Fact]
    public void AnOrdinaryAskPassesOnceBothCadenceGatesAreSatisfied()
    {
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(
            scripted: false, qa: false, msSinceLastAsk: LongAgo, summonCount: EnoughSummons));
    }

    // ---------------------------------------------------------------- the bypass

    /// <summary>
    /// The knock's own case, exactly: the very first summon this install has ever had, with no
    /// previous offer to measure a gap from. Both cadence gates are failing, and the scripted beat
    /// goes through anyway - which is the entire reason the flag exists.
    /// </summary>
    [Fact]
    public void AScriptedAskGoesThroughOnTheVeryFirstSummon()
    {
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(
            scripted: true, qa: false, msSinceLastAsk: 0, summonCount: 0));
    }

    [Fact]
    public void AScriptedAskSkipsTheGapAndTheSummonFloorIndependently()
    {
        // gap failing, summons fine
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, false, Recently, EnoughSummons));
        // summons failing, gap fine
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, false, LongAgo, TooFewSummons));
    }

    // ---------------------------------------------------------------- no leaking

    /// <summary>
    /// THE LEAK TEST. Marking one moment scripted must not loosen anything for anybody else: an
    /// ordinary ask evaluated in the same process, on the same clock, is refused exactly as it was
    /// before the flag existed.
    /// </summary>
    [Fact]
    public void TheBypassDoesNotLeakToOrdinaryAsks()
    {
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, false, 0, 0));
        Assert.False(EmiLineEngine.ScriptedCadenceBypass(false, false, 0, 0));

        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, false, Recently, TooFewSummons));
        Assert.False(EmiLineEngine.ScriptedCadenceBypass(false, false, Recently, TooFewSummons));
    }

    /// <summary>
    /// The QA switch and the scripted flag are independent routes past the same two gates. Neither
    /// implies the other: a normal launch has no QA switch, and QA must keep working for moments
    /// that are not scripted.
    /// </summary>
    [Fact]
    public void TheQaSwitchAndTheScriptedFlagAreIndependent()
    {
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(false, true, 0, 0));
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, false, 0, 0));
        Assert.True(EmiLineEngine.ScriptedCadenceBypass(true, true, 0, 0));
        Assert.False(EmiLineEngine.ScriptedCadenceBypass(false, false, 0, 0));
    }
}

/// <summary>
/// THE CONTENT SIDE of the knock: the three contact moments, as they are actually shipped in
/// <c>Resources/emi/desk-lines.json</c>.
///
/// <para>A moment id is fired by string and the bus drops what it does not know in silence, so a
/// definition that is missing, or one whose dials contradict the machine, costs a beat forever
/// with nothing on screen to say so. <c>EmiMomentIdWiringTests</c> already proves the ids exist;
/// what is checked here is that the DIALS behind them still say what the contract says they
/// should - above all the two that make the knock reachable at all (<c>scripted</c>, priority 3)
/// and the one that is brake 3 (<c>limit: ever/1</c>).</para>
/// </summary>
public class EmiKnockLinesFileTests
{
    private static System.Text.Json.JsonElement Moments()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(
                   System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);

        var path = System.IO.Path.Combine(dir!.FullName, "ConditioningControlPanel",
                                          "Resources", "emi", "desk-lines.json");
        Assert.True(System.IO.File.Exists(path), "desk-lines.json is missing at " + path);

        var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        return doc.RootElement.GetProperty("moments").Clone();
    }

    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    [InlineData(EmiKnockMachine.LaterMoment)]
    public void EveryContactMomentIsDefined(string id)
    {
        Assert.True(Moments().TryGetProperty(id, out _), id + " is not in desk-lines.json");
    }

    /// <summary>
    /// The two that carry the offer must be <c>scripted</c>, or the ask is refused by a cadence
    /// gate the knock cannot possibly satisfy and the chip's six seconds of pulses lead nowhere.
    /// </summary>
    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    public void TheOfferingMomentsAreScriptedAndAsk(string id)
    {
        var m = Moments().GetProperty(id);

        Assert.True(m.TryGetProperty("scripted", out var scripted)
                    && scripted.ValueKind == System.Text.Json.JsonValueKind.True,
            id + " must be scripted: it fires on the FIRST summon, which the ordinary ask cadence forbids");

        Assert.True(m.GetProperty("askOdds").GetDouble() > 0, id + " must actually carry an offer");
    }

    /// <summary>
    /// The re-offer is a pool with NO offer attached: somebody who shrugged once does not need the
    /// same two chips put in front of them a second time.
    /// </summary>
    [Fact]
    public void TheReOfferCarriesNoAsk()
    {
        var m = Moments().GetProperty(EmiKnockMachine.LaterMoment);
        Assert.Equal(0.0, m.GetProperty("askOdds").GetDouble());
    }

    /// <summary>
    /// BRAKE 3, on the content side. Each contact moment fires once in a lifetime, whatever the
    /// machine's own counters say - the two ceilings are deliberately independent.
    /// </summary>
    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    [InlineData(EmiKnockMachine.LaterMoment)]
    public void EveryContactMomentIsLimitedToOnceEver(string id)
    {
        var limit = Moments().GetProperty(id).GetProperty("limit");
        Assert.Equal("ever", limit.GetProperty("per").GetString());
        Assert.Equal(1, limit.GetProperty("max").GetInt32());
    }

    /// <summary>
    /// Priority 3 is a ceremony: it bypasses the 45 s global floor and the odds roll. She was
    /// summoned BY this beat, and losing it to a floor she set two minutes earlier would leave the
    /// user staring at a widget that flashed at them and then had nothing to say.
    /// </summary>
    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    [InlineData(EmiKnockMachine.LaterMoment)]
    public void EveryContactMomentIsACeremony(string id)
    {
        Assert.Equal(3, Moments().GetProperty(id).GetProperty("priority").GetInt32());
    }

    /// <summary>
    /// Onboarding is not the place for the top shelf, whatever the user's spice dial says. The
    /// ceiling is the MINIMUM of this and the dial, so 1 caps her at playful.
    /// </summary>
    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    [InlineData(EmiKnockMachine.LaterMoment)]
    public void EveryContactMomentKeepsTheSpiceDown(string id)
    {
        Assert.True(Moments().GetProperty(id).GetProperty("spiceCeiling").GetInt32() <= 1,
            id + " must not reach the top spice shelf on somebody's first minute");
    }

    /// <summary>
    /// NO SCRIPTED MOMENT SPRAWL. The bypass is narrow on purpose: it exists for the knock's one
    /// offer and it is not a general "let her ask sooner" switch. If a fourth moment ever wants it,
    /// that is a decision somebody should have to make deliberately, by editing this list.
    /// </summary>
    [Fact]
    public void OnlyTheKnockIsScripted()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            EmiKnockMachine.FreshMoment,
            EmiKnockMachine.UpgradeMoment
        };

        var strays = new List<string>();
        foreach (var m in Moments().EnumerateObject())
        {
            if (!m.Value.TryGetProperty("scripted", out var s)) continue;
            if (s.ValueKind != System.Text.Json.JsonValueKind.True) continue;
            if (!allowed.Contains(m.Name)) strays.Add(m.Name);
        }

        Assert.True(strays.Count == 0,
            "these moments skip the offer cadence but are not the knock:\n  " + string.Join("\n  ", strays));
    }
}
