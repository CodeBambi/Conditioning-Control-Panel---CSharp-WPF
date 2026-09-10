using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// The one owner of everything that wants the screen at startup.
    ///
    /// <para><b>Why this exists.</b> Before it, a fresh install could take fourteen modal stops and
    /// eleven non-modal pops in its first thirty seconds, because eight independent code paths each
    /// decided for themselves when they were allowed to open: the failed-update report, the wizard,
    /// What's New, the season recap, the upgrader's mod picker, the enhance nudge, the update
    /// dialog, and a scattering of cards that polled <c>IsStartupDialogShowing</c> on their own 500
    /// ms clocks. Each poll was correct in isolation and none of them could see the others, so the
    /// order was whatever the dispatcher felt like and the count was however many happened to be
    /// due.</para>
    ///
    /// <para><b>The two rules.</b> Modal surfaces go through <see cref="EnqueueModal"/> and run one
    /// at a time, lowest priority number first, first-in-first-out within a priority. Passive
    /// surfaces go through <see cref="PresentOrInbox"/>: they open at once when nothing is quiet,
    /// and otherwise become a row in the <see cref="Inbox"/> that the user opens when they feel
    /// like it.</para>
    ///
    /// <para><b>The flag stays.</b> There is no second "is a dialog up" flag. The presenter drives
    /// the existing static <c>MainWindow.IsStartupDialogShowing</c> around every surface it runs,
    /// so every hand-rolled poller still in the codebase keeps working unchanged.</para>
    /// </summary>
    public sealed class StartupPresenter
    {
        /// <summary>How long the pump will wait for the screen to free up before it gives a
        /// surface up to the next launch. Five minutes: an upgrader legitimately spends that
        /// long reading patch notes, and a mod pack can take that long to download in the wizard.</summary>
        private static readonly TimeSpan MaxWaitPerSurface = TimeSpan.FromMinutes(5);

        /// <summary>Poll interval while waiting for a surface's turn. The idiom the whole
        /// codebase already used before this class existed.</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// A beat before each surface's clearance is first tested. Some of the things the pump
        /// must not step on are POSTED rather than running: What's New queues its "Show me around"
        /// tour at Normal priority as its own dialog unwinds, so the very next pump iteration would
        /// otherwise see a tutorial that is not active yet and open the mod picker straight through
        /// the spotlight. The old code spent a flat Task.Delay(1500) on the same problem.
        /// </summary>
        private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(750);

        private readonly Dispatcher _dispatcher;
        private readonly StartupQueueCore _core = new();
        private readonly Dictionary<string, (Action<Window?> Show, Action? Abandoned)> _shows =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _pumpRunning;
        private bool _modalUp;
        private bool _lastQuiet;
        private DateTime? _firstLaunchUntilUtc;
        private DispatcherTimer? _quietTimer;

        public StartupPresenter(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Inbox = new ObservableCollection<InboxItem>();
            _lastQuiet = IsQuiet;
        }

        // ------------------------------------------------------------------ modal ladder

        /// <summary>
        /// Queues a modal startup surface. <paramref name="show"/> runs on the UI thread when the
        /// screen is free and MUST block until the surface is gone (<c>ShowDialog</c>, or a
        /// <c>MessageBox.Show</c>) - the ladder measures "done" by the call returning.
        ///
        /// <para>Duplicate keys are dropped, so a caller that fires twice gets one turn.</para>
        /// </summary>
        public void EnqueueModal(string key, int priority, Action<Window?> show)
            => EnqueueModal(key, priority, show, null);

        /// <summary>
        /// As <see cref="EnqueueModal(string,int,Action{Window?})"/>, plus a callback for the case
        /// where the surface never got its turn (five minutes of update dialog / tour / an
        /// unloaded window). The first-launch wizard uses it to hand the first run back so the
        /// next launch offers it properly instead of silently spending it.
        /// </summary>
        public void EnqueueModal(string key, int priority, Action<Window?> show, Action? onAbandoned)
        {
            if (string.IsNullOrWhiteSpace(key) || show == null) return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => EnqueueModal(key, priority, show, onAbandoned)),
                    DispatcherPriority.Normal);
                return;
            }

            if (!_core.Enqueue(key, priority))
            {
                App.Logger?.Debug("[Startup] '{Key}' is already on the ladder - ignoring the second offer", key);
                return;
            }

            _shows[key] = (show, onAbandoned);
            App.Logger?.Information("[Startup] queued modal '{Key}' at priority {Priority} ({Count} waiting)",
                key, priority, _core.Count);

            StartPump();
        }

        /// <summary>A ladder surface is on screen right now.</summary>
        public bool IsModalUp => _modalUp;

        /// <summary>
        /// Nothing is on screen and nothing is waiting for a turn. What "after the startup ladder"
        /// means for anything that wants to be last (the EMI knock).
        /// </summary>
        public bool IsLadderIdle => !_modalUp && !_pumpRunning && _core.IsEmpty;

        /// <summary>Raised on the UI thread when the ladder empties and the last surface is gone.</summary>
        public event Action? Drained;

        // ------------------------------------------------------------------ quiet window

        /// <summary>
        /// True while something already owns the user - a ladder surface, the guided tour, a
        /// running session - or while the first-launch grace window is still open.
        /// </summary>
        public bool IsQuiet => StartupQueueCore.IsQuiet(ReadWorld());

        /// <summary>
        /// Opens the first-launch grace window. Called once, by the far side of the first-run
        /// wizard. Ten minutes in which the app does not interrupt on its own initiative.
        /// </summary>
        public void BeginFirstLaunchQuiet(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => BeginFirstLaunchQuiet(span)), DispatcherPriority.Normal);
                return;
            }

            var until = DateTime.UtcNow + span;
            if (_firstLaunchUntilUtc is DateTime existing && existing >= until) return;

            _firstLaunchUntilUtc = until;
            App.Logger?.Information("[Startup] first-launch quiet window open for {Minutes:0.#} min", span.TotalMinutes);
            EnsureQuietWatch();
            NotifyQuietMaybeChanged();
        }

        /// <summary>Raised on the UI thread whenever <see cref="IsQuiet"/> flips.</summary>
        public event Action? QuietChanged;

        // ------------------------------------------------------------------ inbox

        /// <summary>
        /// Opens <paramref name="item"/> now if nothing is quiet, otherwise parks it as an Inbox
        /// row. Safe from any thread.
        /// </summary>
        public void PresentOrInbox(InboxItem item)
        {
            if (item == null) return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => PresentOrInbox(item)), DispatcherPriority.Normal);
                return;
            }

            if (!StartupQueueCore.ShouldInbox(StartupSurfaceKind.Passive, ReadWorld()))
            {
                App.Logger?.Debug("[Startup] '{Key}' presented immediately - nothing is quiet", item.Key);
                RunSafely(item.Open, item.Key, "open");
                return;
            }

            foreach (var existing in Inbox)
            {
                if (string.Equals(existing.Key, item.Key, StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger?.Debug("[Startup] '{Key}' is already in the Inbox - not posting it twice", item.Key);
                    return;
                }
            }

            Inbox.Insert(0, item);
            App.Logger?.Information("[Startup] '{Key}' went to the Inbox ({Count} waiting)", item.Key, Inbox.Count);
            // Something is parked, so somebody has to notice when quiet ends - a session or a tour
            // can start the quiet window without going through the ladder, and the clock is the
            // only thing that watches those.
            EnsureQuietWatch();
            InboxChanged?.Invoke();
        }

        /// <summary>Rows waiting to be read, newest first.</summary>
        public ObservableCollection<InboxItem> Inbox { get; }

        /// <summary>How many rows are waiting. Drives the title-bar badge; hidden at zero.</summary>
        public int UnreadCount => Inbox.Count;

        /// <summary>Raised on the UI thread after a row is added, opened or dismissed.</summary>
        public event Action? InboxChanged;

        /// <summary>Removes the row and runs the surface it was holding.</summary>
        public void OpenItem(InboxItem item)
        {
            if (item == null) return;
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => OpenItem(item)), DispatcherPriority.Normal);
                return;
            }

            Inbox.Remove(item);
            InboxChanged?.Invoke();
            RunSafely(item.Open, item.Key, "open");
        }

        /// <summary>Removes the row and runs the surface's own dismissal bookkeeping, if any.</summary>
        public void DismissItem(InboxItem item)
        {
            if (item == null) return;
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => DismissItem(item)), DispatcherPriority.Normal);
                return;
            }

            Inbox.Remove(item);
            InboxChanged?.Invoke();
            if (item.Dismiss != null) RunSafely(item.Dismiss, item.Key, "dismiss");
        }

        // ------------------------------------------------------------------ internals

        /// <summary>Owner for every ladder surface. Null is tolerated by all of them.</summary>
        private static Window? Owner
        {
            get
            {
                try { return App.MainWindowRef ?? Application.Current?.MainWindow; }
                catch { return null; }
            }
        }

        private QuietInputs ReadWorld() => new()
        {
            // "A modal is up" for the purposes of QUIET means the whole ladder, not just the
            // surface currently on screen. The gap between two queued modals is a fraction of a
            // second in which nothing is showing, and letting a card or a ceremony fire into it
            // would be the pile-up all over again, one frame narrower. IsModalUp stays literal
            // for callers that really do mean "is something on screen right now".
            ModalUp = _modalUp || !IsLadderIdle,
            TutorialActive = SafeTutorialActive(),
            SessionRunning = SafeSessionRunning(),
            FirstLaunchUntilUtc = _firstLaunchUntilUtc,
            NowUtc = DateTime.UtcNow,
        };

        private static bool SafeTutorialActive()
        {
            try { return App.Tutorial?.IsActive == true; } catch { return false; }
        }

        private static bool SafeSessionRunning()
        {
            try { return App.IsSessionRunning; } catch { return false; }
        }

        private static bool SafeUpdateDialogActive()
        {
            try { return App.IsUpdateDialogActive; } catch { return false; }
        }

        private static bool SafeWindowReady()
        {
            try
            {
                var owner = Owner;
                return owner != null && owner.IsLoaded;
            }
            catch { return false; }
        }

        private void StartPump()
        {
            if (_pumpRunning) return;
            _pumpRunning = true;
            _dispatcher.BeginInvoke(new Action(async () => await PumpAsync()), DispatcherPriority.Normal);
        }

        /// <summary>
        /// The ladder itself. One surface at a time, in priority order, each given up to five
        /// minutes of waiting for the screen and then given up on.
        ///
        /// <para>DispatcherPriority.Normal, never Loaded: this app keeps the dispatcher busy enough
        /// (compositor host plus avatar animations) that Loaded-priority work is starved and
        /// silently never runs. That starvation is what killed the original first-launch tour, and
        /// a starved pump would be worse still - it holds the flag every other poller waits on.</para>
        /// </summary>
        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    var next = _core.Next();
                    if (next == null) break;

                    await Task.Delay(SettleDelay);

                    var waited = TimeSpan.Zero;
                    while (waited < MaxWaitPerSurface &&
                           !StartupQueueCore.CanStartModal(false, SafeUpdateDialogActive(), SafeTutorialActive(), SafeWindowReady()))
                    {
                        await Task.Delay(PollInterval);
                        waited += PollInterval;
                    }

                    // The queue can have changed while we waited (a higher-priority surface can
                    // arrive mid-wait), so re-read rather than trusting the peek from before.
                    var entry = _core.Dequeue();
                    if (entry == null) break;

                    if (!_shows.TryGetValue(entry.Value.Key, out var surface)) continue;
                    _shows.Remove(entry.Value.Key);

                    if (!StartupQueueCore.CanStartModal(false, SafeUpdateDialogActive(), SafeTutorialActive(), SafeWindowReady()))
                    {
                        App.Logger?.Information(
                            "[Startup] gave up on '{Key}' after {Seconds:0}s - the screen never came free; it is owed the next launch",
                            entry.Value.Key, waited.TotalSeconds);
                        if (surface.Abandoned != null) RunSafely(surface.Abandoned, entry.Value.Key, "abandon");
                        continue;
                    }

                    RunModal(entry.Value.Key, surface.Show);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Startup] the ladder pump stopped unexpectedly");
            }
            finally
            {
                _pumpRunning = false;
            }

            // A surface can have been queued from inside another surface (What's New offering the
            // tour, the wizard queueing a follow-up). Re-arm rather than declaring the ladder done.
            if (!_core.IsEmpty)
            {
                StartPump();
                return;
            }

            App.Logger?.Information("[Startup] the ladder is drained");
            try { Drained?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug(ex, "[Startup] a Drained handler threw"); }
        }

        /// <summary>
        /// Runs one surface inside the flag every other poller in the app watches. Synchronous by
        /// construction: <paramref name="show"/> blocks in its own nested message loop, and the
        /// finally is what guarantees the flag comes back down even when the surface throws (a
        /// stuck-true flag is how the update dialog and the mod picker both vanished for a launch).
        /// </summary>
        private void RunModal(string key, Action<Window?> show)
        {
            _modalUp = true;
            ConditioningControlPanel.MainWindow.IsStartupDialogShowing = true;
            NotifyQuietMaybeChanged();
            App.Logger?.Information("[Startup] showing '{Key}'", key);

            try
            {
                show(Owner);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Startup] '{Key}' failed to show", key);
            }
            finally
            {
                _modalUp = false;
                ConditioningControlPanel.MainWindow.IsStartupDialogShowing = false;
                App.Logger?.Information("[Startup] '{Key}' is done", key);
                NotifyQuietMaybeChanged();
            }
        }

        private static void RunSafely(Action action, string key, string what)
        {
            try { action(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Startup] Inbox {What} failed for '{Key}'", what, key); }
        }

        /// <summary>
        /// A 1 Hz watch over the things that end the quiet window without telling anyone - the
        /// first-launch clock running out, a session or a tour ending. It only runs while quiet is
        /// actually on, so the steady state of a normal launch is no timer at all.
        /// </summary>
        private void EnsureQuietWatch()
        {
            if (_quietTimer != null) return;

            _quietTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _quietTimer.Tick += (_, _) => NotifyQuietMaybeChanged();
            _quietTimer.Start();
        }

        private void StopQuietWatch()
        {
            if (_quietTimer == null) return;
            _quietTimer.Stop();
            _quietTimer = null;
        }

        private void NotifyQuietMaybeChanged()
        {
            bool quiet;
            try { quiet = IsQuiet; }
            catch { return; }

            if (quiet == _lastQuiet)
            {
                // Nothing to announce, but the clock still needs to exist while quiet is on and
                // needs to stop once it is off.
                if (quiet) EnsureQuietWatch(); else StopQuietWatch();
                return;
            }

            _lastQuiet = quiet;
            App.Logger?.Information("[Startup] quiet window {State}", quiet ? "opened" : "closed");

            if (quiet) EnsureQuietWatch(); else StopQuietWatch();

            try { QuietChanged?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug(ex, "[Startup] a QuietChanged handler threw"); }
        }
    }
}
