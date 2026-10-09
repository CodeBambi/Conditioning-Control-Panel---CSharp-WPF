using System.Collections.Generic;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The file chains both heads resolve program art through (was WPF ProgramArt).</summary>
public sealed class ProgramArtPathsTests
{
    [Fact]
    public void BannerTriesProgramThenModThenOptionalDefault()
    {
        var program = new ProgramDefinition { Id = "The Takeover", ModId = "Sissy-Hypno" };

        Assert.Equal(new[] { "programs/banner_the_takeover.png", "programs/banner_sissy_hypno.png" },
            ProgramArtPaths.Banner(program, includeDefault: false));
        Assert.Equal("programs/banner_default.png", ProgramArtPaths.Banner(program)[^1]);
        Assert.Equal("programs/sigil_the_takeover.png", ProgramArtPaths.Sigil(program));
    }

    [Fact]
    public void PlateKeysOnTheTemplateNameAndStripsAVendorPrefix()
    {
        var program = new ProgramDefinition
        {
            Id = "first_week",
            Templates = new List<ProgramSessionTemplate> { new() { Id = "BW-Drift", Name = "Deep Drift" } }
        };

        Assert.Equal(
            new[] { "programs/first_week_plate_deep_drift.png", "programs/plate_deep_drift.png", "programs/plate_default.png" },
            ProgramArtPaths.DayPlate(program, new ProgramDay { SessionTemplateId = "BW-Drift" }));
        // Unknown template: the id with its "XX-" prefix dropped.
        Assert.Equal("programs/plate_focus.png",
            ProgramArtPaths.DayPlate(program, new ProgramDay { SessionTemplateId = "BW-Focus" })[1]);
        Assert.Equal(new[] { "programs/plate_default.png" },
            ProgramArtPaths.DayPlate(program, new ProgramDay()));
    }

    [Fact]
    public void HeroTriesProgramThenArchetypeThenDefault()
    {
        var program = new ProgramDefinition
        {
            Id = "kept",
            Templates = new List<ProgramSessionTemplate> { new() { Id = "BW-Drift", Name = "Drift" } }
        };

        Assert.Equal(
            new[] { "programs/kept_hero_drift.png", "programs/hero_drift.png", "programs/hero_default.png" },
            ProgramArtPaths.DayHero(program, new ProgramDay { SessionTemplateId = "BW-Drift" }));
        Assert.Equal(new[] { "programs/hero_default.png" }, ProgramArtPaths.DayHero(program, new ProgramDay()));
    }
}
