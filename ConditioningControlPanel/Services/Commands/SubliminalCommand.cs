using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class SubliminalCommand(Subliminal commandData) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var opacity = Math.Clamp(commandData.Opacity, 0, 100);

        Application.Current.Dispatcher.Invoke(() =>
        {
            App.Subliminal.FlashSubliminalCustom(commandData.Text, opacity);
        });
        return await Task.FromResult(true);
    }
}