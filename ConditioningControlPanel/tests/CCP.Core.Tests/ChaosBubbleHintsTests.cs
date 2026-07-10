using System.Collections.Generic;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the first-contact verb-hint key/text tables in <see cref="ChaosBubbleHints"/> against the
/// WPF original (ConditioningControlPanel/Services/Chaos/ChaosBubbleHints.cs; contract:
/// that WPF source + Services/Chaos/CHAOS_DESIGN.md). Covers the full KeyFor priority ladder, the
/// TextFor lexicon, the per-variant live:/treat: split, and the fail-toward-no-hint IsLearned predicate.
/// </summary>
public class ChaosBubbleHintsTests
{
    // ================================================================
    // KeyFor — priority ladder (WPF ChaosBubbleHints.cs KeyFor)

    [Fact]
    public void KeyFor_Null_ReturnsNull() => Assert.Null(ChaosBubbleHints.KeyFor(null));

    [Fact]
    public void KeyFor_Sweeper_ReturnsNull() =>
        Assert.Null(ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsSweeper = true }));

    [Fact]
    public void KeyFor_SweeperBeatsDarter_ReturnsNull() =>
        // Sweeper is checked before Darter: a GG sweeper darter has nothing to teach.
        Assert.Null(ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsSweeper = true, IsDarter = true }));

    [Theory]
    [InlineData("rabbit")]
    public void KeyFor_Darter_ReturnsRabbit(string expected) =>
        Assert.Equal(expected, ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsDarter = true }));

    [Fact]
    public void KeyFor_Freeze_ReturnsFreeze() =>
        Assert.Equal("freeze", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsFreeze = true }));

    [Fact]
    public void KeyFor_Tease_ReturnsTease() =>
        Assert.Equal("tease", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsTease = true }));

    [Fact]
    public void KeyFor_Brittle_ReturnsBrittle() =>
        Assert.Equal("brittle", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsBrittle = true }));

    [Fact]
    public void KeyFor_Escort_ReturnsChaperone() =>
        Assert.Equal("chaperone", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsEscort = true }));

    [Fact]
    public void KeyFor_ChaperoneLive_ReturnsChaperone() =>
        Assert.Equal("chaperone", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsChaperoneLive = true }));

    [Fact]
    public void KeyFor_Echo_ReturnsEcho() =>
        Assert.Equal("echo", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsEcho = true }));

    [Fact]
    public void KeyFor_BoundHalf_ReturnsBound() =>
        Assert.Equal("bound", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsBoundHalf = true }));

    [Fact]
    public void KeyFor_Golden_ReturnsGolden() =>
        Assert.Equal("golden", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsGolden = true }));

    [Fact]
    public void KeyFor_Heart_ReturnsHeart() =>
        Assert.Equal("heart", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsHeart = true }));

    [Fact]
    public void KeyFor_Droplet_ReturnsDroplet() =>
        Assert.Equal("droplet", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsDroplet = true }));

    [Fact]
    public void KeyFor_Prism_ReturnsPrism() =>
        Assert.Equal("prism", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsPrism = true }));

    [Fact]
    public void KeyFor_HeavyDrop_ReturnsHeavy() =>
        // Heavy Drop is flagged only by PayMult >= 2.0 (WPF BuildHeavy PayMult = 3.0).
        Assert.Equal("heavy", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { PayMult = 3.0 }));

    [Fact]
    public void KeyFor_PayMultBelowTwo_IsNotHeavy() =>
        // 1.9 must not trip the heavy branch — falls through to the treat:/live: default.
        Assert.Equal("treat:flash", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { PayMult = 1.9, VariantId = "flash" }));

    [Fact]
    public void KeyFor_OrdinaryLive_ReturnsLivePrefixedVariant() =>
        Assert.Equal("live:pink", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsLive = true, VariantId = "pink" }));

    [Fact]
    public void KeyFor_OrdinaryTreat_ReturnsTreatPrefixedVariant() =>
        Assert.Equal("treat:subliminal", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsLive = false, VariantId = "subliminal" }));

    [Fact]
    public void KeyFor_LiveFlagBeatsHeavyThreshold_WhenPayMultLow() =>
        // A live bubble under the heavy threshold gets the live: key with its own variant.
        Assert.Equal("live:spiral", ChaosBubbleHints.KeyFor(new ChaosBubbleSpec { IsLive = true, VariantId = "spiral", PayMult = 1.0 }));

    [Fact]
    public void KeyFor_TeaseBeatsBrittle()
    {
        // Tease is checked before Brittle in the ladder.
        var spec = new ChaosBubbleSpec { IsTease = true, IsBrittle = true };
        Assert.Equal("tease", ChaosBubbleHints.KeyFor(spec));
    }

    [Fact]
    public void KeyFor_DarterBeatsFreeze()
    {
        var spec = new ChaosBubbleSpec { IsDarter = true, IsFreeze = true };
        Assert.Equal("rabbit", ChaosBubbleHints.KeyFor(spec));
    }

    [Fact]
    public void KeyFor_GoldenBeatsHeavyThreshold()
    {
        // Golden is above the PayMult heavy check in the ladder.
        var spec = new ChaosBubbleSpec { IsGolden = true, PayMult = 3.0 };
        Assert.Equal("golden", ChaosBubbleHints.KeyFor(spec));
    }

    // ================================================================
    // TextFor — lexicon (WPF ChaosBubbleHints.cs TextFor)

    [Fact]
    public void TextFor_Null_ReturnsEmpty() => Assert.Equal("", ChaosBubbleHints.TextFor(null));

    [Fact]
    public void TextFor_ChaperoneLive_TeachesEscortFirst() =>
        Assert.Equal("pop my escort first", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsChaperoneLive = true }));

    [Fact]
    public void TextFor_Escort_TeachesPopMeFirst() =>
        Assert.Equal("pop me first", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsEscort = true }));

    [Fact]
    public void TextFor_Sweeper_ReturnsEmpty() =>
        // KeyFor(sweeper) == null → no text.
        Assert.Equal("", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsSweeper = true }));

    [Fact]
    public void TextFor_Live_TeachesHoldToSnap() =>
        Assert.Equal("hold to snap", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsLive = true, VariantId = "braindrain" }));

    [Fact]
    public void TextFor_Treat_TeachesClickToPop() =>
        Assert.Equal("click to pop", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsLive = false, VariantId = "flash" }));

    [Theory]
    [InlineData("click to catch")]
    public void TextFor_Darter(string expected) =>
        Assert.Equal(expected, ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsDarter = true }));

    [Fact]
    public void TextFor_Freeze() =>
        Assert.Equal("click to freeze", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsFreeze = true }));

    [Fact]
    public void TextFor_Tease() =>
        Assert.Equal("don't touch. let it leave", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsTease = true }));

    [Fact]
    public void TextFor_Brittle() =>
        Assert.Equal("glass. dodge it", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsBrittle = true }));

    [Fact]
    public void TextFor_Echo() =>
        Assert.Equal("hold fully or it splits", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsEcho = true }));

    [Fact]
    public void TextFor_Bound() =>
        Assert.Equal("hold both. fast", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsBoundHalf = true }));

    [Fact]
    public void TextFor_Golden() =>
        Assert.Equal("pop for gold", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsGolden = true }));

    [Fact]
    public void TextFor_Heart() =>
        Assert.Equal("click. +1 resistance", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsHeart = true }));

    [Fact]
    public void TextFor_Droplet() =>
        Assert.Equal("catch the gold", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsDroplet = true }));

    [Fact]
    public void TextFor_Prism() =>
        Assert.Equal("pop. pays 10x", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { IsPrism = true }));

    [Fact]
    public void TextFor_Heavy() =>
        Assert.Equal("click. pays x3", ChaosBubbleHints.TextFor(new ChaosBubbleSpec { PayMult = 3.0 }));

    // ================================================================
    // IsLearned — fail-toward-no-hint predicate (WPF ChaosBubbleHints.cs IsLearned)

    [Fact]
    public void IsLearned_NullKey_ReturnsTrue() =>
        Assert.True(ChaosBubbleHints.IsLearned(new HashSet<string>(), null));

    [Fact]
    public void IsLearned_EmptyKey_ReturnsTrue() =>
        Assert.True(ChaosBubbleHints.IsLearned(new HashSet<string>(), ""));

    [Fact]
    public void IsLearned_NullSet_ReturnsTrue() =>
        Assert.True(ChaosBubbleHints.IsLearned(null, "rabbit"));

    [Fact]
    public void IsLearned_Contains_ReturnsTrue() =>
        Assert.True(ChaosBubbleHints.IsLearned(new HashSet<string> { "rabbit", "tease" }, "tease"));

    [Fact]
    public void IsLearned_NotContained_ReturnsFalse() =>
        Assert.False(ChaosBubbleHints.IsLearned(new HashSet<string> { "rabbit" }, "tease"));

    [Fact]
    public void IsLearned_EmptySet_ReturnsFalse() =>
        Assert.False(ChaosBubbleHints.IsLearned(new HashSet<string>(), "golden"));
}
