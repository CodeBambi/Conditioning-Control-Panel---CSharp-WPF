using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class SpiralCommand : ICommand
    {
        public const int MaxIntensity = 30;

        private readonly SpiralPinkFiler _data;
        public SpiralCommand(SpiralPinkFiler data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            var intensity = Math.Clamp(_data.Intensity, 0, MaxIntensity);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var settings = App.Settings?.Current;
                    if (settings == null || App.Overlay == null) return;

                    settings.SpiralOpacity = intensity;
                    settings.SpiralEnabled = _data.On;

                    if (!App.Overlay.IsRunning)
                    {
                        App.Overlay.BypassLevelCheck = true;
                        App.Overlay.Start();
                    }
                    else if (!App.Overlay.BypassLevelCheck)
                    {
                        App.Overlay.BypassLevelCheck = true;
                    }

                    App.Overlay.RefreshOverlays();
                    App.Settings?.Save();
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SpiralCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
