using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Hidden dev surface (part 8): set <c>CCP_CHAOS_DEBUG=1</c> in the environment and the
/// Dollhouse grows a small strip along its bottom edge — fast-forward RunsCompleted, add
/// currencies, complete lessons, force reveals, reset the whole meta state. Everything
/// routes through the ChaosMeta / RevealService / ChaosLessons public APIs and persists
/// via ChaosMeta.Save (mirrors BarkService's CCP_BARK_DRYRUN env-flag pattern). The panel
/// is only ever BUILT when the flag is set — a normal launch carries zero extra UI.
/// </summary>
public partial class ChaosHubWindow
{
    /// <summary>Opt-in flag, read once per process.</summary>
    private static readonly bool DebugStripEnabled =
        string.Equals(Environment.GetEnvironmentVariable("CCP_CHAOS_DEBUG"), "1", StringComparison.Ordinal);

    private TextBox? _dbgRuns;
    private TextBox? _dbgLesson;
    private TextBox? _dbgReveal;

    /// <summary>Append the strip as an extra row under the footer (flag-gated).</summary>
    private void BuildDebugStrip()
    {
        if (!DebugStripEnabled) return;
        try
        {
            if (Content is not Border root || root.Child is not Grid grid) return;
            Height += 44;   // fixed-size window: make room for the extra row
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var panel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(DbgLabel("DEBUG"));

            _dbgRuns = DbgBox(ChaosMeta.State.RunsCompleted.ToString(), 42);
            panel.Children.Add(_dbgRuns);
            panel.Children.Add(DbgButton("set runs", Dbg_SetRuns));
            panel.Children.Add(DbgButton("+500 ✦", (_, _) => { ChaosMeta.State.Sparks += 500; ChaosMeta.Save(); DebugRefresh(); }));
            panel.Children.Add(DbgButton("+500 gold", (_, _) => { ChaosMeta.AddGold(500); DebugRefresh(); }));

            _dbgLesson = DbgBox("slow_fuses", 110);
            panel.Children.Add(_dbgLesson);
            panel.Children.Add(DbgButton("complete lesson", Dbg_CompleteLesson));

            _dbgReveal = DbgBox(RevealIds.TabLookingGlass, 110);
            panel.Children.Add(_dbgReveal);
            panel.Children.Add(DbgButton("force reveal", Dbg_ForceReveal));

            panel.Children.Add(DbgButton("reset meta", Dbg_ResetMeta));

            Grid.SetRow(panel, grid.RowDefinitions.Count - 1);
            grid.Children.Add(panel);
        }
        catch (Exception ex) { App.Logger?.Warning("Chaos debug strip build failed ({E})", ex.Message); }
    }

    private void Dbg_SetRuns(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_dbgRuns?.Text?.Trim(), out int n) || n < 0) return;
        ChaosMeta.State.RunsCompleted = n;
        ChaosMeta.Save();
        DebugRefresh();
    }

    private void Dbg_CompleteLesson(object sender, RoutedEventArgs e)
    {
        var id = _dbgLesson?.Text?.Trim();
        if (string.IsNullOrEmpty(id)) return;
        var def = ChaosLessons.ById(id!);
        if (def == null) return;
        if (def.HighWater) ChaosLessons.RaiseTo(id!, def.Target);
        else ChaosLessons.Tick(id!, def.Target);   // completion persists inside the engine
        DebugRefresh();
    }

    private void Dbg_ForceReveal(object sender, RoutedEventArgs e)
    {
        var id = _dbgReveal?.Text?.Trim();
        if (string.IsNullOrEmpty(id)) return;
        // Forced straight into the pending set: the next flash pass plays it like a real unlock.
        ChaosMeta.State.SeenReveals.Remove(id!);
        ChaosMeta.State.PendingReveals.Add(id!);
        ChaosMeta.Save();
        DebugRefresh();
    }

    private void Dbg_ResetMeta(object sender, RoutedEventArgs e)
    {
        ChaosMeta.DebugResetState();   // fresh state + save
        DebugRefresh();
    }

    /// <summary>After any debug mutation: let the rank/reveal gates re-evaluate, then rebuild
    /// every surface the way a real state change would.</summary>
    private void DebugRefresh()
    {
        try
        {
            RevealService.Sync("debug");
            ApplyExtremeGate();
            BuildHabits();
            BuildLifetimeBoons();
            BuildLoadoutTiles();
            BuildBench();
            BuildMantras();
            BuildDiary();
            RefreshTopBar();
            RefreshStats();
            ApplyReveals();
            RunRevealFlashes("debug");
            if (_dbgRuns != null) _dbgRuns.Text = ChaosMeta.State.RunsCompleted.ToString();
        }
        catch (Exception ex) { App.Logger?.Warning("Chaos debug refresh failed ({E})", ex.Message); }
    }

    // ---- tiny styled builders (dim, monospaced, out of the way) ----

    private static TextBlock DbgLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x43)),
        FontFamily = new FontFamily("Consolas, Courier New"), FontWeight = FontWeights.Bold, FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
    };

    private static TextBox DbgBox(string text, double width) => new()
    {
        Text = text, Width = width, FontSize = 11,
        Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1F, 0x40)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xE8, 0xB4, 0x43)),
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 2, 4, 2),
    };

    private Button DbgButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text, FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += onClick;
        return b;
    }
}
