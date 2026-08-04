using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.GoonGame;

// ============================================================================
// GOON GAME — dev cockpit: simulated sudden-death play.
//
// WHY THIS EXISTS. With the default GoonNullRoundInputs nothing ever raises an
// input, so BOTH clients return an identical "no input" result for every round
// (quick draw times out at 60 s, reaction duel at 2 s, bubble race at 30 s with
// 0 popped). GoonRoundJudge then returns Draw every time, the net score never
// leaves 0, and the ladder runs forever — a headless duel can reach sudden death
// but can never FINISH one.
//
// This driver is a presenter (it hears when a round starts) bolted onto the
// GoonFake* feeds (it can answer). Each panel seeds its own RNG differently, so
// the two sides post different times and every round has a real winner —
// net -3/+3 lands after 3-9 rounds and the match ends for real.
//
// It is a TEST harness, not Phase E: it renders nothing and touches no service.
// ============================================================================

namespace ConditioningControlPanel
{
    internal sealed class GoonTestSimRoundDriver : GoonNullRoundPresenter
    {
        private readonly Random _rng;
        private readonly Action<string>? _log;
        private readonly List<DispatcherTimer> _timers = new();

        public GoonTestSimRoundDriver(int seed, Action<string>? log = null)
        {
            _rng = new Random(seed);
            _log = log;
        }

        /// <summary>The feeds handed to GoonSuddenDeathRunner alongside this presenter.</summary>
        public GoonFakeRoundInputs Inputs { get; } = new();

        // ------------------------------------------------------------- rounds

        public override void ShowRoundIntro(GoonRoundIntro intro)
        {
            Say($"sim: round {intro.RoundNo} {intro.Kind} d{intro.Difficulty} fires at match {intro.FireAtMatchMs}");
        }

        public override void ShowLockCard(GoonLockCardSpec spec)
        {
            CancelAll();
            // Typing time scales with repeats; the spread across panels decides the round.
            var typeMs = (900 + _rng.Next(0, 2600)) * Math.Max(1, spec.Repeats);
            typeMs = Math.Min(typeMs, Math.Max(1000, spec.TimeLimitMs - 500));
            var mistakes = _rng.Next(0, 4);
            Say($"sim: quick draw \"{spec.Phrase}\" x{spec.Repeats} — solving in {typeMs}ms");
            Schedule(typeMs, () => Inputs.FakeLockCard.RaiseSolved(mistakes));
        }

        public override void HideLockCard() => CancelAll();

        public override void StartStaringContest(GoonStaringContestSpec spec)
        {
            CancelAll();
            // Blink somewhere between 40% and 130% of the barrage: sometimes a survive, sometimes not.
            var blinkAt = (int)(spec.DurationMs * (0.4 + _rng.NextDouble() * 0.9));
            Say($"sim: staring contest {spec.DurationMs}ms, {spec.Beats.Count} beats — blink at {blinkAt}ms");
            Schedule(blinkAt, () => Inputs.FakeAttention.RaiseBlink());
            Repeat(500, spec.DurationMs, () => Inputs.FakeAttention.RaiseSample(0.55 + _rng.NextDouble() * 0.45));
        }

        public override void EndStaringContest() => CancelAll();

        public override void ArmReactionDuel(GoonReactionDuelSpec spec)
        {
            CancelAll();
            Say($"sim: reaction duel armed (delay {spec.DelayMs}ms, {spec.DecoyOffsetsMs.Count} decoys)");
        }

        public override void FireReactionStimulus(GoonStimulusKind kind)
        {
            // Decoys are deliberately ignored — a simulated false start would just hand the round
            // away without exercising anything. Only the real stimulus gets an answer, and it is
            // ALWAYS scheduled (never inline): the round sets realFired AFTER this call returns, so
            // a synchronous press would be scored as a false start.
            if (kind != GoonStimulusKind.Real) return;
            var reactionMs = 140 + _rng.Next(0, 340);
            Schedule(reactionMs, () => Inputs.FakeReaction.RaisePress());
        }

        public override void EndReactionDuel() => CancelAll();

        public override void StartBubbleRace(GoonBubbleRaceSpec spec)
        {
            CancelAll();
            var order = Enumerable.Range(0, spec.Count).OrderBy(_ => _rng.Next()).ToList();
            var at = 250 + _rng.Next(0, 400);
            var total = 0;
            foreach (var index in order)
            {
                var captured = index;
                Schedule(at, () => Inputs.FakeBubbles.RaisePop(captured));
                total = at;
                at += 120 + _rng.Next(0, 520);
                if (at > spec.TimeoutMs - 500) break;   // the rest simply never get popped
            }
            Say($"sim: bubble race {spec.Count} bubbles — clearing by ~{total}ms");
        }

        public override void EndBubbleRace() => CancelAll();

        public override void ShowRoundVerdict(GoonRoundOutcomeEventArgs outcome)
        {
            CancelAll();
            Say($"sim: verdict round {outcome.RoundNo} = {outcome.Verdict} (net {outcome.NetScore})");
        }

        // ----------------------------------------------------------- plumbing

        private void Say(string line)
        {
            var log = _log;
            if (log == null) return;
            try { log(line); }
            catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] sim log threw"); }
        }

        /// <summary>One-shot on the UI dispatcher. Presenter calls already arrive there, but be defensive.</summary>
        private void Schedule(int delayMs, Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Create()
            {
                if (dispatcher.HasShutdownStarted) return;
                var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)),
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _timers.Remove(timer);
                    try { action(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] sim input threw"); }
                };
                _timers.Add(timer);
                timer.Start();
            }

            if (dispatcher.CheckAccess()) Create();
            else dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Create);
        }

        /// <summary>Repeating tick that stops itself after <paramref name="forMs"/>.</summary>
        private void Repeat(int intervalMs, int forMs, Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Create()
            {
                if (dispatcher.HasShutdownStarted) return;
                var deadline = Environment.TickCount64 + Math.Max(1, forMs);
                var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(50, intervalMs)),
                };
                timer.Tick += (s, e) =>
                {
                    if (Environment.TickCount64 >= deadline)
                    {
                        timer.Stop();
                        _timers.Remove(timer);
                        return;
                    }
                    try { action(); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "[GGTest] sim sample threw"); }
                };
                _timers.Add(timer);
                timer.Start();
            }

            if (dispatcher.CheckAccess()) Create();
            else dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Create);
        }

        /// <summary>Drops every pending simulated input (round boundaries, mercy, teardown).</summary>
        public void CancelAll()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Kill()
            {
                foreach (var timer in _timers.ToList())
                {
                    try { timer.Stop(); } catch { /* already dead */ }
                }
                _timers.Clear();
            }

            if (dispatcher.CheckAccess()) Kill();
            else dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)Kill);
        }
    }
}
