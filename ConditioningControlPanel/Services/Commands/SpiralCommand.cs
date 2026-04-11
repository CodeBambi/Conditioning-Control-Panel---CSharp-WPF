using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class SpiralCommand(SpiralPinkFiler commandData) : ICommand
{
    public bool Execute()
    {
        var intensity = Math.Clamp(commandData.Intensity, 0, 50);

        System.Windows.Application.Current.Dispatcher.Invoke((Action)(() =>
        {
            App.Settings.Current.SpiralOpacity = intensity;
            App.Settings.Current.SpiralEnabled = commandData.On;
        }));
        return true;
    }
}