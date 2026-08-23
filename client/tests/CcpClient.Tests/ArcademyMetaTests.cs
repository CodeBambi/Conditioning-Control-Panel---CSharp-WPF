using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slices 3 and 4 of the Arcademy board row: the META STORE and the XP + ATTENDANCE PAYOUT.
///
/// <para><b>THESE ARE THE FIRST FACTS THIS BEHAVIOUR HAS EVER HAD, ON EITHER SIDE.</b> The board
/// row calls slice 3 "the cleanest slice and the one upstream admits has no machine verification at
/// all", and the shipping tree agrees: <c>Tests/ConditioningControlPanel.Tests</c> contains no
/// Arcademy test of any kind, so every clamp, cap, trim, ladder rung and streak rule below has
/// until now been verified by nothing but reading. Each one is written as an INVERSION — delete the
/// behaviour it names and it fails.</para>
///
/// <para><b>THE DOOR IS UNTOUCHED.</b> <see cref="ArcademyDoor.Available"/> is still a
/// <c>static readonly false</c> with no override seam, nothing here flips it, and nothing here goes
/// through <c>Attend()</c>. These facts drive the units directly, which is the only way to reach
/// this code and is exactly how the surface stays unreachable.</para>
///
/// <para><b>Every date here is INJECTED.</b> No fact reads the machine clock or its timezone: the
/// payout takes a <see cref="DateTimeOffset"/>, and the UTC/LOCAL split that regression #978 lives
/// in is checked by handing it one instant that falls on two different days.</para>
///
/// <para><b>What these facts are NOT.</b> No browser starts, no page loads, not one line of the
/// payload's JavaScript runs and no window opens. They pin STORE EFFECTS and FRAME CONTENT — what a
/// command leaves on disk, what a payout computes, what goes out and in what order — never that a
/// page received one, that a streak chip repainted, or that anything is playable.</para>
/// </summary>
public sealed class ArcademyMetaTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<object> _posted = [];
    private readonly TempDir _dir = new();
    private readonly List<PersistenceStore<ArcademyMetaDocument>> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            _ = store.StopAsync();
        }

        _dir.Dispose();
    }

    // ==================================================================================
    // Slice 3 — the persisted shape.
    // ==================================================================================

    /// <summary>
    /// The whole blob survives a real write and a real reload through this port's persistence
    /// machinery: the five HOST-OWNED regions (typed members) and the page-owned regions
    /// (<c>[JsonExtensionData]</c>) come back identical, and the file carries the store-owned
    /// <c>schemaVersion</c> this build writes (persistence contract §1).
    /// </summary>
    [Fact]
    public async Task MetaBlob_RoundTripsBothHalvesThroughARealReload()
    {
        var path = _dir.Path(ArcademyMetaDocument.FileName);
        var (store, meta) = NewMeta(path);

        meta.RecordAttendance("2026-08-20", "the-deep-end");
        Assert.True(meta.TryClaimXpDay("the-deep-end", "2026-08-20"));
        meta.Set("games", Json("""{"the-deep-end":{"tier":2,"promotions":1,"best":"A"}}"""));
        meta.Merge("days", Json("""{"2026-08-20":{"complete":false}}"""));
        await store.SaveImmediate();

        // The file itself, not just the object: schemaVersion is the store's, the key names are
        // upstream's own (upstream ArcademyMetaStore.cs:32-46).
        var raw = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(ArcademyMetaDocument.CurrentSchemaVersion,
            raw[PersistenceStore<ArcademyMetaDocument>.SchemaVersionKey]!.GetValue<int>());
        Assert.Equal("2026-08-20", raw[ArcademyMetaDocument.AttendanceKey]!.GetValue<string>());

        var (_, reloaded) = NewMeta(path);
        var blob = reloaded.Snapshot();

        Assert.Equal(1, blob[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        Assert.Equal("2026-08-20", blob[ArcademyMetaDocument.AttendanceKey]!.GetValue<string>());
        Assert.Equal("the-deep-end", blob[ArcademyMetaDocument.TodayClassesKey]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("the-deep-end",
            blob[ArcademyMetaDocument.XpPaidKey]!["2026-08-20"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(2, blob["games"]!["the-deep-end"]!["tier"]!.GetValue<int>());
        Assert.False(blob["days"]!["2026-08-20"]!["complete"]!.GetValue<bool>());

        // A reloaded ledger is still an authority: the same claim does not pay twice across a
        // restart, which is the whole point of persisting it (upstream ArcademyMetaStore.cs:238-249).
        Assert.False(reloaded.TryClaimXpDay("the-deep-end", "2026-08-20"));
    }

    /// <summary>
    /// Unknown-member preservation (persistence contract §6) is not a nicety here, it is upstream's
    /// ownership model: the page authors its own regions (upstream <c>ArcademyMetaStore.cs:20-23</c>), so a
    /// key this build has never heard of must round-trip through a write it was not part of.
    /// </summary>
    [Fact]
    public async Task MetaBlob_PreservesARegionThisBuildHasNeverHeardOf()
    {
        var path = _dir.Path(ArcademyMetaDocument.FileName);
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "streak": 4,
              "aRegionFromAFuturePage": { "nested": [1, 2, 3], "why": "the page owns this" }
            }
            """);

        var (store, meta) = NewMeta(path);
        // A write that has nothing to do with the unknown region.
        meta.Set("games", Json("""{"impulse-control":{"tier":1}}"""));
        await store.SaveImmediate();

        var raw = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(2, raw["aRegionFromAFuturePage"]!["nested"]!.AsArray()[1]!.GetValue<int>());
        Assert.Equal("the page owns this", raw["aRegionFromAFuturePage"]!["why"]!.GetValue<string>());
        Assert.Equal(4, raw[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        // …and it is readable as a top-level key like any other, because that is what it is.
        Assert.NotNull(meta.Get("aRegionFromAFuturePage"));
    }

    /// <summary>
    /// A document written by a NEWER build is refused rather than downgraded: flagged defaults,
    /// writes disabled, and the file on disk is left exactly as it was (contract §5 rule 4 /
    /// §4 rule 7). Losing a streak to an older launch is the failure this prevents.
    /// </summary>
    [Fact]
    public async Task MetaBlob_RefusesADocumentFromANewerSchemaAndNeverClobbersIt()
    {
        var path = _dir.Path(ArcademyMetaDocument.FileName);
        var fromTheFuture = """
            {"schemaVersion":99,"streak":41,"perfectAttendance":9,"aFutureRegion":{"x":1}}
            """;
        File.WriteAllText(path, fromTheFuture);

        var (store, meta) = NewMeta(path);

        Assert.IsType<LoadOutcome.NewerSchema>(store.LastLoadOutcome);
        Assert.Equal(99, ((LoadOutcome.NewerSchema)store.LastLoadOutcome!).FileVersion);
        Assert.True(store.WritesDisabled);
        Assert.True(store.LastLoadOutcome.IsDegraded);
        // Flagged DEFAULTS — the future streak was not adopted.
        Assert.Equal(0, meta.Snapshot()[ArcademyMetaDocument.StreakKey]!.GetValue<int>());

        // And a write attempt cannot reach the file.
        meta.Set("games", Json("""{"the-deep-end":{"tier":4}}"""));
        await store.SaveImmediate();
        Assert.Equal(fromTheFuture, File.ReadAllText(path));
    }

    // ==================================================================================
    // Slice 3 — the command loop's guards.
    // ==================================================================================

    /// <summary>
    /// The five host-owned keys are refused for the PAGE and still answered, with the value that is
    /// actually stored (<c>Guard</c>, <c>:326-333</c>; the page carries the same list at
    /// <c>arcademy/core/store.js:47-50</c>). A stale or edited page cannot mint a streak.
    /// </summary>
    [Fact]
    public void MetaCommand_RefusesEveryHostOwnedKeyAndAnswersWithTheRealValue()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        meta.RecordAttendance("2026-08-20", "the-deep-end");

        var reply = meta.Handle("set", ArcademyMetaDocument.StreakKey, Json("999"));
        Assert.NotNull(reply);
        Assert.Equal(1, reply!.Value.Value!.GetValue<int>());          // the real streak, not 999
        Assert.Equal(1, meta.Snapshot()[ArcademyMetaDocument.StreakKey]!.GetValue<int>());

        foreach (var key in ArcademyMetaDocument.HostOwnedKeys)
        {
            var before = meta.Snapshot()[key]?.ToJsonString();
            Assert.NotNull(meta.Handle("set", key, Json("42")));
            Assert.NotNull(meta.Handle("merge", key, Json("""{"forged":true}""")));
            Assert.Equal(before, meta.Snapshot()[key]?.ToJsonString());
            Assert.Contains(_log, l => l.Contains($"host-owned key '{key}'"));
        }
    }

    /// <summary>
    /// <c>get</c> / <c>set</c> / <c>merge</c> over a page-owned key, each answering with the
    /// POST-write value (<c>Handle</c>, <c>:118-152</c>; <c>Set</c> <c>:155</c>; <c>Merge</c>
    /// <c>:166-188</c>). Merge is SHALLOW, creates the key when absent, and a non-object patch is a
    /// set — "the page uses merge for per-game bags, and refusing would just lose the write".
    /// </summary>
    [Fact]
    public void MetaCommand_GetSetAndShallowMergeAnswerWithWhatIsStored()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        Assert.Null(meta.Handle("get", "games", null)!.Value.Value);

        meta.Handle("set", "games", Json("""{"the-deep-end":{"tier":1}}"""));
        // Merge CREATES an absent key…
        meta.Handle("merge", "flags", Json("""{"sawTutorial":true}"""));
        // …and shallow-merges into an existing one: the untouched sibling survives, the named one
        // is replaced outright rather than deep-merged.
        var merged = meta.Handle("merge", "games", Json("""{"impulse-control":{"tier":3}}"""));

        Assert.Equal(1, merged!.Value.Value!["the-deep-end"]!["tier"]!.GetValue<int>());
        Assert.Equal(3, merged.Value.Value!["impulse-control"]!["tier"]!.GetValue<int>());
        Assert.True(meta.Get("flags")!["sawTutorial"]!.GetValue<bool>());

        // A non-object patch is a set (:183).
        Assert.Equal("plain", meta.Handle("merge", "flags", Json("\"plain\""))!.Value.Value!.GetValue<string>());

        // A JSON null stores a null rather than removing the key (upstream's JValue.CreateNull()).
        var nulled = meta.Handle("set", "flags", Json("null"));
        Assert.NotNull(nulled);
        Assert.Null(nulled!.Value.Value);
        Assert.True(meta.Snapshot().ContainsKey("flags"));
    }

    /// <summary>
    /// A command whose key is missing, blank or over <see cref="ArcademyMetaStore.MaxKeyLength"/>,
    /// and a command with an op outside the three, are NOT ANSWERED AT ALL (<c>:124-128</c>,
    /// <c>:142-145</c>) — and the key is trimmed before use (<c>NormalizeKey</c>, <c>:385-390</c>).
    /// </summary>
    [Fact]
    public void MetaCommand_AnUnusableKeyOrOpIsNotAnsweredAtAll()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        Assert.Null(meta.Handle("set", null, Json("1")));
        Assert.Null(meta.Handle("set", "   ", Json("1")));
        Assert.Null(meta.Handle("set", new string('k', ArcademyMetaStore.MaxKeyLength + 1), Json("1")));
        Assert.Null(meta.Handle("delete", "games", Json("1")));
        Assert.Null(meta.Handle(null, "games", Json("1")));
        Assert.Contains(_log, l => l.Contains("unknown op 'delete'"));

        // Exactly at the cap is fine, and the key is TRIMMED — " games " and "games" are one key.
        var atCap = new string('k', ArcademyMetaStore.MaxKeyLength);
        Assert.NotNull(meta.Handle("set", atCap, Json("1")));
        meta.Handle("set", " games ", Json("""{"tier":1}"""));
        Assert.Equal(1, meta.Get("games")!["tier"]!.GetValue<int>());
        Assert.False(meta.Snapshot().ContainsKey(" games "));
    }

    /// <summary>
    /// One key's value is capped at <see cref="ArcademyMetaStore.MaxValueChars"/> (<c>:335-352</c>).
    /// Upstream's reason is precise: nothing page-side bounds what a game stores, and one runaway
    /// value would push the blob past the load cap so the NEXT launch reads it as corrupt.
    /// "Refusing one write is recoverable; losing the file is not."
    /// </summary>
    [Fact]
    public void MetaCommand_RefusesAnOversizedValueAndAnswersWithTheSurvivingOne()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        meta.Handle("set", "games", Json("""{"the-deep-end":{"tier":2}}"""));

        var oversized = Json("\"" + new string('x', ArcademyMetaStore.MaxValueChars) + "\"");
        var reply = meta.Handle("set", "games", oversized);

        Assert.NotNull(reply);
        Assert.Equal(2, reply!.Value.Value!["the-deep-end"]!["tier"]!.GetValue<int>());
        Assert.Contains(_log, l => l.Contains("cap 32768") && l.Contains("refused"));

        // Just under the cap still lands, so the fact is measuring the cap and not the mechanism.
        var justUnder = Json("\"" + new string('x', ArcademyMetaStore.MaxValueChars - 3) + "\"");
        Assert.Equal(ArcademyMetaStore.MaxValueChars - 3, meta.Handle("set", "games", justUnder)!
            .Value.Value!.GetValue<string>().Length);
    }

    /// <summary>
    /// The top-level key budget turns away only NEW keys — "an update must never be refused because
    /// the blob is wide" (<c>AcceptNewKey</c>, <c>:354-361</c>). The five host-owned regions count
    /// against the cap, so the page's budget is 59; see
    /// <see cref="ArcademyMetaStore.MaxTopLevelKeys"/> for the stated divergence.
    /// </summary>
    [Fact]
    public void MetaCommand_TheKeyBudgetTurnsAwayNewKeysAndNeverUpdates()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        var pageBudget = ArcademyMetaStore.MaxTopLevelKeys - ArcademyMetaDocument.HostOwnedKeys.Count;

        for (var i = 0; i < pageBudget; i++)
        {
            meta.Handle("set", $"region{i:00}", Json(i.ToString()));
        }

        Assert.Equal(pageBudget,
            meta.Snapshot().Count(k => k.Key.StartsWith("region", StringComparison.Ordinal)));

        // One more NEW key is dropped…
        meta.Handle("set", "oneTooMany", Json("1"));
        Assert.False(meta.Snapshot().ContainsKey("oneTooMany"));
        Assert.Contains(_log, l => l.Contains("new key 'oneTooMany' dropped"));

        // …and an EXISTING key still updates at exactly the same fullness.
        Assert.Equal(77, meta.Handle("set", "region00", Json("77"))!.Value.Value!.GetValue<int>());
    }

    /// <summary>
    /// <c>days</c> is the only unbounded-by-construction region — one row per calendar day, forever
    /// — so every write that could have touched it trims to the newest
    /// <see cref="ArcademyMetaStore.DayHistory"/> rows (<c>TrimDays</c>, <c>:363-383</c>).
    /// <c>yyyy-MM-dd</c> ordinal order IS chronological order, which is what makes the trim correct
    /// rather than arbitrary.
    /// </summary>
    [Fact]
    public void Days_TrimToTheNewestRowsAndTheOldestOnesGoFirst()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        var rows = new JsonObject();
        for (var day = 1; day <= 60; day++)
        {
            rows[$"2026-01-{day:00}"] = new JsonObject { ["complete"] = true };
        }

        meta.Handle("set", "days", Json(rows.ToJsonString()));

        var kept = meta.Get("days")!.AsObject();
        Assert.Equal(ArcademyMetaStore.DayHistory, kept.Count);
        Assert.False(kept.ContainsKey("2026-01-20"));   // the 20th-oldest of 60 — trimmed
        Assert.True(kept.ContainsKey("2026-01-21"));    // the oldest of the 40 kept
        Assert.True(kept.ContainsKey("2026-01-60"));    // the newest
    }

    // ==================================================================================
    // Slice 3 — the over-cap salvage ladder.
    // ==================================================================================

    /// <summary>
    /// The salvage ladder, all three rungs, in upstream's order (<c>Salvage</c>, <c>:492-524</c>):
    /// shed the <c>days</c> transcript, then the oldest <c>games</c> entries one at a time, and if
    /// it is STILL over, keep only what the host owns — "they are tiny and they are the ones a
    /// player would actually mourn". <b>The host-owned regions survive every rung.</b>
    /// </summary>
    [Fact]
    public void Salvage_ShedsDaysThenTheOldestGamesAndNeverTheHostOwnedKeys()
    {
        // Rung 1: a huge `days`, and shedding it alone is enough.
        var one = LoadOversized(new JsonObject
        {
            ["days"] = Bulk("2026-01-", 60, 12000),
            ["games"] = new JsonObject { ["the-deep-end"] = new JsonObject { ["tier"] = 4 } },
        });
        Assert.Empty(one.Get("days")!.AsObject());
        Assert.Equal(4, one.Get("games")!["the-deep-end"]!["tier"]!.GetValue<int>());
        Assert.Equal(7, one.Snapshot()[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        Assert.Contains(_log, l => l.Contains("salvage dropped the 'days' transcript"));

        // Rung 2: no days to shed, so the OLDEST games go, one at a time, until it fits.
        var two = LoadOversized(new JsonObject { ["games"] = Bulk("game-", 50, 12000) });
        var games = two.Get("games")!.AsObject();
        Assert.InRange(games.Count, 1, 49);
        Assert.False(games.ContainsKey("game-00"));           // oldest by ordinal key order
        Assert.True(games.ContainsKey("game-49"));            // newest survives
        Assert.Equal(7, two.Snapshot()[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        Assert.Contains(_log, l => l.Contains("salvage dropped games['game-00']"));

        // Rung 3: the bulk is in neither region, so everything page-owned goes and the host's
        // streak, perfect count and XP ledger are what is left.
        var three = LoadOversized(new JsonObject { ["someRunawayRegion"] = Bulk("k", 50, 12000) });
        var blob = three.Snapshot();
        Assert.False(blob.ContainsKey("someRunawayRegion"));
        Assert.Equal(7, blob[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        Assert.Equal(2, blob[ArcademyMetaDocument.PerfectKey]!.GetValue<int>());
        Assert.Equal("the-deep-end", blob[ArcademyMetaDocument.XpPaidKey]!["2026-08-20"]!
            .AsArray()[0]!.GetValue<string>());
        Assert.Contains(_log, l => l.Contains("salvage kept the host-owned keys only"));
    }

    /// <summary>
    /// Salvage is DESTRUCTIVE, so the original bytes are copied to a <c>.corrupt</c> sidecar first
    /// (<c>PreserveCorrupt</c>, <c>:526-533</c>): "the point is that the bytes still exist to look
    /// at, not an archive". Without this a player's whole transcript is gone with nothing to
    /// inspect.
    /// </summary>
    [Fact]
    public void Salvage_PreservesTheOriginalBytesBeforeSheddingAnything()
    {
        var path = _dir.Path(ArcademyMetaDocument.FileName);
        var original = new JsonObject
        {
            [PersistenceStore<ArcademyMetaDocument>.SchemaVersionKey] = 1,
            [ArcademyMetaDocument.StreakKey] = 7,
            ["days"] = Bulk("2026-01-", 60, 12000),
        }.ToJsonString();
        File.WriteAllText(path, original);

        var (_, meta) = NewMeta(path);

        Assert.Empty(meta.Get("days")!.AsObject());
        Assert.Equal(original, File.ReadAllText(path + ".corrupt"));
    }

    // ==================================================================================
    // Slice 3 — attendance and the XP ledger (the two host-owned writers).
    // ==================================================================================

    /// <summary>
    /// The streak rule (<c>RecordAttendance</c>, <c>:195-236</c>): a gap of exactly one day
    /// continues it, a larger gap restarts it at 1, a day already credited leaves it alone, and an
    /// unparseable stored date restarts rather than extending from garbage
    /// (<c>IsPreviousDay</c>, <c>:392-403</c>).
    /// </summary>
    [Fact]
    public void Attendance_ContinuesOnConsecutiveLocalDaysAndRestartsAfterAGap()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        Assert.Equal(1, meta.RecordAttendance("2026-08-20", "the-deep-end").Streak);
        Assert.Equal(2, meta.RecordAttendance("2026-08-21", "the-deep-end").Streak);
        // The same day again does not advance it.
        Assert.Equal(2, meta.RecordAttendance("2026-08-21", "impulse-control").Streak);
        // A two-day gap restarts.
        Assert.Equal(1, meta.RecordAttendance("2026-08-23", "the-deep-end").Streak);
        // …and the new day's class list started over with it.
        Assert.Equal(1, meta.RecordAttendance("2026-08-23", "the-deep-end").ClassesToday);

        Assert.True(ArcademyMetaStore.IsPreviousDay("2026-08-20", "2026-08-21"));
        Assert.True(ArcademyMetaStore.IsPreviousDay("2026-02-28", "2026-03-01"));   // 2026 is not a leap year
        Assert.False(ArcademyMetaStore.IsPreviousDay("2026-08-21", "2026-08-21"));
        Assert.False(ArcademyMetaStore.IsPreviousDay("2026-08-22", "2026-08-21"));  // never backwards
        Assert.False(ArcademyMetaStore.IsPreviousDay("yesterday", "2026-08-21"));
        Assert.False(ArcademyMetaStore.IsPreviousDay(null, "2026-08-21"));
    }

    /// <summary>
    /// Attendance is idempotent per game key, and the perfect-attendance credit fires on the
    /// TRANSITION into a full day, "so a fourth completion (a retake) can never award it twice"
    /// (<c>:222-231</c>).
    /// </summary>
    [Fact]
    public void Attendance_IsIdempotentPerGameAndPerfectFiresExactlyOnceADay()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        Assert.Equal((1, 0, 1), meta.RecordAttendance("2026-08-20", "the-deep-end"));
        Assert.Equal((1, 0, 1), meta.RecordAttendance("2026-08-20", "the-deep-end"));   // replay
        Assert.Equal((1, 0, 2), meta.RecordAttendance("2026-08-20", "impulse-control"));
        Assert.Equal((1, 1, 3), meta.RecordAttendance("2026-08-20", "daily-trigger")); // the third
        // A fourth distinct class, and every replay after it, cannot award perfect again.
        Assert.Equal((1, 1, 4), meta.RecordAttendance("2026-08-20", "a-fourth-class"));
        Assert.Equal((1, 1, 4), meta.RecordAttendance("2026-08-20", "the-deep-end"));

        // A blank game key still rolls the day — attendance is the thing that must not be lost —
        // but credits no class (:222).
        Assert.Equal((2, 1, 0), meta.RecordAttendance("2026-08-21", ""));

        // The next full day is the second perfect one.
        meta.RecordAttendance("2026-08-22", "the-deep-end");
        meta.RecordAttendance("2026-08-22", "impulse-control");
        Assert.Equal((3, 2, 3), meta.RecordAttendance("2026-08-22", "daily-trigger"));
    }

    /// <summary>
    /// THE FARM GUARD (<c>TryClaimXpDay</c>, <c>:238-276</c>): a class pays once per UTC day and
    /// every later run that day is a free replay. The ledger keeps
    /// <see cref="ArcademyMetaStore.XpPaidDayHistory"/> days and drops the oldest, because an older
    /// day can no longer be replayed into.
    /// </summary>
    [Fact]
    public void XpLedger_PaysOncePerGamePerUtcDayAndKeepsAShortHistory()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));

        Assert.True(meta.TryClaimXpDay("the-deep-end", "2026-08-20"));
        Assert.False(meta.TryClaimXpDay("the-deep-end", "2026-08-20"));
        // A DIFFERENT class the same day still pays, and the same class a different day pays again.
        Assert.True(meta.TryClaimXpDay("impulse-control", "2026-08-20"));
        Assert.True(meta.TryClaimXpDay("the-deep-end", "2026-08-21"));

        // Nothing to bill against pays, rather than refusing (:250).
        Assert.True(meta.TryClaimXpDay("", "2026-08-20"));
        Assert.True(meta.TryClaimXpDay("the-deep-end", null));

        var (_, fresh) = NewMeta(_dir.Path("ledger-history.json"));
        for (var day = 1; day <= 20; day++)
        {
            Assert.True(fresh.TryClaimXpDay("the-deep-end", $"2026-03-{day:00}"));
        }

        var ledger = fresh.Snapshot()[ArcademyMetaDocument.XpPaidKey]!.AsObject();
        Assert.Equal(ArcademyMetaStore.XpPaidDayHistory, ledger.Count);
        Assert.False(ledger.ContainsKey("2026-03-06"));
        Assert.True(ledger.ContainsKey("2026-03-07"));    // the oldest of the 14 kept
        Assert.True(ledger.ContainsKey("2026-03-20"));
    }

    // ==================================================================================
    // Slice 4 — the payout.
    // ==================================================================================

    /// <summary>
    /// The ONE XP table is C#-owned and every field the page sent is re-clamped
    /// (<c>OnClassEnded</c>, <c>:1367-1388</c>): tier to 1..4, an unknown grade to
    /// <see cref="ArcademyClassPayout.DefaultGrade"/>, a zen run to
    /// <see cref="ArcademyClassPayout.ZenGrade"/> whatever it claimed, the flavour bonus to
    /// <see cref="ArcademyClassPayout.FlavorXpCap"/>, and the game key to 64 chars. The page reports
    /// what happened; the host decides what it was worth.
    /// </summary>
    [Fact]
    public void Payout_TheHostOwnsTheTableAndReclampsEveryFieldThePageSent()
    {
        var at = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        // base[3] × mult[A] + flavour = 85 × 1.25 + 5.
        var graded = Compute("""{"gameKey":"the-deep-end","gradeTier":3,"grade":"A","flavorXp":5}""", at);
        Assert.Equal(111.25, graded.Xp);
        Assert.Equal(3, graded.GradeTier);
        Assert.Equal("A", graded.Grade);

        // Tier out of range clamps to the table's ends, never throws on a missing row.
        Assert.Equal(110.0, Compute("""{"gradeTier":9,"grade":"B"}""", at).Xp);
        Assert.Equal(40.0, Compute("""{"gradeTier":-4,"grade":"B"}""", at).Xp);

        // An unknown grade degrades to C (0.6); a zen run reports `pass` and pays the B row,
        // whatever grade it sent with it (DECISIONS #1).
        Assert.Equal(24.0, Compute("""{"gradeTier":1,"grade":"Z"}""", at).Xp);
        var zen = Compute("""{"gradeTier":1,"grade":"S","zen":true}""", at);
        Assert.Equal(ArcademyClassPayout.ZenGrade, zen.Grade);
        Assert.Equal(40.0, zen.Xp);

        // Grade matching is case-insensitive, upstream's own comparer.
        Assert.Equal(60.0, Compute("""{"gradeTier":2,"grade":"b"}""", at).Xp);

        // The flavour bonus is capped and floored, so a game cannot re-invent the table.
        Assert.Equal(15.0, Compute("""{"gradeTier":1,"grade":"C","flavorXp":9000}""", at).FlavorXp);
        Assert.Equal(0.0, Compute("""{"gradeTier":1,"grade":"C","flavorXp":-50}""", at).FlavorXp);

        // The game key is truncated rather than defaulted — the one field with no sane default.
        Assert.Equal(ArcademyClassPayout.MaxGameKeyChars,
            Compute($$"""{"gameKey":"{{new string('g', 200)}}"}""", at).GameKey.Length);
        Assert.Equal("the-deep-end", Compute("""{"gameKey":"  the-deep-end  "}""", at).GameKey);

        // A page that sends a numeric STRING is still understood (:1443-1444, :1457-1458).
        var stringy = Compute("""{"gradeTier":"3","grade":"A","flavorXp":"5"}""", at);
        Assert.Equal(111.25, stringy.Xp);
    }

    /// <summary>
    /// <b>THE MEASURED TRAP, AND IT IS PORTED ON PURPOSE.</b> UTC seeds content and keys the XP
    /// ledger (<c>:1379</c>); the LOCAL date rolls attendance and the streak (<c>:1406</c>). That is
    /// upstream's own regression #978, and it is deliberate at both ends — the day's three classes
    /// are globally identical, while a streak is a thing a person feels at their own midnight.
    ///
    /// <para>The observable consequence, driven here with ONE injected offset: a player two hours
    /// east of UTC who replays a class just after their local midnight is on a NEW attendance day
    /// (the streak advances) but the SAME UTC ledger day (already paid — the replay is free). A
    /// single-clock implementation fails this fact in one direction or the other: on UTC alone the
    /// streak would not advance, on local alone the replay would pay a second time.</para>
    /// </summary>
    [Fact]
    public void Payout_UtcSeedsTheLedgerButTheLocalDateRollsAttendance()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        const string frame = """{"gameKey":"the-deep-end","gradeTier":2,"grade":"B"}""";

        // Evening, UTC+02:00: both clocks agree on the 23rd.
        var evening = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.FromHours(2));
        var first = ArcademyClassPayout.Compute(Json(frame), meta, evening);
        Assert.Equal("2026-08-23", first.XpLedgerUtcDay);
        Assert.Equal("2026-08-23", first.AttendanceLocalDate);
        Assert.Equal(60.0, first.Xp);
        Assert.False(first.Retake);
        Assert.Equal(1, first.Streak);

        // 01:00 the next morning, same offset: the LOCAL date has rolled to the 24th, the UTC date
        // has NOT — it is still 23:00 on the 23rd in UTC.
        var afterMidnight = new DateTimeOffset(2026, 8, 24, 1, 0, 0, TimeSpan.FromHours(2));
        var day = ArcademyClassPayout.ArcademyClassDay.From(afterMidnight);
        Assert.True(day.ClocksDisagree);
        Assert.Equal("2026-08-23", day.UtcSeedDay);
        Assert.Equal("2026-08-24", day.LocalAttendanceDate);

        var second = ArcademyClassPayout.Compute(Json(frame), meta, afterMidnight);
        // The ledger says this class was already paid for on the 23rd UTC — a free replay…
        Assert.True(second.Retake);
        Assert.Equal(0.0, second.Xp);
        Assert.Equal("2026-08-23", second.XpLedgerUtcDay);
        // …and the streak still advances, because it is a genuinely new local day.
        Assert.Equal("2026-08-24", second.AttendanceLocalDate);
        Assert.Equal(2, second.Streak);
        Assert.Equal(1, second.ClassesToday);

        // Both halves are on the blob, under the names upstream gives them.
        var blob = meta.Snapshot();
        Assert.Equal("2026-08-24", blob[ArcademyMetaDocument.AttendanceKey]!.GetValue<string>());
        // ONE ledger day for two classes on two different local dates — the split, on disk.
        Assert.Equal("2026-08-23", Assert.Single(blob[ArcademyMetaDocument.XpPaidKey]!.AsObject()).Key);
    }

    /// <summary>
    /// A malformed frame still credits attendance (<c>:1359-1366</c>) — upstream's own comment
    /// records that one throwing field read used to abort the handler BEFORE the attendance write,
    /// so "one malformed field cost the player the day's attendance and their streak". And a
    /// missing or unparseable <c>dayUtc</c> is RE-DERIVED from the host clock rather than trusted,
    /// "otherwise dropping the field would be the bypass" (<c>:1376-1385</c>).
    /// </summary>
    [Fact]
    public void Payout_AGarbledFrameStillCreditsAttendanceAndCannotBypassTheLedger()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        var at = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        var garbled = ArcademyClassPayout.Compute(
            Json("""
                {"gameKey":123,"gradeTier":{"a":1},"grade":[1],"zen":"yes","flavorXp":"NaN","dayUtc":"not-a-day"}
                """),
            meta, at);

        Assert.Equal(1, garbled.GradeTier);                       // fallback, then clamped
        Assert.Equal(ArcademyClassPayout.DefaultGrade, garbled.Grade);
        Assert.False(garbled.Retake);
        Assert.Equal(0.0, garbled.FlavorXp);                      // "NaN" parses, is not finite, degrades
        Assert.Equal(24.0, garbled.Xp);                           // 40 × 0.6
        Assert.Equal("", garbled.GameKey);                        // a non-string key is no key
        Assert.Equal("2026-08-20", garbled.XpLedgerUtcDay);       // re-derived from the clock
        // ATTENDANCE SURVIVED the garbage: the day rolled and the streak was credited.
        Assert.Equal("2026-08-20", garbled.AttendanceLocalDate);
        Assert.Equal(1, garbled.Streak);
        Assert.Equal("2026-08-20", meta.Snapshot()[ArcademyMetaDocument.AttendanceKey]!.GetValue<string>());

        // A page claiming a day it is not on cannot mint a second payout for a class it already
        // played: the ledger is keyed by whatever day it names, so naming a fake one is answered by
        // the fake one being billed.
        var real = ArcademyClassPayout.Compute(
            Json("""{"gameKey":"the-deep-end","gradeTier":1,"grade":"B","dayUtc":"2026-08-20"}"""), meta, at);
        var replay = ArcademyClassPayout.Compute(
            Json("""{"gameKey":"the-deep-end","gradeTier":1,"grade":"B"}"""), meta, at);
        Assert.False(real.Retake);
        Assert.True(replay.Retake);
        Assert.Equal(0.0, replay.Xp);
    }

    /// <summary>
    /// <b>THE PAYOUT LANDS NOWHERE, AND THE TYPE SAYS SO.</b> This build has no XP store, level or
    /// rank for a computed payout to move, so <see cref="ArcademyClassPayout.ArcademyPayout.Xp"/> is
    /// computed and never granted, the frame's <c>levelUp</c> is a constant false rather than a
    /// before/after comparison, and the seam is
    /// <see cref="ArcademySession.PayoutComputed"/> — the same honest shape
    /// <c>IntakeDraft.XpComputed</c> already takes for the same reason.
    /// </summary>
    [Fact]
    public void Payout_IsComputedNeverBanked_AndTheSeamIsTheOnlyPlaceItGoes()
    {
        var payout = Compute("""{"gameKey":"the-deep-end","gradeTier":4,"grade":"S"}""",
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(165.0, payout.Xp);                       // 110 × 1.5 — a real number…
        Assert.False(payout.XpBanked);                        // …that nothing banks.
        Assert.Equal(ArcademyClassPayout.NoXpStoreReason, payout.XpBankedReason);

        var frame = JsonDocument.Parse(
            ArcademyProtocol.SerializeForPage(ArcademyProtocol.BuildPayoutResult(payout))).RootElement;
        Assert.Equal("payout-result", frame.GetProperty("type").GetString());
        Assert.Equal(165.0, frame.GetProperty("xp").GetDouble());
        Assert.False(frame.GetProperty("levelUp").GetBoolean());
        Assert.False(frame.GetProperty("retake").GetBoolean());

        // Even the largest payout the table can produce is unbanked: there is no branch that banks.
        Assert.False(Compute("""{"gradeTier":4,"grade":"S","flavorXp":15}""",
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)).XpBanked);
    }

    // ==================================================================================
    // Slices 3 + 4 through the session — the frames, and their order.
    // ==================================================================================

    /// <summary>
    /// The session routes both new families (<c>OnPageMessage</c>, <c>:463-480</c>): a
    /// <c>meta-command</c> is answered with the per-key reply, and a finished class pushes the
    /// WHOLE BLOB before <c>payout-result</c> (<c>:1408</c> then <c>:1410</c>) — the page folds the
    /// payout's numbers over its cache afterwards, so the streak chip is right the instant a class
    /// ends (<c>arcademy/core/store.js:236-252</c>).
    /// </summary>
    [Fact]
    public void Session_AnswersAMetaCommandAndPushesTheBlobBeforeThePayout()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        var session = NewSession(meta);
        session.Clock = () => new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        session.Handle("""{"type":"meta-command","op":"set","key":"games","value":{"the-deep-end":{"tier":2}}}""");
        Assert.Single(_posted);
        Assert.Equal("meta", Frame(0).GetProperty("type").GetString());
        Assert.Equal("games", Frame(0).GetProperty("key").GetString());
        Assert.Equal(2, Frame(0).GetProperty("value").GetProperty("the-deep-end").GetProperty("tier").GetInt32());

        _posted.Clear();
        session.Handle("""{"type":"class-started","gameKey":"the-deep-end","gradeTier":2}""");
        Assert.True(session.ClassActive);
        Assert.Empty(_posted);

        session.Handle("""
            {"type":"class-ended","gameKey":"the-deep-end","gradeTier":2,"grade":"A","flavorXp":3,"dayUtc":"2026-08-20"}
            """);
        Assert.False(session.ClassActive);
        Assert.Equal(2, _posted.Count);

        // The whole-blob push FIRST, carrying the rev…
        Assert.Equal("meta", Frame(0).GetProperty("type").GetString());
        Assert.True(Frame(0).GetProperty("rev").GetInt32() > 0);
        Assert.Equal(1, Frame(0).GetProperty("state").GetProperty("streak").GetInt32());
        // …then payout-result.
        Assert.Equal("payout-result", Frame(1).GetProperty("type").GetString());
        Assert.Equal(78.0, Frame(1).GetProperty("xp").GetDouble());   // 60 × 1.25 + 3
        Assert.Equal(1, Frame(1).GetProperty("streak").GetInt32());
        Assert.Equal(1, Frame(1).GetProperty("classesToday").GetInt32());
    }

    /// <summary>
    /// Leaving a class with Esc ENDS no class (<c>:474-480</c>): nothing is graded, paid, credited
    /// or answered. Without this the bracket never closes and a walk-out would pay like a
    /// completion.
    /// </summary>
    [Fact]
    public void Session_LeavingAClassPaysNothingAndCreditsNothing()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        var session = NewSession(meta);

        session.Handle("""{"type":"class-started","gameKey":"the-deep-end","gradeTier":2}""");
        session.Handle("""{"type":"class-left","gameKey":"the-deep-end"}""");

        Assert.False(session.ClassActive);
        Assert.Empty(_posted);
        Assert.Equal(0, meta.Snapshot()[ArcademyMetaDocument.StreakKey]!.GetValue<int>());
        Assert.Empty(meta.Snapshot()[ArcademyMetaDocument.XpPaidKey]!.AsObject());
        Assert.Contains(_log, l => l.Contains("class left"));
    }

    /// <summary>
    /// The XP seam fires with the payout the page was told about, and a throwing subscriber is
    /// isolated PER HANDLER — upstream's own posture around <c>AddXP</c> (<c>:1396-1399</c>),
    /// because a payout must not take the report card down with it, and with an event there is more
    /// than one call to protect.
    /// </summary>
    [Fact]
    public void Session_TheXpSeamReceivesThePayoutAndAThrowingHandlerIsIsolated()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        var session = NewSession(meta);
        session.Clock = () => new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        var seen = new List<ArcademyClassPayout.ArcademyPayout>();
        session.PayoutComputed += _ => throw new InvalidOperationException("an XP store that fell over");
        session.PayoutComputed += seen.Add;

        session.Handle("""{"type":"class-ended","gameKey":"the-deep-end","gradeTier":1,"grade":"B"}""");

        // The page was answered even though a subscriber threw.
        Assert.Equal(2, _posted.Count);
        Assert.Equal("payout-result", Frame(1).GetProperty("type").GetString());
        Assert.Contains(_log, l => l.Contains("payout handler failed, isolated"));
        // The SECOND subscriber still got its payout — a bare Invoke would have stopped at the
        // first throw and this list would be empty.
        Assert.Equal(40.0, Assert.Single(seen).Xp);      // 40 × 1.0
    }

    /// <summary>
    /// The <c>init</c> projection carries the meta blob once a store is attached
    /// (<c>:568</c>), and the empty object when it is not — upstream's <c>?? new JObject()</c> on
    /// the same line.
    /// </summary>
    [Fact]
    public void Init_CarriesTheMetaSnapshotOnlyWhenThereIsAStore()
    {
        var (_, meta) = NewMeta(_dir.Path(ArcademyMetaDocument.FileName));
        meta.RecordAttendance("2026-08-20", "the-deep-end");
        meta.Set("games", Json("""{"the-deep-end":{"tier":3}}"""));

        NewSession(meta).Ready();
        var withStore = Frame(0).GetProperty("meta");
        Assert.Equal(1, withStore.GetProperty("streak").GetInt32());
        Assert.Equal("2026-08-20", withStore.GetProperty("lastAttendanceLocalDate").GetString());
        Assert.Equal(3, withStore.GetProperty("games").GetProperty("the-deep-end").GetProperty("tier").GetInt32());

        _posted.Clear();
        NewSession(meta: null).Ready();
        Assert.Empty(Frame(0).GetProperty("meta").EnumerateObject());
    }

    // ==================================================================================
    // Harness.
    // ==================================================================================

    private (PersistenceStore<ArcademyMetaDocument> Store, ArcademyMetaStore Meta) NewMeta(string path)
    {
        var store = new PersistenceStore<ArcademyMetaDocument>(
            new OperationRegistry().OwnerFor("ArcademyMetaTests"),
            new SinkAdapter(_log),
            path,
            ArcademyMetaDocument.CurrentSchemaVersion);
        _stores.Add(store);
        var meta = new ArcademyMetaStore(store, path, new SinkAdapter(_log));
        // Start loads on the calling thread and hands back nothing to wait on (PersistenceStore's
        // own remarks, pinned by PersistenceStoreTests).
        meta.Start(TestContext.Current.CancellationToken);
        return (store, meta);
    }

    private ArcademySession NewSession(ArcademyMetaStore? meta)
    {
        var settings = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor("ArcademyMetaTests-settings"),
            new SinkAdapter(_log),
            _dir.Path(Guid.NewGuid().ToString("N") + ".json"),
            ArcademySettingsDocument.CurrentSchemaVersion);
        _ = settings.StartAsync(TestContext.Current.CancellationToken);
        return new ArcademySession(settings, new ArcademyAppFacts(), frame => _posted.Add(frame),
            new SinkAdapter(_log), meta);
    }

    /// <summary>Write an over-cap blob, load it (which salvages), and hand back the store. The
    /// host-owned regions are seeded so every rung can be checked for leaving them alone.</summary>
    private ArcademyMetaStore LoadOversized(JsonObject pageRegions)
    {
        var document = new JsonObject
        {
            [PersistenceStore<ArcademyMetaDocument>.SchemaVersionKey] = 1,
            [ArcademyMetaDocument.StreakKey] = 7,
            [ArcademyMetaDocument.PerfectKey] = 2,
            [ArcademyMetaDocument.XpPaidKey] = new JsonObject
            {
                ["2026-08-20"] = new JsonArray("the-deep-end"),
            },
        };
        foreach (var (key, value) in pageRegions)
        {
            document[key] = value?.DeepClone();
        }

        var path = _dir.Path("oversized-" + Guid.NewGuid().ToString("N") + ".json");
        var text = document.ToJsonString();
        Assert.True(text.Length > ArcademyMetaStore.MaxBlobChars,
            $"the fixture is only {text.Length} chars, so it would never reach the salvage ladder");
        File.WriteAllText(path, text);
        return NewMeta(path).Meta;
    }

    /// <summary>An object of <paramref name="count"/> ordinally-named keys, each holding
    /// <paramref name="pad"/> chars, for pushing a blob over the load cap.</summary>
    private static JsonObject Bulk(string prefix, int count, int pad)
    {
        var bulk = new JsonObject();
        for (var i = 0; i < count; i++)
        {
            bulk[$"{prefix}{i:00}"] = new string('x', pad);
        }

        return bulk;
    }

    private ArcademyClassPayout.ArcademyPayout Compute(string frameJson, DateTimeOffset now) =>
        ArcademyClassPayout.Compute(Json(frameJson), meta: null, now);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private JsonElement Frame(int index) =>
        JsonDocument.Parse(ArcademyProtocol.SerializeForPage(_posted[index])).RootElement.Clone();

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-arcademy-meta-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue. A store's chained
                // writer may still be mid-flight, which is a Recoverable outcome there and a
                // leftover file here.
            }
        }
    }
}
