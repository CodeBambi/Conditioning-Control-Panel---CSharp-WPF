using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class BubbleCommand(Bubbles commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var frequency = Math.Clamp(commandData.Frequency, 0, 15);

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (commandData.On)
                App.Bubbles.Start(true, frequency);
            else
                App.Bubbles.Stop();
        });
        return await Task.FromResult(true);
    }
}