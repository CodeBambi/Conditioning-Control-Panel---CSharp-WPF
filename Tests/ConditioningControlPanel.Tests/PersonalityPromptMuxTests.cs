using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The personality multiplexer: two systems (preset presets, and community/asset/hand-edited custom
/// prompts) share ONE wire slot, and until 2026-08-07 they disagreed about who owned it.
///
/// <list type="number">
///   <item><b>Preset selection was a no-op.</b> <c>SetActivePreset</c> wrote only
///   <c>ActivePersonalityPresetId</c>, while the mux handed the slot to any active community prompt.
///   The chip row and the avatar quick menu both read <c>GetActivePreset()</c>, so they confirmed a
///   switch that never reached the model. The four shipped <c>assets/prompts/*.json</c> import as
///   community prompts, which made this the mainline path, not an edge case.</item>
///
///   <item><b>A hand-edited prompt never reached the wire.</b> The prompt editor sets
///   <c>UseCustomPrompt = true</c> and nothing else — no <c>ActiveCommunityPromptId</c> — so the old
///   two-clause mux fell through to the preset and silently discarded the edits, while the quick
///   menu (which keys on the flag alone) announced "Custom Prompt Active".</item>
///
///   <item><b>Turning custom off left half of it standing.</b> Clearing <c>UseCustomPrompt</c>
///   without <c>ActiveCommunityPromptId</c> leaves the Companion tab reporting "Custom: &lt;name&gt;"
///   forever, because its readout checks the community id first.</item>
/// </list>
///
/// None of this had any coverage, which is why all three shipped. These tests pin the invariant the
/// fix rests on: <c>UseCustomPrompt</c> alone decides the mux, and picking a preset clears it.
/// </summary>
[Collection(PromptPrefixStateCollection.Name)]
public class PersonalityPromptMuxTests : IDisposable
{
    private static readonly Dictionary<string, string> Pool = new()
    {
        ["Yes Brain Loop"] = "https://example.test/yes",
        ["Overload"] = "https://example.test/overload"
    };

    /// <summary>Deliberately free of "Bambi" so the mod-aware string rewrite cannot eat it.</summary>
    private const string CustomCanary = "CUSTOM-PROMPT-CANARY-7f3a speak only in riddles";

    /// <summary>Gentle Trainer's deflection block — present only when that preset compiled in.</summary>
    private const string PresetMarker = "GENTLE DEFLECTION";

    private const string CommunityId = "community-prompt-under-test";

    private readonly object? _priorSettings;
    private readonly object? _priorPersonality;
    private readonly AppSettings _settings = new();
    private readonly string _tempDir;

    public PersonalityPromptMuxTests()
    {
        _priorSettings = GetStatic("Settings");
        _priorPersonality = GetStatic("Personality");

        // Same seam SlutModePromptTests uses: App.Settings/App.Personality are process-wide statics
        // with private setters, and a real SettingsService constructor would read (and then write)
        // the developer's own settings.json. The service is created uninitialized and handed an
        // in-memory AppSettings — plus a throwaway settings path, because SetActivePreset really
        // does call Save() and the debounced write must not land anywhere that matters.
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        SetBackingField(service, service.GetType(), "Current", _settings);
        _tempDir = Path.Combine(Path.GetTempPath(), "ccp-mux-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // GetUninitializedObject runs no field initializers, so Save()'s two lock objects are null
        // and lock() throws. Seed them along with the path.
        SetPrivate(service, "_settingsPath", Path.Combine(_tempDir, "settings.json"));
        SetPrivate(service, "_timerLock", new object());
        SetPrivate(service, "_saveLock", new object());

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
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ---------- 1: preset selection is authoritative ----------

    [Fact]
    public void PickingAPreset_TakesAnActiveCommunityPromptOffTheWire()
    {
        ActivateCommunityPrompt();
        var custom = BambiSprite.GetStablePrompt();
        Assert.Contains(CustomCanary, custom, StringComparison.Ordinal);

        // Exactly what the Z4 chip row and the avatar quick menu call.
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.GentleTrainerId));

        var after = BambiSprite.GetStablePrompt();
        Assert.DoesNotContain(CustomCanary, after, StringComparison.Ordinal);
        Assert.Contains(PresetMarker, after, StringComparison.Ordinal);
    }

    [Fact]
    public void PickingAPreset_ClearsBothHalvesOfTheOverride()
    {
        ActivateCommunityPrompt();

        App.Personality!.SetActivePreset(PersonalityPresets.GentleTrainerId);

        // Either half left standing is a UI that disagrees with the wire: the Companion tab reads
        // the community id, the quick menu reads the flag.
        Assert.Null(_settings.ActiveCommunityPromptId);
        Assert.False(_settings.CompanionPrompt.UseCustomPrompt);
        Assert.Equal(PersonalityPresets.GentleTrainerId, _settings.ActivePersonalityPresetId);
        Assert.Equal(PersonalityPresets.GentleTrainerId, App.Personality.GetActivePreset().Id);
    }

    [Fact]
    public void PickingAPreset_RebuildsThePrefixInsteadOfServingTheCachedCustomOne()
    {
        ActivateCommunityPrompt();
        BambiSprite.GetStablePrompt();
        var builds = BambiSprite.PrefixBuildCount;

        // No explicit invalidation call anywhere on this path — the fingerprint has to notice, which
        // is all the running app gives it.
        App.Personality!.SetActivePreset(PersonalityPresets.GentleTrainerId);
        var switched = BambiSprite.GetStablePrompt();

        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
        Assert.DoesNotContain(CustomCanary, switched, StringComparison.Ordinal);

        // ...and it stays cached afterwards, or every turn becomes a rebuild.
        BambiSprite.GetStablePrompt();
        Assert.Equal(builds + 1, BambiSprite.PrefixBuildCount);
    }

    [Fact]
    public void PickingAPreset_StampsThePersonaVoiceFence()
    {
        // The fence is what takes the OLD voice's few-shot off the wire (PersonaWireFidelityTests);
        // this pins the producer side: every successful preset selection — including re-selecting
        // the current one, which is the user re-asserting "this voice, now" — stamps it.
        Assert.Null(_settings.PersonaVoiceFenceUtc);

        var before = DateTime.UtcNow;
        Assert.True(App.Personality!.SetActivePreset(PersonalityPresets.GentleTrainerId));
        Assert.NotNull(_settings.PersonaVoiceFenceUtc);
        Assert.InRange(_settings.PersonaVoiceFenceUtc!.Value, before, DateTime.UtcNow);

        var first = _settings.PersonaVoiceFenceUtc;
        Assert.True(App.Personality.SetActivePreset(PersonalityPresets.GentleTrainerId));
        Assert.True(_settings.PersonaVoiceFenceUtc >= first);
    }

    [Fact]
    public void PickingAPresetThatDoesNotExist_LeavesTheCustomPromptAlone()
    {
        ActivateCommunityPrompt();

        Assert.False(App.Personality!.SetActivePreset("no-such-preset"));

        Assert.Equal(CommunityId, _settings.ActiveCommunityPromptId);
        Assert.True(_settings.CompanionPrompt.UseCustomPrompt);
        Assert.Contains(CustomCanary, BambiSprite.GetStablePrompt(), StringComparison.Ordinal);
    }

    // ---------- 2: a hand-edited prompt carries no community id ----------

    [Fact]
    public void AHandEditedPrompt_ReachesTheWireWithoutACommunityId()
    {
        // What CompanionPromptEditorDialog.SaveSettings writes: the flag and the text, nothing else.
        _settings.CompanionPrompt = new CompanionPromptSettings
        {
            UseCustomPrompt = true,
            Personality = CustomCanary
        };
        Assert.Null(_settings.ActiveCommunityPromptId);

        var prompt = BambiSprite.GetStablePrompt();

        Assert.Contains(CustomCanary, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(PresetMarker, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrphanedCommunityId_DoesNotPutAnythingOnTheWireOnItsOwn()
    {
        // The mirror case, and the reason the mux must not go back to checking the id first: a stale
        // id with the flag off must leave the preset in charge.
        _settings.ActivePersonalityPresetId = PersonalityPresets.GentleTrainerId;
        _settings.ActiveCommunityPromptId = CommunityId;
        _settings.CompanionPrompt = new CompanionPromptSettings
        {
            UseCustomPrompt = false,
            Personality = CustomCanary
        };

        var prompt = BambiSprite.GetStablePrompt();

        Assert.DoesNotContain(CustomCanary, prompt, StringComparison.Ordinal);
        Assert.Contains(PresetMarker, prompt, StringComparison.Ordinal);
    }

    // ---------- 3: turning custom off leaves nothing orphaned ----------

    [Fact]
    public void DisablingTheCustomPrompt_LeavesTheSettingsFullyConsistent()
    {
        ActivateCommunityPrompt();

        // The one path the quick menu's "Disable custom prompt", the editor's un-tick and
        // DeactivatePrompt all now route through.
        Assert.True(CommunityPromptService.ClearCustomPromptOverride(_settings));

        Assert.Null(_settings.ActiveCommunityPromptId);
        Assert.False(_settings.CompanionPrompt.UseCustomPrompt);
        Assert.DoesNotContain(CustomCanary, BambiSprite.GetStablePrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void DisablingTheCustomPrompt_KeepsTheEditedTextForWhenItIsTurnedBackOn()
    {
        ActivateCommunityPrompt();

        CommunityPromptService.ClearCustomPromptOverride(_settings);
        Assert.Equal(CustomCanary, _settings.CompanionPrompt.Personality);

        _settings.CompanionPrompt.UseCustomPrompt = true;
        Assert.Contains(CustomCanary, BambiSprite.GetStablePrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingAnAlreadyClearOverride_ReportsNoChange()
    {
        Assert.False(CommunityPromptService.ClearCustomPromptOverride(_settings));
        Assert.False(CommunityPromptService.ClearCustomPromptOverride(null));
    }

    // ---------- 4: the fingerprint follows the mux ----------

    [Fact]
    public void Fingerprint_ChangesWhenTheMuxOutcomeChanges()
    {
        // The prefix is cached on this hash for the rest of the launch, so a mux flip the
        // fingerprint cannot see is a stale prompt no log line would mention.
        _settings.ActivePersonalityPresetId = PersonalityPresets.GentleTrainerId;
        var presetPrint = Fingerprint();

        ActivateCommunityPrompt();
        var customPrint = Fingerprint();

        CommunityPromptService.ClearCustomPromptOverride(_settings);
        var backToPreset = Fingerprint();

        Assert.NotEqual(presetPrint, customPrint);
        Assert.Equal(presetPrint, backToPreset);
    }

    [Fact]
    public void Fingerprint_ChangesForAHandEditedPromptWithNoCommunityId()
    {
        // The bug-2 shape specifically: before the fix this flag moved the wire prompt not at all,
        // so the fingerprint had nothing to notice either.
        _settings.ActivePersonalityPresetId = PersonalityPresets.GentleTrainerId;
        _settings.CompanionPrompt = new CompanionPromptSettings { Personality = CustomCanary };
        var off = Fingerprint();

        _settings.CompanionPrompt.UseCustomPrompt = true;
        var on = Fingerprint();

        Assert.NotEqual(off, on);
    }

    // ---------- helpers ----------

    /// <summary>
    /// The three writes <c>CommunityPromptService.ActivatePrompt</c> makes once its content gate and
    /// validator pass. Replayed rather than called so the test stays off disk and off the network.
    /// </summary>
    private void ActivateCommunityPrompt()
    {
        _settings.CompanionPrompt = new CompanionPromptSettings { Personality = CustomCanary };
        _settings.CompanionPrompt.UseCustomPrompt = true;
        _settings.ActiveCommunityPromptId = CommunityId;
    }

    private static string Fingerprint() =>
        BambiSprite.ComputeFingerprint(BambiSprite.CaptureFingerprintInputs());

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
