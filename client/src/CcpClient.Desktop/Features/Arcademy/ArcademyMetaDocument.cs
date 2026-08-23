using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The Arcademy's persisted META blob — attendance streak, the XP ledger, the page's graded day
/// rows and per-game progression. Upstream is <c>Services/Arcademy/ArcademyMetaStore.cs</c>, whose
/// state is one free-form <c>JObject</c> written to <c>arcademy_meta.json</c> in
/// <c>App.UserDataPath</c> (<c>:13-28</c>, <c>:94</c>). This is the same file, the same key names
/// and the same ownership split, expressed in this port's persistence idiom
/// (<c>Persistence/PersistenceStore.cs</c>: schemaVersion + unknown-member preservation, the
/// <c>Haptics/HapticSettingsDocument</c> / <see cref="ArcademySettingsDocument"/> shape).
///
/// <para><b>THE OWNERSHIP SPLIT IS THE SHAPE.</b> Upstream keeps the whole blob untyped on purpose
/// (<c>:20-23</c>): "V1 per-game meta is authored page-side (SYNTHESIS-NOTES #15) and pinning a C#
/// class to it would make every game a C# change". So the five HOST-OWNED regions
/// (<c>:32-46</c>) — the only ones C# mints and the only ones a page write is refused for
/// (<c>:76-80</c>, <c>:326-333</c>) — become typed members here, and everything the page authors
/// (<c>days</c>, <c>games</c>, its seen-once flags) lands in <see cref="PageRegions"/>, which is
/// the document's <c>[JsonExtensionData]</c>. Unknown-member preservation (persistence contract §6)
/// and upstream's free-form page half are therefore THE SAME MECHANISM here: a key this build has
/// never heard of round-trips because the page owns it, not despite that.</para>
///
/// <para><b><c>Dictionary&lt;string, JsonElement&gt;</c>, the same as every other document here,
/// and MEASURED rather than chosen on the documentation.</b> <see cref="JsonObject"/> is listed as
/// a supported extension-data type, and it round-TRIPS on read — but on WRITE
/// <c>System.Text.Json</c> emits it as a braced object value where a property name belongs, so
/// serializing the document throws <c>'{' is an invalid start of a property name</c> and every read
/// of the blob dies with it. That was observed on this build (.NET 10) by these very facts, which
/// is the whole reason a slice with "no machine verification at all" upstream gets some here. The
/// mutable half upstream's <c>JObject</c> gave for free is a <see cref="JsonObject"/> built on
/// demand inside <see cref="ArcademyMetaStore"/> and written straight back.</para>
///
/// <para><b>A hand-edited host region is answered by QUARANTINE, not salvage — stated
/// divergence.</b> Typing the five host keys means a file with <c>"streak": "abc"</c> fails typed
/// binding, and this port's store then preserves the bytes at a <c>.corrupt-&lt;stamp&gt;.json</c>
/// sidecar and runs on flagged defaults (persistence contract §5 rule 3). Upstream would coerce
/// and carry on. Nothing on either side WRITES those keys but the host, so the only way to reach
/// this is a hand-edit, and preserve-and-flag loses nothing that silent coercion would have kept.
/// The over-cap blob — the failure upstream actually sees — is salvaged rather than quarantined,
/// exactly as upstream does (<see cref="ArcademyMetaStore"/>).</para>
/// </summary>
public sealed class ArcademyMetaDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;
    /// (upstream <c>ArcademyMetaStore.cs:94</c>).</summary>
    public const string FileName = "arcademy_meta.json";

    /// <summary>The schema version this build writes (persistence contract §1). Upstream's blob
    /// carries no version at all; the port's store owns the member, so this is the port's
    /// idiom rather than a shape upstream declares.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Host-owned: the LOCAL date (yyyy-MM-dd) attendance was last credited for
    /// (<c>ArcademyMetaStore.AttendanceKey</c>, <c>:32-33</c>). Local, never UTC — regression
    /// #978, and see <see cref="ArcademyClassPayout.ArcademyClassDay"/>.</summary>
    public const string AttendanceKey = "lastAttendanceLocalDate";

    /// <summary>Host-owned: consecutive local days with at least one completed class
    /// (<c>:35-36</c>).</summary>
    public const string StreakKey = "streak";

    /// <summary>Host-owned: lifetime count of local days where all 3 classes were completed
    /// (<c>:38-39</c>).</summary>
    public const string PerfectKey = "perfectAttendance";

    /// <summary>Host-owned: game keys completed on <see cref="AttendanceKey"/>'s day
    /// (<c>:41-42</c>).</summary>
    public const string TodayClassesKey = "todayClasses";

    /// <summary>Host-owned: the XP ledger, <c>{ "&lt;utcDay&gt;": ["&lt;gameKey&gt;", …] }</c>. A
    /// class pays its XP once per UTC day; every later run of the same class that day is a free
    /// replay (<c>:44-46</c>).</summary>
    public const string XpPaidKey = "xpPaidDays";

    /// <summary>The keys the page may never write (<c>HostOwnedKeys</c>, <c>:76-80</c>); the page
    /// carries the same list (<c>arcademy/core/store.js:47-50</c>), which is why neither side alone
    /// can mint a streak.</summary>
    public static readonly IReadOnlySet<string> HostOwnedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        AttendanceKey, StreakKey, PerfectKey, TodayClassesKey, XpPaidKey,
    };

    /// <summary>See <see cref="AttendanceKey"/>. Null until the first class is credited — omitted
    /// from the file rather than written as an explicit null, so a fresh blob is
    /// <c>{}</c> plus the counters, the way upstream's fresh <c>new JObject()</c> is
    /// (<c>:463</c>).</summary>
    [JsonPropertyName(AttendanceKey)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastAttendanceLocalDate { get; set; }

    /// <summary>See <see cref="StreakKey"/>. Upstream reads an ABSENT key as 0
    /// (<c>(int?)_state[StreakKey] ?? 0</c>, <c>:208</c>); this member is always present and
    /// defaults to 0, which the page reads identically (<c>core/store.js:229</c> coerces through
    /// <c>num()</c>).</summary>
    [JsonPropertyName(StreakKey)]
    public int Streak { get; set; }

    /// <summary>See <see cref="PerfectKey"/> (<c>:209</c>).</summary>
    [JsonPropertyName(PerfectKey)]
    public int PerfectAttendance { get; set; }

    /// <summary>See <see cref="TodayClassesKey"/>. Reset to empty on a new local day
    /// (<c>:217</c>), appended once per distinct game key (<c>:222-224</c>).</summary>
    [JsonPropertyName(TodayClassesKey)]
    public List<string> TodayClasses { get; set; } = [];

    /// <summary>See <see cref="XpPaidKey"/>. Ordinal-keyed because the trim relies on
    /// <c>yyyy-MM-dd</c> ordinal order BEING chronological order (<c>:267-272</c>).</summary>
    [JsonPropertyName(XpPaidKey)]
    public Dictionary<string, List<string>> XpPaidDays { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Everything the PAGE owns — <c>days</c>, <c>games</c> and its seen-once flags — plus any key
    /// a future page invents. Unknown-member preservation (persistence contract §6) and upstream's
    /// deliberately untyped page half (upstream <c>ArcademyMetaStore.cs:20-23</c>) are one and the same
    /// thing here; see the type remarks.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? PageRegions { get; set; }
}
