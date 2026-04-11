using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class BounceCommand(Bounce commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (commandData.On)
                App.BouncingText.Start(true, commandData.Words);
            else
                App.BouncingText.Stop();
        });
        return await Task.FromResult(true);
    }
}