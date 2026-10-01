using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class FlashImageCommand : ICommand
    {
        // Tightened from PR's 0..20 / 0..30 / 0..200 to keep AI flashes in a safe zone.
        public const int MaxAmount = 8;
        public const int MaxDurationSec = 10;
        public const int MaxSizePct = 150;

        private readonly FlashImage _data;
        public FlashImageCommand(FlashImage data) { _data = data; }

        /// <summary>Head surface: (amount, durationMs, sizePct) on the UI thread (WPF
        /// App.Flash.TriggerFlashOnce). False when nothing could show. Unseeded: refused.</summary>
        public static volatile Func<int, int, int, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            var amount = Math.Clamp(_data.Amount, 0, MaxAmount);
            var durationSec = Math.Clamp(_data.Duration, 0, MaxDurationSec);
            var size = Math.Clamp(_data.Size, 0, MaxSizePct);
            // FlashService now expects duration in milliseconds (was implicitly seconds).
            var durationMs = durationSec * 1000;

            try
            {
                return Task.FromResult(Surface?.Invoke(amount, durationMs, size) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FlashImageCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
