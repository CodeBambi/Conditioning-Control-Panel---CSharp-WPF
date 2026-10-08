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
        /// <summary>The window the last Start was given (tests: #736).</summary>
        internal double? LastWindowMinutes { get; private set; }

        public void Start(double? windowMinutes = null)
        {
            if (_isRunning) return;
            LastWindowMinutes = windowMinutes;

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

            // Debug, not Information: main dropped this line's level in LockCardService.Stop
            // because a stop is routine (pause, toggle, settings apply) and it was noise.
            Log.Debug("LockCardScheduler stopped");
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

        /// <summary>
        /// The hard ceiling on a length-derived repeat count. A one-word phrase against a 600
        /// character budget would otherwise ask for a hundred repeats, which is not a lock card,
        /// it is a wall. Only the length mode can reach this cap - the count modes are bounded by
        /// the sliders at 10.
        /// </summary>
        public const int TargetLengthRepeatCap = 30;

        /// <summary>
        /// How many times this card must be typed. Pure and static so the three modes are testable
        /// without a window, a settings file or a dispatcher.
        ///
        /// <para>Precedence, highest first:</para>
        /// <list type="number">
        /// <item><paramref name="customRepeats"/> when non-negative - an AI, a Goon round or
        /// MantraLockScreenCommand asked for an exact count and must get it, whatever the user's
        /// sliders say.</item>
        /// <item>Length mode (<paramref name="targetLengthEnabled"/>): roll a character budget of
        /// <paramref name="targetLength"/> +/- <paramref name="variance"/> and repeat the phrase
        /// until it covers that budget, so a short line and a long one cost the same effort.
        /// Beats random mode - it already produces a varying count.</item>
        /// <item>Random mode (<paramref name="randomRepeats"/>): a uniform draw over
        /// [<paramref name="repeatsMin"/>, <paramref name="repeatsMax"/>].</item>
        /// <item>Otherwise the flat <paramref name="repeatsMax"/>, which is the pre-6.9.4
        /// behaviour and what every default reproduces.</item>
        /// </list>
        /// </summary>
        /// <param name="roll">A draw in [0,1). One roll serves whichever mode is live.</param>
        public static int ResolveRepeats(
            int customRepeats,
            string? phrase,
            bool randomRepeats,
            int repeatsMin,
            int repeatsMax,
            bool targetLengthEnabled,
            int targetLength,
            int variance,
            double roll)
        {
            if (customRepeats >= 0) return customRepeats;

            // Clamp the roll rather than trust it: Random.NextDouble() never returns 1.0, but a
            // test, a future caller or a different RNG might, and every arithmetic path below
            // overshoots its top end by exactly one when it does.
            if (double.IsNaN(roll)) roll = 0;
            roll = Math.Clamp(roll, 0.0, 0.9999999);

            if (targetLengthEnabled)
            {
                // The NORMALISED length, because that is what the user actually has to type: an
                // ellipsis in the phrase is three keystrokes, a double space is one.
                var typedLength = LockCardText.Normalize(phrase).Length;
                if (typedLength > 0)
                {
                    var spread = Math.Max(0, variance);
                    var budget = targetLength + (int)Math.Round((roll * 2.0 - 1.0) * spread);
                    if (budget < 1) budget = 1;

                    // Ceiling division: the budget is a floor to CLEAR, so a phrase that covers it
                    // only halfway on the last pass still owes that pass.
                    var derived = (budget + typedLength - 1) / typedLength;
                    return Math.Clamp(derived, 1, TargetLengthRepeatCap);
                }
                // An empty phrase has no length to divide by. Fall through rather than return 1:
                // the user still asked for a card, and the count modes below still answer.
            }

            if (randomRepeats)
            {
                var lo = Math.Min(repeatsMin, repeatsMax);
                var hi = Math.Max(repeatsMin, repeatsMax);
                if (lo < 1) lo = 1;
                if (hi < lo) hi = lo;
                return Math.Min(hi, lo + (int)(roll * (hi - lo + 1)));
            }

            return repeatsMax;
        }

        /// <summary>The enabled half of the phrase pool, in settings order. Both heads' "no phrases
        /// enabled" guard and both heads' draw read the pool through here.</summary>
        public static List<string> EnabledPhrases() =>
            CoreSettings.Current.LockCardPhrases?.Where(p => p.Value).Select(p => p.Key).ToList()
            ?? new List<string>();

        public void Dispose() => Stop();
    }
}
