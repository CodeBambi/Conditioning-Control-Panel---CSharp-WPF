using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The LIBRARY vs SELECTION split that SORT (Arcademy room 201) needs and that both shipped
/// pickers now render: <see cref="AppSettings.RemoteSubLibrary"/> is every sub the user KEPT,
/// <see cref="AppSettings.FypOnlineCustomSubs"/> is still exactly what it always was, the
/// app-wide FEED SELECTION every consumer resolves channels from.
///
/// The risk this file exists for is the one the pitch called out: the migration touches two
/// shipped pickers, and "a bad blob must never empty a feed selection". So the migration is
/// asserted to be ONE-WAY (selection to library, never back), IDEMPOTENT (running it on every
/// load must not grow anything twice) and non-destructive.
/// </summary>
public class RemoteSubLibraryTests
{
    private static readonly JsonSerializerSettings LoaderSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, args) => { args.ErrorContext.Handled = true; }
    };

    private static AppSettings Load(string json)
        => JsonConvert.DeserializeObject<AppSettings>(json, LoaderSettings)!;

    // ---------------- migration ----------------

    [Fact]
    public void Migration_CopiesTheSelectionIntoTheLibrary()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "GOONED", "pokemon" } };
        s.MigrateRemoteSubLibrary();

        Assert.Equal(new[] { "GOONED", "pokemon" }, s.LibrarySubs);
        // ...and the selection is untouched: the library is additive, never a move.
        Assert.Equal(new[] { "GOONED", "pokemon" }, s.FypOnlineCustomSubs);
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "GOONED" } };
        s.MigrateRemoteSubLibrary();
        s.MigrateRemoteSubLibrary();
        s.MigrateRemoteSubLibrary();

        Assert.Single(s.RemoteSubLibrary);
    }

    [Fact]
    public void Migration_IsOneWay_ALibraryOnlySubNeverJoinsTheFeed()
    {
        var s = new AppSettings();
        Assert.True(s.TryAddLibrarySub("pokemon"));   // kept for a sort, not for the feed
        s.MigrateRemoteSubLibrary();

        Assert.Empty(s.FypOnlineCustomSubs);
        Assert.Single(s.RemoteSubLibrary);
    }

    [Fact]
    public void Migration_NeverEmptiesTheSelection()
    {
        // The nightmare case from the pitch: a library blob that is nonsense must cost nothing.
        var s = Load("""
            { "FypOnlineCustomSubs": ["EroticHypnosis"], "RemoteSubLibrary": [ {}, { "Name": "  " } ] }
            """);
        s.MigrateRemoteSubLibrary();

        Assert.Equal(new[] { "EroticHypnosis" }, s.FypOnlineCustomSubs);
        Assert.Equal(new[] { "EroticHypnosis" }, s.LibrarySubs);
    }

    [Fact]
    public void Migration_IsCaseInsensitive()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "gooned" } };
        Assert.True(s.TryAddLibrarySub("GOONED"));
        s.MigrateRemoteSubLibrary();

        Assert.Single(s.RemoteSubLibrary);
    }

    // ---------------- the library itself ----------------

    [Fact]
    public void TryAdd_SanitizesAndRefusesJunk()
    {
        var s = new AppSettings();
        Assert.True(s.TryAddLibrarySub("r/BambiSleep"));
        Assert.Equal(new[] { "BambiSleep" }, s.LibrarySubs);

        Assert.False(s.TryAddLibrarySub("https://www.reddit.com/r/BambiSleep/"));   // same sub
        Assert.False(s.TryAddLibrarySub("!"));
        Assert.False(s.TryAddLibrarySub(null));
        Assert.Single(s.RemoteSubLibrary);
    }

    [Fact]
    public void TryAdd_StopsAtTheCap()
    {
        var s = new AppSettings();
        for (int i = 0; i < AppSettings.RemoteSubLibraryCap; i++)
            Assert.True(s.TryAddLibrarySub("sub" + i));

        Assert.False(s.TryAddLibrarySub("oneTooMany"));
        Assert.Equal(AppSettings.RemoteSubLibraryCap, s.RemoteSubLibrary.Count);
    }

    [Fact]
    public void Deserialize_NormalisesDuplicatesAndOverflow()
    {
        var rows = new List<string>();
        for (int i = 0; i < 60; i++) rows.Add($$"""{ "Name": "sub{{i}}" }""");
        rows.Add("""{ "Name": "SUB0" }""");
        var s = Load($$"""{ "RemoteSubLibrary": [ {{string.Join(",", rows)}} ] }""");

        Assert.Equal(AppSettings.RemoteSubLibraryCap, s.RemoteSubLibrary.Count);
        Assert.Equal("sub0", s.RemoteSubLibrary[0].Name);
    }

    [Fact]
    public void Remove_TakesTheVerdictAndTheFeedMembershipWithIt()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "GOONED", "pokemon" } };
        s.MigrateRemoteSubLibrary();
        s.FypOnlineSubVerdicts["GOONED"] = new RemoteSubVerdict { Ok = true, VideoCount = 120, CheckedAtUtc = DateTime.UtcNow };

        Assert.True(s.RemoveLibrarySub("r/gooned"));   // sanitized + case-insensitive

        Assert.DoesNotContain("GOONED", s.LibrarySubs);
        Assert.DoesNotContain("GOONED", s.FypOnlineCustomSubs);
        Assert.False(s.FypOnlineSubVerdicts.ContainsKey("GOONED"));
        // The other name is untouched: one gesture removes one sub, not the row it sat in.
        Assert.Equal(new[] { "pokemon" }, s.FypOnlineCustomSubs);
    }

    [Fact]
    public void Remove_OfSomethingAbsentChangesNothing()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "GOONED" } };
        s.MigrateRemoteSubLibrary();

        Assert.False(s.RemoveLibrarySub("neverAdded"));
        Assert.Equal(new[] { "GOONED" }, s.FypOnlineCustomSubs);
    }

    // ---------------- the projection both pickers render ----------------

    [Fact]
    public void View_JoinsVerdictsAndSelection()
    {
        var s = new AppSettings { FypOnlineCustomSubs = new List<string> { "GOONED" } };
        s.MigrateRemoteSubLibrary();
        Assert.True(s.TryAddLibrarySub("pokemon"));       // kept, not selected
        Assert.True(s.TryAddLibrarySub("architecture"));  // kept, probed stills-only
        s.FypOnlineSubVerdicts["GOONED"] = new RemoteSubVerdict { Ok = true, VideoCount = 120, CheckedAtUtc = DateTime.UtcNow };
        s.FypOnlineSubVerdicts["architecture"] = new RemoteSubVerdict { Ok = true, VideoCount = 0, CheckedAtUtc = DateTime.UtcNow };

        var view = s.BuildRemoteSubLibraryView();
        Assert.Equal(3, view.Count);

        var gooned = view[0];
        Assert.True(gooned.Selected);
        Assert.True(gooned.Ok);
        Assert.False(gooned.StillOnly);

        var pokemon = view[1];
        Assert.False(pokemon.Selected);
        Assert.Null(pokemon.Ok);          // never probed reads as unknown, not as bad

        var arch = view[2];
        Assert.True(arch.Ok);
        Assert.True(arch.StillOnly);      // videoCount 0 is a real answer
    }
}

