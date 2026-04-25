using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class MantraLockScreenCommand : ICommand
    {
        public const int MaxRepeats = 5;
        public const int MaxMantraChars = 200;

        private readonly MantraLockscreen _data;
        public MantraLockScreenCommand(MantraLockscreen data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            var amount = Math.Clamp(_data.Amount, 0, MaxRepeats);
            var phrase = (_data.Mantra ?? string.Empty).Trim();
            if (phrase.Length > MaxMantraChars) phrase = phrase.Substring(0, MaxMantraChars);
            if (string.IsNullOrEmpty(phrase)) return Task.FromResult(false);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    App.LockCard?.ShowLockCard(phrase, amount, customStrict: true);
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MantraLockScreenCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
