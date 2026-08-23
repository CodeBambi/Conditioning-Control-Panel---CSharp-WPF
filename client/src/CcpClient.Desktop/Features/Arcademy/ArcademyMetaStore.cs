using System.Text.Json;
using System.Text.Json.Nodes;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// Slice 3 of the Arcademy board row: the META STORE. C# owns
/// <see cref="ArcademyMetaDocument.FileName"/>; the page holds a rev-numbered read snapshot and
/// sends COMMANDS (<c>meta-command {op,key,value}</c>) which are shape-validated here, applied and
/// answered with the POST-write value (upstream <c>Services/Arcademy/ArcademyMetaStore.cs</c>,
/// <c>:13-28</c> for the ownership model, <c>:118-152</c> for the command loop).
///
/// <para><b>VALIDATION IS INTEGRITY, NOT ANTI-CHEAT</b> (<c>:24-28</c>) — this is an offline save
/// the user owns. Unknown ops are logged and ignored, keys and values are size-capped, and the five
/// host-owned regions are written only by <see cref="RecordAttendance"/> /
/// <see cref="TryClaimXpDay"/> so a stale or hand-edited page cannot mint a streak.</para>
///
/// <para><b>UPSTREAM HAS NO MACHINE VERIFICATION OF ANY OF THIS.</b> The board row says so in as
/// many words ("the cleanest slice and the one upstream admits has no machine verification at
/// all"), and the tree agrees: <c>Tests/ConditioningControlPanel.Tests</c> contains no Arcademy
/// test of any kind. The facts in <c>ArcademyMetaTests</c> are therefore the FIRST verification
/// this behaviour has ever had, on either side.</para>
///
/// <para><b>Three mechanisms are the port's rather than upstream's, and all three are stated.</b>
/// (1) <b>No debounce.</b> Upstream arms a 500 ms <c>DispatcherTimer</c> and writes on tick
/// (<c>:301-325</c>), with a write-through fallback when there is no dispatcher. This port's
/// <see cref="PersistenceStore{TModel}"/> already serializes writes onto one chained writer that
/// never interleaves, so every mutation just enqueues one — the same discipline
/// <see cref="ArcademySession.SetSetting"/> already follows, and a save that cannot be dropped is
/// worth more than a save that is coalesced. (2) <b>No atomic-write of its own.</b> Upstream hand-
/// rolls temp → <c>File.Replace</c> → <c>.bak</c> (<c>:404-439</c>) because a truncating
/// <c>WriteAllText</c> loses the whole transcript on a crash; the port's store is temp + flush-to-
/// disk + rename already (contract §3), and recovers an orphaned temp on the next load — the same
/// outcome by the same physics. (3) <b>A corrupt file is QUARANTINED with its bytes preserved</b>
/// (contract §5 rule 3) where upstream falls back to <c>.bak</c> then to empty (<c>:451-465</c>).
/// The one upstream recovery with NO port equivalent is the over-cap SALVAGE, and that is ported
/// here in full — see <see cref="Start"/>.</para>
///
/// <para><b>No lock of its own.</b> Upstream locks because its state is static and reachable from a
/// dispatcher tick as well as the page lane. Here every mutation runs inside the store's own
/// mutation gate and the page lane is single-threaded.
/// <c>ponytail: single-writer assumption; give this class a lock if a second writer ever
/// appears.</c></para>
/// </summary>
public sealed class ArcademyMetaStore
{
    /// <summary>Classes in a day — the timetable's fixed size (<c>:49</c>, GROUND-RULES §4).</summary>
    public const int ClassesPerDay = 3;

    /// <summary>UTC days of XP ledger kept. Older days can no longer be replayed into, so keeping
    /// them would just grow the blob (<c>:51-53</c>).</summary>
    public const int XpPaidDayHistory = 14;

