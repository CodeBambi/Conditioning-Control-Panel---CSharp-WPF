using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Quest cards wear their art (WPF MainWindow.QuestsTab.cs GetQuestArt): every bundled
/// daily and weekly quest's pack:// picture resolves to a shipped avares copy on this head.</summary>
public sealed class QuestArtTests
{
    [Fact]
    public void EveryBundledQuest_ResolvesItsArt()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var bundled = QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests)
                .Where(q => q.ImagePath.StartsWith("pack://"))
                .ToList();
            Assert.NotEmpty(bundled);
            var missing = bundled.Where(q => QuestsTabView.GetQuestArt(q) == null).Select(q => q.Id).ToList();
            Assert.Empty(missing);
        });
    }
}
