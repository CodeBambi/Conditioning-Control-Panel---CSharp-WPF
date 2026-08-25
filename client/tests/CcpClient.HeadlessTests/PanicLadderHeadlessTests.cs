using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>What the emergency stop DOES, on the real shell, driven through the real method the hotkey
/// calls.</b>
///
/// <para><see cref="PanicKeyTests"/> proves the gesture ARRIVES with a full-monitor topmost click
/// sink over the screen; this file proves what arriving is worth. Two rungs, upstream's
/// (<c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1164</c> and <c>:1227</c>): a press
/// while a session runs stops it, and a press inside the double-press window with nothing running
/// exits the application.</para>
///
/// <para><b>Stopping the engine is what takes the surfaces down</b>, and that link is not re-proved
/// here — <c>SessionEngine.Stop</c> disarms every module and disarm is what posts each surface's
/// withdrawal, which the spine's own facts already hold. What these facts add is the half that did
/// not exist at all until this packet: something other than the shell's own button reaching that
/// stop. The withdrawal itself is checked END TO END on a real desktop in the headed run recorded
/// with this change, where a 2880x1800 <c>HWND_TOPMOST</c> surface goes down on press one.</para>
///
/// <para><b>Why the exit rung is read from the log rather than by exiting.</b> There is no classic
/// desktop lifetime in a headless test host, so <c>RequestApplicationExit</c> takes its own
/// documented no-op branch and says so — a shutdown that really ran would take the test runner with
/// it. So the fact asserts the shell REACHED the exit request, and the headed run recorded with
/// this change is what proves the request really ends the process (exit code 0 through the guarded
/// teardown).</para>
///
/// <para><b>The chord itself is NOT armed here, and that is deliberate.</b> A system-wide hotkey is
/// a process-wide claim, so <c>App</c> makes it once beside the single shell it builds — see the
/// comment at that call site for why arming inside <c>MainWindow</c>'s constructor would make every
/// extra shell a test builds raise a false "no emergency stop" alarm. These facts therefore drive
/// the same <see cref="MainWindow.PanicPress"/> the chord calls, and the headed run recorded with
/// this change is what proves <c>App</c> really wires the two together.</para>
/// </summary>
public class PanicLadderHeadlessTests : HeadlessTest
{
    private async Task<(MainWindow Window, List<string> Diagnostics)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-panic-headless-" + Guid.NewGuid().ToString("N"));
        var diagnostics = new List<string>();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            LogSinkFactory = () => new CapturingLogSink(diagnostics),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return (window, diagnostics);
    }

    [AvaloniaFact]
    public async Task OnePressSTOPSARunningSession_AndTheShellsOwnControlAgreesItStopped()
    {
        var (window, diagnostics) = await BootAsync();

        Assert.True(window.Session.Engine.Start(), "the session did not start, so there is nothing to panic out of");
        Assert.True(window.Session.Engine.Running);

        // Non-vacuity: the start really ARMED the modules, so "nothing is Live afterwards" below is
        // a statement about modules that were asked, not about an engine that never ran.
        Assert.NotEmpty(window.Session.Engine.ArmOutcomes);

        window.PanicPress();

        Assert.False(window.Session.Engine.Running,
            "the panic press did not stop the session. This is the whole defect: with the session running, every "
            + "effect surface is up and HWND_TOPMOST over the shell, and the ONLY thing that takes them down is "
            + "the engine stopping");
        Assert.DoesNotContain(window.Session.Engine.Effects, e => e.Dot == EffectDotState.Live);

        // The user-visible half: the shell's ONE control has to agree, or a user staring at a button
        // that still says STOP has no way to know the panic worked.
        var button = window.FindControl<Button>("SessionStartButton");
        Assert.NotNull(button);
        Assert.Equal("START", button!.Content);

        Assert.Contains(diagnostics, l => l.Contains("panic: session running", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ASecondPressInsideTheWindowASKSTheApplicationToEXIT_AndOneOutsideItDoesNot()
    {
        var (window, diagnostics) = await BootAsync();

        Assert.False(window.Session.Engine.Running);

        // Rung one with nothing running: the shell is raised and the exit is NOT taken. A single
        // stray press must never end the application.
        window.PanicPress();
        Assert.DoesNotContain(diagnostics, l => l.Contains("Exit chosen", StringComparison.Ordinal));

        // Rung two, inside the window: the exit request the tray menu's Exit reaches.
        window.PanicPress();
        Assert.Contains(diagnostics, l => l.Contains("panic: second press inside the double-press window", StringComparison.Ordinal));
        Assert.Contains(diagnostics, l => l.Contains("Exit chosen", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>The exit rung is reached with a session RUNNING, which is the fix for the way this ladder
    /// could trap somebody.</b>
    ///
    /// <para>The rungs used to be ordered stop-then-exit with the exit gated on <c>!Running</c>, and
    /// the emergency stop's review found what that costs: a module that throws on the way down left
    /// <c>Running</c> true, so every later press re-entered the stop rung, threw again, and the
    /// double-press exit became permanently unreachable — no log line, no toast, and a desktop full
    /// of topmost surfaces. The exit is now evaluated FIRST and reads no engine state at all, so no
    /// failure inside the stop can take it away.</para>
    ///
    /// <para>A throwing module cannot be injected into the real composed rack from here (that half
    /// is <see cref="Tests.EmergencyStopTests"/>'s, on a real engine with a real throwing module), so
    /// this drives the same branch through the only other way to be running at press two: the
    /// session came back between the presses — a scheduler auto-start, or the user restarting it.
    /// The ladder's answer is the same in both cases and it is deliberate: two presses of the
    /// emergency chord inside the window mean OUT, whatever the engine believes about itself.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondPressWithASessionRUNNINGStillASKSTheApplicationToEXIT()
    {
        var (window, diagnostics) = await BootAsync();

        window.PanicPress();
        Assert.DoesNotContain(diagnostics, l => l.Contains("Exit chosen", StringComparison.Ordinal));

        // Running at the moment of the second press — the state that used to send this press back
        // into the stop rung instead of out of the application.
        Assert.True(window.Session.Engine.Start());
        Assert.True(window.Session.Engine.Running);

        window.PanicPress();
        Assert.Contains(diagnostics, l => l.Contains("Exit chosen", StringComparison.Ordinal));
    }

    /// <summary>The host's diagnostic sink, captured.</summary>
    private sealed class CapturingLogSink(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
