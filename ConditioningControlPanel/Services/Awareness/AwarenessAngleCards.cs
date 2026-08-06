using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// One authored comedic angle (doc 02 §3.1 item 2). A card is a <b>bit</b> — a description of a
    /// joke SHAPE — never an example line: example lines get parroted back verbatim, which is the
    /// exact failure v2 exists to fix (doc 02 §1.3).
    /// </summary>
    /// <param name="Id">Short slug. The only part of a card that is ever written to the log.</param>
    /// <param name="Bit">The shape itself, already sanitised. Empty means the card did not survive.</param>
    /// <param name="AllowsPlug">
    /// This card is one of the few that explicitly licenses naming something from her own media list.
    /// The output contract forbids plugs unconditionally otherwise (doc 02 §3.1 item 5), which is what
    /// retires the "every path terminates in plug a video" protocols.
    /// </param>
    public sealed record AwarenessAngleCard(string Id, string Bit, bool AllowsPlug)
    {
        /// <summary>A card with no surviving bit text contributes nothing and is never rendered.</summary>
        public bool IsUsable => Bit.Length > 0;
    }

    /// <summary>
    /// The distilled voice card for one mod (~150 tokens), as authored in the deck file.
    /// </summary>
    public sealed record AwarenessPersona(string ModId, string Name, string Digest);

    /// <summary>
    /// A loaded, already-hardened set of cards, personas and key mappings. Immutable once built, so
    /// the prompt builder can cache a prefix against <see cref="Stamp"/> and know it is still valid.
    /// </summary>
    public sealed class AwarenessAngleDeck
    {
        internal AwarenessAngleDeck(
            int version,
            string stamp,
            bool isEmbedded,
            IReadOnlyDictionary<string, AwarenessPersona> personas,
            IReadOnlyDictionary<string, string> aliases,
            IReadOnlyDictionary<string, string> categories,
            IReadOnlyDictionary<string, string> appKeys,
            IReadOnlyDictionary<string, IReadOnlyList<AwarenessAngleCard>> clusters)
        {
            Version = version;
            Stamp = stamp;
            IsEmbedded = isEmbedded;
            Personas = personas;
            Aliases = aliases;
            Categories = categories;
            AppKeys = appKeys;
            Clusters = clusters;
        }

        /// <summary>Schema version declared by the file. Unknown versions still load field-by-field.</summary>
        public int Version { get; }

        /// <summary>
        /// Identity of this deck's CONTENT. Part of the prompt-prefix cache key, so swapping the
        /// override file rebuilds the prefix instead of serving one built from the old cards.
        /// </summary>
        public string Stamp { get; }

        /// <summary>
        /// True when this deck was parsed from the compiled-in resource rather than read off disk.
        ///
        /// <para><b>It is not a "was this modded?" flag.</b> A stock install also ships the same file
        /// as Content next to <c>app_clusters.json</c> (so modders have a template to edit), and that
        /// on-disk copy is byte-identical to the embedded one — so a healthy install normally reports
        /// <c>false</c> here. <c>true</c> means the disk copy was absent, oversized, malformed or
        /// hostile and the fail-closed floor took over.</para>
        /// </summary>
        public bool IsEmbedded { get; }

        public IReadOnlyDictionary<string, AwarenessPersona> Personas { get; }
        public IReadOnlyDictionary<string, string> Aliases { get; }
        public IReadOnlyDictionary<string, string> Categories { get; }
        public IReadOnlyDictionary<string, string> AppKeys { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<AwarenessAngleCard>> Clusters { get; }

        /// <summary>The fallback deck key. Guaranteed present in the embedded deck.</summary>
        public const string DefaultKey = "default";

        /// <summary>
        /// Which card set this frame belongs to: a per-app override first (a code editor is not just
        /// "Working"), then the cluster id, then the broad legacy category, then
        /// <see cref="DefaultKey"/>. Aliases are resolved once, and only once, so a mod cannot build
        /// an alias cycle that spins the resolver.
        /// </summary>
        public string ResolveKey(string? appId, string? cluster, ActivityCategory category)
        {
            var app = AwarenessText.SanitizeId(appId);
            if (app != AwarenessText.UnknownId && AppKeys.TryGetValue(app, out var byApp) && Has(byApp))
                return Resolve(byApp);

            var clusterId = AwarenessText.SanitizeId(cluster);
            if (clusterId != AwarenessText.UnknownId)
            {
                var resolved = Resolve(clusterId);
                if (Has(resolved)) return resolved;
            }

            if (Categories.TryGetValue(category.ToString(), out var byCategory))
            {
                var resolved = Resolve(byCategory);
                if (Has(resolved)) return resolved;
            }

            return DefaultKey;
        }

        /// <summary>The cards for a resolved key, or the default set when the key holds none.</summary>
        public IReadOnlyList<AwarenessAngleCard> CardsFor(string? key)
        {
            var id = AwarenessText.SanitizeId(key);
            if (Clusters.TryGetValue(id, out var cards) && cards.Count > 0) return cards;
            return Clusters.TryGetValue(DefaultKey, out var fallback)
                ? fallback
                : Array.Empty<AwarenessAngleCard>();
        }

        /// <summary>
        /// The persona digest for a mod id, or null when the deck has none — in which case the prompt
        /// builder distils one from the live personality preset instead (doc 02 §3.1 item 1).
        /// </summary>
        public AwarenessPersona? PersonaFor(string? modId)
        {
            var id = AwarenessText.SanitizeId(modId);
            if (id != AwarenessText.UnknownId && Personas.TryGetValue(id, out var persona)) return persona;
            return Personas.TryGetValue(DefaultKey, out var fallback) ? fallback : null;
        }

        private bool Has(string key) => Clusters.TryGetValue(key, out var c) && c.Count > 0;

        private string Resolve(string key) =>
            Aliases.TryGetValue(key, out var target) ? target : key;
    }

    /// <summary>
    /// Loads <c>awareness_angles.json</c> — the data file that carries the comedic angle cards and the
    /// per-mod persona digests (doc 02 §3.1). It mirrors <see cref="AppClusterMap"/>'s design on
    /// purpose: an external copy next to <c>app_clusters.json</c> overrides the built-in deck, so mods
    /// and content updates can extend the writing WITHOUT a code change.
    ///
    /// <para><b>This is an injection surface, and it is treated as one.</b> Card and digest text lands
    /// inside the system prompt, and the override file is mod-supplied — i.e. potentially
    /// attacker-authored. Every value therefore goes through <see cref="AwarenessText"/>:</para>
    /// <list type="bullet">
    /// <item>the whole file is refused above <see cref="MaxFileBytes"/>, before it is parsed;</item>
    /// <item>ids are normalised to <c>a-z0-9_-.</c>; bit text is capped at
    /// <see cref="AwarenessText.MaxCardLength"/> and digests at <see cref="MaxDigestLength"/>;</item>
    /// <item>control characters are dropped and any line that opens like a role marker or an
    /// instruction override ("system:", "ignore previous", "&lt;|", …) is discarded whole;</item>
    /// <item>a deck may hold at most <see cref="MaxCardsPerKey"/> cards per key and
    /// <see cref="MaxKeys"/> keys, and a call renders at most
    /// <see cref="AwarenessPromptBuilder.MaxCardsPerCall"/> of them.</item>
    /// </list>
    ///
    /// <para><b>Fail closed.</b> A missing, unreadable, oversized, malformed or empty override leaves
    /// the embedded deck in place and is logged ONCE per load. There is no path where a broken file
    /// produces a prompt with no cards and no persona: the embedded deck is compiled into the assembly
    /// and cannot be edited on disk.</para>
    /// </summary>
    public static class AwarenessAngleCards
    {
        /// <summary>File name, in the same folder as <c>app_clusters.json</c>.</summary>
        public const string FileName = "awareness_angles.json";

        /// <summary>Manifest name of the compiled-in copy used as the fail-closed floor.</summary>
        public const string EmbeddedResourceName =
            "ConditioningControlPanel.Resources.sounds.companion_audio.awareness_angles.json";

        /// <summary>
        /// Refuse the override above this size without parsing it. An angle deck is prose; a 50 MB one
        /// is either a mistake or an attempt to spend the user's tokens on the attacker's text.
        /// </summary>
        public const int MaxFileBytes = 256 * 1024;

        /// <summary>Persona digests are ~150 tokens; this caps them at roughly 175.</summary>
        public const int MaxDigestLength = 700;

        /// <summary>Cards accepted per key. Only a handful are ever rendered; the rest is rotation pool.</summary>
        public const int MaxCardsPerKey = 12;

        /// <summary>Card keys accepted from a deck file.</summary>
        public const int MaxKeys = 64;

        /// <summary>Persona entries accepted from a deck file.</summary>
        public const int MaxPersonas = 64;

        /// <summary>Alias / category / app-key entries accepted from a deck file, per table.</summary>
        public const int MaxMappings = 256;

        private static readonly object Gate = new();
        private static AwarenessAngleDeck? _deck;
        private static AwarenessAngleDeck? _embedded;

        /// <summary>Where an override would be read from. Same folder as <c>app_clusters.json</c>.</summary>
        public static string FilePath =>
            Path.Combine(CompanionPhraseService.CompanionAudioFolder, FileName);

        /// <summary>
        /// The active deck. Loads on first use: embedded first (so there is always a floor), then the
        /// external override if one parses.
        /// </summary>
        public static AwarenessAngleDeck Deck
        {
            get
            {
                lock (Gate)
                {
                    if (_deck != null) return _deck;
                    _deck = LoadOverrideOrEmbedded();
                    return _deck;
                }
            }
        }

        /// <summary>
        /// True when the active deck came from the compiled-in resource — i.e. the file on disk was
        /// absent or unusable. See <see cref="AwarenessAngleDeck.IsEmbedded"/>: on a healthy install
        /// this is <c>false</c>, because the same deck also ships as a Content file.
        /// </summary>
        public static bool UsingEmbeddedDeck => Deck.IsEmbedded;

        /// <summary>
        /// Drops the cached deck so the next read reloads. For tests and for a mod hot-swap; there is
        /// no watcher, because a deck change mid-launch is not a scenario worth a file handle.
        /// </summary>
        public static void Invalidate()
        {
            lock (Gate) { _deck = null; }
        }

        /// <summary>The compiled-in deck, parsed once. Never null: a broken embed is a build error.</summary>
        internal static AwarenessAngleDeck Embedded()
        {
            if (_embedded != null) return _embedded;

            string? json = null;
            try
            {
                using var stream = typeof(AwarenessAngleCards).Assembly
                    .GetManifestResourceStream(EmbeddedResourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AwarenessAngleCards: embedded deck could not be read");
            }

            _embedded = (json == null ? null : Parse(json, isEmbedded: true)) ?? MinimalDeck();
            return _embedded;
        }

        private static AwarenessAngleDeck LoadOverrideOrEmbedded()
        {
            var embedded = Embedded();

            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return embedded;

                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes)
                {
                    App.Logger?.Warning(
                        "AwarenessAngleCards: override is {Bytes} bytes (cap {Cap}) — using the built-in deck",
                        info.Length, MaxFileBytes);
                    return embedded;
                }

                var parsed = Parse(File.ReadAllText(path), isEmbedded: false);
                if (parsed == null)
                {
                    App.Logger?.Warning(
                        "AwarenessAngleCards: override at {Path} did not survive validation — using the built-in deck",
                        path);
                    return embedded;
                }

                App.Logger?.Information(
                    "AwarenessAngleCards: loaded {Keys} card key(s), {Personas} persona(s) from {Path}",
                    parsed.Clusters.Count, parsed.Personas.Count, path);
                return parsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessAngleCards: override load failed — using the built-in deck");
                return embedded;
            }
        }

        /// <summary>
        /// Parses and hardens a deck. Returns null when nothing usable survived — the caller's cue to
        /// keep the built-in deck rather than ship a prompt with no angles.
        /// </summary>
        internal static AwarenessAngleDeck? Parse(string? json, bool isEmbedded)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (json!.Length > MaxFileBytes) return null;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                App.Logger?.Warning("AwarenessAngleCards: deck did not parse ({Error})", ex.Message);
                return null;
            }

            var clusters = ParseClusters(root["clusters"] as JObject);
            if (clusters.Count == 0) return null;

            var personas = ParsePersonas(root["personas"] as JObject);
            // Only the ALIAS table rejects a self-mapping: there, key and value are the same kind of
            // thing and "x" -> "x" is a cycle. A category or an app id mapping to a card key of the
            // same spelling ("Idle" -> "idle", "dev" -> "dev") is a perfectly ordinary entry, and
            // dropping it silently loses that whole deck.
            var aliases = ParseMap(root["aliases"] as JObject, lowercaseKey: true, rejectSelfMapping: true);
            var categories = ParseMap(root["categories"] as JObject, lowercaseKey: false, rejectSelfMapping: false);
            var appKeys = ParseMap(root["app_keys"] as JObject, lowercaseKey: true, rejectSelfMapping: false);

            int version = root["version"]?.Type == JTokenType.Integer ? root["version"]!.Value<int>() : 0;

            return new AwarenessAngleDeck(
                version,
                Stamp(isEmbedded, clusters, personas),
                isEmbedded,
                personas, aliases, categories, appKeys, clusters);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<AwarenessAngleCard>> ParseClusters(JObject? section)
        {
            var map = new Dictionary<string, IReadOnlyList<AwarenessAngleCard>>(StringComparer.OrdinalIgnoreCase);
            if (section == null) return map;

            foreach (var prop in section.Properties())
            {
                if (map.Count >= MaxKeys) break;

                var key = AwarenessText.SanitizeId(prop.Name);
                if (key == AwarenessText.UnknownId || map.ContainsKey(key)) continue;
                if (prop.Value is not JArray array) continue;

                var cards = new List<AwarenessAngleCard>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in array)
                {
                    if (cards.Count >= MaxCardsPerKey) break;
                    if (entry is not JObject card) continue;

                    var id = AwarenessText.SanitizeId(card["id"]?.ToString());
                    // A bit that sanitised down to nothing was either empty or was trying to be an
                    // instruction. Either way it contributes nothing and is not a card.
                    var bit = AwarenessText.SanitizeCardText(card["bit"]?.ToString(), AwarenessText.MaxCardLength);
                    if (bit.Length == 0) continue;
                    if (id == AwarenessText.UnknownId) id = $"{key}_{cards.Count + 1}";
                    if (!seen.Add(id)) continue;

                    bool plug = card["plug"]?.Type == JTokenType.Boolean && card["plug"]!.Value<bool>();
                    cards.Add(new AwarenessAngleCard(id, bit, plug));
                }

                if (cards.Count > 0) map[key] = cards;
            }

            return map;
        }

        private static IReadOnlyDictionary<string, AwarenessPersona> ParsePersonas(JObject? section)
        {
            var map = new Dictionary<string, AwarenessPersona>(StringComparer.OrdinalIgnoreCase);
            if (section == null) return map;

            foreach (var prop in section.Properties())
            {
                if (map.Count >= MaxPersonas) break;

                var modId = AwarenessText.SanitizeId(prop.Name);
                if (modId == AwarenessText.UnknownId || map.ContainsKey(modId)) continue;
                if (prop.Value is not JObject entry) continue;

                var digest = AwarenessText.SanitizeCardText(entry["digest"]?.ToString(), MaxDigestLength);
                if (digest.Length == 0) continue;

                var name = AwarenessText.SanitizeDisplayName(entry["name"]?.ToString(), 48);
                map[modId] = new AwarenessPersona(modId, name, digest);
            }

            return map;
        }

        private static IReadOnlyDictionary<string, string> ParseMap(
            JObject? section, bool lowercaseKey, bool rejectSelfMapping)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (section == null) return map;

            foreach (var prop in section.Properties())
            {
                if (map.Count >= MaxMappings) break;

                // Category keys are enum names and keep their case; ids do not.
                var key = lowercaseKey
                    ? AwarenessText.SanitizeId(prop.Name)
                    : AwarenessText.SanitizeDisplayName(prop.Name, AwarenessText.MaxIdLength);
                var value = AwarenessText.SanitizeId(prop.Value?.ToString());

                if (string.IsNullOrEmpty(key) || key == AwarenessText.UnknownId) continue;
                if (value == AwarenessText.UnknownId) continue;
                if (rejectSelfMapping && string.Equals(key, value, StringComparison.OrdinalIgnoreCase)) continue;
                map[key] = value;
            }

            return map;
        }

        /// <summary>
        /// Content identity for the prefix cache. Deliberately built from the ids and the LENGTHS of
        /// the authored text rather than a hash of the whole file, so it is cheap, stable across a
        /// reformat that changes no words, and still moves when the writing does.
        /// </summary>
        private static string Stamp(
            bool isEmbedded,
            IReadOnlyDictionary<string, IReadOnlyList<AwarenessAngleCard>> clusters,
            IReadOnlyDictionary<string, AwarenessPersona> personas)
        {
            unchecked
            {
                int hash = isEmbedded ? 17 : 31;
                foreach (var key in clusters.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(key);
                    foreach (var card in clusters[key])
                    {
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(card.Id);
                        hash = hash * 31 + card.Bit.Length;
                        hash = hash * 31 + (card.AllowsPlug ? 1 : 0);
                    }
                }
                foreach (var key in personas.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(key);
                    hash = hash * 31 + personas[key].Digest.Length;
                }
                return (isEmbedded ? "e" : "o") + hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The last-resort deck, used only if the embedded resource itself is missing (a packaging
        /// accident). One generic card and one generic persona is not good comedy, but it keeps the
        /// prompt structurally valid and the safety layers intact.
        /// </summary>
        private static AwarenessAngleDeck MinimalDeck()
        {
            App.Logger?.Error(
                "AwarenessAngleCards: embedded deck '{Resource}' is missing from the assembly — " +
                "awareness reactions will be generic until it is restored", EmbeddedResourceName);

            var cards = new Dictionary<string, IReadOnlyList<AwarenessAngleCard>>(StringComparer.OrdinalIgnoreCase)
            {
                [AwarenessAngleDeck.DefaultKey] = new[]
                {
                    new AwarenessAngleCard(
                        "just_here",
                        "Pick one small specific thing from the numbers in front of you and be present with it, in your own voice. Do not be clever.",
                        false)
                }
            };
            var personas = new Dictionary<string, AwarenessPersona>(StringComparer.OrdinalIgnoreCase)
            {
                [AwarenessAngleDeck.DefaultKey] = new AwarenessPersona(
                    AwarenessAngleDeck.DefaultKey, "Companion",
                    "You are their companion: warm, observant, a little wry, and genuinely amused by them. Short sentences, no lecturing, no apologising for noticing.")
            };

            return new AwarenessAngleDeck(
                0, "minimal", isEmbedded: true, personas,
                new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
                cards);
        }
    }
}