    /// <summary>Trimmed, non-empty, at most this many chars, or the command is ignored
    /// (<c>:55</c>, <c>NormalizeKey</c> <c>:385-390</c>).</summary>
    public const int MaxKeyLength = 128;

    /// <summary>A meta file this big is a bug, not a save (<c>:56</c>) — the salvage trigger.</summary>
    public const int MaxBlobChars = 512 * 1024;

    /// <summary>Serialized ceiling for ONE key's value (<c>:58-62</c>). Nothing page-side bounds
    /// what a game stores, and without a write-side cap one runaway game inflates the blob past
    /// <see cref="MaxBlobChars"/> and the NEXT launch reads it as corrupt. Refusing one write is
    /// recoverable; losing the file is not.</summary>
    public const int MaxValueChars = 32 * 1024;

    /// <summary>Top-level keys the blob may carry (<c>:64-66</c>). <b>Divergence, stricter:</b>
    /// upstream counts only the keys that EXIST, so a fresh blob starts at 0 and the page's budget
    /// grows into the host's four-or-five regions as they appear. This document always carries all
    /// five, so the page's budget is a flat 59 — never looser than upstream's, and never a moving
    /// target.</summary>
    public const int MaxTopLevelKeys = 64;

    /// <summary>Graded day rows kept (<c>:68-71</c>). <c>days</c> is the only
    /// unbounded-by-construction region — one row per calendar day, forever — and the shell only
    /// ever reads today plus a short recent window.</summary>
    public const int DayHistory = 40;

    /// <summary>The page's graded day rows, keyed by LOCAL date (<c>:73-74</c>, regression
    /// #978).</summary>
    public const string DaysKey = "days";

    /// <summary>Per-game tier/state — the second-largest region and the salvage step after
    /// <see cref="DaysKey"/> (<c>:76-77</c>).</summary>
    public const string GamesKey = "games";

    private static readonly JsonSerializerOptions BlobOptions = new();

    private readonly PersistenceStore<ArcademyMetaDocument> _store;
    private readonly string _filePath;
    private readonly ILogSink _log;

    /// <param name="store">The meta document's store. Constructed by the caller the way every
    /// other document's store is, so the composition root keeps one persistence idiom.</param>
    /// <param name="filePath">The same path <paramref name="store"/> was built with. Needed for
    /// ONE thing: the <c>.corrupt</c> sidecar an over-cap salvage preserves before it sheds
    /// anything (<c>:526-533</c>).</param>
    /// <param name="log">Diagnostics. Never receives a stored VALUE, only key names and sizes —
    /// upstream's own posture (<c>:326-347</c>).</param>
    public ArcademyMetaStore(PersistenceStore<ArcademyMetaDocument> store, string filePath, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _filePath = filePath;
        _log = log;
    }

    /// <summary>Bumped by every accepted mutation (<c>Touch</c>, <c>:296-300</c>). Rides the
    /// whole-blob push so the page can tell a stale snapshot from a fresh one.</summary>
    public int Rev { get; private set; }

    /// <summary>
    /// Load, then run the OVER-CAP SALVAGE (<c>:441-524</c>) — the one upstream recovery this
    /// port's persistence machinery does not already provide.
    ///
    /// <para>Upstream triggers on the raw FILE length and then measures compactly while shedding;
    /// this measures compactly for both, which can only ever trigger later than upstream's
    /// indented measure, never earlier. The ladder itself is upstream's, in order: preserve the
    /// bytes at a <c>.corrupt</c> sidecar, shed the <c>days</c> transcript, then the oldest
    /// <c>games</c> entries one at a time, and if it is STILL over, keep only what the host owns —
    /// "they are tiny and they are the ones a player would actually mourn" (<c>:495-521</c>).</para>
    /// </summary>
    public void Start(CancellationToken cancellationToken)
    {
        // StartAsync loads on the calling thread and hands back an already-complete task (its own
        // remarks, pinned by PersistenceStoreTests), so there is nothing here to wait on.
        _ = _store.StartAsync(cancellationToken);

        if (BlobLength(_store.Current) <= MaxBlobChars)
        {
            return;
        }

        _log.Log($"arcademy meta: blob is over {MaxBlobChars} chars — attempting salvage");
        PreserveCorrupt();
        Apply(Salvage);
    }

