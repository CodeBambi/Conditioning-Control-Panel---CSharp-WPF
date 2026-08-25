using System.Diagnostics;

namespace CcpClient.Desktop.Input;

/// <summary>
/// <b>What answers the emergency stop when the UI thread will not.</b>
///
/// <para><b>The situation, measured rather than imagined.</b> Every affordance that stops a session
/// and every window it puts on the desktop belongs to the UI thread, and a measurement taken on this
/// product at maximum settings recorded that thread failing to answer its message loop for
/// <b>607–1734 ms at a stretch, peaking past a 2000 ms probe ceiling</b>, with one core pegged at
/// 83–92 % while fifteen sat idle. <see cref="Win32PanicKey"/> now sees the press whatever the UI
/// thread is doing — it runs its own thread and its own pump — so the question this class answers is
/// the next one: the press has been seen, the UI thread has been asked, and the answer has not come
/// back.</para>
///
/// <para><b>What upstream does here, and what the port can honestly copy.</b> Upstream queues its
/// panic handler on the dispatcher and races it against a two-second watchdog
/// (<c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:895</c>,
/// <c>:903 ArmPanicWatchdog</c>); on expiry it runs an off-thread teardown
/// (<c>:932 RunEmergencyPanicTeardown</c>) that stops haptics, audio and its scanner, guards every
/// step on its own so one failure cannot starve the rest (<c>:947-957</c>), and skips entirely if
/// the app is already shutting down so the fallback cannot race the real teardown
/// (<c>:936-946</c>). <b>It deliberately hides no window</b>, and says why at <c>:968-975</c>: a
/// window belongs to the thread that created it, <c>ShowWindowAsync</c> only POSTS to that thread,
/// so nothing off-thread can take a surface down while the owning thread is the wedged one.</para>
///
/// <para><b>That constraint is the whole design, and it binds the port harder than it binds
/// upstream.</b> What trapped the owner was NINETEEN surfaces, three of them the size of his
/// monitor — and every one of them is a window owned by the wedged thread. So an off-thread teardown
/// of the upstream shape would stop the audio and leave the desktop exactly as unusable as it was.
/// The port therefore does what upstream's own teardown bound already names as the remedy it lacks —
/// <i>"the remedy there is termination, not patience"</i>
/// (<c>Lifecycle/ApplicationHost.cs:212-214</c>) — and ends the process, after running the real
/// teardown off-thread first so the settings flush and the haptic all-stop still happen. That the
/// desktop really comes back from a terminated process of THIS build, holding THIS build's own
/// click-absorbing and keyboard-taking surfaces, is not assumed: it is measured across a process
/// boundary in <c>client/tests/CcpClient.Tests/SurfaceExitObservations.cs</c>.</para>
///
/// <para><b>Why the deadline is longer than upstream's two seconds.</b> Two seconds is INSIDE the
/// measured stall envelope, and the escalation here ends the application rather than muting it. A
/// two-second trigger would kill a healthy app whose UI thread was about to answer at 2.1 s and stop
/// the session properly, which trades the user's session for nothing. The default is therefore
/// <see cref="DefaultAnswerDeadline"/>, comfortably past the measured peak, and the user is not made
/// to wait it out: a SECOND press while the first is still unanswered escalates immediately, which
/// is the same double-press-means-out contract the shell's own ladder already carries
/// (<c>Views/MainWindow.axaml.cs</c>) — delivered off the thread that cannot deliver it.</para>
///
/// <para><b>Threading.</b> <see cref="Press"/> is called on the panic key's pump thread and never
/// blocks it: the watching, like upstream's, happens somewhere else
/// (<c>MainWindow.xaml.cs:900-902</c> — "this only ever spawns the watcher, it never waits here"),
/// so a second press is dequeued the instant it arrives rather than queued behind the first one's
/// deadline.</para>
/// </summary>
public sealed class PanicWatchdog
{
    /// <summary>
    /// How long the UI thread gets to answer a panic press before this class stops waiting for it.
    /// Upstream's is 2 s (<c>MainWindow.xaml.cs:895</c>); this one is longer on purpose, because
    /// upstream's expiry MUTES an app and this one ENDS it — see the class remarks.
    /// </summary>
    public static readonly TimeSpan DefaultAnswerDeadline = TimeSpan.FromSeconds(5);

    private readonly Action<Action> _post;
    private readonly Action _handler;
    private readonly Action<string> _log;
    private readonly Func<bool> _teardownStarted;
    private readonly Func<Task> _teardown;
    private readonly Action _terminate;
    private readonly TimeSpan _deadline;
    private int _outstanding;
    private int _escalated;

