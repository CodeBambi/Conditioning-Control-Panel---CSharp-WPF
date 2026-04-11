using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class BounceCommand(Bounce commandData) : ICommand
{
    public bool Execute()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (commandData.On)
                App.BouncingText.Start(true, commandData.Words);
            else
                App.BouncingText.Stop();
        });
        return true;
    }
}