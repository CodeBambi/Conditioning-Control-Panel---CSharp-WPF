using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service that manages Lock Card popups
    /// </summary>
    public class LockCardService : IDisposable
    {
        private DispatcherTimer? _timer;
        private Random _random = new();
        private bool _isRunning;
        private bool _isDisposed;
        private DateTime _lastShown = DateTime.MinValue;

        public bool IsRunning => _isRunning;

        public void Start()
        {
            if (_isRunning) return;
            
            var settings = App.Settings.Current;
            
            // Check level requirement
            if (settings.PlayerLevel < 35)
            {
                App.Logger?.Information("LockCardService: Level {Level} is below 35, not available", settings.PlayerLevel);
                return;
            }
            
            if (!settings.LockCardEnabled)
            {
                App.Logger?.Information("LockCardService: Disabled in settings");
                return;
            }
            
            _isRunning = true;
            
            // Calculate interval based on frequency (per hour)
            var perHour = settings.LockCardFrequency;
            var intervalMinutes = 60.0 / perHour;
            
            // Add some randomness (±30%)
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;
            
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            
            App.Logger?.Information("LockCardService started - approximately {PerHour}/hour", perHour);
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            
            _timer?.Stop();
            _timer = null;
            
            App.Logger?.Information("LockCardService stopped");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Recalculate next interval with randomness
            var settings = App.Settings.Current;
            var perHour = settings.LockCardFrequency;
            var intervalMinutes = 60.0 / perHour;
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;
            
            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval);
            }
            
            // Check if enabled
            if (!settings.LockCardEnabled || settings.PlayerLevel < 35) return;
            
            // Show the lock card
            ShowLockCard();
        }

        public void ShowLockCard()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Prevent stacking multiple lock cards
                if (Application.Current.Windows.OfType<LockCardWindow>().Any())
                {
                    App.Logger?.Information("LockCardService: A lock card is already open. Skipping.");
                    return;
                }

                try
                {
                    var settings = App.Settings.Current;
                    
                    // Get enabled phrases
                    var enabledPhrases = settings.LockCardPhrases?
                        .Where(p => p.Value)
                        .Select(p => p.Key)
                        .ToList() ?? new List<string>();
                    
                    if (enabledPhrases.Count == 0)
                    {
                        App.Logger?.Warning("LockCardService: No phrases enabled");
                        return;
                    }
                    
                    // Pick a random phrase
                    var phrase = enabledPhrases[_random.Next(enabledPhrases.Count)];
                    var repeats = settings.LockCardRepeats;
                    var strict = settings.LockCardStrict;
                    
                    // Show on all monitors with synced input
                    LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict);
                    
                    _lastShown = DateTime.Now;
                    
                    App.Logger?.Information("Lock Card shown on all monitors - Phrase: {Phrase}", phrase);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to show lock card: {Error}", ex.Message);
                }
            });
        }

        /// <summary>
        /// Manually trigger a test lock card
        /// </summary>
        public void TestLockCard()
        {
            ShowLockCard();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            Stop();
        }
    }
}
