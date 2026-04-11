using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class FlashImageCommand(FlashImage commandData) : ICommand
{

    public async Task<bool> ExecuteAsync()
    {
        var amount = Math.Clamp(commandData.Amount, 0, 20);
        var duration = Math.Clamp(commandData.Duration, 0, 30);
        var size = Math.Clamp(commandData.Size, 0, 200);

        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                App.Flash.TriggerFlashOnce(amount, duration, size);
            });
            return await Task.FromResult(true);
        }
        catch
        {
            return await Task.FromResult(false);
        }
    }
}