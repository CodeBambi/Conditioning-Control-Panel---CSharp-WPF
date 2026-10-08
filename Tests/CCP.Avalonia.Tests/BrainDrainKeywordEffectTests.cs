using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>main-sync 0fc2d4faa (ccp-bugs #1214): Brain Drain is a keyword visual effect in the
/// Awareness preset editor, with its own 🫠 chip like WPF AwarenessPresetDetailDialog.</summary>
public sealed class BrainDrainKeywordEffectTests
{
    [Fact]
    public void BrainDrainEffectHasItsOwnChip()
    {
        var trigger = new KeywordTrigger();
        trigger.Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
        Assert.Equal("🫠", AwarenessPresetDetailDialog.BuildActionChips(trigger).Trim());
    }
}
