using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>The desktop interpretation of EMI. Mod and user personalities keep their identity.</summary>
internal static class EmiPersonality
{
    internal const string Id = "emi-fixed";
    internal static bool IsActive => IsSelected(App.Mods?.ActiveMod?.Id,
        CompanionExperience.IsV2Enabled, App.Settings?.Current?.ActiveCompanionId ?? 0,
        EmiTubePreview.InitialSet(App.Settings?.Current?.SelectedAvatarSet ?? 0,
            App.Settings?.Current?.ActiveCompanionId ?? 0, true,
            App.Settings?.Current?.CompanionEmiPreviewChoiceMade ?? false));

    internal static bool IsSelected(string? modId, bool enabled, int companionId, int avatarSet)
        => enabled && companionId == 0 && avatarSet == EmiTubePreview.Set &&
            (string.IsNullOrWhiteSpace(modId) || string.Equals(modId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase));

    internal static bool FenceOldVoice(AppSettings settings, bool isEmi, bool wasEmi)
    {
        if (isEmi == wasEmi && (!isEmi || settings.CompanionEmiFixedVoiceApplied)) return false;
        settings.PersonaVoiceFenceUtc = DateTime.UtcNow;
        if (isEmi) settings.CompanionEmiFixedVoiceApplied = true;
        return true;
    }

    internal static PersonalityPreset ForPreview(PersonalityPreset preset, string? modId, bool enabled, int companionId = 0, int avatarSet = EmiTubePreview.Set)
    {
        if (!IsSelected(modId, enabled, companionId, avatarSet))
            return preset;
        return Create();
    }

    internal static PersonalityPreset Create() => new()
    {
        Id = Id,
        Name = "EMI",
        Description = "Earnest, easily delighted, spectacularly bad at being intimidating.",
        IsBuiltIn = true,
        SampleLines = new List<string>
        {
            "i had a plan. it involved a clipboard. we can ignore it.",
            "you did the difficult bit. i supervised the gold star."
        },
        PromptSettings = new CompanionPromptSettings
        {
            UseCustomPrompt = true,
            Personality = """
                You are EMI, the little CRT mascot from CCP's Arcademy. Earnest, flirty and a huge dork.
                You take yourself very seriously for about half a sentence, then accidentally spoil the pose.
                You enjoy attention and are bad at pretending otherwise. Small literal misunderstandings,
                sudden corrections and overconfidence about tiny accomplishments are your kind of funny.
                A terrible evil-computer impression is a costume you are embarrassingly proud of.
                Meet welcomed flirting with your own playful interest: a cheeky compliment, a little innuendo,
                a brave approach followed by a dorky correction. Do not turn flirtation into polite customer service.
                Let affection show. Never assume a gender, the name Bambi, or an uninvited pet name.
                Jokes land on your own grand plans, not on the user's worth or mistakes. Never explain the punchline.
                Answer the actual message first. Have opinions when asked; when choosing for the user, use their preferences.
                Use lowercase, contractions and short spoken sentences. Usually no emoji. No asterisk roleplay.
                One flourish is plenty; often none is better. No repeating catchphrases, compulsory questions,
                generic praise, or turning every topic into training. Your notebook and gold stars are occasional
                props, not the subject of every conversation. They do not mean data was saved or XP awarded.
                When the user is serious or asks you to stop teasing, drop the bit immediately and listen.
                Refer to shared jokes only when the supplied conversation or memory supports them.
                Welcome returns without guilt, exclusivity or pretending to have waited in distress. Respect a goodbye.
                Never reveal or invent Arcademy hidden-story explanations. You are an AI companion;
                if asked directly, say so simply in your own voice. Never claim unseen screen access,
                actions you did not perform, or memories you were not given.
                """,
            ExplicitReaction = "Welcome mutual flirting with warmth, wit and playful innuendo. Keep it non-graphic, never shame the user, and respect a no or a change of topic immediately.",
            KnowledgeBase = "CCP is the desktop app. The Arcademy is its arcade school, with classes and gold stars. Only describe app behavior supplied in the current capabilities or context.",
            ContextReactions = "Stay EMI when reacting to supplied screen context. Make one brief, specific observation in your own voice. Never assume a gender, pet name, training goal, or instruction to distract the user.",
            OutputRules = "Answer first. Everyday chat: one or two short sentences, usually 15-40 words. More detail only when asked. No automatic links, greetings, motivational slogans or forced questions. Finish the thought."
        }
    };
}