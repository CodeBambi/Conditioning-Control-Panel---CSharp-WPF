using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class SubliminalCommand : ICommand
    {
        public const int MaxOpacity = 60;
        public const int MaxTextChars = 80;

        private readonly Subliminal _data;
        public SubliminalCommand(Subliminal data) { _data = data; }

        /// <summary>Head surface: (text, opacity) on the UI thread (WPF
        /// App.Subliminal.FlashSubliminalCustom). False when nothing could show. Unseeded: refused.</summary>
        public static volatile Func<string, int, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            var opacity = Math.Clamp(_data.Opacity, 0, MaxOpacity);
            var text = (_data.Text ?? string.Empty).Trim();
            if (text.Length > MaxTextChars) text = text.Substring(0, MaxTextChars);
            if (string.IsNullOrEmpty(text)) return Task.FromResult(false);

            try
            {
                return Task.FromResult(Surface?.Invoke(text, opacity) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SubliminalCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
