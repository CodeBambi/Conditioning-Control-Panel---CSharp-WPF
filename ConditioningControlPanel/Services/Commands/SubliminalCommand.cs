using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class SubliminalCommand : ICommand
    {
        public const int MaxOpacity = 60;
        public const int MaxTextChars = 80;

        private readonly Subliminal _data;
        public SubliminalCommand(Subliminal data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            var opacity = Math.Clamp(_data.Opacity, 0, MaxOpacity);
            var text = (_data.Text ?? string.Empty).Trim();
            if (text.Length > MaxTextChars) text = text.Substring(0, MaxTextChars);
            if (string.IsNullOrEmpty(text)) return Task.FromResult(false);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    App.Subliminal?.FlashSubliminalCustom(text, opacity);
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SubliminalCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