/// <summary>
/// The Arcademy host's side of the SORT contract: a request that names its own subs is served
/// from those subs alone, and a request that names none keeps the app-wide behaviour every other
/// class depends on. These cover the parsing/guard layer, which is where a mistake would be
/// silent (a pile dealt from the wrong subreddits still looks like media).
/// </summary>
public class ArcademyTaggedRequestTests
{
    [Fact]
    public void NoSubsField_KeepsTheAppWidePath()
    {
        // null is the signal for "this is not a tagged request" - the one thing that must not
        // change for Daily Trigger, Deja Vu, Impulse Control or The Deep End.
        Assert.Null(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "type": "assets-request", "count": 8 }""")));
    }

    [Fact]
    public void SubsAreSanitizedAndDeduped()
    {
        var subs = ArcademyHostService.ReadRequestSubs(JObject.Parse("""
            { "subs": ["r/BambiSleep", "https://www.reddit.com/r/BambiSleep/", "sissyhypno", "!", null, "  "] }
            """));
        Assert.Equal(new[] { "BambiSleep", "sissyhypno" }, subs);
    }

    [Fact]
    public void AnEmptySubsArrayIsNotTheAppWidePath()
    {
        // An empty (or all-junk) list must come back as an EMPTY list, not null: the caller
        // answers it with an empty reply rather than falling back to the app-wide pull.
        Assert.Empty(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "subs": [] }"""))!);
        Assert.Empty(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "subs": ["!", "x"] }"""))!);
    }

    [Fact]
    public void AMalformedSubsFieldIsRefused_NotWavedThrough()
    {
        // A bare string where an array belongs is a page bug. Falling back to the app-wide pull
        // would deal a pile from subs the player never picked, which is worse than dealing none.
        Assert.Empty(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "subs": "BambiSleep" }"""))!);
        Assert.Empty(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "subs": { "a": 1 } }"""))!);
        // An explicit null is "no pile named", which IS the app-wide path.
        Assert.Null(ArcademyHostService.ReadRequestSubs(JObject.Parse("""{ "subs": null }""")));
    }

    [Fact]
    public void SubsAreCapped()
    {
        var many = new JArray();
        for (int i = 0; i < 200; i++) many.Add("sub" + i);
        var subs = ArcademyHostService.ReadRequestSubs(new JObject { ["subs"] = many });
        Assert.Equal(64, subs!.Count);
    }

    [Theory]
    [InlineData("target", "target")]
    [InlineData("NOISE", "noise")]
    [InlineData("", "untagged")]
    [InlineData("../../etc", "etc")]
    public void TagIsNormalised(string raw, string expected)
        => Assert.Equal(expected, ArcademyHostService.ReadTag(new JObject { ["tag"] = raw }));

    [Fact]
    public void TagIsAbsentSafely()
        => Assert.Equal("untagged", ArcademyHostService.ReadTag(new JObject()));

    [Fact]
    public void BufferKeysNeverCollideWithTheAppWideOnes()
    {
        // The app-wide buffer is keyed by the bare kind ("loop"/"still"). A tagged key must not
        // be able to become one, or a pile would be answered out of the app-wide pool.
        var key = ArcademyHostService.TaggedBufferKey("target", "loop");
        Assert.NotEqual("loop", key);
        Assert.NotEqual(key, ArcademyHostService.TaggedBufferKey("noise", "loop"));
        Assert.NotEqual(key, ArcademyHostService.TaggedBufferKey("target", "still"));
    }

    [Fact]
    public void AFolderCannotClimbOutOfTheAssetsRoot()
    {
        var root = System.IO.Path.GetTempPath();
        Assert.Null(ArcademyHostService.ResolveAssetsFolder(root, "../.."));
        Assert.Null(ArcademyHostService.ResolveAssetsFolder(root, "images/../../../Windows"));
        Assert.Null(ArcademyHostService.ResolveAssetsFolder(root, ""));
        // A folder that does not exist is refused too - the sample would find nothing anyway,
        // and answering "no such folder" as an empty pile is the honest reply.
        Assert.Null(ArcademyHostService.ResolveAssetsFolder(root, "definitely-not-a-real-folder-9f3a"));
    }

    [Fact]
    public void AFolderInsideTheRootResolves()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-sort-test-" + Guid.NewGuid().ToString("N"));
        var nested = System.IO.Path.Combine(root, "images", "bambi");
        System.IO.Directory.CreateDirectory(nested);
        try
        {
            var resolved = ArcademyHostService.ResolveAssetsFolder(root, "images/bambi");
            Assert.NotNull(resolved);
            Assert.Equal(nested.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                resolved!.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch { } }
    }
}
