using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class SubliminalCommand(Subliminal commandData) : ICommand
{
    public bool Execute()
    {
        var opacity = Math.Clamp(commandData.Opacity, 0, 100);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            App.Subliminal.FlashSubliminalCustom(commandData.Text, opacity);
        });
        return true;
    }
}