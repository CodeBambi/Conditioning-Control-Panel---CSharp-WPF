using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Progression;

/// <summary>
/// The persisted progression spine (`progression.json`) — upstream's three
/// <c>AppSettings</c> fields and no more: <c>PlayerLevel</c> (<c>Models/AppSettings.cs:237</c>,
/// default 1), <c>PlayerXP</c> (<c>:244</c>, default 0.0, and it is progress INTO the current level
/// rather than a lifetime total — <c>Services/Descent/DescentMigrationService.cs:359-362</c> states
/// that pair explicitly) and <c>HighestLevelEver</c> (<c>:5455-5464</c>, kept because it survives
/// season resets).
///
/// <para>Its own file rather than a member of the demo settings document, for the reason
/// <c>Persistence/AssetSelectionDocument.cs:12-14</c> gives for the same choice: a NEW document is
/// additive by construction — no schema bump anywhere else, no absent-member case, and a Missing
/// outcome is honest fresh defaults.</para>
///
/// <para><b>WHAT IS NOT IN HERE, and each is a separate row rather than a field nothing writes.</b>
/// <c>SeasonPeakLevel</c> and <c>CurrentSeason</c> (<c>:5444-5453</c> — Season Recap and the season
/// reset), the server-confirmed XP watermark (<c>:5466</c> and below — there is no server),
/// <c>DescentEpoch</c> and <c>DescentCycleXpBonus</c> (the migration ceremony; see
/// <see cref="XpCurve"/>), and <c>OgLevelUnlockEnabled</c> (<c>:5428</c> — the bypass for a gate
/// upstream has since deleted outright, <see cref="LevelUnlocks"/>).</para>
/// </summary>
public sealed class ProgressionDocument
{
    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The record's file name in the shared &lt;dataDir&gt;. Named here so the three hosts
    /// that open it cannot drift onto three different files.</summary>
    public const string FileName = "progression.json";

    private int _level = XpCurve.FirstLevel;

    /// <summary>Upstream's <c>PlayerLevel</c> (<c>AppSettings.cs:237-242</c>). Clamped on the way in
    /// so a hand-edited 0, a negative, or a value past the ceiling cannot become the level a spend
    /// loop starts from — upstream's own setter is unguarded, but upstream's document is not written
    /// by anything a user can reach with a text editor and then handed straight to a loop.</summary>
    public int Level
    {
        get => _level;
        set => _level = Math.Clamp(value, XpCurve.FirstLevel, XpCurve.MaxLevel);
    }

    private double _xp;

    /// <summary>Upstream's <c>PlayerXP</c> (<c>AppSettings.cs:244-249</c>): progress INTO
    /// <see cref="Level"/>, never a lifetime total. A non-finite or negative value in the file
    /// reads as 0 rather than poisoning every later grant with a NaN.</summary>
    public double Xp
    {
        get => _xp;
        set => _xp = double.IsFinite(value) && value > 0 ? value : 0;
    }

    private int _highestLevelEver;

    /// <summary>Upstream's <c>HighestLevelEver</c> (<c>AppSettings.cs:5455-5464</c>), including its
    /// setter's own <c>Math.Max(0, value)</c> floor (<c>:5463</c>). Default 0, not 1: upstream's
    /// backing field is <c>= 0</c> and the first level-up is what raises it (<c>:5455</c>,
    /// <c>ProgressionService.cs:220-223</c>).</summary>
    public int HighestLevelEver
    {
        get => _highestLevelEver;
        set => _highestLevelEver = Math.Clamp(value, 0, XpCurve.MaxLevel);
    }

    /// <summary>Unknown-member preservation (contract §6 — required on every persisted model).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Why a grant did or did not reach the ledger. Every arm is a thing that really happens;
/// none of them is a fallback for "something went wrong".</summary>
public enum XpGrantState
{
    /// <summary>The XP was added and the write was enqueued.</summary>
    Granted,

    /// <summary>The amount was NaN or an infinity. A hosted page's payout is a double it chooses,
    /// and one non-finite value banked once would make the ledger permanently unreadable.</summary>
    RefusedNotFinite,

