using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The hooks between CCP's events and the price table: which Lockdown tripwires count as trying
/// to leave (the emergency exit never does), which quest row a quest books, and a guard that
/// every row on the page is actually wired to something. A row with no call site is a switch
/// that does nothing, and nothing else would notice.
/// </summary>
public class ChasterHooksTests
{
    [Theory]
    [InlineData(EscapeKinds.Close, true)]
    [InlineData(EscapeKinds.Stop, true)]
    [InlineData(EscapeKinds.WrongPhrase, true)]
    [InlineData(EscapeKinds.EmergencyExit, false)]
    [InlineData(EscapeKinds.Minimize, false)]
    [InlineData(EscapeKinds.SystemKey, false)]
    [InlineData(EscapeKinds.Settings, false)]
    [InlineData(EscapeKinds.Starve, false)]
    [InlineData(null, false)]
    public void Only_really_trying_to_leave_costs_and_the_emergency_exit_never_does(string? kind, bool costs)
    {
        Assert.Equal(costs, ChasterHooks.EscapeCosts(kind));
    }

    [Fact]
    public void A_weekly_quest_books_the_weekly_row()
    {
        Assert.Equal("quest", ChasterHooks.QuestRow(QuestType.Daily));
        Assert.Equal("quest_weekly", ChasterHooks.QuestRow(QuestType.Weekly));
    }

    [Fact]
    public void A_card_pays_for_every_typo_on_it()
    {
        var on = new HashSet<string> { "typo" };

        Assert.Equal(135, TabPrices.Resolve("typo", on, 9));
        Assert.Equal(0, TabPrices.Resolve("typo", on, 0));
    }

    /// <summary>Rows that exist on the page and are not wired yet. The slot's melt and jackpot
    /// and the five web-game rows need a page-to-host message that says the event was real (a
    /// free demo spin fires the same effects as a paid one). This list only ever shrinks.</summary>
    private static readonly string[] OwedRows = { "melt", "bubbles", "ball", "wall", "padlock", "crash" };

    [Fact]
    public void Every_row_on_the_page_is_wired_to_something_or_listed_as_owed()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var source = string.Join("\n", Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.EndsWith("TabPrices.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        // QuestRow picks its row with a ternary, so inside the hooks file the bare id counts.
        var hooks = File.ReadAllText(Path.Combine(app, "Services", "Chaster", "ChasterHooks.cs"));

        // "misses" is booked by the service itself, on the day the player comes back.
        Assert.Contains("CircesMisses.Charges(", source);

        var unwired = TabPrices.All.Select(p => p.Id)
            .Where(id => id != CircesMisses.EventId)
            .Where(id => !source.Contains("Note(\"" + id + "\"", StringComparison.Ordinal)
                      && !source.Contains("NoteSeconds(\"" + id + "\"", StringComparison.Ordinal)
                      && !source.Contains("NoteAt(\"" + id + "\"", StringComparison.Ordinal)
                      && !hooks.Contains("\"" + id + "\"", StringComparison.Ordinal))
            .OrderBy(id => id)
            .ToList();

        Assert.Equal(OwedRows.OrderBy(id => id), unwired);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
