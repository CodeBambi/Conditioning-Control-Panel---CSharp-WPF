using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// THE BANKER: the thing that turns roughly thirty <c>AddXP</c> call sites into a handful of
    /// rare, deliberate BANK moments.
    ///
    /// <para><b>Why this exists.</b> XP in this app does not arrive as events, it arrives as
    /// weather - a flash burst, a mantra, an attention check and a bubble pop can all settle inside
    /// half a second. Celebrating each one is not seven celebrations, it is noise, and noise is the
    /// one thing a reward moment cannot survive. So awards are pooled: the first one opens a
    /// collection window (<see cref="WindowMs"/>), everything landing inside joins the pot, and the
    /// pot flies ONCE.</para>
    ///
    /// <para><b>The gate came first.</b> Pooling alone still celebrated ambient weather, just in
    /// tidier lumps - a flight every few seconds for XP nobody had DONE anything to earn. Only
    /// completion-shaped awards reach a pot at all now (<see cref="IsBankable"/>); everything else
    /// bypasses THE BANK and tweens the counter the way it did before this class existed. Rarity is
    /// what buys the moment its weight, and it is why the flight itself could then be made louder.</para>
    ///
    /// <para><b>The cooldown is the second dial.</b> A window alone would still fire every 1.5s
    /// under sustained XP. <see cref="CooldownMs"/> is the floor between flight STARTS: while it is
    /// running the open pot simply keeps collecting, so two completions landing on top of each
    /// other produce one fuller flight rather than a stutter of thin ones. With the gate in front of
    /// it this rarely binds - it is kept as the backstop for a burst of completions, not as the
    /// thing that makes THE BANK rare.</para>
    ///
    /// <para><b>A level-up poisons the pot.</b> House law, one burst per moment: if an award crossed
    /// a level, <c>CelebrateLevelUp</c> owns that instant and THE BANK yields entirely - the pot is
    /// abandoned rather than deferred, because a flight arriving on the far side of a level-up
    /// celebration is a second celebration for XP the subject already watched be spent.</para>
    ///
    /// <para>Pure: every timestamp comes from the injected monotonic clock, there is no
    /// <c>DateTime.Now</c>, no static state and no timer of its own. The caller owns the poll (see
    /// <see cref="Tick"/>), which is what lets the shell keep its no-idle-clock promise.</para>
    /// </summary>
    public sealed class BankAccumulator
    {
        // ---- DIALS ----

        /// <summary>How long a pot stays open for company after its first award.</summary>
        public const double WindowMs = 1500;

        /// <summary>Minimum gap between flight STARTS. Awards arriving inside it join the next pot.</summary>
        public const double CooldownMs = 3000;

        /// <summary>
        /// THE BANK is a celebration for FINISHING something, and this is the whole guest list.
        ///
        /// <para>The four here share one shape: a fixed award, paid once, at the end of a thing the
        /// subject chose to see through. Every other <see cref="XPSource"/> is weather - a flash
        /// tick, a mantra, a subliminal, a bubble, an attention check - and weather arriving with
        /// tokens and a thud is how a reward moment turns into a notification sound. Those awards
        /// never enter a pot; their XP tweens the counter exactly as it did before THE BANK
        /// existed (see <c>MainWindow.BankFx.cs</c>, which releases the display hold for them on
        /// the spot).</para>
        ///
        /// <para>ONE list, deliberately: adding a future completion (a program graduating, an
        /// Arcademy diploma) is a one-line change here and nowhere else.</para>
        /// </summary>
        public static bool IsBankable(XPSource source) => source switch
        {
            XPSource.Quest => true,        // a daily/weekly quest paying out
            XPSource.Session => true,      // a session run to the end
            XPSource.LockCard => true,     // a lock card served
            XPSource.BubbleCount => true,  // the counting game's result screen
            _ => false,
        };

        private readonly Func<double> _nowMs;

        /// <summary>
        /// The open pot, in first-seen order - which is also the tie-break for
        /// <see cref="Flight.DominantSource"/>. A list and not a per-enum array because the order
        /// IS the tie-break, and because a pot never holds more than a handful of distinct sources.
        /// </summary>
        private readonly List<Contribution> _pot = new(8);

        private bool _open;
        private double _openedMs;
        private double _sum;

        /// <summary>
        /// When the last flight was launched. Starts at negative infinity so the first BANK moment
        /// of a session is never held back by a cooldown that has not happened yet.
        /// </summary>
        private double _lastLaunchMs = double.NegativeInfinity;

        private struct Contribution
        {
            public XPSource Source;
            public double Xp;
        }

        /// <param name="nowMs">A monotonic millisecond clock - a <c>Stopwatch</c> in the app, a
        /// plain variable in tests. Must never go backwards.</param>
        public BankAccumulator(Func<double> nowMs)
        {
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        }

        /// <summary>
        /// One BANK moment, ready to fly. <see cref="XpSum"/> is the whole pot (the ledger figure the
        /// counter must land on), <see cref="DominantSource"/> is the source that put the most XP in
        /// it - the one the tokens should be seen to spawn FROM - and <see cref="TokenCount"/> is
        /// what <see cref="BankFlightPlan.TokenCount"/> prices that sum at.
        /// </summary>
        public sealed record Flight(double XpSum, XPSource DominantSource, int TokenCount);

        /// <summary>True while a pot is collecting. The shell polls <see cref="Tick"/> only while this holds.</summary>
        public bool HasOpenPot => _open;

        /// <summary>
        /// Record one award. Returns null almost always; returns a <see cref="Flight"/> exactly when
        /// this award closed a pot that was already ripe - i.e. XP is still flowing after the
        /// previous window expired, so the award that arrives is the one that ships the pot before
        /// opening the next one.
        ///
        /// <para>Non-positive, NaN and infinite amounts are ignored outright rather than opening a
        /// pot: this sits on the live XP path, and a zero-XP award must not be able to arm a
        /// celebration for nothing.</para>
        ///
        /// <para>The caller is expected to have asked <see cref="IsBankable"/> first - the gate
        /// lives at the call site because that is the only place that can also hand the display
        /// back for a source THE BANK is declining.</para>
        /// </summary>
        public Flight? OnAward(double amount, XPSource source, bool leveledUp)
        {
            // House law: the level-up owns the moment. Abandon, do not defer.
            if (leveledUp)
            {
                Reset();
                return null;
            }

            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0) return null;

            double now = _nowMs();
            var ready = TryClose(now);

            if (!_open)
            {
                _open = true;
                _openedMs = now;
            }

            _sum += amount;

            for (int i = 0; i < _pot.Count; i++)
            {
                if (_pot[i].Source != source) continue;
                var c = _pot[i];
                c.Xp += amount;
                _pot[i] = c;
                return ready;
            }
            _pot.Add(new Contribution { Source = source, Xp = amount });

            return ready;
        }

        /// <summary>
        /// The poll. Returns a <see cref="Flight"/> the first time the open pot's window has expired
        /// AND the cooldown permits a launch; null every other time, including when there is no pot
        /// at all. Idempotent by construction - closing a pot clears it, so a pot can only ever fly
        /// once no matter how often this is called.
        /// </summary>
        public Flight? Tick() => TryClose(_nowMs());

        /// <summary>
        /// Drop the open pot silently - no flight, no callback. The shell calls this when the window
        /// is deactivated, so a half-collected pot cannot ambush a returning user with a celebration
        /// for XP they earned before they left (focus-state silence: not queued, just gone).
        ///
        /// <para>The cooldown deliberately SURVIVES a reset: abandoning a pot must not buy back a
        /// launch slot, or a stream of level-ups would let flights fire back to back.</para>
        /// </summary>
        public void Reset() => ClearPot();

        private Flight? TryClose(double now)
        {
            if (!_open) return null;
            if (now - _openedMs < WindowMs) return null;
            if (now - _lastLaunchMs < CooldownMs) return null;

            var flight = new Flight(_sum, DominantSource(), BankFlightPlan.TokenCount(_sum));
            _lastLaunchMs = now;
            ClearPot();
            return flight;
        }

        /// <summary>Strictly-greater comparison walking first-seen order, so ties go to whoever opened the pot.</summary>
        private XPSource DominantSource()
        {
            if (_pot.Count == 0) return XPSource.Other;

            var best = _pot[0];
            for (int i = 1; i < _pot.Count; i++)
            {
                if (_pot[i].Xp > best.Xp) best = _pot[i];
            }
            return best.Source;
        }

        private void ClearPot()
        {
            _open = false;
            _sum = 0;
            _openedMs = 0;
            _pot.Clear();
        }
    }
}
