using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ConversationPersonalityTests
{
    [Fact]
    public void EmiIdentityOverridesSavedPresetsOnlyForTheEmiAvatar()
    {
        var neutral = PersonalityPresets.GetNeutralDefault();
        Assert.Same(neutral, EmiPersonality.ForPreview(neutral, "circe", true));
        Assert.Same(neutral, EmiPersonality.ForPreview(neutral, null, false));
        Assert.Same(neutral, EmiPersonality.ForPreview(neutral, BuiltInMods.CCPDefaultId, true, 1));
        var gentle = PersonalityPresets.GetGentleTrainer();
        Assert.Equal(EmiPersonality.Id, EmiPersonality.ForPreview(gentle, null, true).Id);
        Assert.Same(gentle, EmiPersonality.ForPreview(gentle, null, true, 0, 1));
        var custom = new PersonalityPreset { Id = "custom", Name = "custom" };
        Assert.Equal(EmiPersonality.Id, EmiPersonality.ForPreview(custom, null, true).Id);
        Assert.Same(custom, EmiPersonality.ForPreview(custom, "circe", true));
        Assert.Equal("EMI", EmiPersonality.ForPreview(neutral, BuiltInMods.CCPDefaultId, true).Name);
    }

    [Fact]
    public void FixedVoiceAdoptionAndAvatarSwitchFenceOldRepliesOnlyOnce()
    {
        var settings = new AppSettings { ActivePersonalityPresetId = "keep-my-preset" };
        Assert.True(EmiPersonality.FenceOldVoice(settings, true, true));
        var first = settings.PersonaVoiceFenceUtc;
        Assert.NotNull(first);
        Assert.False(EmiPersonality.FenceOldVoice(settings, true, true));
        Assert.Equal(first, settings.PersonaVoiceFenceUtc);
        Assert.True(EmiPersonality.FenceOldVoice(settings, false, true));
        Assert.True(EmiPersonality.FenceOldVoice(settings, true, false));
        Assert.Equal("keep-my-preset", settings.ActivePersonalityPresetId);
    }

    [Fact]
    public void DraftIsIndependentAndContainsNoProviderSecretOrPermission()
    {
        var source = EmiPersonality.Create();
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

    [Fact]
    public void PromptKeepsSafetyAndBoundsUntrustedCharacterText()
    {
        var persona = new CompanionPromptSettings { Personality = new string('x', 90000), KnowledgeBase = new string('y', 90000) };
        var prompt = ConversationPrompt.Build(persona, "test", false, false, Enumerable.Range(0, 500).Select(i => "title" + i));
        Assert.StartsWith(SafetyComposer.Preamble, prompt);
        Assert.EndsWith(SafetyComposer.Floor, prompt);
        Assert.True(prompt.Length < 10000);
        Assert.DoesNotContain("title99;", prompt);
        Assert.Contains("No effect controls", prompt);
    }

    [Fact]
    public void CustomStyleAndCharacterArePreservedWithoutAmbientScripts()
    {
        var persona = new CompanionPromptSettings { Personality = "gentle lunar guide", OutputRules = "speak gently", ContextReactions = "ambient-marker" };
        var prompt = ConversationPrompt.Build(persona, "Luna", false, false);
        Assert.Contains("gentle lunar guide", prompt);
        Assert.Contains("speak gently", prompt);
        Assert.DoesNotContain("ambient-marker", prompt);
        Assert.Contains("Answer the user's actual message first", prompt);
    }
}