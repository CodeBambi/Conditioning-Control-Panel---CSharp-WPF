using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE FUSE's wire and its cache (CONTRACT-FUSE-0816 §1.3, §2.6).
///
/// <para>Two claims are pinned here, and both are the kind that fail silently in production:</para>
/// <list type="number">
/// <item>the ISO timestamp survives the sync response's parse boundary INTACT (the repo's standing
/// Newtonsoft date-coercion trap points straight at this one field);</item>
/// <item>an ABSENT block on a readable response clears the cache — the server-side kill switch —
/// while an UNREADABLE response changes nothing at all.</item>
/// </list>
/// </summary>
public class DescentFuseWireTests
{
    private const string Iso = "2026-09-01T00:00:00Z";

    private static string SyncBody(string? countdownBlock) =>
        countdownBlock is null
            ? """{"success":true,"xp":1200,"level":42}"""
            : $$"""{"success":true,"xp":1200,"level":42,"descent_countdown":{{countdownBlock}}}""";

    // ------------------------------------------------------ reading the block

    /// <summary>
    /// THE COERCION TRAP, pinned. Newtonsoft's default DateParseHandling rewrites an ISO-8601-shaped
    /// STRING into a date token before any string reader sees it, and the round-trip back out is a
    /// reformatted instant — or, for a strict <c>Type == String</c> read, nothing at all. Reading
    /// through DescentReader.ParseWire is what stops that. If this test ever fails with
    /// <c>ceremonyAt == null</c>, someone swapped the parse boundary for JObject.Parse and the kill
    /// switch is now firing on every single sync.
    /// </summary>
    [Fact]
    public void CeremonyAt_SurvivesTheParseBoundaryVerbatim()
    {
        var ok = DescentCountdownService.TryReadCeremonyAt(
            SyncBody($$"""{"ceremony_at":"{{Iso}}"}"""), out var ceremonyAt);

        Assert.True(ok);
        Assert.Equal(Iso, ceremonyAt);
    }

