using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// MEMORY LANE — the fuse's keepsake ratchet
/// (<see cref="AppSettings.DescentFuseMaxPhaseWitnessed"/>).
///
/// <para>One number, 0..7, recording the highest phase a subject actually LIVED THROUGH, so that a
/// later wave's easter eggs can tell somebody who kept the vigil apart from somebody who installed
/// the app the following week. Nothing reads it to change behaviour yet, which means the only thing
/// standing between it and a quietly wrong answer on the one night it matters is this file.</para>
///
/// <para>Two claims carry the whole feature, and both fail silently:</para>
/// <list type="number">
/// <item><b>It only ever goes up.</b> The owner moving the ceremony date forward walks the phases
/// BACKWARDS, the kill switch announces Dark, and a completed migration ends the countdown — three
/// ordinary events that must not erase a night somebody sat through.</item>
/// <item><b>Zero is earned, not attended.</b> A launch the morning after gets Zero announced at
/// startup exactly like the session that watched the crack did. If that announcement ratcheted, the
/// two would be stored identically and the keepsake would be worthless.</item>
/// </list>
///
/// <para>Everything here goes through the pure <c>WitnessRatchet</c> and a plain settings object:
/// this suite has no dispatcher and no <c>App</c>, which is what lets the truth table be complete
/// rather than representative.</para>
/// </summary>
public class DescentWitnessRatchetTests
{
    /// <summary>Far to near, the order the enum is declared in and the order a real night runs in.</summary>
    private static readonly DescentFusePhase[] AllPhases =
    {
        DescentFusePhase.Dark,
        DescentFusePhase.Whisper,
        DescentFusePhase.Clock,
        DescentFusePhase.Dimming,
        DescentFusePhase.Candle,
        DescentFusePhase.Vigil,
        DescentFusePhase.Terminal,
        DescentFusePhase.Zero,
    };

    // ================================================================
    // the truth table
    // ================================================================

    /// <summary>
    /// EVERY CELL, both sides of the away/live fork, from a fresh account. Read down the
    /// zeroPassedWhileAway=false column and this is a night lived through in order; the last row of
    /// the true column is the entire reason the parameter exists.
    /// </summary>
    [Theory]
    // ---- the away fork: the app was not running at the instant
    [InlineData(DescentFusePhase.Dark, true, 0)]
    [InlineData(DescentFusePhase.Whisper, true, 1)]
    [InlineData(DescentFusePhase.Clock, true, 2)]
    [InlineData(DescentFusePhase.Dimming, true, 3)]
    [InlineData(DescentFusePhase.Candle, true, 4)]
    [InlineData(DescentFusePhase.Vigil, true, 5)]
    [InlineData(DescentFusePhase.Terminal, true, 6)]
    [InlineData(DescentFusePhase.Zero, true, 0)]     // the morning after. Nothing was witnessed.
    // ---- the live fork: this process was watching when it happened
    [InlineData(DescentFusePhase.Dark, false, 0)]
    [InlineData(DescentFusePhase.Whisper, false, 1)]
    [InlineData(DescentFusePhase.Clock, false, 2)]
    [InlineData(DescentFusePhase.Dimming, false, 3)]
    [InlineData(DescentFusePhase.Candle, false, 4)]
    [InlineData(DescentFusePhase.Vigil, false, 5)]
    [InlineData(DescentFusePhase.Terminal, false, 6)]
    [InlineData(DescentFusePhase.Zero, false, 7)]    // they were there
    public void TruthTable_FromAFreshAccount(DescentFusePhase announced, bool away, int expected)
        => Assert.Equal(expected, DescentCountdownService.WitnessRatchet(0, announced, away));

    /// <summary>
    /// EACH OF WHISPER..TERMINAL RATCHETS TO ITSELF, from the value below it — the ordinary walk of
    /// a subject who leaves the app open. These six phases always arrive with something visible on
    /// screen, so announcing one IS witnessing it and no second condition applies.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper, 0, 1)]
    [InlineData(DescentFusePhase.Clock, 1, 2)]
    [InlineData(DescentFusePhase.Dimming, 2, 3)]
    [InlineData(DescentFusePhase.Candle, 3, 4)]
    [InlineData(DescentFusePhase.Vigil, 4, 5)]
    [InlineData(DescentFusePhase.Terminal, 5, 6)]
    public void EachVisiblePhase_RatchetsToItself(DescentFusePhase announced, int current, int expected)
    {
        Assert.Equal(expected, DescentCountdownService.WitnessRatchet(current, announced, zeroPassedWhileAway: false));
        // The away flag is about ZERO and only about zero; it must not hold the ordinary phases back
        // for the subject who launches mid-countdown on the day after a MISSED, earlier ceremony.
        Assert.Equal(expected, DescentCountdownService.WitnessRatchet(current, announced, zeroPassedWhileAway: true));
    }

