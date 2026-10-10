using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.KeywordTriggers;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>platform#1 (wave B7): the custom keyword-trigger list is live - Add makes a WPF-default row,
/// preset clones stay off the list, a row edit lands in settings with its actions rebuilt.</summary>
public sealed class KeywordTriggerListTests
{
    [Fact]
    public Task AddListsCustomRowsOnlyAndEditsPersist() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var window = new Window();
        try
        {
            var s = CoreSettings.Current;
            s.KeywordTriggers.Clear();
            var preset = KeywordTriggerEngine.NewCustomTrigger("boy");
            preset.Id = "preset:puppy:boy";
            s.KeywordTriggers.Add(preset);

            var panel = new KeywordTriggersPanel();
            window.Content = panel;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = panel.FindControl<StackPanel>("KeywordTriggerListPanel")!;
            Assert.Empty(list.Children);

            panel.AddTrigger();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(list.Children);
            var added = s.KeywordTriggers.Last();
            Assert.Equal(30, added.CooldownSeconds);
            Assert.Equal("SubliminalFlash", KeywordVisualEffectName(added));

            var combo = list.Children[0].GetLogicalDescendantsOfType<ComboBox>().Single();
            combo.SelectedIndex = 4;   // Image Flash
            Assert.Equal(ConditioningControlPanel.Models.KeywordVisualEffect.ImageFlash, added.VisualEffect);
            Assert.Contains(added.Actions, a => a is ConditioningControlPanel.Models.VisualEffectAction v
                && v.Effect == ConditioningControlPanel.Models.KeywordVisualEffect.ImageFlash);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    private static string KeywordVisualEffectName(ConditioningControlPanel.Models.KeywordTrigger t) => t.VisualEffect.ToString();
}

internal static class LogicalExt
{
    internal static System.Collections.Generic.IEnumerable<T> GetLogicalDescendantsOfType<T>(this global::Avalonia.LogicalTree.ILogical root)
        => global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
