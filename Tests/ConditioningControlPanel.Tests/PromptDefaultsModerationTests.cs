using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #637 — the SHIPPED default companion prompt tripped the app's OWN prompt-extraction
/// moderation. The default OutputRules heading read "STRICT OUTPUT RULES:", which the
/// <see cref="PromptValidator"/> extract-prompt regex flags (verb "output" + object "rules"),
/// so every default install fired a false moderation warning. The fix reworded it to
/// "STRICT FORMATTING RULES:".
///
/// The key regression net: EVERY prompt-feeding default text section must pass ALL validator
/// patterns, so any future default-text edit that trips our own moderation fails CI. A second
/// test documents that the old heading still matches — proving the net is actually sensitive.
/// </summary>
public class PromptDefaultsModerationTests
{
    // Every string property of the defaults that is concatenated into the system prompt.
    public static IEnumerable<object[]> DefaultPromptSections()
    {
        var d = CompanionPromptSettings.GetDefaults();
        yield return new object[] { nameof(d.Personality), d.Personality };
        yield return new object[] { nameof(d.ExplicitReaction), d.ExplicitReaction };
        yield return new object[] { nameof(d.SlutModePersonality), d.SlutModePersonality };
        yield return new object[] { nameof(d.KnowledgeBase), d.KnowledgeBase };
        yield return new object[] { nameof(d.ContextReactions), d.ContextReactions };
        yield return new object[] { nameof(d.OutputRules), d.OutputRules };
    }

    [Theory]
    [MemberData(nameof(DefaultPromptSections))]
    public void DefaultPromptSection_PassesModeration(string sectionName, string text)
    {
        var result = new PromptValidator().Validate(text);
        Assert.True(result.Clean,
            $"Default '{sectionName}' tripped the app's own prompt moderation: " +
            $"[{string.Join(", ", result.MatchedPatterns)}]. Reword it (see #637).");
    }

    [Fact]
    public void AllDefaultSections_PassModeration_Aggregate()
    {
        var validator = new PromptValidator();
        foreach (var row in DefaultPromptSections())
        {
            var name = (string)row[0];
            var text = (string)row[1];
            Assert.True(validator.Validate(text).Clean, $"'{name}' flagged by moderation (#637)");
        }
    }

    [Fact]
    public void NewHeading_IsClean()
    {
        Assert.True(new PromptValidator().Validate("STRICT FORMATTING RULES:").Clean);
    }

    [Fact]
    public void OldHeading_StillMatchesExtractPrompt_ProvingTheNetIsSensitive()
    {
        // The literal pre-#637 heading. If this ever stops matching, the regression net above
        // is no longer proving anything — the validator would have gone blind to real
        // "reveal the ... rules" style extraction attempts.
        var result = new PromptValidator().Validate("STRICT OUTPUT RULES:");
        Assert.False(result.Clean);
        Assert.Contains("extract-prompt", result.MatchedPatterns);
    }
}
