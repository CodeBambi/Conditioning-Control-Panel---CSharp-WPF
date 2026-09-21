using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Two things Kathryn could not find out from the UI on 2026-09-14: what the "Customize AI
/// Companion" boxes were showing her (stock Bambi sprite text, under a Circe install), and what
/// any of the preset chips actually change (the tooltip repeated the chip's own label).
/// </summary>
public class CompanionPromptEditorSourceTests
{
    // ---------- the editor's starting text ----------

    [Fact]
    public void TheEditorStartsFromTheRunningPersona()
    {
        var stock = new CompanionPromptSettings { Personality = "bambi sprite text" };
        var circe = new CompanionPromptSettings { Personality = "circe text" };

        Assert.Equal("circe text", ActivePersonaPromptText.Resolve(circe, stock).Personality);
    }

    [Fact]
    public void FieldsThePersonaLeavesBlankKeepTheStockText()
    {
        // A mod persona usually sets Personality and nothing else; an empty knowledge-base box
        // would be a worse start than the stock paragraph it replaced.
        var stock = new CompanionPromptSettings
        {
            Personality = "bambi sprite text",
            KnowledgeBase = "stock knowledge",
            OutputRules = "stock rules",
        };
        var persona = new CompanionPromptSettings { Personality = "circe text", KnowledgeBase = "   " };

        var resolved = ActivePersonaPromptText.Resolve(persona, stock);

        Assert.Equal("circe text", resolved.Personality);
        Assert.Equal("stock knowledge", resolved.KnowledgeBase);
        Assert.Equal("stock rules", resolved.OutputRules);
    }

    [Fact]
    public void NoPersonaAtAllIsTheStockPrompt()
    {
        var stock = CompanionPromptSettings.GetDefaults();
        Assert.Same(stock, ActivePersonaPromptText.Resolve(null, stock));
    }

    [Fact]
    public void ResolvingDoesNotMutateEitherInput()
    {
        var stock = new CompanionPromptSettings { Personality = "stock" };
        var persona = new CompanionPromptSettings { Personality = "persona" };

        ActivePersonaPromptText.Resolve(persona, stock);

        Assert.Equal("stock", stock.Personality);
        Assert.Equal("persona", persona.Personality);
    }

    // ---------- the chip tooltip ----------

    [Fact]
    public void APresetChipCarriesItsOwnDescription()
    {
        var chip = new CompanionPresetChip("circe", "Circe", description: "  Silk and instruction.  ");
        Assert.Equal("Silk and instruction.", chip.Description);
    }

    [Fact]
    public void AChipWithNothingToSayShowsNoTooltip()
    {
        // Null, not empty: WPF draws an empty box for "".
        Assert.Null(new CompanionPresetChip("x", "X").Description);
        Assert.Null(new CompanionPresetChip("x", "X", description: "   ").Description);
    }

    [Fact]
    public void EveryBuiltInPresetHasSomethingToSay()
    {
        foreach (var preset in PersonalityPresets.GetAllBuiltIn())
            Assert.False(string.IsNullOrWhiteSpace(preset.Description), preset.Id);
    }
}
