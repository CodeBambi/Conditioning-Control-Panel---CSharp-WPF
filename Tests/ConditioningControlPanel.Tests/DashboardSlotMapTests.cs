using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models.Dashboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The join between the Home mosaic's nine empty cells and the layout that fills them.
///
/// <para>Nothing in the compiler can see this join: the renderer looks the hosts up by their
/// generated field names (<c>tab.Slot0</c> ... <c>tab.Slot8</c>) and the XAML decides which grid
/// cell each of those names sits in. Rename a host and the build breaks loudly; MOVE one - swap
/// two Grid.Column values, drop a Grid.Row while retuning the wall - and nothing breaks at all.
/// The wall just comes back in a different order, with the default layout no longer matching the
/// wall that shipped. So the map is pinned here, scraped out of the XAML, the same way
/// PlayDoorRenderTests pins the Play hero brushes.</para>
/// </summary>
public class DashboardSlotMapTests
{
    /// <summary>Slot index -> (row, column), as the 6.9.5 wall was authored (plan 3.1).</summary>
    public static readonly (int Slot, int Row, int Col)[] ShippedCells =
    {
        (0, 0, 0), (1, 0, 1), (2, 0, 2), (3, 0, 3),
        (4, 1, 0), (5, 1, 3), (6, 2, 3), (7, 3, 2), (8, 3, 3),
    };

    public static IEnumerable<object[]> Cells() => ShippedCells.Select(c => new object[] { c.Slot, c.Row, c.Col });

    [Theory]
    [MemberData(nameof(Cells))]
    public void EverySlotHostSitsInTheCellItShippedIn(int slot, int row, int col)
    {
        var host = HostDeclaration(slot);
        Assert.Equal(row, Attribute(host, "Grid.Row"));
        Assert.Equal(col, Attribute(host, "Grid.Column"));
    }

    [Fact]
    public void TheNineHostsAreInternalSoTheRendererCanReachThem()
    {
        // x:FieldModifier="internal" is what turns a host into a field on the generated partial.
        // Without it the renderer cannot address the cell and the wall renders empty.
        foreach (var (slot, _, _) in ShippedCells)
            Assert.Contains("x:FieldModifier=\"internal\"", HostDeclaration(slot), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostsAreEmptyCells_NotTilesInDisguise()
    {
        // A host holds whatever the layout puts in it and nothing else. An authored child would
        // be a tile the renderer neither knows about nor can move.
        foreach (var (slot, _, _) in ShippedCells)
            Assert.EndsWith("/>", HostDeclaration(slot).Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoMovableTileIsStillAddressedByItsOldName()
    {
        // The nine x:Names the four render sites used to reach for. One left behind is a tile the
        // slot model does not own, painting over the cell the renderer is filling.
        var xaml = Xaml();
        foreach (var name in new[]
                 {
                     "CardFlash", "ComboVideoBubble", "CardSubliminal", "CardBouncingText", "CardJustDrop",
                     "ComboSpiralPink", "ComboMindDrain", "CardBubblePop", "CardLockCard",
                 })
            Assert.DoesNotContain($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixedCellsAndTheOverlaysAreUntouched()
    {
        var xaml = Xaml();
        foreach (var name in new[] { "LogoBrandFrame", "MysteryFlipHost", "CardMystery", "CardVault",
                                     "MysteryBadge", "VaultCta", "MosaicFx", "ProgramFeatureLockRibbon" })
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);

        // MosaicFx is the grid's FIRST child on purpose: declaration order is z-order, and the fog
        // must read through the gutters rather than over a card face.
        var grid = xaml.IndexOf("x:Name=\"VelvetFeatureGrid\"", StringComparison.Ordinal);
        Assert.True(grid >= 0);
        Assert.True(xaml.IndexOf("x:Name=\"MosaicFx\"", grid, StringComparison.Ordinal)
                    < xaml.IndexOf("x:Name=\"Slot0\"", grid, StringComparison.Ordinal));
    }

    [Fact]
    public void ThereIsAHostForEverySlotTheLayoutCanHold()
        => Assert.Equal(DashboardLayout.SlotCount, ShippedCells.Length);

    [Fact]
    public void TheDefaultLayoutStillNamesTheWallThatShipped()
    {
        // The pixel-parity claim in one line: slot N of the default layout is the feature that was
        // authored in slot N's cell before the grid became slot-driven.
        var expected = new[]
        {
            ("flash", (string?)null), ("video", "bubblecount"), ("subliminal", null), ("bouncingtext", null),
            ("justdrop", null), ("spiral", "pinkfilter"), ("mindwipe", "braindrain"), ("bubbles", null),
            ("lockcard", null),
        };

        var layout = DashboardLayout.Default();
        for (int i = 0; i < DashboardLayout.SlotCount; i++)
        {
            Assert.Equal(expected[i].Item1, layout.Slots[i].Primary);
            Assert.Equal(expected[i].Item2, layout.Slots[i].Secondary);
        }
    }

    // ── the scrape ───────────────────────────────────────────────

    private static string? _xaml;

    private static string Xaml() => _xaml ??= File.ReadAllText(
        Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "SettingsTabView.xaml"));

    private static string HostDeclaration(int slot)
    {
        var m = Regex.Match(Xaml(), $@"<Grid\s+x:Name=""Slot{slot}""[^>]*>");
        Assert.True(m.Success, $"Slot{slot} has no <Grid x:Name=\"Slot{slot}\" .../> host in SettingsTabView.xaml");
        return m.Value;
    }

    private static int Attribute(string declaration, string name)
    {
        var m = Regex.Match(declaration, $@"{Regex.Escape(name)}=""(\d+)""");
        Assert.True(m.Success, $"{name} is missing from: {declaration}");
        return int.Parse(m.Groups[1].Value);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
