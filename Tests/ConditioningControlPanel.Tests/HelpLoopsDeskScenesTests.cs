using System.Linq;
using ConditioningControlPanel.Controls.HelpLoops;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Wave 2 desk scenes (Remote Control, Screen Text Detection, Presets, Session Editor) each have a
/// loop. The step and draw checks run for every registered scene in <see cref="HelpLoopsTests"/>.
/// </summary>
public sealed class HelpLoopsDeskScenesTests
{
    public static readonly string[] DeskIds = { "RemoteControl", "ScreenOcr", "Presets", "SessionEditor" };

    [Fact]
    public void Registry_HasALoopForEveryDeskScene()
    {
        var missing = DeskIds.Where(id => !HelpLoopRegistry.Has(id)).ToList();
        Assert.True(missing.Count == 0, "no help loop for: " + string.Join(", ", missing));
    }
}
