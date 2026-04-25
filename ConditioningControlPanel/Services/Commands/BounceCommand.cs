using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class BounceCommand : ICommand
    {
        private readonly Bounce _data;
        public BounceCommand(Bounce data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_data.On)
                        App.BouncingText?.Start(true, _data.Words);
                    else
                        App.BouncingText?.Stop();
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BounceCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