    /// <param name="post">Hands work to the UI thread. Non-blocking, and it may never run the
    /// delegate at all — that possibility IS the subject of this class.</param>
    /// <param name="handler">The shell's own panic ladder (<c>MainWindow.PanicPress</c>). It touches
    /// windows, so it may only ever run on the thread <paramref name="post"/> reaches.</param>
    /// <param name="log">Where every rung of this ladder is said out loud. Not optional: an
    /// emergency path that ended a process without a line explaining why would be indistinguishable
    /// from a crash.</param>
    /// <param name="teardownStarted">True once the application's own teardown has begun
    /// (<c>ApplicationHost.IsShutdown</c>). Upstream asks the same question for the same reason
    /// (<c>MainWindow.xaml.cs:936-946</c>): the double-press rung ends in a shutdown that outlives
    /// the deadline, and a fallback that fired into it would race the teardown it is duplicating —
    /// and would truncate the settings flush that teardown is in the middle of.</param>
    /// <param name="teardown">The application's real teardown (<c>ApplicationHost.ShutdownAsync</c>):
    /// the settings flush in its reserved head slot, the generation drain, and the reverse-order
    /// participant stop that carries the haptic all-stop and the audio release. It is run BEFORE the
    /// process ends so termination costs a release rather than a write.</param>
    /// <param name="terminate">Ends this process. Separated so a test can observe the decision
    /// without dying of it; the product passes <see cref="TerminateThisProcess"/>.</param>
    /// <param name="deadline">Overrides <see cref="DefaultAnswerDeadline"/>. The calibration knob:
    /// the right number is a property of the machine's worst stall, not of this code.</param>
    public PanicWatchdog(
        Action<Action> post,
        Action handler,
        Action<string> log,
        Func<bool> teardownStarted,
        Func<Task> teardown,
        Action terminate,
        TimeSpan? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(teardownStarted);
        ArgumentNullException.ThrowIfNull(teardown);
        ArgumentNullException.ThrowIfNull(terminate);
        _post = post;
        _handler = handler;
        _log = log;
        _teardownStarted = teardownStarted;
        _teardown = teardown;
        _terminate = terminate;
        _deadline = deadline ?? DefaultAnswerDeadline;
    }

    /// <summary>
    /// End this process, now. <c>TerminateProcess</c> on ourselves rather than
    /// <c>Environment.Exit</c>: exit runs managed shutdown, which can block on the very threads this
    /// path exists because it cannot reach, and the caller here has already run the real teardown.
    /// The operating system destroys every window the process owns, which is the only lever left
    /// that reaches a surface belonging to a wedged thread.
    /// </summary>
    public static void TerminateThisProcess()
    {
        using var self = Process.GetCurrentProcess();
        self.Kill();
    }

    /// <summary>Presses that were handed to the UI thread and have not come back. Public so a test
    /// can see the state the escalation reads rather than inferring it.</summary>
    public int Outstanding => Volatile.Read(ref _outstanding);

    /// <summary>
    /// A press arrived. Hands it to the UI thread and starts watching for the answer.
    ///
    /// <para>Never throws: it is called from inside a native window procedure, where an escaping
    /// exception ends the process — which for THIS press would be the emergency stop killing the app
    /// it was pressed to make safe.</para>
    /// </summary>
    public void Press()
    {
        try
        {
            if (Interlocked.Increment(ref _outstanding) > 1)
            {
                Escalate("a second press arrived while the first had still not been answered");
                return;
            }

            var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _post(() =>
            {
                try
                {
                    _handler();
                }
                catch (Exception ex)
                {
                    // The UI thread ANSWERED and the answer was a failure. That is not this class's
                    // subject — a thread that can throw is a thread that is alive — so it is said
                    // and the watch stands down. Saying it is the point: this exception used to
                    // die inside Win32PanicKey's window procedure with nothing to hear it.
                    _log($"panic: the shell's panic handler threw ({ex.GetType().Name}: {ex.Message})");
                }
                finally
                {
                    Interlocked.Decrement(ref _outstanding);
                    answered.TrySetResult();
                }
            });

            _ = WatchAsync(answered.Task);
        }
        catch (Exception ex)
        {
            _log($"panic: the watchdog itself failed to hand the press on ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private async Task WatchAsync(Task answered)
    {
        try
        {
            await answered.WaitAsync(_deadline).ConfigureAwait(false);
            return;
        }
        catch (TimeoutException)
        {
            // The one expected outcome on this path, and the whole reason the class exists.
        }
        catch (Exception ex)
        {
            _log($"panic: the watchdog's wait failed ({ex.GetType().Name}: {ex.Message})");
            return;
        }

        Escalate($"the UI thread did not answer the panic press within {_deadline.TotalSeconds:0.#}s");
    }

    private void Escalate(string why)
    {
        // Upstream's first question, asked first here too (MainWindow.xaml.cs:936-946). Checked
        // BEFORE the one-shot latch: a press that arrives during a healthy teardown must not spend
        // the latch that a later, real wedge would need.
        if (_teardownStarted())
        {
            _log($"panic: FALLBACK stands down — {why}, but this application is already tearing down, "
                + "and its teardown carries its own bounds");
            return;
        }

        if (Interlocked.Exchange(ref _escalated, 1) != 0)
        {
            return;
        }

        _log($"panic: FALLBACK — {why}. Every surface on screen belongs to that thread and cannot be "
            + "taken down from this one, so this process is being torn down off-thread and then ended");

        _ = TerminateAsync();
    }

    private async Task TerminateAsync()
    {
        try
        {
            // Bounded from OUT HERE as well as inside: ShutdownAsync bounds each participant's stop,
            // but the premise of this whole path is that something in this process is not answering,
            // and a teardown that hung would leave the user under the surfaces with the one lever
            // that still works unpulled.
            await _teardown().WaitAsync(_deadline).ConfigureAwait(false);
            _log("panic: FALLBACK teardown completed off-thread (settings flushed, participants stopped)");
        }
        catch (TimeoutException)
        {
            _log($"panic: FALLBACK teardown did not finish within {_deadline.TotalSeconds:0.#}s and was abandoned; "
                + "ending the process anyway");
        }
        catch (Exception ex)
        {
            _log($"panic: FALLBACK teardown failed ({ex.GetType().Name}: {ex.Message}); ending the process anyway");
        }

        try
        {
            _log("panic: FALLBACK ending this process now — the operating system reclaims every window it owns");
            _terminate();
        }
        catch (Exception ex)
        {
            _log($"panic: FALLBACK could not end this process ({ex.GetType().Name}: {ex.Message}); "
                + "there is nothing further this application can do for the user");
        }
    }
}
