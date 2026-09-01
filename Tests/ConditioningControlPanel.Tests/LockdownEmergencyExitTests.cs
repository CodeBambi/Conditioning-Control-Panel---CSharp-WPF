using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Emergency Exit / Lockdown seam (<c>Services/Haptics/LockdownService.cs</c>,
/// <c>Services/EmergencyExit/EmergencyExitHostService.cs</c>, the three timed games under
/// <c>Resources/web/emergency-exit/games/</c>). Primers: <c>Services/EmergencyExit/EMERGENCY_EXIT.md</c>
/// and <c>Services/Possession/POSSESSION.md</c>.
///
/// <para>Two kinds of test live here. The first is real behaviour: a LockdownService instance is
/// seeded through its private clock fields (Activate needs App.Settings, a DispatcherTimer and a
/// live app, none of which a unit test has) and then driven through RestartTimer / Deactivate. That
/// covers the bug this suite exists for - a sendback used to reset the clock that
/// <see cref="LockdownService.LastActiveDuration"/> is measured from, so 45 minutes + a sendback +
/// 45 more reported 45 and the long-lockdown achievement could never unlock.</para>
///
/// <para>The second is source-level, in the house style of <c>SessionLockMarkerTests</c>: the
/// remaining fixes are ORDERING and LIFETIME rules inside WPF-bound or WebView2-bound code, where
/// the regression is silent (an event raised one line too early, a verdict posted to a window that
/// was already disposed) and there is no seam to observe it through. Pinning the source is worth
/// more than pinning nothing.</para>
/// </summary>
public class LockdownEmergencyExitTests
{
    // ============================ behaviour ============================

    private static LockdownService SeedRunningLockdown(TimeSpan duration, TimeSpan alreadyServed)
    {
        var svc = new LockdownService();
        var started = DateTime.Now - alreadyServed;
        Set(svc, "_duration", duration);
        Set(svc, "_startedAt", started);
        Set(svc, "_activatedAt", started);
        Set(svc, "_isActive", true);
        return svc;
    }

    private static void Set(object target, string field, object value)
    {
        var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(f != null, $"LockdownService has no field '{field}' - the clock fields were renamed");
        f!.SetValue(target, value);
    }

    [Fact]
    public void LastActiveDuration_CountsTheWholeSitting_AcrossASendback()
    {
        // 45 minutes served, then the Emergency Exit sends them back into a fresh 45.
        var svc = SeedRunningLockdown(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(45));

        svc.RestartTimer("labyrinth");
        svc.Deactivate();

        // The achievement gate (GamificationBridge, throw_away_the_key) reads this. Measured from
        // the rebased clock it would have been ~0.
        Assert.True(svc.LastActiveDuration >= TimeSpan.FromMinutes(44.5),
            $"LastActiveDuration was {svc.LastActiveDuration}; a sendback must not erase time already served");
    }

    [Fact]
    public void RestartTimer_RewindsTheCountdownAndThePossessionLadder()
    {
        var svc = SeedRunningLockdown(TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(45));
        Assert.True(svc.ElapsedFraction > 0.9, "seeded lockdown should be nearly over");

        svc.RestartTimer("labyrinth");

        // The ladder NEEDS the rebase: the director drops back to Settle off ElapsedFraction.
        Assert.True(svc.ElapsedFraction < 0.01, $"ElapsedFraction was {svc.ElapsedFraction}, expected a full rewind");
        Assert.True(svc.Remaining > TimeSpan.FromMinutes(44.5), $"Remaining was {svc.Remaining}, expected the full duration back");
        Assert.Equal(1, svc.RestartCount);
    }

    [Fact]
    public void RestartTimer_IsANoOp_WhenNoLockdownIsRunning()
    {
        var svc = new LockdownService();
        svc.RestartTimer("labyrinth");
        Assert.Equal(0, svc.RestartCount);
    }

    // ============================ source pins ============================

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "Services");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "ConditioningControlPanel");
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the app project walking up from {AppContext.BaseDirectory}");
    }

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(AppDir(), Path.Combine(parts));
        Assert.True(File.Exists(path), $"missing source file: {path}");
        return File.ReadAllText(path);
    }

    private static string LockdownSource() => SourceRoots.ReadProductFile("Services", "Haptics", "LockdownService.cs");
    private static string HostSource() => SourceRoots.ReadProductFile("Services", "EmergencyExit", "EmergencyExitHostService.cs");
    private static string GameSource(string id) => Source("Resources", "web", "emergency-exit", "games", id + ".js");

    /// <summary>The body of a method, from its signature to the next method at the same indent.</summary>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"could not find '{signature}'");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"'{signature}' has no body");
        int depth = 0, i = open;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) break;
        }
        return source.Substring(open, Math.Min(i + 1, source.Length) - open);
    }

    [Fact]
    public void LastActiveDuration_IsMeasuredFromStartedAt_NotTheRebasedClock()
    {
        var deactivate = Body(LockdownSource(), "public void Deactivate()");
        Assert.Contains("LastActiveDuration = DateTime.Now - _startedAt;", deactivate);

        // ...and RestartTimer must never touch it, or the separation is decorative.
        var restart = Body(LockdownSource(), "public void RestartTimer(string reason)");
        Assert.DoesNotContain("_startedAt", restart);
        Assert.Contains("_activatedAt = DateTime.Now;", restart);
    }

    [Fact]
    public void RestartTimer_RaisesTimerRestarted_BeforeCountdownTick()
    {
        // A CountdownTick raised first shows the director an elapsed fraction that is already back
        // at ~0, so it walks the ladder down to Settle and barks it, and OnTimerRestarted then
        // resets and barks the same rung a second time. One restart, two Settles.
        var restart = Body(LockdownSource(), "public void RestartTimer(string reason)");
        var restarted = restart.IndexOf("TimerRestarted?.Invoke", StringComparison.Ordinal);
        var tick = restart.IndexOf("CountdownTick?.Invoke", StringComparison.Ordinal);
        Assert.True(restarted >= 0 && tick >= 0, "RestartTimer must raise both events");
        Assert.True(restarted < tick, "TimerRestarted has to be raised before CountdownTick");
    }

    [Fact]
    public void ExpiryTick_DeactivatesWithoutRaisingALiveCountdownTick()
    {
        // LockdownDoseKeeper.Enforce answers a tick by conscripting features and restarting the
        // engine. On the expiry tick it used to see IsActive == true and do exactly that, one
        // second before Deactivate tore it all down again.
        var tickBody = Body(LockdownSource(), "private void OnCountdownTick(object? sender, EventArgs e)");
        var expiry = tickBody.IndexOf("if (remaining <= TimeSpan.Zero)", StringComparison.Ordinal);
        var firstInvoke = tickBody.IndexOf("CountdownTick?.Invoke", StringComparison.Ordinal);
        Assert.True(expiry >= 0, "OnCountdownTick lost its expiry branch");
        Assert.True(firstInvoke > expiry, "the expiry branch must be reached before any CountdownTick is raised");
        Assert.Contains("Deactivate();", tickBody);
    }

    [Fact]
    public void Activate_ResetsTheSystemKeyTripwireThrottle()
    {
        // The syskey tripwire is throttled to 1 per 2 s. Left un-reset, a back-to-back lockdown
        // swallows its own first system-key attempt.
        var activate = Body(LockdownSource(), "public void Activate(TimeSpan duration)");
        Assert.Contains("_lastSysKeyAttempt = DateTime.MinValue;", activate);
        Assert.Contains("_startedAt = _activatedAt;", activate);
    }

    [Fact]
    public void OnGameFinished_PostsTheVerdictToThePage_BeforeApplyingIt()
    {
        // Applying an `escape` deactivates the lockdown, which fires LockdownDeactivated
        // synchronously, which used to close and dispose the window - so the post landed on a null
        // host and the winning player's window just vanished.
        var body = Body(HostSource(), "private static void OnGameFinished(JObject msg)");
        var post = body.IndexOf("type = \"verdict\"", StringComparison.Ordinal);
        var deactivate = body.IndexOf("App.Lockdown?.Deactivate()", StringComparison.Ordinal);
        var restart = body.IndexOf("App.Lockdown?.RestartTimer", StringComparison.Ordinal);
        Assert.True(post >= 0, "the verdict is never posted to the page");
        Assert.True(deactivate > post, "Deactivate must not run before the page has been told");
        Assert.True(restart > post, "RestartTimer must not run before the page has been told");
    }

    [Fact]
    public void DeactivationHook_LeavesTheWindowAloneWhileAVerdictOutroIsPending()
    {
        var src = HostSource();
        var hook = Body(src, "private static void EnsureHooked()");
        Assert.Contains("if (!_outroPending) Close();", hook);
        // Close is the single place the flag is cleared, so a stale one cannot survive a window.
        Assert.Contains("_outroPending = false;", Body(src, "public static void Close()"));
    }

    [Fact]
    public void OutroFailsafe_BelongsToTheWindowItGuards()
    {
        // The 8 s timer used to be armed globally AFTER Close() had already cancelled it, so it
        // could close a FRESH game window opened by the next lockdown.
        var src = HostSource();
        Assert.Contains("private static bool ArmOutroFailsafe(ChaosWebViewHost? host)", src);
        var arm = Body(src, "private static bool ArmOutroFailsafe(ChaosWebViewHost? host)");
        Assert.Equal(2, Regex.Matches(arm, @"ReferenceEquals\(_host, host\)").Count);
    }

    // ---- the three timed games -------------------------------------------------
    // Opening the window and getting distracted used to spend the whole budget on wall time, and a
    // `failed` game is a sendback: the FULL lockdown timer restarts with zero user action.

    [Theory]
    [InlineData("labyrinth")]
    [InlineData("jigsaw")]
    [InlineData("captcha")]
    public void TimedGames_StartTheirClockOnFirstInteraction_AndPauseWhileHidden(string game)
    {
        var src = GameSource(game);

        Assert.Contains("clockBegin", src);
        Assert.Contains("visibilitychange", src);
        Assert.Contains("document.hidden", src);
        Assert.Contains("clockPause", src);
        Assert.Contains("clockResume", src);

        // The listener has to come off again: the shell reuses the page for the outro card.
        Assert.Contains("removeEventListener('visibilitychange', onVisibility)", src);

        // No survivor of the old wall-clock: every budget/schedule reads the active clock.
        Assert.DoesNotContain("performance.now() - t0", src);
    }

    [Theory]
    [InlineData("labyrinth", "TIME_LIMIT = 25")]
    [InlineData("jigsaw", "TIME_LIMIT_MS = 60000")]
    [InlineData("captcha", "TIME_LIMIT_MS = 90000")]
    public void TimedGames_KeepTheirActiveTimeBudget(string game, string declaration)
        => Assert.Contains(declaration, GameSource(game));

    // ---- consent copy ----------------------------------------------------------

    [Theory]
    [InlineData("MainWindow.Lab.cs")]
    [InlineData("MainWindow.PremiumRail.cs")]
    public void BothConsentDialogs_SayTheEmergencyExitCanRestartTheTimer(string file)
    {
        var src = SourceRoots.ReadProductFile("MainWindow", file);
        Assert.Contains("The Emergency Exit button is a gamble", src);
        Assert.Contains("the timer restarts at its FULL length", src);
    }
}
