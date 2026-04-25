using System;
using System.Threading.Tasks;
using System.Windows;
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

        public Task<bool> ExecuteAsync()
        {
            var amount = Math.Clamp(_data.Amount, 0, MaxAmount);
            var duration = Math.Clamp(_data.Duration, 0, MaxDurationSec);
            var size = Math.Clamp(_data.Size, 0, MaxSizePct);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    App.Flash?.TriggerFlashOnce(amount, duration, size);
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "FlashImageCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