    /// <summary>The amount was zero or negative — upstream's own first line in
    /// <c>AddClaimedXP</c> (<c>ProgressionService.cs:85</c>, <c>if (amount &lt;= 0) return;</c>).
    /// Arcademy produces exactly this on a retake (<c>ArcademyHostService.cs:1388,1394</c>).</summary>
    RefusedNotPositive,

    /// <summary>The ledger could not be read, so what is in it is UNKNOWN — see
    /// <see cref="ProgressionLedger.Known"/>. Banking onto a quarantined or newer-schema document
    /// would either overwrite a record the user still has or silently restart them at level 1.</summary>
    RefusedLedgerUnknown,
}

/// <summary>
/// The outcome of one grant. <see cref="LevelBefore"/> and <see cref="LevelAfter"/> are the pair
/// upstream reads around its own <c>AddXP</c> call to fill a payout frame's <c>levelUp</c>
/// (<c>Services/Arcademy/ArcademyHostService.cs:1390</c> and <c>:1399</c>); they are null on a
/// refusal for an unknown ledger, because there is no level to report either side of a grant that
/// did not happen.
/// </summary>
public sealed record XpGrant(
    XpGrantState State,
    double Amount,
    int? LevelBefore,
    int? LevelAfter,
    double? XpIntoLevel,
    bool AtCeiling,
    string Reason)
{
    /// <summary>Whether the XP actually reached the ledger.</summary>
    public bool Banked => State == XpGrantState.Granted;

    /// <summary>Upstream's <c>levelBefore != levelAfter</c> (<c>ArcademyHostService.cs:1390-1399</c>,
    /// <c>:1416</c>). False on every refusal, because nothing moved.</summary>
    public bool LeveledUp => LevelAfter > LevelBefore;
}

/// <summary>
/// THE XP STORE — upstream's <c>ProgressionService.AddXP</c> (<c>:42-104</c>) and
/// <c>SpendXPOnLevels</c> (<c>:212-259</c>), reduced to the half that has somewhere to land here.
///
/// <para><b>WHY THIS EXISTS.</b> Four landed features in this port computed a payout and threw it
/// away, each saying so in its own source: the intake completion loop
/// (<c>Features/Intake/IntakeDraft.cs</c>), the descent's run-ended payout
/// (<c>Features/Dtrh/DtrhMeta.cs</c>) and the Arcademy class payout
/// (<c>Features/Arcademy/ArcademyClassPayout.cs</c>). The numbers were already right; there was no
/// ledger. There is one now, and NO new economy was invented for it — every amount this ledger
/// banks was already being computed by the feature that hands it over.</para>
///
/// <para><b>WHAT OF <c>AddXP</c> IS NOT PORTED, and why each one cannot be.</b></para>
/// <list type="bullet">
/// <item><b>The account gate</b> (<c>:45-53</c>): upstream refuses XP unless the user is logged in,
/// OR is in offline mode with a local username. This port has no login and no cloud sync of any
/// kind, which makes every install here upstream's SECOND arm — offline with a local identity,
/// where XP is granted and kept local-only. That is the branch this port is permanently on, not a
/// gate that was dropped.</item>
/// <item><b>Idle / AFK suppression</b> (<c>:55-64</c>): a separate row. It is also inert for all
/// three wired sources — upstream suppresses only <c>Flash</c>, <c>Subliminal</c> and
/// <c>BouncingText</c>, and none of the three sources here is one of those.</item>
/// <item><b>The skill-tree and Descent-cycle multipliers</b> (<c>:71-77</c>): both are 1.0 by
/// upstream's own fallbacks — see the remarks on <see cref="XpCurve"/>.</item>
/// <item><b>Companion XP routing</b> (<c>:96</c>), <b>quest credit</b> (<c>:99</c>) and
/// <b>lifetime-XP achievement tracking</b> (<c>:83</c>): three subsystems this build does not
/// have.</item>
/// <item><b>The whole level-up ceremony</b> (<c>:226-255</c>): haptics, level achievements, skill
/// points, Discord presence, the milestone webhook and the leaderboard sync. This ledger raises no
/// event and plays nothing; a grant RETURNS a value and a caller decides what to say.</item>
/// <item><b><c>AddClaimedXP</c></b> (<c>:83-114</c>) and its one-time web-XP toast
/// (<c>:136-170</c>): server-minted XP, and there is no server.</item>
/// </list>
///
/// <para><b>AN UNREADABLE LEDGER ANSWERS UNKNOWN, NEVER 1.</b> See <see cref="Known"/>. This is the
/// rule <c>TrainerCard</c> already set for the award record — an unreadable file produces
/// <c>Unknown</c> and a NULL count rather than a zero that reads as a score
/// (<c>Features/Progression/TrainerCard.cs:22-25</c>, <c>:299-300</c>) — applied to a number where
/// getting it wrong is worse, because a level rendered as 1 for someone standing at 40 is not just
/// uninformative, it is a claim about them.</para>
/// </summary>
public sealed class ProgressionLedger : IDisposable
{
    private readonly PersistenceStore<ProgressionDocument> _store;
    private readonly Action<string> _log;
    private readonly bool _ownsStore;

