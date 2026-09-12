using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ONBOARDING PROMPT THAT ONLY EVER HAPPENS ONCE.
///
/// <para>On a settled first launch the dock chip pulses and EMI comes out in the same beat, and
/// what she says is one question: walk with me? It is the only discovery mechanism a feature that
/// ships switched on and silent has ever had - and it is also the single easiest thing in this app
/// to get wrong, because an onboarding prompt that comes back is the fastest possible route to the
/// whole widget being switched off. The owner's requirement, after the first-run redesign, is
/// "once, asked out loud, and then never".</para>
///
/// <para>It used to be quieter and worse: three pink pulses and nothing else, with the offer
/// reachable only if the user read a 40 px ring as an invitation and clicked it inside six seconds.
/// The offer was spent whether or not they did, so a shrug bought one softer re-offer on a later
/// launch to make up for it. Now she asks the first time, so there is nothing to make up for:
/// <c>OfferCap</c> is 1, the later beat is gone, and upgraders are somebody else's job (What's New
/// offers them their tour, on a launch where they are already reading it).</para>
///
/// <para><see cref="EmiKnockMachine"/> is pure for exactly that reason - no timers, no dispatcher,
/// no <c>App</c>, an injectable clock and a world behind an interface - so the four brakes and the
/// population branch can be walked in a millisecond instead of across three fresh installs and a
/// pair of upgrades. What is checked here is that she knocks at all, and much more importantly
/// that she stops: at the answer, at the one-offer cap, at a tour already taken, and behind every
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

    /// <summary>
    /// AND AN UPGRADER IS LEFT ALONE. This assertion is the reverse of the one it replaces, and
    /// deliberately so: an upgrader's launch already has What's New on it, offering the upgrade
    /// tour from the surface they are looking at. A companion materialising on top of that to offer
    /// a second tour is precisely the pile-up the first-run redesign exists to end, so the knock
    /// gave the population up rather than competing for it.
    ///
    /// <para>None, not Walked. They have not taken anything; they are simply not owed it by
    /// her.</para>
    /// </summary>
    [Fact]
    public void AnUpgraderIsNotTheKnocksProblemAnyMore()
    {
        var (m, w) = Upgrader();
        Assert.Equal(EmiKnockPopulation.None, m.Population(w));
        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
        Assert.Null(m.ContactMoment(w));
    }

    /// <summary>
    /// The upgrade tour did not stop existing, it changed owner. The moment id and the verb are
    /// still addressable so the surface that took the population over has something to fire, and
    /// this pins them against a future tidy-up that deletes them as "unreachable".
    /// </summary>
    [Fact]
    public void TheUpgradeOfferSurvivesAsAnIdEvenThoughTheKnockNeverReachesIt()
    {
        Assert.Equal("firstContactUpgrade", EmiKnockMachine.UpgradeMoment);
        Assert.Equal(EmiKnockMachine.UpgradeTour, EmiKnockMachine.TourFor(EmiKnockPopulation.Upgrader));
        Assert.Equal("tour:upgrade", EmiKnockMachine.EffectFor(EmiKnockPopulation.Upgrader));
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
    /// BRAKE 2. ONE offer, ever. The cap is checked on its own, with the state still at "knocked"
    /// rather than "spent", so it cannot be passing because of brake 1 - which matters more than
    /// ever now, because with the cap at one this brake is the ONLY thing that makes a no
    /// permanent. A no writes nothing; it simply never earns a second offer.
    /// </summary>
    [Fact]
    public void Brake2_TheOfferCapEndsIt()
    {
        Assert.Equal(1, EmiKnockMachine.OfferCap);

        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;

        w.KnockOffers = 0;
        Assert.True(m.OfferOwed(w));

        w.KnockOffers = 1;
        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
    }

    /// <summary>
    /// THE OWNER'S PROMISE, stated as one assertion: a ledger that says she has knocked and made
    /// her one offer is never owed another, on any launch, however long ago it was and whatever
    /// they answered. This is the state every install lands in seconds after its first launch, so
    /// it is the state that has to hold forever.
    /// </summary>
    [Fact]
    public void KnockedWithOneOfferIsNeverOwedAgain()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;
        w.KnockOffers = 1;

        // A year later, a new launch, nothing else owning the screen, the walk still not taken.
        w.KnockAtUtc = w.LaunchStartedUtc.AddDays(-365).Ticks;

        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));

        // ...and the same with the old two-offer ledger from before the redesign.
        w.KnockOffers = 2;
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

    /// <summary>
    /// Brake 4 is PER TOUR, not a single "has been shown something" flag - which is the shape of
    /// gate this codebase has been bitten by before. A fresh install that somehow has the upgrade
    /// tour latched (a restored settings file, a hand-edited ledger) is still owed the short walk,
    /// because the short walk is the tour she is actually offering.
    /// </summary>
    [Fact]
    public void Brake4_IsPerTourAndNotABareSeenFlag()
    {
        var (m, w) = Fresh();
        w.Tours.Add(EmiKnockMachine.UpgradeTour);

        Assert.Equal(EmiKnockPopulation.Fresh, m.Population(w));
        Assert.True(m.MayKnock(w));
        Assert.Equal(EmiKnockMachine.FreshMoment, m.ContactMoment(w));
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
    //  never twice, and never again
    // =========================================================================================

    /// <summary>
    /// NEVER TWICE IN ONE SITTING. With the cap at one, brake 2 stops this on its own for any
    /// honest ledger - so the world here is a DISHONEST one, which is the only way the guard is
    /// reachable and therefore the only way it is worth testing: the state says she has knocked
    /// this launch and the counter says no offer was spent. A QA replay leaves exactly that, and so
    /// does a settings file rolled back under a running app.
    ///
    /// <para>Without the launch-time guard, that world would put her back on screen seconds after
    /// the user sent her away - the single worst thing this feature can do.</para>
    /// </summary>
    [Fact]
    public void SheNeverKnocksTwiceInOneLaunch()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;
        w.KnockOffers = 0;
        w.KnockAtUtc = w.LaunchStartedUtc.AddMinutes(2).Ticks;

        Assert.True(m.OfferOwed(w));      // the brakes are all clear...
        Assert.False(m.MayKnock(w));      // ...and she still stays put this sitting
    }

    /// <summary>
    /// A SHRUG IS FINAL. She came out, she asked, they said no or simply closed her - and there is
    /// no later launch on which that turns back into an offer. This is the test the old machine
    /// could not have passed: it deliberately gave a shrug one more go, back when the "offer" had
    /// been three pulses on a ring nobody knew was a button.
    /// </summary>
    [Fact]
    public void AShruggedOfferNeverComesBack()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;   // asked, not answered: they shrugged
        w.KnockOffers = 1;
        w.KnockAtUtc = w.LaunchStartedUtc.AddDays(-1).Ticks;   // a different launch entirely

        Assert.False(m.OfferOwed(w));
        Assert.False(m.MayKnock(w));
    }

    /// <summary>
    /// THE ORDER TRAP. The offer is spent at the knock and the summon it triggers reads
    /// <see cref="EmiKnockMachine.ContactMoment"/> a beat later - by which time the counter is
    /// already at the cap. So the contact moment must NOT consult the counter: if it did, the
    /// knock's own summon would be told "nothing scripted to say" and she would arrive out of
    /// nowhere and play the ambient hello, with the walk never offered at all.
    /// </summary>
    [Fact]
    public void TheContactMomentStillSpeaksAfterTheOfferIsSpent()
    {
        var (m, w) = Fresh();
        w.KnockState = EmiKnockMachine.Knocked;
        w.KnockOffers = EmiKnockMachine.OfferCap;   // exactly what NoteKnocked just wrote

        Assert.Equal(EmiKnockMachine.FreshMoment, m.ContactMoment(w));
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
/// THE CONTENT SIDE of the knock: the two contact moments, as they are actually shipped in
/// <c>Resources/emi/desk-lines.json</c>.
///
/// <para>A moment id is fired by string and the bus drops what it does not know in silence, so a
/// definition that is missing, or one whose dials contradict the machine, costs a beat forever
/// with nothing on screen to say so. <c>EmiMomentIdWiringTests</c> already proves the ids exist;
/// what is checked here is that the DIALS behind them still say what the contract says they
/// should - above all the two that make the offer reachable at all (<c>scripted</c>, priority 3)
/// and the one that is brake 3 (<c>limit: ever/1</c>).</para>
///
/// <para>There were three until the first-run redesign. <c>firstContactLater</c> was the softer
/// re-offer a shrug used to earn, and it went with the second offer.</para>
/// </summary>
public class EmiKnockLinesFileTests
{
    private static System.Text.Json.JsonElement LinesFile()
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
        return doc.RootElement.Clone();
    }

    private static System.Text.Json.JsonElement Moments() => LinesFile().GetProperty("moments");

    /// <summary>Every ask authored against a moment, in file order.</summary>
    private static List<System.Text.Json.JsonElement> AsksFor(string moment)
    {
        var found = new List<System.Text.Json.JsonElement>();
        var root = LinesFile();
        if (!root.TryGetProperty("asks", out var asks)) return found;
        foreach (var a in asks.EnumerateArray())
        {
            if (a.TryGetProperty("moment", out var m) && m.GetString() == moment) found.Add(a);
        }
        return found;
    }

    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
    public void EveryContactMomentIsDefined(string id)
    {
        Assert.True(Moments().TryGetProperty(id, out _), id + " is not in desk-lines.json");
    }

    /// <summary>
    /// AND THE LATER BEAT IS GONE, from the content side as well as from the machine. Left behind,
    /// it would be a written, limited, ceremony-priority moment that nothing can ever fire: dead
    /// weight that reads like a live feature to the next person who opens the file.
    /// </summary>
    [Fact]
    public void TheRetiredLaterMomentIsNotInTheLinesFile()
    {
        Assert.False(Moments().TryGetProperty("firstContactLater", out _),
            "firstContactLater is still defined, but nothing can reach it: the second offer is gone");

        var root = LinesFile();
        Assert.False(root.GetProperty("pools").TryGetProperty("firstContactLater", out _),
            "the firstContactLater pool is still shipped with no moment to draw it");
    }

    /// <summary>
    /// The moments that carry the offer must be <c>scripted</c>, or the ask is refused by a cadence
    /// gate the knock cannot possibly satisfy (never before the third summon; this is the first)
    /// and she arrives out of nowhere with nothing to ask.
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
    /// BRAKE 3, on the content side. Each contact moment fires once in a lifetime, whatever the
    /// machine's own counters say - the two ceilings are deliberately independent.
    /// </summary>
    [Theory]
    [InlineData(EmiKnockMachine.FreshMoment)]
    [InlineData(EmiKnockMachine.UpgradeMoment)]
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
    public void EveryContactMomentKeepsTheSpiceDown(string id)
    {
        Assert.True(Moments().GetProperty(id).GetProperty("spiceCeiling").GetInt32() <= 1,
            id + " must not reach the top spice shelf on somebody's first minute");
    }

    /// <summary>
    /// THE ASK IS THE FIRST THING SHE EVER SAYS, so it has to introduce her AND land on the walk in
    /// one breath.
    ///
    /// <para>The engine's ask branch RETURNS the ask and never the pool line behind it, so when the
    /// offer fires, the ask's <c>q</c> is the whole of first contact - there is no greeting bubble
    /// in front of it any more, because nobody clicked anything to summon her. An ask left reading
    /// "show you around?" would therefore be a stranger materialising mid-sentence.</para>
    ///
    /// <para>Every one of them carries <c>tour:shortwalk</c>: this is the only offer she makes on
    /// this moment, and there is no second one coming, so an ask wired to anything else would
    /// silently cost a user the app's only tour.</para>
    /// </summary>
    [Fact]
    public void EveryFirstContactAskIntroducesHerAndOffersTheWalk()
    {
        var asks = AsksFor(EmiKnockMachine.FreshMoment);
        Assert.True(asks.Count >= 3, "the contract asks for 3+ variants; found " + asks.Count);

        foreach (var a in asks)
        {
            var id = a.GetProperty("id").GetString() ?? "?";
            var q = a.GetProperty("q").GetString() ?? string.Empty;

            Assert.Equal("tour:shortwalk", a.GetProperty("effect").GetString());
            Assert.Equal(2, a.GetProperty("chips").GetArrayLength());

            // It ends on the offer: the last thing on the glass is the question, not a preamble.
            Assert.EndsWith("?", q.TrimEnd());

            // ...and it says who is asking, which the pool line used to do.
            Assert.Contains("emi", q, StringComparison.Ordinal);

            // VOICE.md, the two rules a test can actually hold: her own lowercase, and no dashes.
            Assert.Equal(q.ToLowerInvariant(), q);
            Assert.DoesNotContain("\u2014", q);   // em dash
            Assert.DoesNotContain("\u2013", q);   // en dash
            Assert.True(q.Length <= 60, id + " is " + q.Length + " chars; VOICE.md caps a line at 60");
        }
    }

    /// <summary>
    /// AND THE POOL BEHIND IT MUST NOT OFFER THE WALK. Those lines are the FALLBACK: the engine
    /// only reaches them when <c>PickAsk</c> came back empty, which on this moment means the walk
    /// is not feasible at all (already taken, a session running, a tutorial up). Copy that ends on
    /// "shall we?" there is an offer with no chips under it and nothing behind the answer.
    /// </summary>
    [Fact]
    public void TheFirstContactPoolIsAGreetingNotASecondOffer()
    {
        var pool = LinesFile().GetProperty("pools").GetProperty(EmiKnockMachine.FreshMoment);
        Assert.True(pool.GetArrayLength() >= 8, "the shuffle bag needs 8+ lines");

        foreach (var line in pool.EnumerateArray())
        {
            var id = line.GetProperty("id").GetString() ?? "?";
            var t = line.GetProperty("t").GetString() ?? string.Empty;
            Assert.False(t.TrimEnd().EndsWith("?", StringComparison.Ordinal),
                id + " ends on a question, but this pool only ever plays when the walk cannot be "
                   + "offered - there are no chips coming to answer it");
        }
    }

    /// <summary>
    /// NO SCRIPTED MOMENT SPRAWL. The bypass is narrow on purpose: it exists for the knock's one
    /// offer and it is not a general "let her ask sooner" switch. If a fourth moment ever wants it,
    /// that is a decision somebody should have to make deliberately, by editing this list.
    ///
    /// <para>Wave 3 made that decision once, for <c>noMediaYet</c>: an empty library is the app's
    /// most common first-run dead end, its ask IS the fix, and a new user who hits it inside the
    /// ten-minute inter-ask gap would otherwise be shown the problem and never the door.</para>
    /// </summary>
    [Fact]
    public void OnlyTheKnockIsScripted()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            EmiKnockMachine.FreshMoment,
            EmiKnockMachine.UpgradeMoment,
            "noMediaYet"
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
