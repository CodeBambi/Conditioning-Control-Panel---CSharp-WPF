using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class HapticCommand : ICommand
    {
        public const int MaxDurationSec = 10;

        private readonly HapticCommandData _data;
        public HapticCommand(HapticCommandData data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            var duration = Math.Clamp(_data.Duration, 0, MaxDurationSec);
            // User-set ceiling on AI haptic intensity.
            var maxIntensity = App.Settings?.Current?.CompanionPrompt?.MaxAiHapticIntensity ?? 0.6;
            var intensity = Math.Clamp(_data.Intensity, 0, maxIntensity);

            try
            {
                _ = App.Haptics?.ApplyVibrationModeAsync(intensity, duration * 1000, VibrationMode.Pulse);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "HapticCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
