using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable half of the lock card: <b>when</b> the next card is due and <b>which</b> phrase
    /// it carries. Both are arithmetic over <c>AppSettings</c> and a clock, so both live here.
    ///
    /// <para>Drawing is not. A lock card is an ownerless topmost cover over every monitor, and on
    /// the WPF head it also has to negotiate with the interaction queue and a pop quiz before it
    /// may appear. That whole decision stays in the head behind <see cref="CoreLockCard"/>, which
    /// this class calls with nothing but "a card is due, and it is/isn't a test".</para>
    ///
    /// <para>One shared instance, because the rotation memory has to be shared: WPF's
    /// <c>LockCardService</c> wraps this one, and every ad-hoc card (voice command, Deeper, the
    /// dashboard Test button, remote trigger) draws its phrase from the same window of recently
    /// shown ones the scheduled cards do.</para>
    ///
    /// <para>The WPF original ran on a <c>DispatcherTimer</c>, so its tick was on the UI thread. A
    /// <see cref="Timer"/> ticks on the thread pool, so the body hops back through
    /// <see cref="CoreDispatch"/> - which, unseeded, runs it in place. <see cref="PickPhrase"/> is
    /// deliberately NOT called from the tick: the head calls it at show time, after its queue gate,
    /// on its UI thread. Rotation state is therefore touched from one thread only, and a card that
    /// is deferred or dropped never burns a rotation slot.</para>
    /// </summary>
    public sealed class LockCardScheduler : IDisposable
    {
        /// <summary>The app's one lock-card schedule. Both heads drive this instance.</summary>
        public static LockCardScheduler Instance { get; } = new();

        private Timer? _timer;
        private volatile bool _isRunning;

        // Per-session no-repeat rotation, in-memory only - lock-card rotation deliberately does NOT
        // persist to disk. Avoids replaying any of the last few phrases so a pure random draw can't
        // repeat the same phrase back-to-back.
        private readonly Queue<string> _recentPhrases = new();
        private readonly HashSet<string> _recentPhrasesSet = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>How many distinct just-shown phrases to avoid replaying.</summary>
        private const int RecentPhrasesMemory = 3;

        public bool IsRunning => _isRunning;

        /// <param name="windowMinutes">
        /// #736: how long the caller expects to keep the schedule running - a session's remaining
        /// minutes. When supplied, the first card is guaranteed to land inside that window with
        /// room to complete it. Null (dashboard use) means open-ended.
        /// </param>
        public void Start(double? windowMinutes = null)
        {
            if (_isRunning) return;

            if (!CoreSettings.Current.LockCardEnabled)
            {
                Log.Information("LockCardScheduler: Disabled in settings");
                return;
            }

            var perHour = Math.Max(1, CoreSettings.Current.LockCardFrequency);
            var firstDelay = ComputeFirstCardDelayMinutes(perHour, windowMinutes, Random.Shared.NextDouble());

            _isRunning = true;

            // One-shot, re-armed inside the tick - which is all reassigning DispatcherTimer.Interval
            // ever amounted to. The callback closes over its own timer so the tick can tell whether
            // it is still the live one (see Tick).
            Timer? created = null;
            created = new Timer(_ => { var mine = created; CoreDispatch.Post(() => Tick(mine)); });
            _timer = created;
            created.Change(TimeSpan.FromMinutes(firstDelay), Timeout.InfiniteTimeSpan);

            Log.Information(
                "LockCardScheduler started - approximately {PerHour}/hour, first card in {First:F1}min (window {Window})",
                perHour, firstDelay, windowMinutes is > 0 ? $"{windowMinutes.Value:F1}min" : "open-ended");
        }

        /// <summary>Stop the schedule. Scheduler only: a card already on screen is the head's to
        /// drop, and most Stop() callers (pausing a session, un-ticking the feature, applying a
        /// preset) must never walk through strict mode on a card the user is mid-way through
        /// typing.</summary>
        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            var timer = _timer;
            _timer = null;
            timer?.Dispose();

            Log.Information("LockCardScheduler stopped");
        }

        /// <summary>A card is due: re-arm for the next one, then ask the head to show it.</summary>
        /// <param name="firedBy">
        /// The timer this tick belongs to. A <see cref="Timer"/> callback already in flight - plus
        /// the <see cref="CoreDispatch"/> hop - can land AFTER <see cref="Stop"/>, where WPF's
        /// <c>DispatcherTimer.Stop()</c> cancelled a pending tick outright. Without this check,
        /// pausing a session could still pop a card.
        /// </param>
        private void Tick(Timer? firedBy)
        {
            if (!_isRunning || !ReferenceEquals(firedBy, _timer)) return;

            var settings = CoreSettings.Current;
            var next = ComputeNextIntervalMinutes(settings.LockCardFrequency, Random.Shared.NextDouble());
            try { _timer?.Change(TimeSpan.FromMinutes(next), Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { return; }

            if (!settings.LockCardEnabled) return;

            CoreLockCard.Show(isTest: false);
        }

        /// <summary>#736: delay before the FIRST lock card of a run, in minutes. Pure so the
        /// reachability guarantee is unit-testable without a dispatcher.
        ///
        /// The first card is an OFFSET into the opening interval, not a whole inter-arrival gap.
        /// Scheduling it at 60/freq ±30% (as before) put the earliest possible card at 1/hour at
        /// minute 42, so a 30-minute session could never produce one — which hard-blocked every
        /// program day whose task required a lock card. Subsequent cards keep the ±30% spacing in
        /// <see cref="ComputeNextIntervalMinutes"/>.
        ///
        /// When <paramref name="windowMinutes"/> is supplied the card is additionally clamped to
        /// land inside it, leaving the tail free so the user can actually complete the card.
        /// </summary>
        /// <param name="perHour">Cards per hour; values below 1 are treated as 1.</param>
        /// <param name="windowMinutes">Minutes the schedule will keep running, or null for open-ended.</param>
        /// <param name="roll">A uniform random sample in [0,1).</param>
        public static double ComputeFirstCardDelayMinutes(int perHour, double? windowMinutes, double roll)
        {
            var intervalMinutes = 60.0 / Math.Max(1, perHour);

            var maxFirst = intervalMinutes;
            if (windowMinutes is > 0)
                maxFirst = Math.Min(maxFirst, windowMinutes.Value * 0.8);

            return roll * maxFirst;
        }

        /// <summary>Gap to the card after that: the nominal 60/freq, jittered ±30% so the cards
        /// don't land on a metronome. Extracted from the WPF tick body verbatim.</summary>
        /// <param name="roll">A uniform random sample in [0,1).</param>
        public static double ComputeNextIntervalMinutes(int perHour, double roll)
        {
            var intervalMinutes = 60.0 / Math.Max(1, perHour);
            var min = intervalMinutes * 0.7;
            var max = intervalMinutes * 1.3;
            return roll * (max - min) + min;
        }

        /// <summary>
        /// Pick a phrase at random while avoiding the last few shown, so the same phrase can't
        /// repeat back-to-back. Filters the enabled pool against the recent set (rather than
        /// re-rolling in a loop); if that empties the pool — or only one phrase is enabled — we
        /// skip rotation and draw from the full list so we can never loop forever or go silent.
        /// </summary>
        /// <returns>The chosen phrase, or null when the pool is empty.</returns>
        public string? PickPhrase(IReadOnlyList<string> enabledPhrases)
        {
            if (enabledPhrases is null || enabledPhrases.Count == 0) return null;

            IReadOnlyList<string> candidates = enabledPhrases;
            if (enabledPhrases.Count > 1)
            {
                var fresh = enabledPhrases.Where(p => !_recentPhrasesSet.Contains(p)).ToList();
                if (fresh.Count > 0) candidates = fresh;
            }

            var phrase = candidates[Random.Shared.Next(candidates.Count)];

            // Remember it, then trim the window to at most (pool - 1) so there's always at least one
            // fresh candidate next time, capped at RecentPhrasesMemory. Skip tracking a lone phrase.
            if (enabledPhrases.Count > 1 && _recentPhrasesSet.Add(phrase))
            {
                _recentPhrases.Enqueue(phrase);
                int cap = Math.Min(enabledPhrases.Count - 1, RecentPhrasesMemory);
                while (_recentPhrases.Count > cap)
                    _recentPhrasesSet.Remove(_recentPhrases.Dequeue());
            }

            return phrase;
        }

        /// <summary>The enabled half of the phrase pool, in settings order. Both heads' "no phrases
        /// enabled" guard and both heads' draw read the pool through here.</summary>
        public static List<string> EnabledPhrases() =>
            CoreSettings.Current.LockCardPhrases?.Where(p => p.Value).Select(p => p.Key).ToList()
            ?? new List<string>();

        public void Dispose() => Stop();
    }
}
