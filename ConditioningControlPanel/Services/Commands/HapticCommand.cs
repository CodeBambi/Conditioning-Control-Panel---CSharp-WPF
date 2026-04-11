using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class HapticCommand(HapticCommandData commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var duration = Math.Clamp(commandData.Duration, 0, 10);
        var intensity = Math.Clamp(commandData.Intensity, 0, 1);

        _ = App.Haptics.ApplyVibrationModeAsync(intensity, duration * 2, VibrationMode.Pulse);
        return await Task.FromResult(true);
    }
}