    /// <param name="ownsStore">True when <see cref="Dispose"/> should flush and stop the store —
    /// i.e. when this ledger opened it. False when the caller's own teardown already handles it.</param>
    public ProgressionLedger(PersistenceStore<ProgressionDocument> store, Action<string> log, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _log = log;
        _ownsStore = ownsStore;
    }

    /// <summary>
    /// Open the ledger over the shared &lt;dataDir&gt;, in the shape
    /// <c>Persistence/AssetSelectionStore.Start</c> established for exactly this situation: ONE file
    /// per install, opened by more than one host, each with its own uniquely-named registry owner.
    /// The three hosts that call this are modal windows the user opens one at a time.
    /// </summary>
    /// <param name="ownerName">Unique <c>OperationRegistry</c> owner name — e.g.
    /// "IntakeProgression" / "DtrhProgression".</param>
    public static ProgressionLedger Open(ApplicationHost host, string dataDir, string ownerName)
    {
        ArgumentNullException.ThrowIfNull(host);
        var store = new PersistenceStore<ProgressionDocument>(
            host.Registry.OwnerFor(ownerName),
            new HostLogSink(host),
            Path.Combine(dataDir, ProgressionDocument.FileName),
            ProgressionDocument.CurrentSchemaVersion);
        store.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (store.LastLoadOutcome is not LoadOutcome.Loaded and not LoadOutcome.Missing)
        {
            host.LogDiagnostic(
                $"progression: load → {store.LastLoadOutcome?.GetType().Name} (typed Degraded — the level is UNKNOWN and every grant is refused; the record is not overwritten)");
        }

        return new ProgressionLedger(store, host.LogDiagnostic, ownsStore: true);
    }

    /// <summary>
    /// Whether this build can say anything about the ledger at all. False before the store has
    /// started, after it has stopped, and on either typed Degraded load —
    /// <c>LoadOutcome.Quarantined</c> or <c>LoadOutcome.NewerSchema</c>
    /// (<c>Persistence/PersistenceStore.cs:34-43</c>).
    ///
    /// <para>Quarantine is included on purpose, even though the store itself is perfectly willing to
    /// write fresh defaults after one. For a PREFERENCE that is right — the asset-selection store
    /// says so about its own quarantine. For a LEDGER it is not: writing 1 over a quarantined
    /// progression file is how a user who had a level loses it permanently on the next grant, while
    /// their real record sits in the backup the quarantine just made.</para>
    /// </summary>
    public bool Known => _store.Running && _store.LastLoadOutcome is { IsDegraded: false };

    /// <summary>The level, or null when the ledger is not <see cref="Known"/>. NEVER 0 and never a
    /// consoling 1.</summary>
    public int? Level => Known ? _store.Current.Level : null;

    /// <summary>Progress into <see cref="Level"/>, or null when the ledger is not
    /// <see cref="Known"/>.</summary>
    public double? XpIntoLevel => Known ? _store.Current.Xp : null;

    /// <summary>XP to clear the current level, or null when the ledger is not <see cref="Known"/>.
    /// The pair a progress bar needs, and neither half is invented.</summary>
    public double? XpForNextLevel => Known ? XpCurve.XpForLevel(_store.Current.Level) : null;

