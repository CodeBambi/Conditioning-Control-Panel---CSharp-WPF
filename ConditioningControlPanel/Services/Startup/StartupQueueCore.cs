using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// What kind of startup surface a decision is being made about.
    ///
    /// <para><b>Modal</b> owns the screen and goes through the ladder one at a time. It is never
    /// routed to the Inbox: a modal that quietly became a row nobody clicked is a modal that
    /// never ran, and the ladder already has a "wait, then give up to the next launch" rule.</para>
    ///
    /// <para><b>Passive</b> is everything that used to pop itself in the user's face without
    /// asking - the announcement popup, the weekly nudge, the feature cards, the unlock toasts.
    /// Those are the ones the quiet window converts into Inbox rows.</para>
    /// </summary>
    public enum StartupSurfaceKind
    {
        Modal,
        Passive,
    }

    /// <summary>
    /// Where a surface goes right now.
    ///
    /// <para><b>Present</b> opens it. <b>Inbox</b> parks it as a row the user opens when they feel
    /// like it. <b>Defer</b> is the third answer, and it exists because the second one was being
    /// given far too often: the ladder counts as quiet from the instant a modal is QUEUED, and on
    /// an ordinary launch the ladder is briefly non-empty while its predicates resolve even when
    /// every one of them says no. A card that arrived in that gap was filed away unread on a
    /// launch where nothing modal ever appeared. Deferred means "ask me again once the ladder is
    /// drained": if quiet has really ended by then it opens, and if a modal did run it becomes a
    /// row after all.</para>
    /// </summary>
    public enum StartupRouting
    {
        Present,
        Inbox,
        Defer,
    }

    /// <summary>
    /// Everything the quiet-window rule reads, in one struct so the rule itself is a pure
    /// function of its inputs and can be tested without a Dispatcher, an App, or a clock.
    /// </summary>
    public readonly struct QuietInputs
    {
        /// <summary>A ladder surface is on screen right now.</summary>
        public bool ModalUp { get; init; }

        /// <summary>
        /// The ladder has something queued or a pump in flight, but nothing is on screen yet.
        ///
        /// <para>Kept apart from <see cref="ModalUp"/> even though both make it quiet, because the
        /// two deserve different answers: a modal really on screen means a passive surface has to
        /// wait for the user, while a merely busy ladder is usually the few hundred milliseconds in
        /// which the ladder's own predicates are still deciding, and is worth re-asking about
        /// rather than filing away. See <see cref="Route"/>.
        /// </para>
        /// </summary>
        public bool LadderBusy { get; init; }

        /// <summary>The guided tour / spotlight overlay is running.</summary>
        public bool TutorialActive { get; init; }

        /// <summary>A conditioning session is running.</summary>
        public bool SessionRunning { get; init; }

        /// <summary>
        /// End of the first-launch grace window, or null when no such window was ever opened.
        /// The wizard's far side opens it (10 minutes); nothing else does.
        /// </summary>
        public DateTime? FirstLaunchUntilUtc { get; init; }

        /// <summary>The instant the decision is being made, UTC.</summary>
        public DateTime NowUtc { get; init; }
    }

    /// <summary>
    /// The ordering and quiet-window rules behind <c>StartupPresenter</c>, with no WPF in sight.
    ///
    /// <para>The app used to decide "may I open now?" in eight different places, each with its own
    /// copy of a 500 ms poll over <c>IsUpdateDialogActive</c> / <c>IsStartupDialogShowing</c> /
    /// <c>Tutorial.IsActive</c>, and each with its own idea of what counted as settled. That is how
    /// a fresh install ended up with fourteen modal stops in its first thirty seconds. The rules
    /// live here instead, once, as pure functions over explicit inputs.</para>
    ///
    /// <para>Not thread-safe by itself; the presenter owns one instance and only ever touches it
    /// from the UI thread.</para>
    /// </summary>
    public sealed class StartupQueueCore
    {
        /// <summary>One queued modal. <paramref name="Seq"/> is the arrival counter that makes
        /// equal priorities resolve first-in-first-out instead of by dictionary luck.</summary>
        public readonly record struct Entry(string Key, int Priority, long Seq);

        private readonly List<Entry> _pending = new();
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        private long _seq;

        /// <summary>How many surfaces are waiting for their turn.</summary>
        public int Count => _pending.Count;

        /// <summary>True when nothing is queued.</summary>
        public bool IsEmpty => _pending.Count == 0;

        /// <summary>True when <paramref name="key"/> is already waiting.</summary>
        public bool Contains(string key) => !string.IsNullOrWhiteSpace(key) && _keys.Contains(key);

        /// <summary>
        /// Queues a surface. Returns false - and changes nothing - when the key is blank or is
        /// already queued.
        ///
        /// <para>Dedupe is by key on purpose. Two callers racing to offer the same card (the
        /// Dashboard becoming visible twice, a tier event firing on both providers) must produce
        /// one turn on the ladder, and the caller that lost must be able to tell.</para>
        /// </summary>
        public bool Enqueue(string key, int priority)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (!_keys.Add(key)) return false;

            _pending.Add(new Entry(key, priority, _seq++));
            return true;
        }

        /// <summary>
        /// The surface that should run next - lowest priority number first, then oldest arrival -
        /// or null when nothing is queued. Does not remove it; the presenter peeks while it waits
        /// for the screen to be free, so a surface that is passed over keeps its place.
        /// </summary>
        public Entry? Next()
        {
            if (_pending.Count == 0) return null;

            var best = _pending[0];
            for (int i = 1; i < _pending.Count; i++)
            {
                var candidate = _pending[i];
                if (candidate.Priority < best.Priority ||
                    (candidate.Priority == best.Priority && candidate.Seq < best.Seq))
                {
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>Removes and returns <see cref="Next"/>, or null when nothing is queued.</summary>
        public Entry? Dequeue()
        {
            var next = Next();
            if (next == null) return null;
            Remove(next.Value.Key);
            return next;
        }

        /// <summary>
        /// Drops a queued surface without running it. Returns false when it was not queued.
        ///
        /// <para>This is also how the presenter CONSUMES a turn: it peeks with <see cref="Next"/>,
        /// waits for the screen, and then takes that exact key rather than re-asking who is best.
        /// A higher-priority surface arriving mid-wait must not inherit the waiter's give-up
        /// verdict - it gets its own turn, and its own five minutes, on the next lap.</para>
        ///
        /// <para>Both stores are swept whatever the other one says. They cannot disagree through
        /// the public API, but the pump loops on <see cref="Next"/> until this returns the key it
        /// asked for, so a list entry surviving a set-only removal would spin forever.</para>
        /// </summary>
        public bool Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            bool wasQueued = _keys.Remove(key);
            for (int i = 0; i < _pending.Count; i++)
            {
                if (string.Equals(_pending[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    _pending.RemoveAt(i);
                    return true;
                }
            }
            return wasQueued;
        }

        /// <summary>Everything queued, in the order it will run. Snapshot; for tests and logging.</summary>
        public IReadOnlyList<Entry> Drain()
        {
            var ordered = new List<Entry>(_pending);
            ordered.Sort(static (a, b) =>
            {
                int byPriority = a.Priority.CompareTo(b.Priority);
                return byPriority != 0 ? byPriority : a.Seq.CompareTo(b.Seq);
            });
            return ordered;
        }

        /// <summary>Forgets everything queued.</summary>
        public void Clear()
        {
            _pending.Clear();
            _keys.Clear();
        }

        /// <summary>
        /// The quiet window: true while anything already owns the user's attention, or while the
        /// first-launch grace period is still open.
        ///
        /// <para>"Quiet" is not "idle". It is the app's promise that for the first ten minutes of
        /// a fresh install - and any time a modal, a tour or a session is running - nothing will
        /// interrupt on its own initiative. Things that would have popped become Inbox rows and
        /// wait to be asked for.</para>
        /// </summary>
        public static bool IsQuiet(in QuietInputs w)
        {
            if (w.ModalUp) return true;
            if (w.LadderBusy) return true;
            if (w.TutorialActive) return true;
            if (w.SessionRunning) return true;
            if (w.FirstLaunchUntilUtc is DateTime until && w.NowUtc < until) return true;
            return false;
        }

        /// <summary>
        /// Should this surface become an Inbox row rather than open itself?
        ///
        /// <para>Only passive surfaces are ever inboxed. A modal on the ladder has already been
        /// ordered against every other modal, and the ladder is what keeps it from stacking.</para>
        /// </summary>
        public static bool ShouldInbox(StartupSurfaceKind kind, in QuietInputs w)
            => Route(kind, w) == StartupRouting.Inbox;

        /// <summary>
        /// The whole routing rule for one surface: open it, file it, or ask again later.
        ///
        /// <para>Modals never route anywhere but Present - the ladder, not this rule, is what
        /// keeps them from stacking, and a modal that quietly became a row nobody clicked is a
        /// modal that never ran.</para>
        ///
        /// <para>For a passive surface, the reason it is quiet decides the answer. A real modal on
        /// screen, a tour, a session, or the first-launch grace window are all somebody ELSE owning
        /// the user, and the honest response is a row they can come back to. A merely busy ladder
        /// is not: on every ordinary launch the ladder holds queued entries for a second or two
        /// while What's New, the recap and the picker each decide they have nothing to do, and the
        /// dashboard's feature card or a fast announcement landing in that window was being filed
        /// away unread on launches where no modal ever appeared. Those DEFER, and the presenter
        /// asks again when the ladder drains.</para>
        /// </summary>
        public static StartupRouting Route(StartupSurfaceKind kind, in QuietInputs w)
        {
            if (kind != StartupSurfaceKind.Passive) return StartupRouting.Present;
            if (!IsQuiet(w)) return StartupRouting.Present;

            bool somebodyElseOwnsTheUser =
                w.ModalUp ||
                w.TutorialActive ||
                w.SessionRunning ||
                (w.FirstLaunchUntilUtc is DateTime until && w.NowUtc < until);

            return somebodyElseOwnsTheUser ? StartupRouting.Inbox : StartupRouting.Defer;
        }

        /// <summary>
        /// May the ladder start its next surface right now?
        ///
        /// <para>The three refusals are the ones every hand-rolled poll in this codebase already
        /// used, gathered in one place: never behind the update dialog (it is Topmost, and a modal
        /// nested under it owns input that nobody can reach - #481), never on top of the guided
        /// tour (the spotlight measures live controls and a modal ambushes it), and never before
        /// the window exists to own the dialog.</para>
        /// </summary>
        public static bool CanStartModal(bool modalUp, bool updateDialogActive, bool tutorialActive, bool windowReady)
            => !modalUp && !updateDialogActive && !tutorialActive && windowReady;
    }
}
