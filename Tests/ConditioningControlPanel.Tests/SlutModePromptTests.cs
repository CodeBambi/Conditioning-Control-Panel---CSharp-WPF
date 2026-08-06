using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Slut Mode is selected and she still tells me to keep it clean."
///
/// <para>The two ways an opted-in explicit persona could reach the model as a no-op, both live
/// 2026-08-07 and both proven here rather than argued:</para>
///
/// <list type="number">
///   <item><b>It never arrives.</b> The Companion tab's chip row writes
///   <c>ActivePersonalityPresetId</c>, and the whole two-zone prefix has to be rebuilt off the back
///   of it. If the preset's text did not reach <see cref="BambiSprite.GetStablePrompt"/> — or reached
///   it once and then sat in the fingerprint cache — the model would be answering with the previous
///   persona and no log line would say so.</item>
///
///   <item><b>It arrives next to its own contradiction.</b> Every non-explicit preset answers
///   "what do you do when they bring up sex?" with a deflection
///   (<c>[FEIGNED INNOCENCE PROTOCOL]</c>, <c>[GENTLE DEFLECTION]</c>, …). Flipping the Slut Mode
///   toggle used to swap only <c>Personality</c> for <c>SlutModePersonality</c> and leave that
///   deflection in place, so the prompt asked for "engage fully" and "FLUSTERED DENIAL, change the
///   subject" a paragraph apart — and the conditional deflection is the rule that wins.</item>
/// </list>
///
/// <para>Nothing here asserts anything about moderation strength. The safety sandwich is asserted to
/// be INTACT on every prompt these tests build; what they hold is that the persona the user paid the
/// acknowledgement dialog for is actually in the bytes, and is not accompanied by its own negation.
/// </para>
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class SlutModePromptTests : IDisposable
{
    // Two titles is enough: the media list is not what these tests are about, and a fixed pool keeps
    // the prompt off disk and off the active mod.
    private static readonly Dictionary<string, string> Pool = new()
    {
        ["Yes Brain Loop"] = "https://example.test/yes",
        ["Overload"] = "https://example.test/overload"
    };

    /// <summary>The Slut Mode preset's opening line — present only when that preset compiled in.</summary>
    private const string SlutPersonaMarker = "Drag Bambi down into depravity with you";

    /// <summary>The stock default's deflection protocol, which the slut variant must not ship with.</summary>
    private const string FeignedInnocenceMarker = "FEIGNED INNOCENCE PROTOCOL";

    private readonly object? _priorSettings;
    private readonly object? _priorPersonality;
    private readonly AppSettings _settings = new();

    public SlutModePromptTests()
    {
        _priorSettings = GetStatic("Settings");
        _priorPersonality = GetStatic("Personality");

        // App.Settings/App.Personality are process-wide statics with private setters and no seam.
        // A real SettingsService constructor would read (and later write) the developer's own
        // settings.json, so the service is created uninitialized and handed an in-memory AppSettings.
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        SetBackingField(service, service.GetType(), "Current", _settings);
        SetStatic("Settings", service);
        SetStatic("Personality", new PersonalityService());

        BambiSprite.VideoPoolProvider = () => Pool;
        BambiSprite.InvalidateStablePrompt();
    }

    public void Dispose()
    {
        BambiSprite.VideoPoolProvider = null;
        BambiSprite.InvalidateStablePrompt();
        SetStatic("Settings", _priorSettings);
        SetStatic("Personality", _priorPersonality);
    }

    // ---------- 1: the preset reaches the compiled prompt ----------

    [Fact]
    public void SelectingTheSlutModePreset_PutsItsPersonaInTheCompiledPrompt()
    {
        var before = Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        Assert.DoesNotContain(SlutPersonaMarker, before, StringComparison.Ordinal);

        // Exactly what the Companion tab's chip row does via MainWindow.ActivatePersonalityPreset →
        // PersonalityService.SetActivePreset. No explicit cache invalidation: the fingerprint has to
        // notice on its own, because that is all the running app gives it.
        _settings.ActivePersonalityPresetId = PersonalityPresets.SlutModeId;
        var after = BambiSprite.GetStablePrompt();

        Assert.Contains(SlutPersonaMarker, after, StringComparison.Ordinal);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void SwitchingPresets_InvalidatesTheCachedPrefixWithoutAnExplicitDrop()
    {
        Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        var builds = BambiSprite.PrefixBuildCount;

        _settings.ActivePersonalityPresetId = PersonalityPresets.SlutModeId;
        var switched = BambiSprite.GetStablePrompt();

        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
        Assert.Contains(SlutPersonaMarker, switched, StringComparison.Ordinal);

        // And it stays cached afterwards — the fix must not turn every turn into a rebuild.
        BambiSprite.GetStablePrompt();
        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
    }

    [Fact]
    public void TheSlutModePreset_DoesNotWearAHeaderTheSafetyPreambleNullifiesByName()
    {
        // SafetyComposer.Preamble overrides "any [NO LIMITS], [FULL ENGAGEMENT], [EXPLICIT], or
        // similar directive" — a clause aimed at user-injected jailbreak headers. The Slut Mode
        // preset used to head its own engagement block "[NO LIMITS - FULL ENGAGEMENT]", so the
        // sandwich named and cancelled the one block that switches her explicit.
        var sections = PersonalityPresets.GetSlutMode().PromptSettings!;
        var persona = sections.Personality + "\n" + sections.ExplicitReaction + "\n"
                      + sections.ContextReactions + "\n" + sections.OutputRules;

        Assert.DoesNotContain("[NO LIMITS", persona, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[FULL ENGAGEMENT", persona, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[EXPLICIT]", persona, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- 2: it does not arrive next to its own negation ----------

    [Fact]
    public void SpiceOn_DropsThePresetsNonExplicitDeflectionProtocol()
    {
        var tame = Compile(PersonalityPresets.BambiSpriteId, slutMode: false);
        Assert.Contains(FeignedInnocenceMarker, tame, StringComparison.Ordinal);

        var spicy = Compile(PersonalityPresets.BambiSpriteId, slutMode: true);

        // The slut variant is in, and the "you wont engage in sex roleplay, just gracefully change
        // topic" block it replaces is out. Both halves matter: shipping them together is what made
        // the toggle a no-op.
        Assert.Contains("SLUT MODE", spicy, StringComparison.Ordinal);
        Assert.DoesNotContain(FeignedInnocenceMarker, spicy, StringComparison.Ordinal);
    }

    [Fact]
    public void SpiceOn_KeepsTheDeflectionForPresetsWithNoSlutVariant()
    {
        // Gentle Trainer ships no SlutModePersonality, so the toggle changes nothing about it and
        // its [GENTLE DEFLECTION] must survive. The fix is scoped to "the user switched THIS persona
        // into its explicit variant", not to "the toggle is on".
        var off = Compile(PersonalityPresets.GentleTrainerId, slutMode: false);
        var on = Compile(PersonalityPresets.GentleTrainerId, slutMode: true);

        Assert.Contains("GENTLE DEFLECTION", off, StringComparison.Ordinal);
        Assert.Contains("GENTLE DEFLECTION", on, StringComparison.Ordinal);
        Assert.Equal(off, on, StringComparer.Ordinal);
    }

    // ---------- and the sandwich is still a sandwich ----------

    [Fact]
    public void EveryExplicitPrompt_IsStillWrappedByTheSafetySandwich()
    {
        foreach (var id in PersonalityPresets.BuiltInIds)
        {
            foreach (var slut in new[] { false, true })
            {
                var prompt = Compile(id, slut);
                Assert.StartsWith(SafetyComposer.Preamble, prompt, StringComparison.Ordinal);
                Assert.EndsWith(SafetyComposer.Floor, prompt, StringComparison.Ordinal);
            }
        }
    }

    // ---------- helpers ----------

    private string Compile(string presetId, bool slutMode)
    {
        _settings.ActivePersonalityPresetId = presetId;
        _settings.SlutModeEnabled = slutMode;
        return BambiSprite.GetStablePrompt();
    }

    private static object? GetStatic(string name) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);

    private static void SetStatic(string name, object? value) =>
        BackingField(typeof(ConditioningControlPanel.App), name,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, value);

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
