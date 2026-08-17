using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// What the mod editor's framing widget actually writes, and what it reads back. The widget itself
/// needs a Dispatcher and a picked file, but every decision worth getting wrong lives in
/// <c>ModArtFramingRules</c> -- split out of the window for that reason, exactly like
/// <c>ModImageSlotRules</c> before it -- so it is all pinned here.
///
/// <para>Two filters carry the weight. A <b>default</b> framing (dead centre, no zoom) is byte-for-byte
/// what the ABSENCE of an entry already means to <see cref="ModArtFramingRegistry.ResolveViewbox"/>,
/// so writing it would fill mod.json with no-ops that read like deliberate choices. A framing for a
/// slot holding <b>no image</b> describes a crop of a file the package does not ship, which means the
/// app is showing its own built-in art -- and built-in art deliberately ignores artFraming and keeps
/// its shipped rect, so the entry is a claim about a picture that does not exist.</para>
/// </summary>
public class ModArtFramingExportTests
{
    private const double Tol = 1e-9;

    // ── the export filter ────────────────────────────────────────

    [Fact]
    public void NonDefaultFraming_ForAFilledSlot_IsWritten()
    {
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.45, 0.66, 2.0)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.NotNull(section);
        var framing = section!["features/fyp.png"][ModArtFramingRegistry.SurfaceRailChip];
        Assert.Equal(0.45, framing.CenterX, Tol);
        Assert.Equal(0.66, framing.CenterY, Tol);
        Assert.Equal(2.0, framing.Zoom, Tol);
    }

    [Fact]
    public void DefaultFraming_IsNeverWritten()
    {
        // Dead centre, no zoom: absence already means this, so the entry buys nothing.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.5, 0.5, 1.0)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.Null(section);
    }

    [Fact]
    public void FramingForAnEmptySlot_IsNeverWritten()
    {
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.2, 0.8, 3.0)),
            Slots(("features/fyp.png", null)));

        Assert.Null(section);
    }

    [Fact]
    public void FramingForASlotTheEditorDoesNotHave_IsNeverWritten()
    {
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.2, 0.8, 3.0)),
            Slots(("features/vault.png", @"C:\art\vault.png")));

        Assert.Null(section);
    }

    [Fact]
    public void OnlyTheNonDefaultSurfacesOfASharedFileSurvive()
    {
        // features/fyp.png is a rail chip AND a Play card. Framing only the chip must not drag an
        // untouched playCard entry along with it.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(
                ("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.30, 0.70, 1.8),
                ("features/fyp.png", ModArtFramingRegistry.SurfacePlayCard, 0.5, 0.5, 1.0)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.NotNull(section);
        var perSurface = section!["features/fyp.png"];
        Assert.Single(perSurface);
        Assert.True(perSurface.ContainsKey(ModArtFramingRegistry.SurfaceRailChip));
    }

    [Fact]
    public void UnknownSurfaceId_IsNotWrittenBack()
    {
        // A surface id this build does not have cannot have been framed against a real preview, so
        // it is dropped rather than echoed on as numbers nobody could check.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", "hologramSlab", 0.1, 0.9, 4.0)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.Null(section);
    }

    [Fact]
    public void NothingWorthWriting_YieldsNullSoTheKeyStaysOutOfTheJson()
        => Assert.Null(ModArtFramingRules.BuildManifestSection(
            new Dictionary<string, Dictionary<string, ModArtFraming>>(),
            Slots(("features/fyp.png", @"C:\art\fyp.png"))));

    [Fact]
    public void StoredNumbersAreRounded()
    {
        // A drag produces 0.47321981...; four decimals is finer than a pixel on any of these
        // surfaces and the rest is noise an author has to read past in mod.json.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.473219814, 0.128888888, 1.777777)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        var framing = section!["features/fyp.png"][ModArtFramingRegistry.SurfaceRailChip];
        Assert.Equal(0.4732, framing.CenterX, Tol);
        Assert.Equal(0.1289, framing.CenterY, Tol);
        Assert.Equal(1.7778, framing.Zoom, Tol);
    }

    [Fact]
    public void OutOfRangeNumbers_AreClampedNotDropped()
    {
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, -3, 12, 999)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        var framing = section!["features/fyp.png"][ModArtFramingRegistry.SurfaceRailChip];
        Assert.Equal(0.0, framing.CenterX, Tol);
        Assert.Equal(1.0, framing.CenterY, Tol);
        Assert.Equal(ModArtFramingRegistry.MaxZoom, framing.Zoom, Tol);
    }

    [Fact]
    public void SlotKeyCasing_DoesNotHideTheImage()
    {
        // A hand-edited mod.json can name the path with different casing; the editor's slot map is
        // ordinal, so the image lookup has to be the forgiving one or the framing vanishes on save.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("Features/FYP.png", ModArtFramingRegistry.SurfaceRailChip, 0.2, 0.3, 2.5)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.NotNull(section);
        Assert.True(section!.ContainsKey("Features/FYP.png"));
    }

    // ── the read-back ────────────────────────────────────────────

    [Fact]
    public void AbsentSection_ReadsAsEmptyNotNull()
        => Assert.Empty(ModArtFramingRules.ReadManifestSection(null));

    [Fact]
    public void ManifestRoundTripsThroughTheEditor()
    {
        var written = ModArtFramingRules.BuildManifestSection(
            Framings(
                ("features/lab_quiz_hero.png", ModArtFramingRegistry.SurfaceRailChip, 0.25, 0.4, 2.2),
                ("features/lab_quiz_hero.png", ModArtFramingRegistry.SurfacePageHeader, 0.5, 0.15, 1.4)),
            Slots(("features/lab_quiz_hero.png", @"C:\art\quiz.png")));

        var read = ModArtFramingRules.ReadManifestSection(written);

        var perSurface = read["features/lab_quiz_hero.png"];
        Assert.Equal(2, perSurface.Count);
        Assert.Equal(0.25, perSurface[ModArtFramingRegistry.SurfaceRailChip].CenterX, Tol);
        Assert.Equal(2.2, perSurface[ModArtFramingRegistry.SurfaceRailChip].Zoom, Tol);
        Assert.Equal(0.15, perSurface[ModArtFramingRegistry.SurfacePageHeader].CenterY, Tol);
        Assert.Equal(1.4, perSurface[ModArtFramingRegistry.SurfacePageHeader].Zoom, Tol);
    }

    [Fact]
    public void ReadingIgnoresAnUnknownSurfaceIdInsteadOfThrowing()
    {
        // A mod framed on a later build, or a typo in a hand-edited mod.json. The known surface
        // beside it must still load -- ignoring the file wholesale would lose real work.
        var manifest = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["features/fyp.png"] = new()
            {
                ["hologramSlab"] = new ModArtFraming { CenterX = 0.1, CenterY = 0.1, Zoom = 3 },
                [ModArtFramingRegistry.SurfacePlayCard] = new ModArtFraming { CenterX = 0.6, CenterY = 0.2, Zoom = 1.5 },
            },
        };

        var read = ModArtFramingRules.ReadManifestSection(manifest);

        var perSurface = read["features/fyp.png"];
        Assert.Single(perSurface);
        Assert.Equal(0.6, perSurface[ModArtFramingRegistry.SurfacePlayCard].CenterX, Tol);
    }

    [Fact]
    public void AFileWhoseOnlyFramingIsUnknown_DropsOutEntirely()
    {
        var manifest = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["features/fyp.png"] = new() { ["hologramSlab"] = new ModArtFraming { Zoom = 3 } },
        };

        Assert.Empty(ModArtFramingRules.ReadManifestSection(manifest));
    }

    [Fact]
    public void ReadingDropsAnExplicitlyDefaultFraming()
    {
        // Some hand-edits spell out the no-op. Keeping it would light up the "Framed" label on a
        // slot that is not framed at all.
        var manifest = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["features/fyp.png"] = new() { [ModArtFramingRegistry.SurfaceRailChip] = new ModArtFraming() },
        };

        Assert.Empty(ModArtFramingRules.ReadManifestSection(manifest));
    }

    [Fact]
    public void ReadingSanitizesGarbageRatherThanPropagatingIt()
    {
        // NaN out of a mangled mod.json would poison the readout, the IsDefault check and the drag
        // arithmetic all at once, so it is scrubbed at the door.
        var manifest = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["lockdown_icon.png"] = new()
            {
                [ModArtFramingRegistry.SurfaceRailCard] = new ModArtFraming
                {
                    CenterX = double.NaN,
                    CenterY = 4.5,
                    Zoom = double.PositiveInfinity,
                },
            },
        };

        var framing = ModArtFramingRules.ReadManifestSection(manifest)
            ["lockdown_icon.png"][ModArtFramingRegistry.SurfaceRailCard];

        Assert.Equal(0.5, framing.CenterX, Tol);
        Assert.Equal(1.0, framing.CenterY, Tol);

        // 1.0, not MaxZoom: a NON-FINITE zoom is garbage rather than "as far in as possible", and
        // ToViewbox already resolves it to 1.0. The editor has to agree with the runtime or the
        // preview would show a crop the app does not draw. A merely too-large FINITE zoom does
        // clamp to MaxZoom -- see OutOfRangeNumbers_AreClampedNotDropped.
        Assert.Equal(1.0, framing.Zoom, Tol);
    }

    [Fact]
    public void ReadIsCaseInsensitiveOnThePath()
    {
        var manifest = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["Features/FYP.png"] = new()
            {
                [ModArtFramingRegistry.SurfaceRailChip] = new ModArtFraming { CenterX = 0.3, Zoom = 2 },
            },
        };

        var read = ModArtFramingRules.ReadManifestSection(manifest);

        Assert.True(read.ContainsKey("features/fyp.png"));
    }

    // ── sanitize / round in isolation ────────────────────────────

    [Fact]
    public void SanitizeDoesNotMutateItsInput()
    {
        var original = new ModArtFraming { CenterX = 9, CenterY = -1, Zoom = 100 };

        var clean = ModArtFramingRules.Sanitize(original);

        Assert.Equal(9, original.CenterX, Tol);          // untouched
        Assert.Equal(1.0, clean.CenterX, Tol);
        Assert.Equal(0.0, clean.CenterY, Tol);
        Assert.Equal(ModArtFramingRegistry.MaxZoom, clean.Zoom, Tol);
    }

    [Fact]
    public void RoundingANearlyCentredFraming_MakesItDefaultAndSoUnwritten()
    {
        // 0.50001 is a rounding artefact of a drag, not a decision. It must not survive as an entry.
        var section = ModArtFramingRules.BuildManifestSection(
            Framings(("features/fyp.png", ModArtFramingRegistry.SurfaceRailChip, 0.50001, 0.49999, 1.00002)),
            Slots(("features/fyp.png", @"C:\art\fyp.png")));

        Assert.Null(section);
    }

    // ── helpers ──────────────────────────────────────────────────

    private static Dictionary<string, Dictionary<string, ModArtFraming>> Framings(
        params (string Path, string SurfaceId, double CenterX, double CenterY, double Zoom)[] entries)
    {
        var map = new Dictionary<string, Dictionary<string, ModArtFraming>>();
        foreach (var (path, surfaceId, cx, cy, zoom) in entries)
        {
            if (!map.TryGetValue(path, out var perSurface))
                map[path] = perSurface = new Dictionary<string, ModArtFraming>();
            perSurface[surfaceId] = new ModArtFraming { CenterX = cx, CenterY = cy, Zoom = zoom };
        }
        return map;
    }

    private static Dictionary<string, string?> Slots(params (string Key, string? Path)[] entries)
    {
        var map = new Dictionary<string, string?>();
        foreach (var (key, path) in entries) map[key] = path;
        return map;
    }
}
