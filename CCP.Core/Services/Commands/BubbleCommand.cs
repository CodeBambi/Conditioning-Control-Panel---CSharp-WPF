using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class BubbleCommand : ICommand
    {
        // Tightened from PR's 0..15 to 0..10 spawns/min.
        public const int MaxFrequency = 10;

        private readonly Bubbles _data;
        public BubbleCommand(Bubbles data) { _data = data; }

        /// <summary>Head surface: (start, frequency or null for the user's) on the UI thread (WPF
        /// App.Bubbles Start/Stop). False when nothing could show. Unseeded: refused.</summary>
        public static volatile Func<bool, int?, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            var frequency = Math.Clamp(_data.Frequency, 0, MaxFrequency);

            // Tolerant intent detection: treat frequency > 0 as "start" even if the
            // AI forgot to set On=true. Conversely, frequency == 0 with On=false is stop.
            // This handles models that emit only one of the two fields.
            var shouldStart = _data.On || frequency > 0;
            Log.Information("BubbleCommand: On={On} Frequency={Freq} -> {Action}",
                _data.On, frequency, shouldStart ? "Start" : "Stop");

            try
            {
                return Task.FromResult(Surface?.Invoke(shouldStart, frequency > 0 ? frequency : (int?)null) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BubbleCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