    // ================================================================
    // zero: the whole point
    // ================================================================

    /// <summary>
    /// THE VIGIL-KEEPER. Live at the instant reaches 7, whatever they had before — including the
    /// subject who only opened the app inside the final minutes, because they were still there for
    /// the one thing the number is about.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void LiveZero_Reaches7(int current)
        => Assert.Equal(7, DescentCountdownService.WitnessRatchet(current, DescentFusePhase.Zero, zeroPassedWhileAway: false));

    /// <summary>
    /// THE MORNING AFTER, which is the case this whole field exists to exclude. Zero is announced at
    /// <c>Start()</c> for them exactly as it is for the live session, and it must leave the number
    /// exactly where their last real phase left it: 5 for the one who watched the Vigil and closed
    /// the app half an hour early, 0 for the one who installed the app afterwards.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void AwayZero_NeverReaches7(int current)
    {
        var after = DescentCountdownService.WitnessRatchet(current, DescentFusePhase.Zero, zeroPassedWhileAway: true);
        Assert.Equal(current, after);
        Assert.NotEqual(7, after);
    }

    /// <summary>
    /// A SUBJECT WHO ALREADY HAS 7 KEEPS IT, even on a later away-launch that re-announces Zero.
    /// The exclusion above is a refusal to RAISE, never a reason to lower.
    /// </summary>
    [Fact]
    public void AwayZero_DoesNotTakeThe7Back()
        => Assert.Equal(7, DescentCountdownService.WitnessRatchet(7, DescentFusePhase.Zero, zeroPassedWhileAway: true));

    // ================================================================
    // monotonicity
    // ================================================================

    /// <summary>
    /// DARK NEVER WRITES. It is the absence of a countdown (every install on today's server), and it
    /// is also what the kill switch and the disarm announce — a cleared server timestamp must not
    /// cost somebody the night they already sat through.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(7)]
    public void Dark_NeverWrites(int current)
    {
        Assert.Equal(current, DescentCountdownService.WitnessRatchet(current, DescentFusePhase.Dark, zeroPassedWhileAway: false));
        Assert.Equal(current, DescentCountdownService.WitnessRatchet(current, DescentFusePhase.Dark, zeroPassedWhileAway: true));
    }

    /// <summary>
    /// THE OWNER MOVES THE DATE FORWARD. A fresh timestamp a week out re-announces Whisper to
    /// somebody sitting on 5, walking the phases BACKWARDS — the single most likely way a naive
    /// "write the announced phase" would silently eat a keepsake.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper)]
    [InlineData(DescentFusePhase.Clock)]
    [InlineData(DescentFusePhase.Dimming)]
    [InlineData(DescentFusePhase.Candle)]
    public void ABackwardsPhaseJump_DoesNotLowerIt(DescentFusePhase announced)
        => Assert.Equal(5, DescentCountdownService.WitnessRatchet(5, announced, zeroPassedWhileAway: false));

    /// <summary>The equal case: re-announcing the phase already recorded changes nothing (which is
    /// what stops the service saving settings on every forced announcement).</summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper, 1)]
    [InlineData(DescentFusePhase.Vigil, 5)]
    [InlineData(DescentFusePhase.Terminal, 6)]
    public void AnEqualAnnouncement_IsANoOp(DescentFusePhase announced, int current)
        => Assert.Equal(current, DescentCountdownService.WitnessRatchet(current, announced, zeroPassedWhileAway: false));

    /// <summary>
    /// THE LAW ITSELF, exhaustively: no phase, on either side of the fork, from any stored value,
    /// ever returns less than what it was given. Every other test in this file is a named instance
    /// of this one.
    /// </summary>
    [Fact]
    public void NoAnnouncement_FromAnyValue_EverLowersIt()
    {
        for (var current = 0; current <= 7; current++)
            foreach (var phase in AllPhases)
                foreach (var away in new[] { false, true })
                {
                    var after = DescentCountdownService.WitnessRatchet(current, phase, away);
                    Assert.True(after >= current,
                        $"WitnessRatchet({current}, {phase}, away:{away}) lowered it to {after}.");
                }
    }

