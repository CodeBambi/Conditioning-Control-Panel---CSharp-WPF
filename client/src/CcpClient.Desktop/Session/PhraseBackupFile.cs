using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CcpClient.Desktop.Session;

/// <summary>Why a chosen file is not a phrase backup this build can restore. A CODE, so a crafted
/// file's own text can never become this app's error message.</summary>
public enum PhraseFileRefusal
{
    /// <summary>The bytes are not JSON at all.</summary>
    NotJson,

    /// <summary>Valid JSON, but not a JSON object — an array, a number, a bare null.</summary>
    NotAnObject,

    /// <summary>A JSON object whose <c>schema</c> is not <see cref="PhraseBackupFile.Schema"/>.</summary>
    WrongSchema,

    /// <summary>Right schema, but no <c>phrases</c> object with anything in it. Upstream's own
    /// "No phrases in file" (<c>Services/PhraseBackupService.cs:99</c>).</summary>
    NoPhrases,

    /// <summary>A real backup, but nothing in it is a pool this build has — restoring it would be
    /// a no-op reported as a success.</summary>
    NoKnownPools,
}

/// <summary>The pools a file yielded, and the ones it did not.</summary>
public abstract record PhraseFileRead
{
    private PhraseFileRead() { }

    /// <param name="Pools">Applied pools, in upstream's own declaration order, keyed by upstream's
    /// property name.</param>
    /// <param name="PoolsSkipped">Members of the file's <c>phrases</c> object that were NOT
    /// applied, in file order: a pool this build does not have, one whose shape is wrong, or one
    /// that is empty. Named rather than swallowed — an import that quietly drops half a file is
    /// the silent partial write this type exists to make impossible.</param>
    public sealed record Parsed(
        IReadOnlyList<KeyValuePair<string, Dictionary<string, bool>>> Pools,
        IReadOnlyList<string> PoolsSkipped) : PhraseFileRead;

    /// <summary>Nothing is applied and nothing is asked of the user.</summary>
    public sealed record Refused(PhraseFileRefusal Reason) : PhraseFileRead;
}

/// <summary>
/// The <c>.ccpphrases.json</c> document itself: build it, and read one back.
///
/// <para><b>It is upstream's file, deliberately, byte-compatible in both directions.</b> The schema
/// string (<c>Services/PhraseBackupService.cs:24</c>), the four envelope members
/// (<c>:72-78</c>) and the pool names (<c>:32-49</c>) are the shipping WPF product's, so a backup
/// exported from the WPF app restores here and a backup exported here restores there. That is not
/// nostalgia: this row exists because losing hand-written phrases to "a bad update or a machine
/// move" is a DATA-LOSS risk (<c>PhraseBackupService.cs:14-16</c>), and moving from the shipping
/// product to this client is the machine move this port itself creates. A private schema would have
/// made the one migration the user actually faces the one the file cannot carry.</para>
///
/// <para><b>What this build does not have, it names rather than drops.</b> Upstream captures
/// seventeen pools; this client has three modules with user-editable phrase pools, so an upstream
/// file's other fourteen members (mantras, attention words, custom triggers, the per-mod and
/// per-mode variants, the add/remove tracking sets) come back as
/// <see cref="PhraseFileRead.Parsed.PoolsSkipped"/>. They are not silently discarded and they are
/// not pretended to be restored.</para>
/// </summary>
public static class PhraseBackupFile
{
    /// <summary>Upstream's schema constant (<c>Services/PhraseBackupService.cs:24</c>).</summary>
    public const string Schema = "ccp-phrases/v1";

    /// <summary>Upstream's <c>SubliminalPool</c> (<c>PhraseBackupService.cs:35</c>).</summary>
    public const string SubliminalPoolName = "SubliminalPool";

    /// <summary>Upstream's <c>LockCardPhrases</c> (<c>PhraseBackupService.cs:38</c>).</summary>
    public const string LockCardPhrasesName = "LockCardPhrases";

    /// <summary>Upstream's <c>BouncingTextPool</c> (<c>PhraseBackupService.cs:40</c>).</summary>
    public const string BouncingTextPoolName = "BouncingTextPool";

    private const string SchemaKey = "schema";
    private const string ExportedAtKey = "exported_at";
    private const string AppVersionKey = "app_version";
    private const string PhrasesKey = "phrases";

    private static readonly string[] KnownPoolOrder =
        [SubliminalPoolName, LockCardPhrasesName, BouncingTextPoolName];

    /// <summary>The three pools this build can restore, in upstream's own declaration order.</summary>
    public static IReadOnlyList<string> KnownPools => KnownPoolOrder;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Upstream's suggested name (<c>PhraseBackupService.cs:52-53</c>), on the user's own calendar
    /// day rather than UTC — it is a name a person reads to find the backup they made on Tuesday.
    /// The instant passed in carries its own offset, so this and <see cref="Build"/>'s UTC stamp are
    /// the SAME instant read two ways, exactly as upstream reads <c>DateTime.Now</c> for the name
    /// and <c>DateTime.UtcNow</c> for the envelope.
    /// </summary>
    public static string SuggestedFileName(DateTimeOffset now) =>
        $"ccp-phrases-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.ccpphrases.json";

