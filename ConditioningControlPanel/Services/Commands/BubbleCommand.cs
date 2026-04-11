using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class BubbleCommand(Bubbles commandData) : ICommand
{
    public bool Execute()
    {
        var frequency = Math.Clamp(commandData.Frequency, 0, 15);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (commandData.On)
                App.Bubbles.Start(true, frequency);
            else
                App.Bubbles.Stop();
        });
        return true;
    }
}