    /// <summary>
    /// Junk in the file does not become junk in the ratchet: a negative reads as "nothing yet"
    /// (which is a raise, not a lowering), and a value ABOVE the enum is left alone rather than
    /// clamped down to 7 — clamping would be the one thing the law forbids.
    /// </summary>
    [Fact]
    public void OutOfRangeValues_AreHandledWithoutLowering()
    {
        Assert.Equal(0, DescentCountdownService.WitnessRatchet(-4, DescentFusePhase.Dark, zeroPassedWhileAway: true));
        Assert.Equal(5, DescentCountdownService.WitnessRatchet(-4, DescentFusePhase.Vigil, zeroPassedWhileAway: false));
        Assert.Equal(42, DescentCountdownService.WitnessRatchet(42, DescentFusePhase.Vigil, zeroPassedWhileAway: false));
        Assert.Equal(42, DescentCountdownService.WitnessRatchet(42, DescentFusePhase.Zero, zeroPassedWhileAway: false));
    }

    /// <summary>A phase this build does not know about (a hand-edited file, a future enum member)
    /// is ignored rather than trusted into the number.</summary>
    [Fact]
    public void AnUnknownPhaseValue_IsIgnored()
        => Assert.Equal(3, DescentCountdownService.WitnessRatchet(3, (DescentFusePhase)99, zeroPassedWhileAway: false));

    // ================================================================
    // the timelines, walked
    // ================================================================

    /// <summary>Apply a scripted run of announcements to a settings object the way the service does:
    /// pure decision, write ONLY on an increase. Returns how many writes it took.</summary>
    private static int Walk(AppSettings settings, bool away, params DescentFusePhase[] announcements)
    {
        var writes = 0;
        foreach (var phase in announcements)
        {
            var next = DescentCountdownService.WitnessRatchet(
                settings.DescentFuseMaxPhaseWitnessed, phase, away);
            if (next == settings.DescentFuseMaxPhaseWitnessed) continue;
            settings.DescentFuseMaxPhaseWitnessed = next;
            writes++;
        }
        return writes;
    }

    /// <summary>
    /// THE VIGIL-KEEPER'S WHOLE NIGHT, announcement by announcement: seven phases, seven writes,
    /// ending at 7. Note the repeats — the service force-announces on every arm and re-announces the
    /// same phase for half an hour at a time — and that they cost nothing.
    /// </summary>
    [Fact]
    public void TheVigilKeeper_EndsAt7_AndSavesOnlyOnIncreases()
    {
        var settings = new AppSettings();

        var writes = Walk(settings, away: false,
            DescentFusePhase.Whisper, DescentFusePhase.Whisper, DescentFusePhase.Clock,
            DescentFusePhase.Clock, DescentFusePhase.Dimming, DescentFusePhase.Candle,
            DescentFusePhase.Candle, DescentFusePhase.Vigil, DescentFusePhase.Terminal,
            DescentFusePhase.Zero, DescentFusePhase.Zero);

        Assert.Equal(7, settings.DescentFuseMaxPhaseWitnessed);
        Assert.Equal(7, writes);   // one per genuinely new phase, and not one more
    }

    /// <summary>
    /// THE ONE WHO WENT TO BED HALF AN HOUR EARLY. They lived the Vigil, closed the app, and their
    /// next launch is an away-launch that announces Zero. They keep 5 — and 5 is the honest answer,
    /// because it is exactly what they saw.
    /// </summary>
    [Fact]
    public void TheEarlySleeper_KeepsTheVigil_AndNeverGetsTheZero()
    {
        var settings = new AppSettings();

        // Session one: live, up to the Vigil, then the app closes.
        Walk(settings, away: false,
            DescentFusePhase.Whisper, DescentFusePhase.Clock, DescentFusePhase.Dimming,
            DescentFusePhase.Candle, DescentFusePhase.Vigil);
        Assert.Equal(5, settings.DescentFuseMaxPhaseWitnessed);

        // Session two: launched the next morning. Start() forces one Zero announcement, and the
        // catch-up path — not the ratchet — is what owes them the condensed crack.
        var writes = Walk(settings, away: true, DescentFusePhase.Zero, DescentFusePhase.Zero);

        Assert.Equal(5, settings.DescentFuseMaxPhaseWitnessed);
        Assert.Equal(0, writes);   // no settings churn either
    }

    /// <summary>
    /// THE LATE JOINER, which is the population the gate exists to keep out: they install after the
    /// ceremony, their first launch announces Zero from an away start, and they stay at 0 forever.
    /// A later keepsake surface reading this number will not offer them a memory they never had.
    /// </summary>
    [Fact]
    public void TheLateJoiner_StaysAtZero()
    {
        var settings = new AppSettings();

        var writes = Walk(settings, away: true, DescentFusePhase.Zero, DescentFusePhase.Dark);

        Assert.Equal(0, settings.DescentFuseMaxPhaseWitnessed);
        Assert.Equal(0, writes);
    }