    /// <summary>Highest level ever reached (<c>AppSettings.cs:5455-5464</c>), or null when the
    /// ledger is not <see cref="Known"/>.</summary>
    public int? HighestLevelEver => Known ? _store.Current.HighestLevelEver : null;

    /// <summary>The store, for a caller that owns its own teardown ordering.</summary>
    public PersistenceStore<ProgressionDocument> Store => _store;

    /// <summary>
    /// Bank XP — upstream's <c>AddXP</c> minus everything named as unported in the type remarks,
    /// which leaves exactly its two load-bearing statements: <c>settings.PlayerXP += amount</c>
    /// (<c>ProgressionService.cs:80</c>) followed by <c>SpendXPOnLevels</c> (<c>:102</c>).
    ///
    /// <para>The write reaches disk immediately rather than riding a timer. Upstream banks into
    /// <c>AppSettings</c> and lets its own save cadence carry it; this port's equivalent of "mark
    /// dirty" is "the change reaches disk", which is the reading
    /// <c>Features/Progression/GradedRunAwards.cs:227-231</c> already argued for the award record.
    /// A refused grant writes nothing at all.</para>
    /// </summary>
    /// <param name="amount">The XP the source computed. Never scaled here.</param>
    /// <param name="source">Where it came from, for the diagnostic line only.</param>
    public XpGrant Grant(double amount, string source)
    {
        if (!double.IsFinite(amount))
        {
            return Refuse(XpGrantState.RefusedNotFinite, amount, source,
                "the amount is not a finite number");
        }

        if (amount <= 0)
        {
            return Refuse(XpGrantState.RefusedNotPositive, amount, source,
                "the amount is zero or negative (ProgressionService.cs:85)");
        }

        if (!Known)
        {
            return Refuse(XpGrantState.RefusedLedgerUnknown, amount, source,
                _store.Running
                    ? $"the ledger could not be read ({_store.LastLoadOutcome?.GetType().Name}) — the level is UNKNOWN and the record is left untouched"
                    : "the ledger is not open");
        }

        var before = _store.Current.Level;
        XpCurve.LevelSpend spend = default;

        _store.Mutate(d =>
        {
            d.Xp += amount;                                                  // :80
            spend = XpCurve.Spend(d.Level, d.Xp);                            // :102 → :212-223
            d.Level = spend.Level;
            d.Xp = spend.XpIntoLevel;
            if (d.Level > d.HighestLevelEver)
            {
                d.HighestLevelEver = d.Level;                                // :220-223
            }
        });

        _ = _store.Save();

        _log($"progression: +{amount:0.##} XP from {source} — level {before}"
            + (spend.LevelsGained > 0 ? $" → {spend.Level} (LEVEL UP x{spend.LevelsGained})" : string.Empty)
            + $", {spend.XpIntoLevel:0.##}/{XpCurve.XpForLevel(spend.Level):0} into it"
            + (spend.AtCeiling ? $" — AT THE LEVEL CEILING ({XpCurve.MaxLevel}); further XP banks but buys nothing" : string.Empty));

        return new XpGrant(XpGrantState.Granted, amount, before, spend.Level, spend.XpIntoLevel,
            spend.AtCeiling, string.Empty);
    }

    private XpGrant Refuse(XpGrantState state, double amount, string source, string reason)
    {
        _log($"progression: {amount:0.##} XP from {source} NOT banked — {reason}");
        var known = Known;
        return new XpGrant(
            state,
            amount,
            known ? _store.Current.Level : null,
            known ? _store.Current.Level : null,
            known ? _store.Current.Xp : null,
            AtCeiling: known && _store.Current.Level >= XpCurve.MaxLevel,
            reason);
    }

    /// <summary>Flush then stop, and only when this ledger opened the store (contract §11: a stop is
    /// not a flush). Idempotent and best-effort, the shape every other host-local store teardown in
    /// this tree already takes.</summary>
    public void Dispose()
    {
        if (!_ownsStore)
        {
            return;
        }

        try { _store.FlushAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3)); } catch { /* best-effort */ }
        try { _store.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
    }

    private sealed class HostLogSink(ApplicationHost host) : ILogSink
    {
        public void Log(string message) => host.LogDiagnostic(message);
    }
}