    /// <summary>
    /// The document. Envelope members and their order are upstream's
    /// (<c>PhraseBackupService.cs:72-78</c>); nothing else is added, and in particular no path, no
    /// file name, no machine name and no user identity — the file the user keeps is their phrases
    /// and nothing about the machine they left.
    /// </summary>
    public static string Build(
        IReadOnlyList<KeyValuePair<string, Dictionary<string, bool>>> pools,
        DateTimeOffset now,
        string appVersion)
    {
        var phrases = new JsonObject();
        foreach (var (name, pool) in pools)
        {
            var entries = new JsonObject();
            foreach (var (phrase, enabled) in pool)
            {
                entries[phrase] = enabled;
            }

            phrases[name] = entries;
        }

        var document = new JsonObject
        {
            [SchemaKey] = Schema,
            [ExportedAtKey] = now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            [AppVersionKey] = appVersion,
            [PhrasesKey] = phrases,
        };

        return document.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Reads a chosen file. Every rejection is a value: a malformed document produces
    /// <see cref="PhraseFileRead.Refused"/>, never an exception, so a bad file can never take a
    /// path through a catch block that has already half-applied something.
    ///
    /// <para>A single unusable POOL does not lose the others — upstream tolerates one unparseable
    /// member rather than failing the whole import (<c>PhraseBackupService.cs:136-147</c>) — but
    /// unlike upstream, which writes the skip to a log the user never sees, the skipped names come
    /// back to the caller.</para>
    /// </summary>
    public static PhraseFileRead Read(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return new PhraseFileRead.Refused(PhraseFileRefusal.NotJson);
        }

        if (root is not JsonObject document)
        {
            return new PhraseFileRead.Refused(PhraseFileRefusal.NotAnObject);
        }

        // Upstream validates the schema before anything else (PhraseBackupService.cs:97-98).
        if (!string.Equals(AsString(document[SchemaKey]), Schema, StringComparison.Ordinal))
        {
            return new PhraseFileRead.Refused(PhraseFileRefusal.WrongSchema);
        }

        if (document[PhrasesKey] is not JsonObject phrases || phrases.Count == 0)
        {
            return new PhraseFileRead.Refused(PhraseFileRefusal.NoPhrases);
        }

        var applied = new List<KeyValuePair<string, Dictionary<string, bool>>>();
        var skipped = new List<string>();
        foreach (var (name, value) in phrases)
        {
            if (Array.IndexOf(KnownPoolOrder, name) < 0)
            {
                skipped.Add(SkipName(name)); // a pool this build does not have — named, not dropped
                continue;
            }

            var pool = AsFlatPool(value);
            if (pool is null or { Count: 0 })
            {
                // Wrong shape, or empty. An EMPTY pool is refused rather than applied because two
                // of the three documents treat an empty phrase list as "restore the shipped
                // defaults" (Session/SubliminalPresetDocument.cs Phrases setter,
                // Session/BouncingTextPresetDocument.cs Phrases setter) — so applying one would
                // hand the user twenty-one phrases their backup does not contain and call it a
                // restore. A recorded divergence: upstream would apply the empty pool.
                skipped.Add(name);
                continue;
            }

            applied.Add(new KeyValuePair<string, Dictionary<string, bool>>(name, pool));
        }

        if (applied.Count == 0)
        {
            return new PhraseFileRead.Refused(PhraseFileRefusal.NoKnownPools);
        }

        // Upstream's own order, not the file's, so an import applies the same way whatever tool
        // wrote the file (PhraseBackupService.cs:129-134 iterates its whitelist, not the document).
        applied.Sort((left, right) =>
            Array.IndexOf(KnownPoolOrder, left.Key).CompareTo(Array.IndexOf(KnownPoolOrder, right.Key)));
        return new PhraseFileRead.Parsed(applied, skipped);
    }

    /// <summary>
    /// Upstream's confirmation count for a flat phrase pool: the number of entries, enabled or not
    /// (<c>PhraseBackupService.cs:170-173</c> — "Flat pool (phrase -&gt; bool) counts its keys").
    /// </summary>
    public static int CountEntries(IReadOnlyList<KeyValuePair<string, Dictionary<string, bool>>> pools)
    {
        var total = 0;
        foreach (var (_, pool) in pools)
        {
            total += pool.Count;
        }

        return total;
    }

    /// <summary>
    /// The greatest length of a member name reported back as skipped. The names come out of a file
    /// the user chose, not out of this program, so an absurd one is a UI bomb rather than
    /// information: a single 10 MB JSON key would otherwise be pasted into a message box. Sixty-four
    /// characters comfortably fits every one of upstream's seventeen property names, the longest of
    /// which is <c>RemovedDefaultSubliminals</c> at 25.
    /// </summary>
    public const int MaxSkipNameLength = 64;

    private static string SkipName(string name) =>
        name.Length <= MaxSkipNameLength ? name : name[..MaxSkipNameLength];

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A flat <c>phrase -&gt; bool</c> pool, or null if the member is any other shape.</summary>
    private static Dictionary<string, bool>? AsFlatPool(JsonNode? node)
    {
        if (node is not JsonObject entries)
        {
            return null;
        }

        var pool = new Dictionary<string, bool>(entries.Count, StringComparer.Ordinal);
        foreach (var (phrase, value) in entries)
        {
            if (value is not JsonValue slot || !slot.TryGetValue<bool>(out var enabled))
            {
                return null; // one non-boolean entry makes the whole pool an unknown shape
            }

            if (!string.IsNullOrWhiteSpace(phrase))
            {
                pool[phrase] = enabled;
            }
        }

        return pool;
    }
}
