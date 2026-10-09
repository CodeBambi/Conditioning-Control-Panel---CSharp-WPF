using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The daily Studio/Presets paths (WPF MainWindow.Presets.cs): preset New / Save-over /
/// Load / Delete write Core settings, Load is refused mid-session, and a wall or rack toggle
/// flips the flag and lights the tile's ring.</summary>
public sealed class StudioPresetsWireTests
{
    [Fact]
    public Task PresetCrudRoundTripsThroughCoreSettings() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var created = tab.SaveNewPreset("Wire Preset")!;
        Assert.Contains(s.UserPresets, p => p.Id == created.Id);
        Assert.Null(tab.SaveNewPreset("wire preset"));                // name taken
        Assert.True(tab.BtnLoadPreset.IsEnabled && tab.BtnSaveOverPreset.IsEnabled && tab.BtnDeletePreset.IsEnabled);

        var freq = s.FlashFrequency;
        s.FlashFrequency = freq + 7;
        var updated = tab.SaveOverPreset(created)!;
        Assert.Equal((created.Id, freq + 7), (updated.Id, s.UserPresets.Single(p => p.Id == created.Id).FlashFrequency));

        s.FlashFrequency = freq;
        CoreSession.IsSessionRunningProvider = () => true;
        Assert.False(tab.LoadPreset(updated));                       // refused mid-session
        Assert.Equal(freq, s.FlashFrequency);
        CoreSession.IsSessionRunningProvider = null;
        Assert.True(tab.LoadPreset(updated));
        Assert.Equal(freq + 7, s.FlashFrequency);

        tab.DeletePreset(updated);
        Assert.DoesNotContain(s.UserPresets, p => p.Id == created.Id);
        Assert.DoesNotContain(tab.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>(),
            b => (b.Tag as string) == created.Id);
    });

    [Fact]
    public Task WallAndRackTogglesFlipTheFlagAndLightTheRing() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var dash = shell.SettingsPage!;
        s.FlashEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(dash.CardFlash.IsActive);

        dash.CardFlash.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(FeatureCard.ToggleRequestedEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(s.FlashEnabled);
        Assert.True(dash.CardFlash.IsActive);

        // The breath parks while the tab is hidden and resumes when it is shown (better than WPF).
        // The cards breathe on a BreathClock now (AVALONIA EFFECT/CACHE RULE); IsBreathing is the seam.
        object? Breath(Control c) => (bool)c.GetType().GetProperty("IsBreathing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(c)! ? c : null;
        Assert.NotNull(Breath(dash.CardFlash));
        dash.IsVisible = false;
        Assert.Null(Breath(dash.CardFlash));
        dash.IsVisible = true;
        Assert.NotNull(Breath(dash.CardFlash));

        s.MindWipeEnabled = true;                                    // any writer, e.g. a preset load
        Dispatcher.UIThread.RunJobs();
        Assert.True(dash.ComboMindDrain.IsActiveA);
        dash.IsVisible = false;
        Assert.Null(Breath(dash.ComboMindDrain));
        dash.IsVisible = true;
        Assert.NotNull(Breath(dash.ComboMindDrain));

        var rack = shell.StudioRack!;
        typeof(StudioTabView).GetMethod("QuickToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(rack, new object[] { "flash" });
        Dispatcher.UIThread.RunJobs();
        Assert.False(s.FlashEnabled);
        Assert.False(dash.CardFlash.IsActive);
    });

    private static Task Run(System.Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var snapshot = Preset.FromSettings(s, "snapshot");
        var users = s.UserPresets.ToList();
        var presetName = s.CurrentPresetName;
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell); }
        finally
        {
            shell.Close();
            CoreSession.IsSessionRunningProvider = null;
            snapshot.ApplyTo(s);
            s.UserPresets.Clear();
            s.UserPresets.AddRange(users);
            s.CurrentPresetName = presetName;
        }
        return Task.CompletedTask;
    });
}
