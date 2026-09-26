using System.Linq;
using ConditioningControlPanel.Controls.HelpLoops;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Wave 2 desk scenes (Remote Control, Screen Text Detection, Presets, Session Editor): each has a
/// loop, and each goes through the same step and draw checks as the dashboard loops.
/// </summary>
public sealed class HelpLoopsDeskScenesTests
{
    public static readonly string[] DeskIds = { "RemoteControl", "ScreenOcr", "Presets", "SessionEditor" };

    public static TheoryData<string> Ids()
    {
        var d = new TheoryData<string>();
        foreach (var id in DeskIds) d.Add(id);
        return d;
    }

    [Fact]
    public void Registry_HasALoopForEveryDeskScene()
    {
        var missing = DeskIds.Where(id => !HelpLoopRegistry.Has(id)).ToList();
        Assert.True(missing.Count == 0, "no help loop for: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Steps_AreOrderedAndInsideTheLoop(string id) =>
        new HelpLoopsTests().Steps_AreOrderedAndInsideTheLoop(id);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Scene_DrawsEvery50msOfItsLoop(string id) =>
        new HelpLoopsTests().Scene_DrawsEvery50msOfItsLoop(id);
}
