using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class SpiralCommand(SpiralPinkFiler commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var intensity = Math.Clamp(commandData.Intensity, 0, 50);

        Application.Current.Dispatcher.Invoke((Action)(() =>
        {
            App.Settings.Current.SpiralOpacity = intensity;
            App.Settings.Current.SpiralEnabled = commandData.On;

            // Ensure overlay is running and bypass level check
            if (!App.Overlay.IsRunning)
            {
                App.Overlay.BypassLevelCheck = true;
                App.Overlay.Start();
            }
            else if (!App.Overlay.BypassLevelCheck)
            {
                App.Overlay.BypassLevelCheck = true;
            }

            App.Overlay.RefreshOverlays();

            App.Settings.Save();
        }));
        return await Task.FromResult(true);
    }
}