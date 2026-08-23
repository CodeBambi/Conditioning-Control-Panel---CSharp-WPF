using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>Two ramps, two opacities, one document.</b> The global Intensity Ramp and a scripted session
/// both drive spiral and pink opacity, and until this stood down the user saw whichever wrote last.
///
/// <para>Upstream's answer is a PER-LINK stand-down read once per tick from
/// <c>_sessionEngine?.IsRunning == true</c> (<c>MainWindow/MainWindow.StartStop.cs:492</c>): the
/// flash (<c>:509</c>), spiral (<c>:515</c>) and pink (<c>:523</c>) link branches skip their write
/// while a session runs, and the master-volume (<c>:529</c>) and subliminal-volume (<c>:537</c>)
/// branches deliberately do not. Upstream states the reason itself at <c>:490-491</c>: "sessions
/// have their own built-in ramping … prevents the two systems from fighting and causing values to
/// jump around".</para>
///
/// <para><b>These facts are composed, not doubled.</b> The rig is the real
/// <see cref="SessionParticipant"/> — the real <see cref="IntensityRampEffect"/> over the real
/// <see cref="SpiralOpacityDial"/> and <see cref="PinkFilterOpacityDial"/>, and the real
/// <see cref="ScriptedSessionRun"/> over the same eleven documents — because the defect is exactly
/// that those two objects share a document, and a rig that separated them could not have it. BOTH
/// clocks are injected and moved BY HAND: the ramp's 2-second cadence runs on the session clock,
/// the run's 1-second tick on the scripted clock, and nothing here reads a wall clock, sleeps or
/// polls.</para>
///
/// <para>Nothing here claims a pixel. What is asserted is the number in the document every module
/// and every panel reads — which is the whole of what this module does
/// (<c>MainWindow.StartStop.cs:453-456</c>: "the settings write is the whole job").</para>
/// </summary>
public class RampSessionStandDownTests
{
    /// <summary>The user's own opacity, on both dials. Deliberately not a default, and deliberately
    /// the same on both so a value that came from the wrong place is obvious.</summary>
    private const int UsersOpacity = 20;

    /// <summary>What the session imposes. Different from <see cref="UsersOpacity"/> and different
    /// from every value the global ramp can reach from it (20 × 3.0 = 60 &gt; 50 is clamped to the
    /// pink ceiling, and the spiral would pass through 80 only at the very end of its climb — see
    /// the fact that pins the ramp's own progress).</summary>
    private const int SessionsSpiralOpacity = 80;

    private const int SessionsPinkOpacity = 45;

    [Fact]
    public async Task WhileASessionRunsTheGlobalRampWritesNeitherOpacity_AndTheSESSIONSVALUESSTAND()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersOpacities();
        rig.LinkTheRamp();

        // The manual engine, exactly as the user starts it. The ramp takes custody of both dials.
        rig.Participant.Engine.Start();
        rig.AdvanceSessionClock(TimeSpan.FromMinutes(1));

        // THE NEGATIVE CONTROL, inside the fact: the global ramp really does drive these two dials.
        // Progress 1/10 on a linear curve, multiplier 1 + (3.0 - 1) × 0.1 = 1.2, so both land on
        // (int)(20 × 1.2) = 24 (MainWindow.StartStop.cs:501, :517, :523).
        Assert.Equal(24, rig.SpiralOpacity);
        Assert.Equal(24, rig.PinkOpacity);
        Assert.Equal(
            [FlashImagesEffect.EffectId, PinkFilterEffect.EffectId, SpiralOverlayEffect.EffectId],
            rig.Participant.Ramp.HeldDials.Order(StringComparer.Ordinal));

        Assert.True(rig.Participant.Scripted.Start(TheSession()));

        // The session imposed its own opacities at t=0 and they are what is in the documents — the
        // ramp handed its borrowed values back on the way in and took no new custody.
        Assert.Equal(SessionsSpiralOpacity, rig.SpiralOpacity);
        Assert.Equal(SessionsPinkOpacity, rig.PinkOpacity);
        Assert.Empty(rig.Participant.Ramp.HeldDials);

        // NOW BOTH RAMPS RUN OVER THE SAME TICK WINDOW. Two minutes of the run's own 1-second ticks
        // and sixty of the global ramp's 2-second ones, interleaved a tick at a time.
        for (var i = 0; i < 60; i++)
        {
            rig.AdvanceScriptedClock(TimeSpan.FromSeconds(2));
            rig.AdvanceSessionClock(TimeSpan.FromSeconds(2));
        }

