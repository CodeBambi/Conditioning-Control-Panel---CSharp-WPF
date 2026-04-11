using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class MantraLockScreenCommand(MantraLockscreen commandData) : ICommand
{
    public bool Execute()
    {
        var amount = Math.Clamp(commandData.Amount, 0, 10);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            App.LockCard.ShowLockCard(commandData.Mantra, amount, true);
        });
        return true;
    }
}