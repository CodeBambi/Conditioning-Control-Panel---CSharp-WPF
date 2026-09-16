using System;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE VAT'S REFRESH MEMORY, pinned. The Discord report behind it: the daily vat on the Trainer
/// Card (jar, tooltip, XP readout) sometimes stayed dark after logging in until a sign-out and
/// sign-in. DescentService answered a request it could not serve (no token yet, a fetch in
/// flight, inside the 10s floor) with a silent false and nothing ever asked again. These tests
/// hold the gate to the opposite rule: a want is remembered until a fetch answers it, the floor
/// only drops a request that a same-credential fetch has ALREADY answered, and sign-in and logout
/// leave the gate exactly where each needs it.
/// </summary>
public class DescentRefreshGateTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private const string Uid = "u_1";
    private const string Token = "tok_a";

    private static DescentRefreshVerdict Ask(
        DescentRefreshGate gate, DateTime now, out TimeSpan retryIn,
        string? uid = Uid, string? token = Token,
        bool offline = false, bool inFlight = false, bool force = false, string reason = "test")
        => gate.Ask(now, uid, token, offline, inFlight, force, reason, out retryIn);

    // ---------------------------------------------------------------- sign-in

    /// <summary>
    /// THE REPORTED RACE. The Trainer Card opens before the auth token lands: the request is
    /// not lost, it waits, and the sign-in poke turns it into a fetch.
    /// </summary>
    [Fact]
    public void NoToken_IsRememberedAndFetchesOnSignIn()
    {
        var gate = new DescentRefreshGate();

        var v = Ask(gate, T0, out _, token: null, reason: "trainer card open");
        Assert.Equal(DescentRefreshVerdict.WaitForSignIn, v);
        Assert.True(gate.Pending);
        Assert.Equal("trainer card open", gate.PendingReason);

        // The sign-in poke, credentials now in hand.
        v = Ask(gate, T0.AddSeconds(3), out _, reason: "signed in");
        Assert.Equal(DescentRefreshVerdict.Fetch, v);
        Assert.Equal("trainer card open", gate.PendingReason);   // the first ask explains the log

        gate.BeginFetch(T0.AddSeconds(3), Uid, Token);
        gate.EndFetch(answered: true);
        Assert.False(gate.Pending);
        Assert.Null(gate.PendingReason);
    }

    [Fact]
    public void NoAccount_WaitsTheSameWay()
    {
        var gate = new DescentRefreshGate();
        Assert.Equal(DescentRefreshVerdict.WaitForSignIn, Ask(gate, T0, out _, uid: ""));
        Assert.True(gate.Pending);
    }

    // ---------------------------------------------------------------- the floor

    /// <summary>The floor's whole purpose: a burst after an answered fetch is one fetch.</summary>
    [Fact]
    public void InsideTheFloor_AfterAnAnsweredFetchWithTheSameCredentials_IsRedundant()
    {
        var gate = new DescentRefreshGate();
        Assert.Equal(DescentRefreshVerdict.Fetch, Ask(gate, T0, out _));
        gate.BeginFetch(T0, Uid, Token);
        gate.EndFetch(answered: true);

        var v = Ask(gate, T0.AddSeconds(4), out var retryIn, reason: "profile loaded");
        Assert.Equal(DescentRefreshVerdict.Skip, v);
        Assert.Equal(TimeSpan.Zero, retryIn);
        Assert.False(gate.Pending);
    }

    /// <summary>
    /// The half the old service got wrong: a request inside the floor after a fetch that did NOT
    /// answer (a 401 on a rotated token, a network hiccup) is deferred to the end of the window,
    /// not dropped.
    /// </summary>
    [Fact]
    public void InsideTheFloor_AfterAFailedFetch_IsDeferredToTheEndOfTheFloor()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);
        gate.EndFetch(answered: false);

        var v = Ask(gate, T0.AddSeconds(4), out var retryIn, reason: "profile loaded");
        Assert.Equal(DescentRefreshVerdict.Defer, v);
        Assert.Equal(TimeSpan.FromSeconds(6), retryIn);
        Assert.True(gate.Pending);
    }

    /// <summary>A token healed or rotated since the last fetch makes that fetch's answer stale.</summary>
    [Fact]
    public void InsideTheFloor_AfterAFetchWithOtherCredentials_IsDeferred()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, "tok_old");
        gate.EndFetch(answered: true);

        var v = Ask(gate, T0.AddSeconds(2), out var retryIn, token: "tok_new", reason: "signed in");
        Assert.Equal(DescentRefreshVerdict.Defer, v);
        Assert.Equal(TimeSpan.FromSeconds(8), retryIn);
        Assert.True(gate.Pending);
    }

    [Fact]
    public void PastTheFloor_Fetches()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);
        gate.EndFetch(answered: true);

        Assert.Equal(DescentRefreshVerdict.Fetch, Ask(gate, T0.AddSeconds(10), out _));
    }

    [Fact]
    public void Force_BypassesTheFloor()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);
        gate.EndFetch(answered: true);

        Assert.Equal(DescentRefreshVerdict.Fetch, Ask(gate, T0.AddSeconds(1), out _, force: true));
    }

    // ---------------------------------------------------------------- in flight

    /// <summary>
    /// A request during a fetch is deferred; if that fetch answers, the want is satisfied and the
    /// retry has nothing to do. No double network call.
    /// </summary>
    [Fact]
    public void InFlight_IsDeferred_AndTheAnsweredFetchClearsTheWant()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);

        var v = Ask(gate, T0.AddSeconds(2), out var retryIn, inFlight: true, reason: "signed in");
        Assert.Equal(DescentRefreshVerdict.Defer, v);
        Assert.Equal(TimeSpan.FromSeconds(8), retryIn);
        Assert.True(gate.Pending);

        gate.EndFetch(answered: true);
        Assert.False(gate.Pending);
    }

    /// <summary>...and if that fetch fails, the want stands for the retry.</summary>
    [Fact]
    public void InFlight_AFailedFetchLeavesTheWantStanding()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);
        Ask(gate, T0.AddSeconds(2), out _, inFlight: true);

        gate.EndFetch(answered: false);
        Assert.True(gate.Pending);
        Assert.Equal(DescentRefreshVerdict.Fetch, Ask(gate, T0.AddSeconds(10), out _));
    }

    [Fact]
    public void RetryDelay_NeverDropsBelowTheMinimum()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);

        // A hung request past the floor: the retry still waits a beat instead of spinning.
        Ask(gate, T0.AddSeconds(30), out var retryIn, inFlight: true);
        Assert.Equal(DescentRefreshGate.MinRetryDelay, retryIn);
    }

    // ---------------------------------------------------------------- offline, logout

    [Fact]
    public void Offline_IsSkippedAndNotRemembered()
    {
        var gate = new DescentRefreshGate();
        Assert.Equal(DescentRefreshVerdict.Skip, Ask(gate, T0, out _, offline: true));
        Assert.False(gate.Pending);
    }

    /// <summary>
    /// Logout drops the floor, the credentials and the want, so the next account's first request
    /// goes straight out and user A's pending ask cannot fire for user B.
    /// </summary>
    [Fact]
    public void Reset_ClearsTheFloorTheCredentialsAndTheWant()
    {
        var gate = new DescentRefreshGate();
        gate.BeginFetch(T0, Uid, Token);
        gate.EndFetch(answered: false);
        Ask(gate, T0.AddSeconds(1), out _, reason: "trainer card open");
        Assert.True(gate.Pending);

        gate.Reset();
        Assert.False(gate.Pending);
        Assert.Null(gate.PendingReason);
        Assert.Equal(DateTime.MinValue, gate.LastFetchUtc);

        Assert.Equal(DescentRefreshVerdict.Fetch, Ask(gate, T0.AddSeconds(2), out _, uid: "u_2", token: "tok_b"));
    }
}
