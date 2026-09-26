using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ConversationPersonalityTests
{
    [Fact]
    public void DraftIsIndependentAndContainsNoProviderSecretOrPermission()
    {
        var source = new PersonalityPreset
        {
            Id = "source",
            Name = "Source",
            IsBuiltIn = true,
            SampleLines = new List<string> { "original line" },
            PromptSettings = new CompanionPromptSettings { Personality = "original" }
        };
        source.RequiresExplicitAcknowledgement = true;
        source.PromptSettings!.OpenAiCompatibleApiKey = "test-secret";
        source.PromptSettings.AllowAiToControlEffects = true;
        var draft = PersonalityStudio.Draft(source);
        draft.PromptSettings!.Personality = "changed";
        draft.SampleLines![0] = "changed";
        Assert.NotEqual(draft.Id, source.Id);
        Assert.False(draft.IsBuiltIn);
        Assert.True(draft.RequiresExplicitAcknowledgement);
        Assert.Empty(draft.PromptSettings.OpenAiCompatibleApiKey);
        Assert.False(draft.PromptSettings.AllowAiToControlEffects);
        Assert.NotEqual("changed", source.PromptSettings.Personality);
        Assert.NotEqual("changed", source.SampleLines![0]);
    }
}
