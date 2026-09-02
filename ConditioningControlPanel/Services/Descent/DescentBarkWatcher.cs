using System;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE SEAM. DescentService already re-reads the block on its own poll and after
    // every sync, and it already raises BlockChanged on the UI thread when it does.
    // So the four block-driven barks need no clock, no subscription of their own to
    // any feature service, and nothing added to the Descent read path: they are a
    // listener on an event that was already firing.
    //
    // THE MEMORY IS ON DISK, not in AppSettings. A day key per moment is exactly
    // four small strings, they are meaningless outside this watcher, and the
    // settings file is not the place to grow a per-day ledger.
    //
    // NOTHING HERE MAY THROW. A bark is decoration on a meter; a decoration that
    // can break a profile poll is a bug with a much bigger blast radius than the
    // feature is worth.
    // ============================================================================

    /// <summary>
    /// Turns <see cref="DescentService.BlockChanged"/> into at most one companion
    /// bark, using <see cref="DescentBarkPolicy"/> for every decision and a tiny
    /// persisted memory so a restart cannot replay a milestone.
    /// </summary>
    public static class DescentBarkWatcher
    {
        private static readonly object _gate = new();
        private static bool _attached;
        private static DescentBarkMemory? _memory;

        /// <summary>Where the day keys live. Beside the other small per-user stores.</summary>
        private static string FilePath => Path.Combine(App.UserDataPath, "descent_barks.json");

        /// <summary>
        /// Subscribe to the Descent block. Idempotent, and safe to call before the
        /// service exists (it simply does nothing and can be called again). Wired
        /// from <c>BarkService.Start()</c>, which App.OnStartup runs well after
        /// <c>App.Descent</c> is constructed.
        /// </summary>
        public static void Attach()
        {
            lock (_gate)
            {
                if (_attached) return;
                var descent = App.Descent;
                if (descent == null) return;
                descent.BlockChanged += OnBlockChanged;
                _attached = true;
            }
            App.Logger?.Debug("[Descent] bark watcher attached");
        }

        /// <summary>Symmetric teardown. Only the tests and a shutdown need it.</summary>
        public static void Detach()
        {
            lock (_gate)
            {
                if (!_attached) return;
                var descent = App.Descent;
                if (descent != null) descent.BlockChanged -= OnBlockChanged;
                _attached = false;
            }
        }

        private static void OnBlockChanged(object? sender, EventArgs e)
        {
            try { Evaluate(App.Descent?.Current); }
            catch (Exception ex) { App.Logger?.Debug("[Descent] bark watcher failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Read the block, ask the policy, speak at most one line. Public so the
        /// Spiral room can prod it on entry without waiting for the next poll.
        /// </summary>
        public static void Evaluate(DescentBlock? block)
        {
            if (block is null) return;

            DescentBarkDecision decision;
            lock (_gate)
            {
                var memory = LoadLocked();
                decision = DescentBarkPolicy.Decide(block, memory, DescentBarkPolicy.TodayUtc(DateTime.UtcNow));
                SaveLocked(memory);
            }

            var bark = App.Bark;
            if (bark == null || decision.Moment == DescentBarkMoment.None) return;

            switch (decision.Moment)
            {
                case DescentBarkMoment.NearBank:
                    bark.NotifyDescentNearBank(decision);
                    break;
                case DescentBarkMoment.DayBanked:
                    bark.NotifyDescentDayBanked(decision);
                    break;
                case DescentBarkMoment.StageCrossed:
                    bark.NotifyDescentStageCrossed(decision);
                    break;
                case DescentBarkMoment.LapseReturn:
                    bark.NotifyDescentLapseReturn(decision);
                    break;
            }
        }

        /// <summary>
        /// The Spiral room was entered. The once-ever latch lives in the bark rule
        /// itself (repeatable false, lifetime scope), so this may be called on every
        /// entry and will only ever speak the first time.
        /// </summary>
        public static void NotifySpiralOpened()
        {
            try { App.Bark?.NotifyDescentFirstSpiralOpen(App.Descent?.Current); }
            catch (Exception ex) { App.Logger?.Debug("[Descent] spiral-open bark failed: {E}", ex.Message); }
        }

        // ------------------------------------------------------------- the memory

        private static DescentBarkMemory LoadLocked()
        {
            if (_memory != null) return _memory;
            try
            {
                if (File.Exists(FilePath))
                    _memory = JsonConvert.DeserializeObject<DescentBarkMemory>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Descent] bark memory unreadable, starting fresh: {E}", ex.Message);
            }
            // An unreadable memory seeds again rather than replaying: Seeded stays
            // false, so the next observation records and says nothing.
            return _memory ??= new DescentBarkMemory();
        }

        private static void SaveLocked(DescentBarkMemory memory)
        {
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(memory, Formatting.Indented));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Descent] bark memory could not be written: {E}", ex.Message);
            }
        }

        /// <summary>Test seam: drop the cached memory so the next Evaluate re-reads disk.</summary>
        internal static void ResetCacheForTests()
        {
            lock (_gate) { _memory = null; }
        }
    }
}
