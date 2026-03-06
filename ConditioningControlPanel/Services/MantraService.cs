using System;
using System.Diagnostics;

namespace ConditioningControlPanel.Services
{
    public class MantraService : IDisposable
    {
        private readonly Random _random = new();
        private string? _lastMantra;
        private Stopwatch? _mantraTimer;
        private int _completionsThisMinute;
        private DateTime _minuteWindowStart;

        public string? CurrentMantra { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public int Completions { get; private set; }
        public int TargetCount { get; private set; }
        public bool IsActive { get; private set; }

        public event Action<int>? StreakChanged;
        public event Action? StreakBroken;
        public event Action? MantraCompleted;
        public event Action<int, int>? SessionComplete; // totalReps, bestStreak

        public void StartSession(int targetReps)
        {
            TargetCount = Math.Clamp(targetReps, 1, 100);
            Completions = 0;
            Streak = 0;
            BestStreak = 0;
            IsActive = true;
            _completionsThisMinute = 0;
            _minuteWindowStart = DateTime.UtcNow;
            _mantraTimer = Stopwatch.StartNew();
            NextMantra();
        }

        public bool TryCompleteMantra()
        {
            if (!IsActive || CurrentMantra == null) return false;

            // Anti-cheat: minimum 1.5s per mantra
            if (_mantraTimer != null && _mantraTimer.Elapsed.TotalSeconds < 1.5)
                return false;

            // Anti-cheat: max 20 completions per minute
            if ((DateTime.UtcNow - _minuteWindowStart).TotalSeconds >= 60)
            {
                _completionsThisMinute = 0;
                _minuteWindowStart = DateTime.UtcNow;
            }
            if (_completionsThisMinute >= 20)
                return false;

            _completionsThisMinute++;
            Completions++;
            Streak++;
            if (Streak > BestStreak) BestStreak = Streak;

            // XP: 30 base + min(streak*5, 50)
            var bonusXP = Math.Min(Streak * 5, 50);
            App.Progression?.AddXP(30 + bonusXP, XPSource.Mantra);
            App.Quests?.TrackMantraCompleted();

            if (Completions >= TargetCount)
            {
                IsActive = false;
                MantraCompleted?.Invoke();
                StreakChanged?.Invoke(Streak);
                SessionComplete?.Invoke(Completions, BestStreak);
                return true;
            }

            _mantraTimer?.Restart();
            NextMantra();

            MantraCompleted?.Invoke();
            StreakChanged?.Invoke(Streak);
            return true;
        }

        public void BreakStreak()
        {
            if (!IsActive || Streak == 0) return;
            Streak = 0;
            StreakBroken?.Invoke();
            StreakChanged?.Invoke(0);
        }

        public void EndSession()
        {
            if (!IsActive) return;
            IsActive = false;
            CurrentMantra = null;
            _mantraTimer?.Stop();
        }

        private void NextMantra()
        {
            var pool = App.Settings?.Current?.MantraPool;
            if (pool == null || pool.Count == 0)
            {
                CurrentMantra = "I am deeply relaxed";
                return;
            }

            if (pool.Count == 1)
            {
                CurrentMantra = pool[0];
                return;
            }

            // No immediate repeats
            string next;
            do
            {
                next = pool[_random.Next(pool.Count)];
            } while (next == _lastMantra && pool.Count > 1);

            _lastMantra = next;
            CurrentMantra = next;
        }

        public void Dispose()
        {
            IsActive = false;
            _mantraTimer?.Stop();
        }
    }
}