    /// <summary>
    /// THE SHAPE THE SERVER ACTUALLY SENDS (server lane, PR #44): always normalized to UTC with
    /// milliseconds, even when the operator configured an offset zone. This is the only form that
    /// will ever arrive in production, so it gets its own test rather than riding on the general
    /// one — and the milliseconds are exactly the part a sloppier parse would round away.
    /// </summary>
    [Fact]
    public void ProductionShape_UtcWithMilliseconds_ParsesToTheRightInstant()
    {
        const string wire = "2026-09-01T00:00:00.000Z";

        var ok = DescentCountdownService.TryReadCeremonyAt(
            SyncBody($$"""{"ceremony_at":"{{wire}}"}"""), out var ceremonyAt);

        Assert.True(ok);
        Assert.Equal(wire, ceremonyAt);   // cached verbatim, not re-rendered

        var parsed = DescentCountdownService.ParseCeremonyAt(ceremonyAt);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    /// <summary>Sub-second precision is not silently discarded on the way to an instant.</summary>
    [Fact]
    public void Milliseconds_SurviveIntoTheInstant()
    {
        var parsed = DescentCountdownService.ParseCeremonyAt("2026-09-01T00:00:00.250Z");

        Assert.NotNull(parsed);
        Assert.Equal(250, parsed!.Value.Millisecond);
    }

    /// <summary>An offset (rather than Z) is also passed through byte-for-byte — we cache the
    /// wire, we do not re-render it.</summary>
    [Fact]
    public void CeremonyAt_WithOffset_IsPassedThroughUnchanged()
    {
        const string offset = "2026-09-01T02:00:00+02:00";

        var ok = DescentCountdownService.TryReadCeremonyAt(
            SyncBody($$"""{"ceremony_at":"{{offset}}"}"""), out var ceremonyAt);

        Assert.True(ok);
        Assert.Equal(offset, ceremonyAt);
    }

    // ------------------------------------------------------ the kill switch

    /// <summary>
    /// NO BLOCK ON A READABLE RESPONSE = CLEAR. This is the whole server-side kill switch: the
    /// owner unsets DESCENT_CEREMONY_AT, the block stops arriving, and every desktop tears its
    /// surfaces down on the next sync without a patch. A reading that treated absence as "leave it
    /// alone" would strand the countdown on every install that ever saw it once.
    /// </summary>
    [Fact]
    public void AbsentBlock_OnAReadableResponse_ClearsTheCache()
    {
        var ok = DescentCountdownService.TryReadCeremonyAt(SyncBody(null), out var ceremonyAt);

        Assert.True(ok);            // the response was readable, so the answer is authoritative
        Assert.Null(ceremonyAt);    // and the answer is "no fuse"
    }

    /// <summary>
    /// An explicit null, or a block with no ceremony_at, means the same thing as absence.
    ///
    /// <para>The server never actually sends the block present-with-null on the SYNC response — it
    /// omits the key (server lane, PR #44) — so these are defensive. They are pinned anyway to
    /// prove there is no third behaviour hiding here: every unusable shape lands on "clear", and a
    /// future branch that treated explicit null as "leave it alone" would break the kill switch on
    /// exactly the payloads nobody tests by hand.</para>
    /// </summary>
    [Theory]
    [InlineData("""{"ceremony_at":null}""")]
    [InlineData("""{}""")]
    [InlineData("""{"vigil":12}""")]
    public void NullOrEmptyBlock_AlsoClears(string block)
    {
        var ok = DescentCountdownService.TryReadCeremonyAt(SyncBody(block), out var ceremonyAt);

        Assert.True(ok);
        Assert.Null(ceremonyAt);
    }

    /// <summary>A wrong-typed or blank value is not a timestamp, and is not trusted into the cache.</summary>
    [Theory]
    [InlineData("""{"ceremony_at":1756684800}""")]
    [InlineData("""{"ceremony_at":true}""")]
    [InlineData("""{"ceremony_at":"   "}""")]
    [InlineData("""{"ceremony_at":{"at":"2026-09-01T00:00:00Z"}}""")]
    public void WrongShapedValue_IsTreatedAsAbsent(string block)
    {
        var ok = DescentCountdownService.TryReadCeremonyAt(SyncBody(block), out var ceremonyAt);

        Assert.True(ok);
        Assert.Null(ceremonyAt);
    }

    /// <summary>
    /// AN UNREADABLE PAYLOAD CHANGES NOTHING. A truncated or garbled response tells us nothing
    /// about the owner's intent, and inferring the kill switch from a bad transport is the one
    /// failure mode that would silently un-ship the feature mid-flight.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"success":true,"descent_countdown":""")]
    [InlineData("""[{"success":true}]""")]
    public void UnreadablePayload_IsNotAKillSwitch(string? body)
    {
        Assert.False(DescentCountdownService.TryReadCeremonyAt(body, out var ceremonyAt));
        Assert.Null(ceremonyAt);
    }

    // ------------------------------------------------------ parsing the cache

    /// <summary>
    /// The cached string becomes an instant exactly once, here. Z, an offset and a bare
    /// (zone-less) value must all land on the same UTC moment — the field's name says UTC, and
    /// guessing local for the bare form would shift the whole countdown by the user's offset.
    /// </summary>
    [Theory]
    [InlineData("2026-09-01T00:00:00Z")]
    [InlineData("2026-09-01T02:00:00+02:00")]
    [InlineData("2026-09-01T00:00:00")]
    [InlineData("2026-08-31T20:00:00-04:00")]
    public void ParseCeremonyAt_NormalizesToTheSameUtcInstant(string iso)
    {
        var parsed = DescentCountdownService.ParseCeremonyAt(iso);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), parsed.Value);
    }

    /// <summary>Garbage in the cache is a dark fuse, not a crash and not an epoch date.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("soon")]
    [InlineData("2026-13-45T99:99:99Z")]
    public void ParseCeremonyAt_RejectsUnusableValues(string? iso)
    {
        Assert.Null(DescentCountdownService.ParseCeremonyAt(iso));
        Assert.Equal(DescentFusePhase.Dark,
            DescentCountdownService.PhaseFor(DescentCountdownService.ParseCeremonyAt(iso), DateTime.UtcNow));
    }

    // ------------------------------------------------------ settings round-trip

    /// <summary>
    /// The four settings survive a save/load cycle with NON-DEFAULT values — the real loader
    /// swallows deserialization faults silently, so "it did not throw" proves nothing and only a
    /// value check catches a lost setter or a renamed JsonProperty.
    /// </summary>
    [Fact]
    public void FuseSettings_RoundTrip()
    {
        var settings = new AppSettings
        {
            DescentCeremonyAtUtc = Iso,
            DescentLastNightWitnessed = true,
            DescentCatchUpCrackPlayed = true,
            DescentCountdownAudio = false,
        };

        var json = JsonConvert.SerializeObject(settings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json, new JsonSerializerSettings
        {
            // Mirrors Services/Settings/SettingsService.Load, not a friendlier configuration.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        });

        Assert.NotNull(restored);
        Assert.Equal(Iso, restored!.DescentCeremonyAtUtc);
        Assert.True(restored.DescentLastNightWitnessed);
        Assert.True(restored.DescentCatchUpCrackPlayed);
        Assert.False(restored.DescentCountdownAudio);
    }

    /// <summary>
    /// THE TIMESTAMP IS CACHED AS A STRING, and it must round-trip through settings BYTE-IDENTICAL.
    /// Storing it as a DateTime would bake this client's parse into the file and let a re-read
    /// disagree with what the server actually said; this is that decision, pinned.
    /// </summary>
    [Fact]
    public void CachedTimestamp_RoundTripsVerbatim()
    {
        const string offset = "2026-09-01T02:00:00+02:00";
        var settings = new AppSettings { DescentCeremonyAtUtc = offset };

        var restored = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(settings));

        Assert.Equal(offset, restored!.DescentCeremonyAtUtc);
    }

    /// <summary>
    /// A FRESH INSTALL HAS NO FUSE. Defaults are the dormancy claim in settings form: no timestamp
    /// means no timer, no surface and no request, on every install in the world today. The audio
    /// gate defaults ON because the hook it gates does not ship an asset — it is a switch that
    /// exists so the hook can be turned off without a patch, not a knob anyone is asked about.
    /// </summary>
    [Fact]
    public void FuseDefaults_AreDark()
    {
        var fresh = new AppSettings();

        Assert.Null(fresh.DescentCeremonyAtUtc);
        Assert.False(fresh.DescentLastNightWitnessed);
        Assert.False(fresh.DescentCatchUpCrackPlayed);
        Assert.True(fresh.DescentCountdownAudio);

        Assert.Equal(DescentFusePhase.Dark,
            DescentCountdownService.PhaseFor(
                DescentCountdownService.ParseCeremonyAt(fresh.DescentCeremonyAtUtc), DateTime.UtcNow));
    }

    /// <summary>
    /// A settings file written by a build that predates the fuse still loads, and loads DARK. The
    /// four properties are additive; nothing about an older file may resurrect a countdown.
    /// </summary>
    [Fact]
    public void PreFuseSettingsFile_LoadsDark()
    {
        const string preFuse = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.8.0",
          "PlayerLevel": 42,
          "DescentEpoch": 0,
          "DescentMigrationCompleted": false
        }
        """;

        var restored = JsonConvert.DeserializeObject<AppSettings>(preFuse, new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        });

        Assert.NotNull(restored);
        Assert.Null(restored!.DescentCeremonyAtUtc);
        Assert.False(restored.DescentLastNightWitnessed);
        Assert.False(restored.DescentCatchUpCrackPlayed);
        Assert.True(restored.DescentCountdownAudio);
    }
}