    /// <summary>The full blob for the <c>init</c> projection (<c>Snapshot</c>, <c>:101-104</c>) —
    /// host-owned regions and page-owned regions in one object. A fresh serialization every call,
    /// so the page's snapshot is never a live handle onto the state a later command
    /// mutates.</summary>
    public JsonObject Snapshot() => JsonSerializer.SerializeToNode(_store.Current, BlobOptions)!.AsObject();

    /// <summary>One top-level key's current value, detached (<c>Get</c>, <c>:190-193</c>). Null for
    /// a key that is not there.</summary>
    public JsonNode? Get(string key) => Snapshot()[key]?.DeepClone();

    /// <summary>
    /// Handle one <c>meta-command {op,key,value?}</c> (<c>Handle</c>, <c>:118-152</c>). Returns the
    /// key and the POST-write value for the caller to echo, so the page's cache converges on what
    /// C# actually stored even when a write was clamped or refused — null only when the command
    /// itself was unusable (missing/oversized key, unknown op), which upstream also answers with
    /// silence (<c>:124-128</c>, <c>:142-145</c>).
    /// </summary>
    public (string Key, JsonNode? Value)? Handle(string? op, string? key, JsonElement? value)
    {
        var normalized = NormalizeKey(key);
        if (normalized is null)
        {
            _log.Log($"arcademy meta: command '{op}' with a missing/oversized key — ignored");
            return null;
        }

        try
        {
            switch (op)
            {
                case "get":
                    break;   // the reply below IS the get (:131)
                case "set":
                    if (Guard(normalized) && SizeGuard(normalized, value))
                    {
                        Set(normalized, value);
                    }

                    break;
                case "merge":
                    if (Guard(normalized) && SizeGuard(normalized, value))
                    {
                        Merge(normalized, value);
                    }

                    break;
                default:
                    _log.Log($"arcademy meta: unknown op '{op}' — ignored");
                    return null;
            }

            return (normalized, Get(normalized));
        }
        catch (Exception ex)
        {
            _log.Log($"arcademy meta: handle('{op}') failed ({ex.GetType().Name})");   // :150
            return null;
        }
    }

    /// <summary>Replace one page-owned key outright (<c>Set</c>, <c>:155-164</c>). A JSON
    /// <c>null</c> stores a null, which is upstream's <c>JValue.CreateNull()</c>.</summary>
    public void Set(string key, JsonElement? value) => Apply(document =>
    {
        if (!AcceptNewKey(document, key))
        {
            return false;
        }

        Regions(document)[key] = value?.Clone() ?? NullElement;
        TrimDays(document);
        return true;
    });

