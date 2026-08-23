using System.Text.Json;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 6 of the Arcademy row: THE PANIC LADDER
/// (<c>ArcademyHostService.HandlePanicPress</c>, <c>:321-340</c>;
/// <c>CloseActive</c>, <c>:246-263</c>; <c>OnResumeRequest</c>, <c>:346-370</c>).
///
/// <para><b>The failure these facts exist to prevent is upstream's own, in upstream's own
/// words</b> (<c>MainWindow/MainWindow.xaml.cs:1085-1090</c>): without this rung "two Esc taps
/// with no session running fell straight through to the 'not running' branch below and EXITED
/// THE WHOLE APP from inside a mini-game". So every press that reaches a live session must take
/// a RUNG — freeze, or leave — and must be visibly consumed, never fall through.</para>
///
/// <para><b>What these facts are NOT.</b> No page receives any of these frames: there is no
/// browser in this assembly, and this build has no window for the Arcademy and no app-wide panic
/// key hook to hand a press over from. They pin the LADDER — which rung a press takes, which
/// frame goes out, what the close plan is — never that a key was pressed on a desktop, that a
/// class visibly froze, or that a window went away.</para>
///
/// <para>The clock is INJECTED (<see cref="ArcademySession.Clock"/>): the two-second double-press
/// window is measured against a value this file sets, never against a wall clock.</para>
/// </summary>
public sealed class ArcademyPanicTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<object> _posted = [];
    private readonly List<ArcademyCloseRequest> _closes = [];
    private readonly string _dir;
    private readonly PersistenceStore<ArcademySettingsDocument> _store;
    private DateTimeOffset _now = new(2026, 8, 23, 21, 30, 0, TimeSpan.FromHours(2));

    public ArcademyPanicTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-panic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor("ArcademyPanicTests"),
            new SinkAdapter(_log),
            Path.Combine(_dir, ArcademySettingsDocument.FileName),
            ArcademySettingsDocument.CurrentSchemaVersion);
        _ = _store.StartAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _ = _store.StopAsync();
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // best-effort teardown
        }
    }

    /// <summary>A live session with its clock and its close sink wired to this fixture.</summary>
    private ArcademySession NewSession()
    {
        var session = new ArcademySession(
            _store, new ArcademyAppFacts(), frame => _posted.Add(frame), new SinkAdapter(_log))
        {
            Clock = () => _now,
        };
        session.CloseRequested += _closes.Add;
        return session;
    }

    /// <summary>A session that has completed its boot handshake, with the handshake frames
    /// cleared so a fact reads only what the ladder produced.</summary>
    private ArcademySession BootedSession()
    {
        var session = NewSession();
        session.Ready();
        _posted.Clear();
        return session;
    }

    private JsonElement Frame(int index) =>
        JsonDocument.Parse(ArcademyProtocol.SerializeForPage(_posted[index])).RootElement.Clone();

    private void AssertFrame(int index, string type)
    {
        Assert.Equal(type, Frame(index).GetProperty("type").GetString());
    }

    private void AssertSuspend(int index, bool on)
    {
        var frame = Frame(index);
        Assert.Equal("suspend", frame.GetProperty("type").GetString());
        Assert.Equal(on, frame.GetProperty("on").GetBoolean());
        // The reason is protocol vocabulary and the page reads it: only a "panic" suspend offers
        // the Resume affordance, because it is the one suspend with no natural end (:342-345).
        Assert.Equal("panic", frame.GetProperty("reason").GetString());
    }

    // ==================================================================================
    // Rung 1: freeze.
    // ==================================================================================

    [Fact]
    public void PanicPressOne_FreezesEverything_AndClosesNothing()
    {
        var session = BootedSession();

        var rung = session.PanicPress();

        // ONE frame: the freeze (:339). Not an end-run, not a close.
        var suspended = Assert.IsType<ArcademyPanicRung.Suspended>(rung);
        Assert.False(suspended.MidClass);
        Assert.Single(_posted);
        AssertSuspend(0, on: true);
        Assert.Empty(_closes);

        // The transcript says what the second press will do, which is the whole affordance
        // upstream logs (:337-338).
        Assert.Contains(_log, l => l.Contains("panic press 1") && l.Contains("press again to leave"));
    }

    [Fact]
    public void PanicPressOne_MidClass_FreezesTheClass_AndEndsNoClass()
    {
        var session = BootedSession();
        session.Handle("""{"type":"class-started","gameKey":"the-deep-end","gradeTier":2}""");
        Assert.True(session.ClassActive);

        var suspended = Assert.IsType<ArcademyPanicRung.Suspended>(session.PanicPress());

        // The freeze is INSIDE a class, and the class is still open: a class abandoned mid-panic
        // simply never ended, so nothing is graded, paid or credited (:318-320).
        Assert.True(suspended.MidClass);
        Assert.True(session.ClassActive);
        Assert.Single(_posted);
        AssertSuspend(0, on: true);
        Assert.DoesNotContain(_posted, f => ArcademyProtocol.SerializeForPage(f).Contains("payout-result", StringComparison.Ordinal));
    }

    // ==================================================================================
    // Rung 2: leave — and the press that only LOOKS like rung 2.
    // ==================================================================================

    [Fact]
    public void PanicPressTwo_InsideTheDoublePressWindow_ClosesGracefully()
    {
        var session = BootedSession();
        session.PanicPress();
        _now += TimeSpan.FromMilliseconds(1900);

        var closing = Assert.IsType<ArcademyPanicRung.Closing>(session.PanicPress());

        // The page is up, so it is ASKED to wind down rather than cut off (:250-256).
        Assert.Equal(ArcademyClosePlan.WaitForExitDone, closing.Plan);
        Assert.Equal(2, _posted.Count);
        AssertFrame(1, "end-run");
        // The end-run's reason is the literal "host" on every close path upstream (:254) — the
        // page reads it as "the host asked", not as the user's panic.
        Assert.Equal("host", Frame(1).GetProperty("reason").GetString());

        // Exactly ONE close, and it names the panic that caused it.
        var close = Assert.Single(_closes);
        Assert.Equal("panic", close.Reason);
        Assert.Equal(ArcademyClosePlan.WaitForExitDone, close.Plan);

        // Rung 2 does NOT re-freeze: the second frame is the wind-down, never a second suspend.
        Assert.DoesNotContain(_posted.Skip(1), f => ArcademyProtocol.SerializeForPage(f).Contains("suspend", StringComparison.Ordinal));
    }

    [Fact]
    public void PanicPress_AtTheWindowBoundary_StillLeaves_ButOneTickLaterIsAFreshRungOne()
    {
        // EXACTLY the window is still a double-tap (`<=`, :325).
        var session = BootedSession();
        session.PanicPress();
        _now += ArcademySession.PanicDoublePressWindow;
        Assert.IsType<ArcademyPanicRung.Closing>(session.PanicPress());

        // One tick past it is not. This is upstream's forgiving reading, and the reason it is
        // forgiving is stated at :313-317: "the emergency stop must not become an accidental
        // exit". A ladder that only counted presses would close here.
        _posted.Clear();
        _closes.Clear();
        var later = NewSession();
        later.Ready();
        _posted.Clear();
        later.PanicPress();
        _now += ArcademySession.PanicDoublePressWindow + TimeSpan.FromMilliseconds(1);

        var second = Assert.IsType<ArcademyPanicRung.Suspended>(later.PanicPress());

        Assert.False(second.MidClass);
        Assert.Equal(2, _posted.Count);
        AssertSuspend(0, on: true);
        AssertSuspend(1, on: true);
        Assert.Empty(_closes);

        // And the slow press RE-TIMES the window (:326), so the NEXT press is rung 2 again.
        _now += TimeSpan.FromMilliseconds(500);
        Assert.IsType<ArcademyPanicRung.Closing>(later.PanicPress());
    }

    [Fact]
    public void PanicPressTwo_OnAPageThatNeverBooted_ClosesImmediately()
    {
        // No `ready`, so no init: there is nothing on the other end to answer an exit-done, and
        // waiting for one is exactly how a panic press leaves someone stuck (:250, :259).
        var session = NewSession();

        session.PanicPress();
        _now += TimeSpan.FromMilliseconds(100);
        var closing = Assert.IsType<ArcademyPanicRung.Closing>(session.PanicPress());

        Assert.Equal(ArcademyClosePlan.Immediate, closing.Plan);
        Assert.Equal(ArcademyClosePlan.Immediate, Assert.Single(_closes).Plan);
        // The freeze still went out; the wind-down ASK did not, because nobody could wind down.
        Assert.Single(_posted);
        AssertSuspend(0, on: true);
    }

    [Fact]
    public void PanicPressTwo_WhileThePageIsAlreadyWindingDown_DoesNotAskTwice()
    {
        var session = BootedSession();

        // The page's own Esc-HOLD exit ladder ran first (:487-490).
        session.Handle("""{"type":"exit","reason":"esc-hold"}""");
        Assert.Equal(ArcademyClosePlan.WaitForExitDone, Assert.Single(_closes).Plan);
        Assert.Equal("page-exit", _closes[0].Reason);
        Assert.Empty(_posted);   // the page initiated it; the host does not ask it to do it again

        // Now the user panics on top of a wind-down that is already in flight.
        session.PanicPress();
        _now += TimeSpan.FromMilliseconds(200);
        var closing = Assert.IsType<ArcademyPanicRung.Closing>(session.PanicPress());

        // Immediate, and NO second end-run: a close on top of a close goes now (:250, :259).
        Assert.Equal(ArcademyClosePlan.Immediate, closing.Plan);
        Assert.Single(_posted);
        AssertSuspend(0, on: true);
        Assert.Equal(2, _closes.Count);
        Assert.Equal("panic", _closes[1].Reason);
    }

    [Fact]
    public void ExitDone_ClosesImmediately()
    {
        var session = BootedSession();
        session.CloseActive("host");
        Assert.Equal(ArcademyClosePlan.WaitForExitDone, Assert.Single(_closes).Plan);

        // The page finished winding down: the window may go NOW (:492-493).
        session.Handle("""{"type":"exit-done"}""");

        Assert.Equal(2, _closes.Count);
        Assert.Equal("exit-done", _closes[1].Reason);
        Assert.Equal(ArcademyClosePlan.Immediate, _closes[1].Plan);
    }

    // ==================================================================================
    // The un-freeze: a request, never a page-side resume.
    // ==================================================================================

    [Fact]
    public void ResumeRequest_IsGrantedOnlyForPanic_AndOnlyWithAFreezeOutstanding()
    {
        var session = BootedSession();

        // Nothing is frozen: ignored, and answered with nothing (:354-357).
        session.Handle("""{"type":"resume-request","reason":"panic"}""");
        Assert.Empty(_posted);
        Assert.Contains(_log, l => l.Contains("no panic suspend outstanding"));

        session.PanicPress();
        Assert.Single(_posted);

        // A video/audio-only resume is refused: those suspends have a natural end and only the
        // host lifts them (:349-352).
        session.Handle("""{"type":"resume-request","reason":"video"}""");
        Assert.Single(_posted);
        Assert.Contains(_log, l => l.Contains("only panic resumes on request"));

        // A missing reason READS AS "panic" (:348) — the page's own default — and is granted.
        session.Handle("""{"type":"resume-request"}""");
        Assert.Equal(2, _posted.Count);
        AssertSuspend(1, on: false);

        // The grant CLEARS the freeze (:366), so a repeat request — a double-clicked Resume
        // button, a page replaying its queue — is ignored rather than un-freezing what is no
        // longer frozen. Without that, the page would be told to resume a class it is running.
        session.Handle("""{"type":"resume-request","reason":"panic"}""");
        Assert.Equal(2, _posted.Count);

        // THE LADDER RE-ARMS AT RUNG 1 (:367): the very next press freezes again rather than
        // closing, even though it lands inside the double-press window of the press before it.
        _now += TimeSpan.FromMilliseconds(200);
        Assert.IsType<ArcademyPanicRung.Suspended>(session.PanicPress());
        Assert.Equal(3, _posted.Count);
        AssertSuspend(2, on: true);
        Assert.Empty(_closes);
    }

    [Fact]
    public void ResumeRequest_IsHeldWhileNativeStateOwnsTheScreen()
    {
        var owned = true;
        var session = BootedSession();
        session.NativeStateOwnsScreen = () => owned;
        session.PanicPress();
        Assert.Single(_posted);

        // Un-freezing here "would drop a class back on top of a video the user is supposed to be
        // watching" (:359-364). The freeze stands, and nothing is answered.
        session.Handle("""{"type":"resume-request","reason":"panic"}""");
        Assert.Single(_posted);
        Assert.Contains(_log, l => l.Contains("resume-request held"));

        // The hold is not a refusal: when the screen is free, the same request is granted.
        owned = false;
        session.Handle("""{"type":"resume-request","reason":"panic"}""");
        Assert.Equal(2, _posted.Count);
        AssertSuspend(1, on: false);
    }

    // ==================================================================================
    // The hand-off contract: consumed, isolated, and never thrown back at the key.
    // ==================================================================================

    [Fact]
    public void PanicPress_WithNoLiveSession_IsNotConsumed()
    {
        // Upstream's first line is `if (_host == null) return;` (:322): with no live Arcademy the
        // press belongs to the app-wide ladder, and the hand-off is gated on IsActive
        // (MainWindow/MainWindow.xaml.cs:1092). A session that is over must say so rather than
        // silently swallowing a press nothing else will then handle.
        var session = BootedSession();
        session.Dispose();

        Assert.IsType<ArcademyPanicRung.NotLive>(session.PanicPress());
        Assert.Empty(_posted);
        Assert.Empty(_closes);
    }

    [Fact]
    public void AThrowingCloseHandler_CannotThrowBackThroughThePanicKey()
    {
        var session = BootedSession();
        var reached = 0;
        session.CloseRequested += _ => throw new InvalidOperationException("a window that fell over");
        session.CloseRequested += _ => reached++;

        session.PanicPress();
        _now += TimeSpan.FromMilliseconds(300);
        var closing = Assert.IsType<ArcademyPanicRung.Closing>(session.PanicPress());

        // The press still took its rung, the surviving subscriber still got its close, and the
        // fault is named rather than swallowed. A press that threw is a press the ladder
        // underneath would be free to treat as its own.
        Assert.Equal(ArcademyClosePlan.WaitForExitDone, closing.Plan);
        Assert.Equal(1, reached);
        Assert.Contains(_log, l => l.Contains("close handler failed, isolated"));
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }
}
