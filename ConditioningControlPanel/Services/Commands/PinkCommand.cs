using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class PinkCommand(SpiralPinkFiler commandData) : ICommand
{
    public bool Execute()
    {
        var intensity = Math.Clamp(commandData.Intensity, 0, 50);

        System.Windows.Application.Current.Dispatcher.Invoke((Action)(() =>
        {
            App.Settings.Current.PinkFilterOpacity = intensity;
            App.Settings.Current.PinkFilterEnabled = commandData.On;
        }));
        return true;
    }
}