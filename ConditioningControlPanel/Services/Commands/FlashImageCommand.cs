using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class FlashImageCommand(FlashImage commandData) : ICommand
{

    public bool Execute()
    {
        var amount = Math.Clamp(commandData.Amount, 0, 20);
        var duration = Math.Clamp(commandData.Duration, 0, 30);
        var size = Math.Clamp(commandData.Size, 0, 200);

        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                App.Flash.TriggerFlashOnce(amount, duration, size);
            });
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }
}