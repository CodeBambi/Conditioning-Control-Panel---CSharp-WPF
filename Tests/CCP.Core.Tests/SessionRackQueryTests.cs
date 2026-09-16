using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SessionRackQueryTests
{
    [Fact]
    public void RackAccepts_ComposesKnownFiltersAndSearchesRawModeAwareText()
    {
        var previous = CoreMods.MakeModAwareProvider;
        try
        {
            CoreMods.MakeModAwareProvider = text => text.Replace("Bambi", "Bimbo", StringComparison.Ordinal);
            var difficulties = new HashSet<SessionDifficulty> { SessionDifficulty.Hard };
            var nameMatch = Session("name", "BambiTitle", "plain", SessionSource.Custom, SessionDifficulty.Hard);
            var descriptionMatch = Session("description", "plain", "BambiDescription", SessionSource.Custom, SessionDifficulty.Hard);
            var catalogueMatch = Session("catalogue", "BambiCatalogue", "plain", SessionSource.Imported, SessionDifficulty.Hard);

            Assert.True(SessionRackQuery.RackAccepts(nameMatch, "yours", difficulties, "bimbo"));
            Assert.True(SessionRackQuery.RackAccepts(descriptionMatch, "yours", difficulties, "bimbo"));
            Assert.True(SessionRackQuery.RackAccepts(catalogueMatch, "catalogue", difficulties, "bimbo"));
            Assert.True(SessionRackQuery.RackAccepts(nameMatch, "future-source", difficulties, "bimbo"));
            Assert.True(SessionRackQuery.RackAccepts(nameMatch, "all", difficulties, ""));
            Assert.False(SessionRackQuery.RackAccepts(nameMatch, "builtin", difficulties, "bimbo"));
            Assert.False(SessionRackQuery.RackAccepts(
                Session("easy", "BambiTitle", "plain", SessionSource.Custom, SessionDifficulty.Easy),
                "yours", difficulties, "bimbo"));
            Assert.False(SessionRackQuery.RackAccepts(nameMatch, "yours", difficulties, " "));
            Assert.False(SessionRackQuery.RackAccepts(nameMatch, "yours", difficulties, "missing"));

            // The query follows the row's raw mode-aware metadata, never LocalizedName or its key.
            var raw = Session("morning_drift", "rawname", "rawdescription", SessionSource.Custom, SessionDifficulty.Hard);
            Assert.True(SessionRackQuery.RackAccepts(raw, "yours", difficulties, "rawname"));
            Assert.False(SessionRackQuery.RackAccepts(raw, "yours", difficulties, "session_morning_drift_name"));
        }
        finally
        {
            CoreMods.MakeModAwareProvider = previous;
        }
    }

    [Fact]
    public void SortRackSessions_UsesAllModesAndFileRecencyContract()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-rack-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var oldest = WriteFile(directory, "oldest", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local));
            var middle = WriteFile(directory, "middle", new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Local));
            var newest = WriteFile(directory, "newest", new DateTime(2020, 1, 3, 0, 0, 0, DateTimeKind.Local));
            var deleted = WriteFile(directory, "deleted", new DateTime(2020, 1, 4, 0, 0, 0, DateTimeKind.Local));
            File.Delete(deleted); // Missing before the query is called: it is undated, not an exception.

            var a = Session("a", "Zulu", "", SessionSource.BuiltIn, SessionDifficulty.Medium, 20, 100, newest);
            var b = Session("b", "alpha", "", SessionSource.Custom, SessionDifficulty.Easy, 30, 500, middle);
            var c = Session("c", "ALPHA", "", SessionSource.Imported, SessionDifficulty.Easy, 10, 200);
            var d = Session("d", "Beta", "", SessionSource.Custom, SessionDifficulty.Hard, 50, 500, deleted);
            var e = Session("e", "Omega", "", SessionSource.BuiltIn, SessionDifficulty.Extreme, 5, 100, oldest);
            var registry = new List<Session> { a, b, c, d, e };
            var rows = new List<Session> { e, c, a, d, b };

            AssertOrder("name", rows, registry, "b", "c", "d", "e", "a");
            AssertOrder("easiest", rows, registry, "c", "b", "a", "d", "e");
            AssertOrder("hardest", rows, registry, "e", "d", "a", "b", "c");
            AssertOrder("shortest", rows, registry, "e", "c", "a", "b", "d");
            AssertOrder("xp", rows, registry, "b", "d", "c", "a", "e");
            AssertOrder("recent", rows, registry, "a", "b", "e", "c", "d");
            AssertOrder("unknown-sort", rows, registry, "a", "b", "e", "c", "d");

            // A duplicate row ID is stamped by the dictionary's last input row, while both rows
            // retain their object identity and the other row keeps its own timestamp.
            var duplicateOld = Session("duplicate", "old", "", SessionSource.Custom, SessionDifficulty.Easy,
                1, 1, oldest);
            var other = Session("other", "other", "", SessionSource.Custom, SessionDifficulty.Easy,
                1, 1, middle);
            var duplicateNew = Session("duplicate", "new", "", SessionSource.Custom, SessionDifficulty.Easy,
                1, 1, newest);
            var duplicateOrder = SessionRackQuery.SortRackSessions(
                new List<Session> { duplicateOld, other, duplicateNew },
                new List<Session> { duplicateOld, other, duplicateNew }, "recent");
            Assert.Same(duplicateOld, duplicateOrder[0]);
            Assert.Same(duplicateNew, duplicateOrder[1]);
            Assert.Same(other, duplicateOrder[2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SortRackSessions_UsesLastRegistryIndexAndStableIdentityTies()
    {
        var registry = new List<Session>
        {
            Session("duplicate", "same", "", SessionSource.BuiltIn, SessionDifficulty.Easy),
            Session(null, "same", "", SessionSource.BuiltIn, SessionDifficulty.Easy),
            Session("", "same", "", SessionSource.BuiltIn, SessionDifficulty.Easy),
            Session("duplicate", "same", "", SessionSource.BuiltIn, SessionDifficulty.Easy)
        };
        var duplicateFirst = Session("duplicate", "same", "", SessionSource.Custom, SessionDifficulty.Hard);
        var empty = Session("", "same", "", SessionSource.Custom, SessionDifficulty.Hard);
        var nullId = Session(null, "same", "", SessionSource.Custom, SessionDifficulty.Hard);
        var duplicateLast = Session("duplicate", "same", "", SessionSource.Custom, SessionDifficulty.Hard);
        var unknown = Session("unknown", "same", "", SessionSource.Custom, SessionDifficulty.Hard);
        var rows = new List<Session> { duplicateFirst, empty, nullId, duplicateLast, unknown };

        foreach (var sort in new[] { "name", "easiest", "hardest", "shortest", "xp", "recent", "future" })
        {
            var ordered = SessionRackQuery.SortRackSessions(rows, registry, sort);
            Assert.Same(empty, ordered[0]);       // null and empty IDs share the last registry index (2).
            Assert.Same(nullId, ordered[1]);     // LINQ's stable tie keeps input order.
            Assert.Same(duplicateFirst, ordered[2]); // duplicate ID uses the last registry index (3).
            Assert.Same(duplicateLast, ordered[3]);
            Assert.Same(unknown, ordered[4]);    // absent IDs sort at int.MaxValue.
        }
    }

    private static Session Session(
        string? id,
        string name,
        string description,
        SessionSource source,
        SessionDifficulty difficulty,
        int duration = 30,
        int xp = 50,
        string path = "") => new()
        {
            Id = id!,
            Name = name,
            Description = description,
            Source = source,
            Difficulty = difficulty,
            DurationMinutes = duration,
            BonusXP = xp,
            SourceFilePath = path
        };

    private static string WriteFile(string directory, string name, DateTime lastWrite)
    {
        var path = Path.Combine(directory, name + ".session.json");
        File.WriteAllText(path, "{}");
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    private static void AssertOrder(
        string sort,
        IReadOnlyList<Session> rows,
        IReadOnlyList<Session> registry,
        params string[] expected) =>
        Assert.Equal(expected, SessionRackQuery.SortRackSessions(rows, registry, sort)
            .Select(session => session.Id).ToArray());
}