    /// <summary>Shallow-merge an object into one page-owned key, creating it when absent
    /// (<c>Merge</c>, <c>:166-188</c>). A non-object patch is a SET: the page uses merge for its
    /// per-game bags, and refusing would just lose the write.</summary>
    public void Merge(string key, JsonElement? patch) => Apply(document =>
    {
        if (!AcceptNewKey(document, key))
        {
            return false;
        }

        if (patch is { ValueKind: JsonValueKind.Object } incoming && RegionObject(document, key) is { } existing)
        {
            foreach (var property in incoming.EnumerateObject())
            {
                existing[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }

            PutRegion(document, key, existing);
        }
        else
        {
            Regions(document)[key] = patch?.Clone() ?? NullElement;
        }

        TrimDays(document);
        return true;
    });

    /// <summary>
    /// Credit one completed class to today's attendance and roll the streak
    /// (<c>RecordAttendance</c>, <c>:195-236</c>). <b>LOCAL date, always</b> — regression #978:
    /// local midnight rolls the streak, the UTC date only seeds content. The caller supplies the
    /// date because the two clocks are named apart on purpose; see
    /// <see cref="ArcademyClassPayout.ArcademyClassDay"/>.
    ///
    /// <para>Idempotent per game key: replaying the same class on the same day does not re-count it
    /// toward perfect attendance. A gap of exactly one day continues the streak, a larger gap
    /// restarts it at 1, and a day already credited leaves the streak alone.</para>
    /// </summary>
    /// <returns>(streak, perfectAttendance, classesToday) after the write.</returns>
    public (int Streak, int Perfect, int ClassesToday) RecordAttendance(string localDate, string? gameKey)
    {
        var streak = 0;
        var perfect = 0;
        var classesToday = 0;

        Apply(document =>
        {
            streak = document.Streak;
            perfect = document.PerfectAttendance;

            if (!string.Equals(document.LastAttendanceLocalDate, localDate, StringComparison.Ordinal))
            {
                streak = IsPreviousDay(document.LastAttendanceLocalDate, localDate) ? streak + 1 : 1;  // :213
                document.LastAttendanceLocalDate = localDate;
                document.Streak = streak;
                document.TodayClasses = [];                                                            // :217
            }

            var today = document.TodayClasses ??= [];
            if (!string.IsNullOrWhiteSpace(gameKey) && !today.Contains(gameKey, StringComparer.Ordinal))
            {
                today.Add(gameKey);
                // The perfect-attendance credit fires on the transition INTO a full day, so a
                // fourth completion (a retake) can never award it twice (:225-231).
                if (today.Count == ClassesPerDay)
                {
                    perfect += 1;
                    document.PerfectAttendance = perfect;
                }
            }

            classesToday = today.Count;
            return true;   // upstream Touches unconditionally (:233), even on a no-op credit
        });

        return (streak, perfect, classesToday);
    }

    /// <summary>
    /// Claim the one XP payment <paramref name="gameKey"/> is worth on <paramref name="dayUtc"/>
    /// (<c>TryClaimXpDay</c>, <c>:238-276</c>). True the first time that pair is seen, false
    /// forever after.
    ///
    /// <para><b>THE FARM GUARD.</b> A class is replayable by design (the seed is the day's, so a
    /// retake is the same script) and nothing stops a player finishing one ten times before lunch.
    /// The ledger lives host-side for the same reason the streak does: a stale or edited page must
    /// not be able to mint a payout. Attendance is deliberately NOT keyed off this —
    /// <see cref="RecordAttendance"/> is idempotent on its own terms and runs on the LOCAL date
    /// (#978), so a retake still cannot double-count a class but a genuine new local day still
    /// credits one.</para>
    /// </summary>
    public bool TryClaimXpDay(string? gameKey, string? dayUtc)
    {
        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(dayUtc))
        {
            return true;   // :250 — nothing to bill against, so the class pays
        }

        return Apply(document =>
        {
            var paid = document.XpPaidDays ??= new(StringComparer.Ordinal);
            if (!paid.TryGetValue(dayUtc, out var day))
            {
                day = [];
                paid[dayUtc] = day;
            }

            if (day.Contains(gameKey, StringComparer.Ordinal))
            {
                return false;   // :265 — already paid, and NOT a mutation
            }

            day.Add(gameKey);
            // yyyy-MM-dd ordinal order IS chronological order, so SkipLast keeps the newest
            // XpPaidDayHistory days and drops the rest (:267-272).
            foreach (var stale in paid.Keys
                         .OrderBy(name => name, StringComparer.Ordinal)
                         .SkipLast(XpPaidDayHistory)
                         .ToList())
            {
                paid.Remove(stale);
            }

            return true;
        });
    }

    /// <summary>Flush any pending write now (teardown / app exit; <c>FlushSave</c>,
    /// <c>:278-290</c>). Bounded by the store's own teardown flush — never a wall-clock
    /// wait of this class's own.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    // ============================ internals ============================

