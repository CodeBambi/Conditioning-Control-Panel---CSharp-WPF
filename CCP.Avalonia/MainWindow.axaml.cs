using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Deliberately thin. Every value shown is produced by CCP.Core; this class only renders.
    /// That is the whole point of the split - if this window had to compute anything, the logic
    /// would be living in the head again and the Windows and Linux heads would drift.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ModerationGuard _guard = new();

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);

            var runtime = this.FindControl<TextBlock>("TxtRuntime")!;
            var paths = this.FindControl<TextBlock>("TxtPaths")!;
            var engine = this.FindControl<TextBlock>("TxtEngine")!;
            var input = this.FindControl<TextBox>("TxtInput")!;
            var verdict = this.FindControl<TextBlock>("TxtVerdict")!;

            runtime.Text =
                $"OS         {RuntimeInformation.OSDescription.Trim()}\n" +
                $"Arch       {RuntimeInformation.OSArchitecture}  ({RuntimeInformation.RuntimeIdentifier})\n" +
                $".NET       {Environment.Version}\n" +
                $"Session    {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "n/a"}" +
                $"  desktop={Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "n/a"}";

            // CorePaths is the seam that made per-platform storage work without a second code
            // path: the same call lands in %LOCALAPPDATA% on Windows and ~/.local/share here.
            paths.Text =
                $"UserData         {CorePaths.UserData}\n" +
                $"EffectiveAssets  {CorePaths.EffectiveAssets}";

            // GoonRng mirrors Resources/web/goon/core/*.js, where draw order is protocol - if it
            // diverged per platform the C# and JS implementations would desync.
            var rng = new GoonRng(0x0123456789ABCDEFUL);
            var draws = new StringBuilder();
            for (var i = 0; i < 4; i++) draws.Append(rng.NextULong().ToString("X16", CultureInfo.InvariantCulture)).Append("  ");

            engine.Text =
                $"GoonRng(seed=0123456789ABCDEF)   {draws.ToString().TrimEnd()}\n" +
                $"BubbleSizing.Scale(100,150,null)  {BubbleSizing.Scale(100, 150, null)}\n" +
                $"ProgramHeat.Compute(3,30,0.5)     {ProgramHeat.Compute(3, 30, 0.5, false):0.0000}";

            input.TextChanged += (_, _) => Moderate(input.Text ?? string.Empty, verdict);
            Moderate(string.Empty, verdict);
        }

        /// <summary>Palette lookup with a literal fallback, so a missing key degrades to the
        /// right colour rather than to Avalonia's default and hides the gap.</summary>
        private global::Avalonia.Media.IBrush Brush(string key, string fallback) =>
            global::Avalonia.Application.Current!.TryFindResource(key, out var v) && v is global::Avalonia.Media.IBrush b
                ? b
                : global::Avalonia.Media.Brush.Parse(fallback);

        private void Moderate(string text, TextBlock target)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                target.Text = "awaiting input…";
                target.Foreground = Brush("TextMutedBrush", "#A0A0BC");
                return;
            }

            var result = _guard.CheckInput(text);
            if (result.Allow)
            {
                target.Text = "ALLOW";
                target.Foreground = Brush("SuccessGreenBrush", "#3EE87A");
            }
            else
            {
                target.Text = $"BLOCK   category={result.Category}   {result.Note}";
                target.Foreground = Brush("DangerBrush", "#E53935");
            }
        }
    }
}