    /// <summary>
    /// THE KILL SWITCH, mid-vigil. The owner clears the server timestamp while somebody is sitting
    /// at Terminal: every surface tears down, Dark is announced, and the 6 stays. Then the owner
    /// re-arms a week out and Whisper is announced — still 6.
    /// </summary>
    [Fact]
    public void TheKillSwitchAndAReArm_LeaveTheRecordAlone()
    {
        var settings = new AppSettings();

        Walk(settings, away: false,
            DescentFusePhase.Whisper, DescentFusePhase.Clock, DescentFusePhase.Dimming,
            DescentFusePhase.Candle, DescentFusePhase.Vigil, DescentFusePhase.Terminal);
        Assert.Equal(6, settings.DescentFuseMaxPhaseWitnessed);

        var writes = Walk(settings, away: false,
            DescentFusePhase.Dark,                       // kill switch
            DescentFusePhase.Whisper, DescentFusePhase.Clock);   // re-armed, seven days out again
        Assert.Equal(6, settings.DescentFuseMaxPhaseWitnessed);
        Assert.Equal(0, writes);
    }

    // ================================================================
    // the settings field itself
    // ================================================================

    /// <summary>
    /// A FRESH INSTALL REMEMBERS NOTHING. 0 is "not there", and it is the value on every install in
    /// the world today, because the fuse itself is dark for all of them.
    /// </summary>
    [Fact]
    public void Default_IsZero()
        => Assert.Equal(0, new AppSettings().DescentFuseMaxPhaseWitnessed);

    /// <summary>
    /// The ratchet survives a save/load cycle with a NON-DEFAULT value, through the real loader's
    /// configuration. The loader swallows deserialization faults silently, so only a value check
    /// catches a lost setter or a renamed JsonProperty — and a keepsake that resets on restart is
    /// worse than one that never existed.
    /// </summary>
    [Fact]
    public void MaxPhaseWitnessed_RoundTrips()
    {
        var settings = new AppSettings { DescentFuseMaxPhaseWitnessed = 7 };

        var json = JsonConvert.SerializeObject(settings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json, new JsonSerializerSettings
        {
            // Mirrors Services/Settings/SettingsService.Load, not a friendlier configuration.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        });

        Assert.NotNull(restored);
        Assert.Equal(7, restored!.DescentFuseMaxPhaseWitnessed);
    }

    /// <summary>The JSON key is the property name verbatim — the whole cluster's convention, and
    /// what a later server-side or support-side reader will look for.</summary>
    [Fact]
    public void TheJsonKey_IsThePropertyName()
    {
        var json = JsonConvert.SerializeObject(new AppSettings { DescentFuseMaxPhaseWitnessed = 4 });
        Assert.Contains("\"DescentFuseMaxPhaseWitnessed\":4", json);
    }

    /// <summary>
    /// A settings file written by a build that predates this field still loads, and loads at 0. The
    /// property is additive; nothing about an older file may invent a night that was not attended.
    /// </summary>
    [Fact]
    public void APreRatchetSettingsFile_LoadsAtZero()
    {
        const string preRatchet = """
        {
          "Welcomed": true,
          "LastSeenVersion": "6.8.0",
          "DescentCeremonyAtUtc": "2026-09-01T00:00:00Z",
          "DescentLastNightWitnessed": true,
          "DescentCatchUpCrackPlayed": false
        }
        """;

        var restored = JsonConvert.DeserializeObject<AppSettings>(preRatchet, new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        });

        Assert.NotNull(restored);
        Assert.Equal(0, restored!.DescentFuseMaxPhaseWitnessed);
        Assert.True(restored.DescentLastNightWitnessed);
    }

    /// <summary>
    /// The stored number and the enum are the same scale, and every enum member is reachable as a
    /// stored value. A renumbered enum would silently rewrite everybody's history.
    /// </summary>
    [Fact]
    public void TheStoredNumber_IsThePhaseNumber()
    {
        var expected = Enumerable.Range(0, 8).ToArray();
        var actual = AllPhases.Select(p => (int)p).ToArray();
        Assert.Equal(expected, actual);

        var names = new List<string>();
        foreach (var phase in AllPhases) names.Add(phase.ToString());
        Assert.Equal(
            new[] { "Dark", "Whisper", "Clock", "Dimming", "Candle", "Vigil", "Terminal", "Zero" },
            names);
    }
}