        // The SESSION's ramp is live and climbing — this is not a fact about a session that stopped.
        var parked = rig.Participant.Scripted.Ramp;
        Assert.NotNull(parked.PinkOpacityPercent);
        Assert.NotNull(parked.SpiralOpacityPercent);

        // And after every one of those ticks the documents still hold the SESSION's answer. Before
        // the stand-down the global ramp wrote (int)(20 × mult) into both of them every 2 seconds.
        Assert.Equal(SessionsSpiralOpacity, rig.SpiralOpacity);
        Assert.Equal(SessionsPinkOpacity, rig.PinkOpacity);
        Assert.Empty(rig.Participant.Ramp.HeldDials);
    }

    [Fact]
    public async Task WhenTheSessionENDSTheUserGetsTheirOWNOpacityBack_NotTheOneTheRampHadReached()
    {
        await using var rig = await Rig.StartAsync();
        rig.WriteTheUsersOpacities();
        rig.LinkTheRamp();

        rig.Participant.Engine.Start();
        rig.AdvanceSessionClock(TimeSpan.FromMinutes(1));
        Assert.Equal(24, rig.SpiralOpacity);
        Assert.Equal(24, rig.PinkOpacity);

        Assert.True(rig.Participant.Scripted.Start(TheSession()));
        for (var i = 0; i < 60; i++)
        {
            rig.AdvanceScriptedClock(TimeSpan.FromSeconds(2));
            rig.AdvanceSessionClock(TimeSpan.FromSeconds(2));
        }

        Assert.True(rig.Participant.Scripted.Stop());

        // THE VALUE THAT COMES BACK IS 20, THE USER'S OWN — not 24, which is what the global ramp
        // had climbed to when the session started. A session snapshots the dials it borrows, so a
        // snapshot taken while the ramp still held them would preserve the RAMP's value as if the
        // user had chosen it and hand it back for good at the next stop; that is upstream's
        // #471/#476 class, and it is why ScriptedSessionRun.Start hands the engine's borrowed dials
        // back BEFORE it captures (upstream never meets this — its session touches neither
        // StartEngine nor StopEngine, Services/Session/SessionEngine.cs:148-270, and its ramp bases
        // are captured once before any session at MainWindow/MainWindow.StartStop.cs:420-424).
        Assert.Equal(UsersOpacity, rig.SpiralOpacity);
        Assert.Equal(UsersOpacity, rig.PinkOpacity);

        // The stop re-armed the engine, so the ramp is holding the USER's values again and climbing
        // from them — and STOP still gives exactly those back (:439-481).
        Assert.Equal(
            [FlashImagesEffect.EffectId, PinkFilterEffect.EffectId, SpiralOverlayEffect.EffectId],
            rig.Participant.Ramp.HeldDials.Order(StringComparer.Ordinal));
        Assert.Equal(UsersOpacity, rig.Participant.Ramp.BaseValueFor(SpiralOverlayEffect.EffectId));
        Assert.Equal(UsersOpacity, rig.Participant.Ramp.BaseValueFor(PinkFilterEffect.EffectId));

        rig.AdvanceSessionClock(TimeSpan.FromMinutes(1));
        Assert.Equal(24, rig.SpiralOpacity);
        Assert.Equal(24, rig.PinkOpacity);

        rig.Participant.Engine.Stop();
        Assert.Equal(UsersOpacity, rig.SpiralOpacity);
        Assert.Equal(UsersOpacity, rig.PinkOpacity);
    }

    /// <summary>A session that turns both overlays on at t=0, so it owns both opacities from its
    /// first instant and no start-time jitter is in play (jitter applies only to a start minute
    /// already greater than zero — <c>Services/Session/SessionEngine.cs:784-795</c>).</summary>
    private static ScriptedSession TheSession() => new()
    {
        Id = "two_ramps",
        Name = "Two Ramps",
        Icon = "\U0001F300",
        DurationMinutes = 30,
        Settings = new ScriptedSessionSettings
        {
            PinkFilterEnabled = true,
            PinkFilterStartMinute = 0,
            PinkFilterStartOpacity = SessionsPinkOpacity,
            PinkFilterEndOpacity = SessionsPinkOpacity,
            SpiralEnabled = true,
            SpiralStartMinute = 0,
            SpiralOpacity = SessionsSpiralOpacity,
            // Deliberately NOT equal to the start: upstream leaves a spiral alone when its two
            // opacities match (#897, Services/Session/SessionEngine.cs:625), and a session whose
            // spiral ramp is inert could not show that BOTH ramps were live over the same ticks.
            SpiralOpacityEnd = SessionsSpiralOpacity + 10,
        },
    };

    private sealed class Rig : IAsyncDisposable
    {
        private Rig(
            ApplicationHost host, SessionParticipant participant, HandClock session,
            HandScriptedClock scripted, string directory)
        {
            Host = host;
            Participant = participant;
            SessionClock = session;
            ScriptedClock = scripted;
            Directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Participant { get; }

        public HandClock SessionClock { get; }

        public HandScriptedClock ScriptedClock { get; }

        public string Directory { get; }

        public int SpiralOpacity => Participant.SpiralPreset.Current.OpacityPercent;

        public int PinkOpacity => Participant.PinkFilterPreset.Current.OpacityPercent;

        public static async Task<Rig> StartAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "ccp-ramp-standdown-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var session = new HandClock();
            var scripted = new HandScriptedClock();
            var participant = new SessionParticipant(
                infra, directory, session, onSignalThread: () => true, scriptedClock: scripted);
            var host = new ApplicationHost(log, [participant], new StartupTrace(), registry, infra.UiDispatch);
            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));
            return new Rig(host, participant, session, scripted, directory);
        }

        public void WriteTheUsersOpacities()
        {
            Participant.SpiralPreset.Mutate(d => d.OpacityPercent = UsersOpacity);
            Participant.PinkFilterPreset.Mutate(d => d.OpacityPercent = UsersOpacity);
        }

        /// <summary>The ramp as a user would set it: on, both overlay links ticked, the flash link
        /// ticked too because it is upstream's FIRST guarded branch (<c>:509</c>) and the fact that
        /// custody covers three dials is what makes "no dial was held" mean something.</summary>
        public void LinkTheRamp() => Participant.RampPreset.Mutate(p =>
        {
            p.Enabled = true;
            p.DurationMinutes = 10;
            p.Multiplier = 3.0;
            p.Curve = RampCurve.Linear;
            p.LinkSpiralOpacity = true;
            p.LinkPinkFilterOpacity = true;
            p.LinkFlashOpacity = true;
        });

        public void AdvanceSessionClock(TimeSpan by) => SessionClock.Advance(by);

        public void AdvanceScriptedClock(TimeSpan by) => ScriptedClock.Advance(by);

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that will not delete is not this fact's subject.
            }
        }
    }

    /// <summary>
    /// The session clock, moved by hand: <see cref="Advance"/> walks the readings forward and fires
    /// every timer that came due, including ones a firing schedules, so a 2-second cadence really
    /// does tick thirty times over a minute. No wall clock is read anywhere.
    /// </summary>
    private sealed class HandClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow
        {
            get { lock (_timers) { return _now; } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            Entry entry;
            lock (_timers)
            {
                entry = new Entry { Due = _now + (due < TimeSpan.Zero ? TimeSpan.Zero : due), Fire = fire };
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target;
            lock (_timers)
            {
                target = _now + by;
            }

            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        _now = target;
                        return;
                    }

                    _timers.Remove(next);
                    _now = next.Due > _now ? next.Due : _now;
                }

                next.Fire();
            }
        }

        private void Cancel(Entry entry)
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(HandClock clock, Entry entry) : IDisposable
        {
            public void Dispose() => clock.Cancel(entry);
        }
    }

    /// <summary>The scripted run's clock, the same shape over its own two readings.</summary>
    private sealed class HandScriptedClock : IScriptedClock
    {
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _wall = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        private TimeSpan _monotonic = TimeSpan.Zero;

        public DateTimeOffset Now
        {
            get { lock (_timers) { return _wall; } }
        }

        public TimeSpan Monotonic
        {
            get { lock (_timers) { return _monotonic; } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            Entry entry;
            lock (_timers)
            {
                entry = new Entry
                {
                    Due = _monotonic + (due < TimeSpan.Zero ? TimeSpan.Zero : due),
                    Fire = fire,
                };
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            TimeSpan target;
            lock (_timers)
            {
                target = _monotonic + by;
            }

            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        _wall += target - _monotonic;
                        _monotonic = target;
                        return;
                    }

                    _timers.Remove(next);
                    if (next.Due > _monotonic)
                    {
                        _wall += next.Due - _monotonic;
                        _monotonic = next.Due;
                    }
                }

                next.Fire();
            }
        }

        private void Cancel(Entry entry)
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }
        }

        private sealed class Entry
        {
            public TimeSpan Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(HandScriptedClock clock, Entry entry) : IDisposable
        {
            public void Dispose() => clock.Cancel(entry);
        }
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
}
