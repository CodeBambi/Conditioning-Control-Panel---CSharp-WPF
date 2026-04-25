using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class BubbleCommand : ICommand
    {
        // Tightened from PR's 0..15 to 0..10 spawns/min.
        public const int MaxFrequency = 10;

        private readonly Bubbles _data;
        public BubbleCommand(Bubbles data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            var frequency = Math.Clamp(_data.Frequency, 0, MaxFrequency);
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_data.On)
                        App.Bubbles?.Start(true, frequency);
                    else
                        App.Bubbles?.Stop();
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BubbleCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
