using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class MantraLockScreenCommand : ICommand
    {
        public const int MaxRepeats = 5;
        public const int MaxMantraChars = 200;

        private readonly MantraLockscreen _data;
        public MantraLockScreenCommand(MantraLockscreen data) { _data = data; }

        /// <summary>Head surface: (phrase, repeats) as a STRICT card on the UI thread (WPF
        /// App.LockCard.ShowLockCard customStrict). False when nothing could show. Unseeded: refused.</summary>
        public static volatile Func<string, int, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            var amount = Math.Clamp(_data.Amount, 0, MaxRepeats);
            var phrase = (_data.Mantra ?? string.Empty).Trim();
            if (phrase.Length > MaxMantraChars) phrase = phrase.Substring(0, MaxMantraChars);
            if (string.IsNullOrEmpty(phrase)) return Task.FromResult(false);

            try
            {
                return Task.FromResult(Surface?.Invoke(phrase, amount) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MantraLockScreenCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