    /// <summary>Run one mutation under the store's mutation gate; bump <see cref="Rev"/> and
    /// enqueue a write only when it actually applied (<c>Touch</c>, <c>:296-300</c>). A refused
    /// key must not cost a disk write.</summary>
    private bool Apply(Func<ArcademyMetaDocument, bool> change)
    {
        var applied = false;
        _store.Mutate(document =>
        {
            applied = change(document);
            if (applied)
            {
                Rev++;
            }
        });

        if (applied)
        {
            _ = _store.Save();
        }

        return applied;
    }

    /// <summary>False (and logged) for a key the host owns (<c>Guard</c>, <c>:326-333</c>). The
    /// page gets a reply either way, so a refused write shows up page-side as "the value did not
    /// change".</summary>
    private bool Guard(string key)
    {
        if (!ArcademyMetaDocument.HostOwnedKeys.Contains(key))
        {
            return true;
        }

        _log.Log($"arcademy meta: page tried to write host-owned key '{key}' — refused");
        return false;
    }

    /// <summary>False (and logged) for a value whose COMPACT serialized form exceeds
    /// <see cref="MaxValueChars"/> (<c>SizeGuard</c>, <c>:335-352</c>). Bounded here rather than at
    /// load time because a blob that has already grown past <see cref="MaxBlobChars"/> is a save
    /// the player has to lose part of.</summary>
    private bool SizeGuard(string key, JsonElement? value)
    {
        if (value is not { } element)
        {
            return true;
        }

        // Compact, because JsonElement.GetRawText() would return the frame's own whitespace and a
        // pretty-printed command would then be measured against the wrong number.
        var length = JsonSerializer.Serialize(element, BlobOptions).Length;
        if (length <= MaxValueChars)
        {
            return true;
        }

        _log.Log($"arcademy meta: '{key}' is {length} chars (cap {MaxValueChars}) — refused");
        return false;
    }

    /// <summary>Cap the number of top-level keys (<c>AcceptNewKey</c>, <c>:354-361</c>). An
    /// EXISTING key always passes — an update must never be refused because the blob is wide — and
    /// only a NEW key can be turned away. The five host-owned regions count against the cap
    /// because they are in the blob; see <see cref="MaxTopLevelKeys"/>.</summary>
    private bool AcceptNewKey(ArcademyMetaDocument document, string key)
    {
        // ContainsKey, not "the value is non-null": upstream's `_state[key] != null` reads a key
        // stored as JSON null as absent, which at exactly the cap would refuse an update. Same
        // answer everywhere else, correct at that edge.
        if (Regions(document).ContainsKey(key)
            || Regions(document).Count + ArcademyMetaDocument.HostOwnedKeys.Count < MaxTopLevelKeys)
        {
            return true;
        }

        _log.Log($"arcademy meta: top-level key budget is full — new key '{key}' dropped");
        return false;
    }

    /// <summary>Keep the newest <see cref="DayHistory"/> day rows (<c>TrimDays</c>,
    /// <c>:363-383</c>). Runs on every write that could have touched the region rather than on a
    /// schedule: the store has no clock of its own, and an unbounded region only bites at the next
    /// load.</summary>
    private static void TrimDays(ArcademyMetaDocument document)
    {
        if (RegionObject(document, DaysKey) is not { } days || days.Count <= DayHistory)
        {
            return;
        }

        foreach (var stale in days
                     .Select(row => row.Key)
                     .OrderBy(name => name, StringComparer.Ordinal)
                     .SkipLast(DayHistory)
                     .ToList())
        {
            days.Remove(stale);
        }

        PutRegion(document, DaysKey, days);
    }

