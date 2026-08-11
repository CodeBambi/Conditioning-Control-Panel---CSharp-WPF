using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 2026-08-07 "persona never changes what she says" root causes, pinned at the WIRE level —
/// the exact bytes <see cref="PromptAssembler.BuildRequest"/> emits for the owner's repro config
/// (built-in strict-domme preset, Slut Mode off, Bambi mod).
///
/// <para>Three layers of prompt-side fixes (mux, override-clear, persona-overrides-history line)
/// all "worked" and none changed the audible voice, because the contamination was never in the
/// prompt mux. Wire receipts (2026-08-07, [AI-METER] in_tok vs the server-reported token spend)
/// showed calls shipping ~2,900-3,400 real tokens while OpenRouter billed only ~1,545-1,690 prompt
/// tokens: the default "middle-out" context compression on the 4k-context cloud model silently
/// gutted the middle of every system prompt, and the surviving end-of-sequence content — the
/// restored old-voice chat history — kept teaching the model the old voice. These tests pin the
/// two client-side defenses:</para>
///
/// <list type="number">
///   <item>the persona-switch history fence (<see cref="PromptAssembler.FenceHistoryToPersona"/>):
///   assistant turns from before the switch never reach the wire;</item>
///   <item>the context-fit shed (<see cref="PromptAssembler.ShedHistoryToContextFit"/>): no
///   request is ever big enough for the provider-side compressor to fire.</item>
/// </list>
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class PersonaWireFidelityTests : IDisposable
{
    /// <summary>Lines the old slutmode voice would have left in a restored session.</summary>
    private static readonly string[] OldVoice =
    {
        "Mmm good girl~ Bambi's brain is so empty~ *giggles*",
        "Such a dumb slut~ Cock goes in brain goes out~ Watch Overload for me~",
        "Hehe~ drip drip drip~ Bambi needs to drop for cock~",
        "Bimbodoll~ blank and empty~ giggle for me Bambi~"
    };

    private readonly object? _priorSettings;
    private readonly object? _priorPersonality;
    private readonly object? _priorMods;
    private readonly AppSettings _settings = new();
    private readonly string _tempDir;

    public PersonaWireFidelityTests()
    {
        _priorSettings = GetStatic("Settings");
        _priorPersonality = GetStatic("Personality");
        _priorMods = GetStatic("Mods");

        // Same seam as PersonalityPromptMuxTests: an uninitialized SettingsService around an
        // in-memory AppSettings, saving to a throwaway path.
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        SetBackingField(service, service.GetType(), "Current", _settings);
        _tempDir = Path.Combine(Path.GetTempPath(), "ccp-persona-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        SetPrivate(service, "_settingsPath", Path.Combine(_tempDir, "settings.json"));
        SetPrivate(service, "_timerLock", new object());
        SetPrivate(service, "_saveLock", new object());
        SetStatic("Settings", service);
        SetStatic("Personality", new PersonalityService());

        // Bambi mod active — the owner's repro config (ActiveModId=builtin-bambisleep), which is
        // also the branch of GetCoreMediaLinks with the playlist/link examples.
        var mods = (ModService)RuntimeHelpers.GetUninitializedObject(typeof(ModService));
        SetPrivate(mods, "_activeMod", new ModPackage(BuiltInMods.BambiSleep, null, isBuiltIn: true));
        SetStatic("Mods", mods);

        BambiSprite.VideoPoolProvider = () => BuiltInMods.BambiSleep.Browser!.DefaultVideoLinks;
        BambiSprite.InvalidateStablePrompt();
    }

    public void Dispose()
    {
        BambiSprite.VideoPoolProvider = null;
        BambiSprite.InvalidateStablePrompt();
        SetStatic("Settings", _priorSettings);
        SetStatic("Personality", _priorPersonality);
        SetStatic("Mods", _priorMods);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ---------- the wire payload carries the persona ----------

    [Fact]
    public void StrictDommeWire_CarriesTheDommePersona_AndTheHistoryOverride()
    {
        _settings.SlutModeEnabled = false;
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.StrictDommeId));

        var request = BuildStrictDommeRequest(RestoredOldVoiceSession(turnPairs: 4));

        Assert.Contains("You are a strict, commanding domme trainer", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("CURRENT PERSONA OVERRIDES HISTORY", request.SystemPrompt, StringComparison.Ordinal);
        // And no slutmode persona bleed-through in the system message.
        Assert.DoesNotContain("SLUT MODE", request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("cock-obsessed", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- root cause 2: the old-voice history fence ----------

    [Fact]
    public void SwitchingToStrictDomme_TakesPreSwitchAssistantVoiceOffTheWire()
    {
        // A restored session full of the old voice, all stamped BEFORE the switch...
        var session = RestoredOldVoiceSession(turnPairs: 4);

        // ...then the user picks Strict Domme (this stamps PersonaVoiceFenceUtc)...
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.StrictDommeId));
        Assert.NotNull(_settings.PersonaVoiceFenceUtc);

        // ...and one exchange happens after the switch.
        session.Append(TurnKind.AssistantChat, "You will address me properly. Sit up.",
            utc: DateTime.UtcNow.AddSeconds(1));
        session.Append(TurnKind.UserChat, "yes ma'am", utc: DateTime.UtcNow.AddSeconds(2));

        var request = BuildStrictDommeRequest(session);
        var assistantLines = request.Messages
            .Where(m => m.Role == ChatMessage.RoleAssistant).Select(m => m.Content).ToList();

        // Every pre-switch assistant line — the bambi-voice contaminant few-shot — is gone.
        foreach (var line in OldVoice)
            Assert.DoesNotContain(assistantLines, l => l.Contains(line, StringComparison.Ordinal));
        Assert.DoesNotContain(request.Messages, m => m.Content.Contains("giggles", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(request.Messages, m => m.Content.Contains("good girl~", StringComparison.OrdinalIgnoreCase));

        // The post-switch reply and the user's own words are still there.
        Assert.Contains(assistantLines, l => l.Contains("You will address me properly", StringComparison.Ordinal));
        Assert.Contains(request.Messages, m => m.Role == ChatMessage.RoleUser
            && m.Content.Contains("what should i watch?", StringComparison.Ordinal));
    }

    [Fact]
    public void PersonaFence_KeepsUserTurnsAndBarkEchoes_DropsOnlyStaleAssistantVoice()
    {
        var fence = new DateTime(2026, 8, 7, 19, 0, 0, DateTimeKind.Utc);
        var assembler = Assembler(personaFence: () => fence);
        var before = fence.AddMinutes(-10);
        var after = fence.AddMinutes(10);

        var window = new[]
        {
            CompanionTurn.Create(TurnKind.UserChat, "hi", utc: before),
            CompanionTurn.Create(TurnKind.AssistantChat, "old voice reply", utc: before),
            CompanionTurn.Create(TurnKind.AmbientReply, "old voice quip", utc: before),
            CompanionTurn.Create(TurnKind.BarkEcho, "«She said aloud: \"scripted line\"»", utc: before),
            CompanionTurn.Create(TurnKind.AssistantChat, "new voice reply", utc: after),
        };

        var fenced = assembler.FenceHistoryToPersona(window);

        Assert.Equal(new[] { "hi", "«She said aloud: \"scripted line\"»", "new voice reply" },
            fenced.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void PersonaFence_WithNoFenceSet_LeavesTheWindowUntouched()
    {
        var assembler = Assembler(personaFence: () => null);
        var window = new[]
        {
            CompanionTurn.Create(TurnKind.AssistantChat, "any voice at all",
                utc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        Assert.Same(window, assembler.FenceHistoryToPersona(window));
    }

    // ---------- root cause 1: the context-fit shed ----------

    [Fact]
    public void ContextFitShed_DropsOldestHistoryFirst_NeverTheSystemOrNewestMessage()
    {
        var messages = new List<ChatMessage> { ChatMessage.System(new string('s', 9000)) };
        for (int i = 0; i < 10; i++)
        {
            messages.Add(ChatMessage.User($"old-{i} " + new string('u', 500)));
            messages.Add(ChatMessage.Assistant($"reply-{i} " + new string('a', 500)));
        }
        messages.Add(ChatMessage.User("NEWEST"));

        PromptAssembler.ShedHistoryToContextFit(messages);

        Assert.Equal(ChatMessage.RoleSystem, messages[0].Role);            // system never shed
        Assert.Equal("NEWEST", messages[^1].Content);                       // newest never shed
        Assert.True(messages.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content))
                    <= PromptAssembler.ContextFitTokenBudget);
        // Whatever survived is the newest slice, not the oldest.
        Assert.DoesNotContain(messages, m => m.Content.StartsWith("old-0", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextFitShed_LeavesAFittingRequestByteIdentical()
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.System("small prefix"),
            ChatMessage.User("hi"),
            ChatMessage.Assistant("hello"),
            ChatMessage.User("newest"),
        };
        var snapshot = messages.ToList();

        PromptAssembler.ShedHistoryToContextFit(messages);

        Assert.Equal(snapshot, messages);
    }

    [Fact]
    public void StrictDommeWire_TotalRequestFitsThe4kModelWithMarginToSpare()
    {
        // The end-to-end guarantee: for the owner's exact config, even with a fat restored
        // history, the assembled request stays under the budget that keeps OpenRouter's
        // middle-out compressor dormant — so the WHOLE system prompt (persona included)
        // verifiably reaches the model.
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.StrictDommeId));
        var session = RestoredOldVoiceSession(turnPairs: 40);

        var request = BuildStrictDommeRequest(session);

        var estimate = request.Messages.Sum(m => PromptAssembler.ConservativeRealTokens(m.Content));
        Assert.True(estimate <= PromptAssembler.ContextFitTokenBudget,
            $"wire request estimates {estimate} real tokens, over the {PromptAssembler.ContextFitTokenBudget} fit budget");
    }

    // ---------- the media block no longer demonstrates the house voice ----------

    [Fact]
    public void MediaLinkExamples_CarryNoHouseVoiceTilde()
    {
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.StrictDommeId));

        var prompt = BambiSprite.GetStablePrompt();

        // The two HOW TO LINK examples used to end in the bambi "~", modelling the house voice
        // inside every persona's prompt. Example phrasing must stay persona-neutral.
        Assert.DoesNotMatch("Example: \"[^\"\n]*~\"", prompt);
    }

    // ---------- helpers ----------

    /// <summary>A session as restored from session.json: alternating turns in the old voice,
    /// stamped minutes in the past (before any fence a test sets afterwards).</summary>
    private static ChatSession RestoredOldVoiceSession(int turnPairs)
    {
        var start = DateTime.UtcNow.AddMinutes(-60);
        var session = new ChatSession();
        for (int i = 0; i < turnPairs; i++)
        {
            session.Append(TurnKind.UserChat, i % 2 == 0 ? "hehe hi bambi~" : "what should i watch?",
                utc: start.AddSeconds(i * 2));
            session.Append(TurnKind.AssistantChat, OldVoice[i % OldVoice.Length],
                utc: start.AddSeconds(i * 2 + 1));
        }
        return session;
    }

    /// <summary>Builds the chat request exactly as CompanionBrain would: real stable prompt,
    /// default persona fence (App.Settings), the Bambi pool as link pool.</summary>
    private PromptRequest BuildStrictDommeRequest(ChatSession session)
    {
        var input = "hey, what should I do tonight?";
        session.Append(TurnKind.UserChat, input, utc: DateTime.UtcNow.AddSeconds(5));

        var pool = BuiltInMods.BambiSleep.Browser!.DefaultVideoLinks!
            .Select(kvp => (kvp.Key, kvp.Value)).ToList();
        var assembler = new PromptAssembler(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: BambiSprite.GetStablePrompt,
            localClock: () => new DateTime(2026, 8, 7, 19, 13, 0),
            linkPool: () => (IReadOnlyList<(string, string)>)pool);

        return assembler.BuildRequest(AiPurpose.Chat, session, input);
    }

    private static PromptAssembler Assembler(Func<DateTime?> personaFence) =>
        new(new InertMemoryStore(), new RecentRecommendations(),
            systemPromptProvider: () => "PREFIX",
            localClock: () => new DateTime(2026, 8, 7, 19, 13, 0),
            linkPool: () => Array.Empty<(string, string)>(),
            personaFence: personaFence);

    private static object? GetStatic(string name) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

    private static void SetStatic(string name, object? value) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, value);

    private static void SetPrivate(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static void SetBackingField(object target, Type owner, string name, object? value) =>
        BackingField(owner, name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(target, value);

    private static FieldInfo BackingField(Type owner, string name, BindingFlags flags)
    {
        var field = owner.GetField($"<{name}>k__BackingField", flags);
        Assert.NotNull(field);
        return field!;
    }
}
