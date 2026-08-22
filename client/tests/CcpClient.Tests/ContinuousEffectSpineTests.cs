using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THREE modules under one spine, and only two of them have a clock.
///
/// <para><b>The claim under test.</b> An earlier packet proved the spine was a template for a second PACED
/// module and said in its own verdict that this was the untested axis: the spine might have been
/// quietly assumed to be a scheduler. Every fact below fails if <see cref="ISessionEffect"/> turns
/// out to need a schedule — and the sharpest of them is the one that asserts the continuous module
/// puts <b>nothing at all</b> on the session clock while running.</para>
///
/// <para><b>Zero wall-clock.</b> The two paced modules pace on an injected clock advanced by hand;
/// the third has nothing to advance.</para>
/// </summary>
public class ContinuousEffectSpineTests
{
    // ---------------------------------------------------------------------------------
    //  one session, three modules, two clocks' worth of work between them
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task PressingStart_ArmsAllThree_AndTheContinuousOneIsAlreadyRunningWithNoClockAtAll()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();

        Assert.True(rig.Engine.Start());

        // The two paced modules put one one-shot each on the clock; the continuous module puts
        // NOTHING there — and it is nonetheless already doing its work. That pair of assertions is
        // the whole packet in four lines.
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.True(rig.Subliminals.ScheduleArmed);
        Assert.Equal(2, rig.Clock.PendingCount);