    /// <summary>Shed the big page-owned regions until the blob is back under the cap
    /// (<c>Salvage</c>, <c>:492-524</c>). Order matters: <c>days</c> is the graded transcript (nice
    /// to have), <c>games</c> carries tier progression (worse to lose), and the host-owned
    /// streak/ledger keys are never touched.</summary>
    private bool Salvage(ArcademyMetaDocument document)
    {
        if (RegionObject(document, DaysKey) is { Count: > 0 } days)
        {
            days.Clear();
            PutRegion(document, DaysKey, days);
            _log.Log("arcademy meta: salvage dropped the 'days' transcript");
            if (BlobLength(document) <= MaxBlobChars)
            {
                return true;
            }
        }

        if (RegionObject(document, GamesKey) is { } games)
        {
            // Oldest first by key order — there is no timestamp in a per-game bag, and ordinal
            // order is at least stable, so the same entries go each time rather than a random half.
            // The re-measure per entry is upstream's own shape (:504-511).
            foreach (var name in games.Select(entry => entry.Key).OrderBy(n => n, StringComparer.Ordinal).ToList())
            {
                if (BlobLength(document) <= MaxBlobChars)
                {
                    break;
                }

                games.Remove(name);
                PutRegion(document, GamesKey, games);
                _log.Log($"arcademy meta: salvage dropped games['{name}']");
            }

            if (BlobLength(document) <= MaxBlobChars)
            {
                return true;
            }
        }

        // Still over: keep only what the host owns, which is all that cannot be re-earned (:518).
        Regions(document).Clear();
        _log.Log("arcademy meta: salvage kept the host-owned keys only");
        return true;
    }

    /// <summary>Copy a file we are about to recover from destructively to a <c>.corrupt</c>
    /// sidecar (<c>PreserveCorrupt</c>, <c>:526-533</c>). One generation, overwritten: the point is
    /// that the bytes still exist to look at, not an archive.</summary>
    private void PreserveCorrupt()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _filePath + ".corrupt", overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log.Log($"arcademy meta: .corrupt sidecar failed ({ex.GetType().Name})");
        }
    }

    private static int BlobLength(ArcademyMetaDocument document) =>
        JsonSerializer.Serialize(document, BlobOptions).Length;

    /// <summary>A JSON <c>null</c> to store for an absent or null command value — upstream's
    /// <c>JValue.CreateNull()</c> (<c>:161</c>, <c>:184</c>).</summary>
    private static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement.Clone();

    private static Dictionary<string, JsonElement> Regions(ArcademyMetaDocument document) =>
        document.PageRegions ??= [];

    /// <summary>The MUTABLE view of one page-owned region, or null when it is absent or is not an
    /// object. Built on demand and written straight back by <see cref="PutRegion"/> — see the
    /// remarks on <see cref="ArcademyMetaDocument"/> for why the document cannot simply hold
    /// <see cref="JsonObject"/>s.</summary>
    private static JsonObject? RegionObject(ArcademyMetaDocument document, string key) =>
        Regions(document).TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Object
            ? JsonObject.Create(element)
            : null;

    private static void PutRegion(ArcademyMetaDocument document, string key, JsonObject region) =>
        Regions(document)[key] = JsonSerializer.SerializeToElement(region, BlobOptions);

    /// <summary>Trimmed, non-empty, at most <see cref="MaxKeyLength"/> chars, else null
    /// (<c>NormalizeKey</c>, <c>:385-390</c>).</summary>
    public static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        return trimmed.Length > MaxKeyLength ? null : trimmed;
    }

    /// <summary>Is <paramref name="last"/> exactly the calendar day before
    /// <paramref name="today"/> (<c>IsPreviousDay</c>, <c>:392-403</c>)? Anything unparseable
    /// answers false, which RESTARTS the streak rather than extending one from a corrupt
    /// date.</summary>
    public static bool IsPreviousDay(string? last, string today)
    {
        if (!ArcademyClassPayout.TryParseDay(last, out var previous)
            || !ArcademyClassPayout.TryParseDay(today, out var now))
        {
            return false;
        }

        return (now.Date - previous.Date).TotalDays == 1;
    }
}
