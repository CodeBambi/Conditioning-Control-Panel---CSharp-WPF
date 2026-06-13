using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class PinkCommand : ICommand
    {
        public const int MaxIntensity = 30;

        private readonly SpiralPinkFiler _data;
        public PinkCommand(SpiralPinkFiler data) { _data = data; }

        public Task<CommandResult> ExecuteAsync()
        {
            var activeIntensity = App.Settings?.Current?.PinkFilterOpacity ?? 0;

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var settings = App.Settings?.Current;
                    if (settings == null || App.Overlay == null) return;

                    // The main-panel opacity slider is authoritative; the AI only decides on/off.
                    settings.PinkFilterEnabled = _data.On;

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
                return Task.FromResult(new CommandResult(
                    "pink",
                    CommandResultStatus.Executed,
                    ParameterSummary: _data.On ? $"on, intensity={activeIntensity}%" : "off"));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PinkCommand failed");
                return Task.FromResult(new CommandResult(
                    "pink",
                    CommandResultStatus.Failed,
                    Reason: ex.Message));
            }
        }
    }
}
