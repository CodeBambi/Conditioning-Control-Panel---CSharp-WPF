using Newtonsoft.Json;

using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Validates the S2a-1 SchemaVersion 3 additive model bump on
/// <see cref="ChaosMetaState"/>: the 5 new properties (PurchasedDials, ConsumableSlots,
/// ForceScriptedRun, SeenFirstReturn, SeenWarrenWelcome) take neutral defaults so legacy
/// v2 saves load idempotently, and all new fields round-trip through Newtonsoft.Json.
/// </summary>
public class ChaosMetaStateV3Tests
{
    [Fact]
    public void SchemaVersion_IsThree_OnFreshState()
    {
        var state = new ChaosMetaState();
        Assert.Equal(3, state.SchemaVersion);
    }

    [Fact]
    public void FreshState_ConsumableSlots_DefaultsToOne()
    {
        var state = new ChaosMetaState();

        // The one non-zero neutral default: old saves must get a working HUD slot.
        Assert.Equal(1, state.ConsumableSlots);
        Assert.NotNull(state.PurchasedDials);
        Assert.Empty(state.PurchasedDials);

        // All three FTUE bools default false so old saves never replay one-shot beats.
        Assert.False(state.ForceScriptedRun);
        Assert.False(state.SeenFirstReturn);
        Assert.False(state.SeenWarrenWelcome);
    }

    [Fact]
    public void V2Save_LoadsIdempotently_WithNeutralDefaults()
    {
        // Hand-written v2 save: PascalCase (Core has no custom JSON resolver), SchemaVersion 2,
        // and the 5 new v3 fields OMITTED entirely. Sparks/Gold carry non-zero v2 data.
        const string V2Json =
            "{\"SchemaVersion\":2,\"Sparks\":42,\"Gold\":7}";

        var loaded = JsonConvert.DeserializeObject<ChaosMetaState>(V2Json)!;

        // Neutral defaults fill the absent v3 fields.
        Assert.Equal(1, loaded.ConsumableSlots);
        Assert.NotNull(loaded.PurchasedDials);
        Assert.Empty(loaded.PurchasedDials);
        Assert.False(loaded.ForceScriptedRun);
        Assert.False(loaded.SeenFirstReturn);
        Assert.False(loaded.SeenWarrenWelcome);

        // Present v2 fields survive untouched.
        Assert.Equal(42, loaded.Sparks);
        Assert.Equal(7, loaded.Gold);

        // SchemaVersion is a plain property: the model does NOT auto-upgrade — the store layer does.
        // A v2 save loads as SchemaVersion 2 here; that migration is a later slice.
        Assert.Equal(2, loaded.SchemaVersion);

        // Re-serialize and re-deserialize => byte-stable round-trip of the v2 payload.
        var reserialized = JsonConvert.SerializeObject(loaded);
        var reloaded = JsonConvert.DeserializeObject<ChaosMetaState>(reserialized)!;
        Assert.Equal(reserialized, JsonConvert.SerializeObject(reloaded));
        Assert.Equal(2, reloaded.SchemaVersion);
        Assert.Equal(42, reloaded.Sparks);
        Assert.Equal(7, reloaded.Gold);
    }

    [Fact]
    public void RoundTrip_AllNewProps_Persist()
    {
        var state = new ChaosMetaState
        {
            PurchasedDials = { "hydra" },
            ConsumableSlots = 4,
            ForceScriptedRun = true,
            SeenFirstReturn = true,
            SeenWarrenWelcome = true,
        };

        var json = JsonConvert.SerializeObject(state);
        var reloaded = JsonConvert.DeserializeObject<ChaosMetaState>(json)!;

        Assert.Contains("hydra", reloaded.PurchasedDials);
        Assert.Equal(4, reloaded.ConsumableSlots);
        Assert.True(reloaded.ForceScriptedRun);
        Assert.True(reloaded.SeenFirstReturn);
        Assert.True(reloaded.SeenWarrenWelcome);
    }
}