        Assert.True(rig.PinkSurface.Showing);
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);
    }

    [Fact]
    public async Task NoAmountOfClockChangesTheContinuousModule_BecauseItIsNotPaced()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        var engagementsAtStart = rig.PinkSurface.Engagements;
        for (var i = 0; i < 20; i++)
        {
            rig.Clock.Advance(rig.LongestFlashInterval);
        }

        // Twenty flash windows have gone by and the tint has neither re-placed itself nor gone
        // away. A module wrapped in a fake timer to make it fit the spine would show movement here.
        Assert.Equal(engagementsAtStart, rig.PinkSurface.Engagements);
        Assert.Equal(0, rig.PinkSurface.Withdrawals);
        Assert.True(rig.PinkSurface.Showing);
        Assert.True(rig.Flash.FlashCount >= 20);
    }

    [Fact]
    public async Task Stop_TakesTheTintOffAtOnce_AndNoAmountOfClockBringsAnyOfTheThreeBack()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestFlashInterval);

        Assert.True(rig.Engine.Stop());

        // WPF's stop closes every filter window (MainWindow.StartStop.cs:338 ->
        // OverlayService.cs:407 -> :1233-1249).
        //
        // WHAT THIS ASSERTS, precisely (corrected at final review — the comment used to say
        // the withdraw "has already happened when Stop returns", and that is not what is proved):
        // Stop DECIDES synchronously, and the withdraw is POSTED — ReleaseWork ends in
        // Signal.Post(_surface.Withdraw), and EffectSignal.Post is "always a post, never inline"
        // by contract. This rig's dispatch runs a post on the calling thread, so the effect is
        // observable here on the same line; on a real dispatcher it lands on the UI thread's next
        // turn. What is genuinely proved is the ORDER — nothing is left scheduled, nothing can
        // re-place itself afterwards, and the teardown is queued before Stop returns rather than
        // left to some later reconcile.
        Assert.False(rig.PinkSurface.Showing);
        Assert.Equal(0, rig.Clock.PendingCount);

        var flashes = rig.Flash.FlashCount;
        var subliminals = rig.Subliminals.SubliminalCount;
        var engagements = rig.PinkSurface.Engagements;
        for (var i = 0; i < 5; i++)
        {
            rig.Clock.Advance(rig.LongestFlashInterval);
        }

        Assert.Equal(flashes, rig.Flash.FlashCount);
        Assert.Equal(subliminals, rig.Subliminals.SubliminalCount);
        Assert.Equal(engagements, rig.PinkSurface.Engagements);
        Assert.False(rig.PinkSurface.Showing);
    }

    // ---------------------------------------------------------------------------------
    //  the rack's second gesture, on a module with no schedule
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheQuickToggleSwitchesTheContinuousModuleOnMidSession_AndTheTintIsUpImmediately()
    {
        await using var rig = await Rig.StartAsync();
        rig.Engine.Start();

        Assert.False(rig.PinkSurface.Showing);
        Assert.Equal(EffectDotState.Off, rig.PinkFilter.Dot);

        Assert.True(rig.Engine.QuickToggle(PinkFilterEffect.EffectId));

        // The rack's own onboarding text promises this ("right-click turns it on"), and for a
        // continuous module "on" means visible NOW — there is no next interval to wait for.
        Assert.True(rig.PinkFilter.Enabled);
        Assert.True(rig.PinkSurface.Showing);
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);
    }

    [Fact]
    public async Task TheQuickToggleSwitchesItOffMidSession_AndTheTintIsGoneAtOnce()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();
        Assert.True(rig.PinkSurface.Showing);

        Assert.True(rig.Engine.QuickToggle(PinkFilterEffect.EffectId));

        Assert.False(rig.PinkFilter.Enabled);
        Assert.False(rig.PinkSurface.Showing);
        Assert.Equal(EffectDotState.Off, rig.PinkFilter.Dot);

        // And the session records WHY, per module, exactly as it does for a paced one.
        var outcome = Assert.IsType<CapabilityState.Unavailable>(
            rig.Engine.ArmOutcomes[PinkFilterEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.EffectDialOff, outcome.Reason.Code);
    }

    [Fact]
    public async Task TheThreeModulesDialsAreIndependent_AndSwitchingOneNeverMovesAnother()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        rig.Engine.QuickToggle(FlashImagesEffect.EffectId);

        // Flash ships on (AppSettings.cs:751) and has just been switched off; the other two are
        // untouched. Three modules sharing one document, or one shared arm flag, would show here.
        Assert.False(rig.Flash.Enabled);
        Assert.False(rig.Subliminals.Enabled);
        Assert.True(rig.PinkFilter.Enabled);
        Assert.True(rig.PinkSurface.Showing);
        Assert.False(rig.Flash.ScheduleArmed);
    }

    // ---------------------------------------------------------------------------------
    //  the shared body really is shared, and each module's generation really is its own
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task EveryModuleOwnsItsOwnGeneration_AndAllThreeTerminateCancelledOnStop()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        var completions = new[]
        {
            rig.Flash.Completion,
            rig.Subliminals.Completion,
            rig.PinkFilter.Completion,
        };
        Assert.All(completions, Assert.NotNull);
        Assert.Equal(3, completions.Distinct().Count());

        rig.Engine.Stop();

        foreach (var completion in completions)
        {
            Assert.IsType<OperationOutcome.Cancelled>(await completion!);
        }
    }

    [Fact]
    public async Task TheContinuousModulesArmOutcomeIsRecordedLikeAnyOthers_AndTheSessionNamesEveryHole()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();

        rig.Engine.Start();

        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[FlashImagesEffect.EffectId]);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[PinkFilterEffect.EffectId]);

        // Subliminals ships off, and so does the Intensity Ramp
        // (CCP.Core/Models/AppSettings.cs:2574-2579); the continuous module took the session.
        //
        // Two more modules that ship off were added later — Mind Wipe and Brain Drain's audio half — and
        // then a third, Lock Card (AppSettings.cs:3331), and a fourth, Bubble Count, so
        // this list grows again in rack order. The FACT is unchanged and the assertion is stronger:
        // six modules declined the same session for the same reason and all six are named. A later packet
        // adds a fifth that ships off (Bubble Pop, CCP.Core/Models/AppSettings.cs:2737-2738's BubblesEnabled default),
        // and it lands FIRST in GAMES & CARDS because that is the rack's order
        // (StudioTabView.xaml.cs:499).
        // A later packet adds a sixth that ships off (Bouncing Text,
        // CCP.Core/Models/AppSettings.cs:3598), and it lands after Spiral Overlay in the rack's order.
        Assert.Equal(
            [
                MandatoryVideoEffect.EffectId, SubliminalsEffect.EffectId, BouncingTextEffect.EffectId,
                BubblePopEffect.EffectId, BubbleCountEffect.EffectId, LockCardEffect.EffectId,
                MindWipeEffect.EffectId, BrainDrainEffect.EffectId, IntensityRampEffect.EffectId,
            ],
            rig.Engine.ArmRefusals.Select(r => r.Id));
    }

    [Fact]
    public async Task TheRackOrderIsWpfsOwn_AndTheContinuousModuleIsArmedLastBecauseWpfArmsItLast()
    {
        await using var rig = await Rig.StartAsync();

        // StartEngine's order: flash (:178), subliminal (:186), then the overlay service that owns
        // the continuous pair (:192-193). It is also StudioTabView's rack order (:484-493) with the
        // unported rows removed. The list IS the order, so this pins it where a reader will look.
        //
        // A later packet added the fourth member. The fact is unchanged and the assertion is STRONGER: the
        // rack's order is upstream's for four modules now, and Spiral Overlay sits where the rack
        // puts it (fourth, between Subliminals and Pink Filter) rather than where OverlayService's
        // own body starts it (second of the pair). Those two orders disagree upstream and D90
        // records which one the port took and why.
        //
        // The Intensity Ramp adds the fifth, and it is the first member from a group other than EFFECTS. It goes
        // LAST for two upstream reasons that agree: the rack puts TIMING after EFFECTS, GAMES & CARDS
        // and IMMERSION (StudioTabView.xaml.cs:482-541), and StartEngine starts the ramp timer after
        // every effect service (MainWindow.StartStop.cs:265-269).
        //
        // Two IMMERSION rows were added later, and they land BETWEEN the effects and the ramp — which is
        // where both orders that matter put them, and they agree again: the rack's group order, and
        // StartEngine's own (Mind Wipe :229-230 and Brain Drain :241-244 come after every effect
        // service and before the ramp timer). Mind Wipe first, Brain Drain second, in both.
        //
        // Lock Card opens the GAMES & CARDS group, and it lands between the effects and
        // the IMMERSION pair for the same reason: that is the rack's group order
        // (StudioTabView.xaml.cs:483/498/508/530) AND StartEngine's own (:206-209 comes after the
        // overlay service and before Mind Wipe at :229-230).
        //
        // Bubble Pop then sits at the HEAD of that group, which is upstream's own rack order
        // (Add("bubbles", ...) at :499, before bubblecount at :501 and lockcard at :503) and is
        // uncontested by StartEngine, which never starts the ambient bubble game at all.
        Assert.Equal(
            [
                FlashImagesEffect.EffectId, MandatoryVideoEffect.EffectId, SubliminalsEffect.EffectId,
                SpiralOverlayEffect.EffectId, BouncingTextEffect.EffectId,
                PinkFilterEffect.EffectId, BubblePopEffect.EffectId, BubbleCountEffect.EffectId,
                LockCardEffect.EffectId,
                MindWipeEffect.EffectId, BrainDrainEffect.EffectId, IntensityRampEffect.EffectId,
            ],
            rig.Engine.Effects.Select(e => e.Id));
    }

    // ---------------------------------------------------------------------------------
    //  teardown, which reaches the module without going through Stop
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task HostTeardown_StopsTheContinuousModuleToo_AndItsDotStopsClaimingToBeLive()
    {
        var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);

        await rig.Host.ShutdownAsync();

        // The participant's stop runs Engine.Stop() before the stores stop, and the host has
        // already cancelled every generation. Either route alone must leave the dot honest.
        Assert.False(rig.PinkSurface.Showing);
        Assert.NotEqual(EffectDotState.Live, rig.PinkFilter.Dot);
        await rig.DisposeAsync();
    }

    // =====================================================================================

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _directory;

        private Rig(ApplicationHost host, SessionParticipant session, ManualSessionClock clock,
            RecordingPinkSurface pinkSurface, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            PinkSurface = pinkSurface;
            _directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public RecordingPinkSurface PinkSurface { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        public SubliminalsEffect Subliminals => Session.Subliminals;

        public PinkFilterEffect PinkFilter => Session.PinkFilter;

        public TimeSpan LongestFlashInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);

        public static async Task<Rig> StartAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-sp105-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var pink = new RecordingPinkSurface();
            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(3), new NullFlashSurface(), new NullSubliminalSurface(),
                // This project has no Avalonia runtime to ask, which is exactly why EffectSignal
                // takes the predicate rather than reaching for the dispatcher itself.
                static () => true,
                pink);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, session, clock, pink, dir);
        }

        public void EnableSubliminals() => Session.SubliminalPreset.Mutate(p => p.Enabled = true);

        public void EnablePinkFilter() => Session.PinkFilterPreset.Mutate(p => p.Enabled = true);

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>A pink-filter surface that records what the module asked for and never touches a
    /// screen. It is the module's only evidence that anything is up, which is what makes the dot's
    /// third state assertable here at all.</summary>
    private sealed class RecordingPinkSurface : IPinkFilterSurface
    {
        public int Engagements { get; private set; }

        public int Withdrawals { get; private set; }

        public bool Showing { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(PinkFilterTint tint)
        {
            Engagements++;
            Showing = true;
            LastPlacement = new CapabilityState.Available("recording surface: the tint is up");
            return LastPlacement;
        }

        public void Withdraw()
        {
            Withdrawals++;
            Showing = false;
        }
    }

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> images)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullSubliminalSurface : ISubliminalSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class StubPool(int population) : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) =>
            [.. Enumerable.Range(0, Math.Min(count, population)).Select(i => $"image-{i}.png")];
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>The manual clock, in the established shape. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
                }
            }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
