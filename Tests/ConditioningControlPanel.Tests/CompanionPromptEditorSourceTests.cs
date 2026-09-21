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

    // ---------- what Save is allowed to persist ----------

    [Fact]
    public void ABoxTheUserDidNotTouchPersistsAsBlank()
    {
        // Open, look, Save under a themed mod. Writing the box back verbatim would freeze that
        // mod's paragraph into the user's own settings, and the persona could never show through
        // again - not after a mod update, not on another preset, not back on CCP Default.
        Assert.Equal("", ActivePersonaPromptText.PersistedField("circe text", "circe text"));
    }

    [Fact]
    public void ABoxTheUserEditedIsKept()
    {
        Assert.Equal("circe text, but mine",
            ActivePersonaPromptText.PersistedField("circe text, but mine", "circe text"));
    }

    [Fact]
    public void ClearingABoxMeansFollowTheDefault()
    {
        // Same landing place as "untouched", which is the honest reading of an empty field and
        // the contract LoadCurrentSettings already had.
        Assert.Equal("", ActivePersonaPromptText.PersistedField("", "circe text"));
        Assert.Equal("", ActivePersonaPromptText.PersistedField("     ", "circe text"));
        Assert.Equal("", ActivePersonaPromptText.PersistedField(null, "circe text"));
    }

    [Fact]
    public void TheComparisonIsExact()
    {
        // Whitespace the user really typed is an edit; matching is not fuzzy.
        Assert.Equal("circe text ", ActivePersonaPromptText.PersistedField("circe text ", "circe text"));
        Assert.Equal("Circe Text", ActivePersonaPromptText.PersistedField("Circe Text", "circe text"));
    }

    [Fact]
    public void SeedThenSaveUntouchedThenReSeedFollowsTheModAgain()
    {
        // The whole point, end to end: seed a box from the persona, save without touching it,
        // and the next open must still follow the persona rather than a frozen copy of it.
        var stock = new CompanionPromptSettings { Personality = "stock" };
        var persona = new CompanionPromptSettings { Personality = "persona v1" };

        var shown = ActivePersonaPromptText.Resolve(persona, stock);
        var persisted = ActivePersonaPromptText.PersistedField(shown.Personality, shown.Personality);
        Assert.Equal("", persisted);

        // The mod ships a new version of its persona; the editor follows it.
        var personaV2 = new CompanionPromptSettings { Personality = "persona v2" };
        var shownAgain = ActivePersonaPromptText.Resolve(personaV2, stock);
        var box = string.IsNullOrWhiteSpace(persisted) ? shownAgain.Personality : persisted;
        Assert.Equal("persona v2", box);
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
