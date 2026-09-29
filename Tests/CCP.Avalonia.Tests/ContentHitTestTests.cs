using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Keincheck's hit_test put points in the tab content on the window template's Panel. This
/// proves real pointer input in the content area at the default size reaches the control drawn
/// there: every visible, enabled, unclipped button in four tabs is the hit-test result at its own
/// centre and receives a headless press there. The press is swallowed at the button's tunnel so no
/// click side effect runs.</summary>
public sealed class ContentHitTestTests
{
    [Fact]
    public async Task ContentButtonsReceiveThePointerAtTheirCentre()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var misses = new List<string>();
            foreach (var (tab, panelName) in new[] { ("studio", "StudioTab"), ("presets", "PresetsTab"), ("awareness", "AwarenessTab"), ("quests", "QuestsTab") })
            {
                w.ShowTab(tab);
                // The logged-out cover is WPF behaviour (MainWindow.Login.cs:378); test the tab under it.
                if (tab == "quests") w.Named<Control>(panelName)!.FindControl<Border>("QuestsLoginOverlay")!.IsVisible = false;
                // Same for the free-tier veil (WPF MainWindow.Patreon.cs:63 RefreshPremiumGate).
                if (tab == "awareness") w.Named<Control>(panelName)!.FindControl<Border>("AwarenessGate")!.IsVisible = false;
                Dispatcher.UIThread.RunJobs();
                // Hit testing reads the compositor's last frame: a tab shown since then is not in it yet.
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                int tested = 0;
                foreach (var b in w.Named<Control>(panelName)!.GetVisualDescendants().OfType<Button>().ToList())
                {
                    if (!b.IsEffectivelyVisible || !b.IsEffectivelyEnabled || b.Bounds.Width < 4 || b.TemplatedParent is ScrollBar) continue;
                    var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
                    if (!new Rect(w.Bounds.Size).Contains(p) || b.GetVisualAncestors().OfType<ScrollViewer>().Any(s => !Inside(s, p, w))) continue;
                    if (w.GetVisualsAt(p).OfType<Button>().FirstOrDefault(o => o != b && !b.IsVisualAncestorOf(o)) is { } over && !over.IsVisualAncestorOf(b)) continue;   // another control drawn on top, e.g. the Start footer
                    tested++;
                    var hit = w.InputHitTest(p) as Visual;
                    bool pressed = false;
                    void Swallow(object? s, PointerPressedEventArgs e) { pressed = true; e.Handled = true; }
                    b.AddHandler(InputElement.PointerPressedEvent, Swallow, RoutingStrategies.Tunnel);
                    w.MouseDown(p, MouseButton.Left);
                    w.MouseUp(p, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                    b.RemoveHandler(InputElement.PointerPressedEvent, (System.EventHandler<PointerPressedEventArgs>)Swallow);
                    if (hit is null || (hit != b && !b.IsVisualAncestorOf(hit)) || !pressed)
                        misses.Add($"{tab} {b.Name ?? b.GetType().Name} @{p}: hit {hit?.GetType().Name}/{(hit as Control)?.Name}, pressed {pressed}");
                }
                Assert.True(tested >= 3, $"{tab}: only {tested} buttons on screen to test");
            }
            w.Close();
            Assert.True(misses.Count == 0, string.Join("\n", misses));
            return Task.CompletedTask;
        });
    }

    private static bool Inside(Visual v, Point p, Visual root)
        => v.TranslatePoint(default, root) is { } tl && v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), root) is { } br
           && new Rect(tl, br).Contains(p);
}
