using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class MantraLockScreenCommand(MantraLockscreen commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var amount = Math.Clamp(commandData.Amount, 0, 10);

        Application.Current.Dispatcher.Invoke(() =>
        {
            App.LockCard.ShowLockCard(commandData.Mantra, amount, true);
        });
        return await Task.FromResult(true);
    }
}