using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Per-mod relationship counters (doc 01 §2.2, <c>relationship</c> block).
    ///
    /// <para><b>Train 1 scope.</b> Counters only. The <c>stage</c>/<c>affinity</c> arc of doc 01 §4.1
    /// is Train 4 — nothing here reads these numbers back into the prompt yet, they are simply
    /// accumulated so the arc has real history to start from on the day it ships.</para>
    /// </summary>
    /// <param name="ChatTurnsTotal">Lifetime user chat turns sent while this mod was active.</param>
    /// <param name="LastChat">UTC of the most recent one, or null when there has never been one.</param>
    public sealed record RelationshipState(int ChatTurnsTotal, DateTime? LastChat)
    {
        public static RelationshipState Empty { get; } = new(0, null);
    }

    /// <summary>
    /// The companion's durable model of the user, persisted to
    /// <c>%LOCALAPPDATA%\ConditioningControlPanel\companion\memory.json</c> (doc 01 §2.2).
    ///
    /// <para><b>Deterministic only.</b> Everything in here is either written by the user (the "What
    /// she knows about you" panel) or mirrored from app state the process already owns by
    /// <see cref="MemorySignalWriter"/>. There is NO LLM extraction — that is Train 4 — so this store
    /// costs exactly zero tokens to fill and still buys most of the "she knows me" feeling.</para>
    ///
    /// <para><b>Never throws.</b> A corrupt, truncated or hand-mangled memory.json must degrade the
    /// companion to "amnesiac but working", never to "chat is down". Every disk path is wrapped and
    /// falls back to an empty store that immediately starts rebuilding itself from app signals.</para>
    ///
    /// <para><b>Plaintext, on purpose</b> (doc 01 §8 risk 4): the file is user-inspectable and
    /// user-deletable, which is the compliance story for an adult app that stores personal
    /// statements. <see cref="Wipe"/> is the panel's "Forget everything" button.</para>
    /// </summary>
    public sealed class MemoryStore : IMemoryStore, IDisposable
    {
        /// <summary>Hard ceiling on the injected block regardless of the budget a caller asks for.</summary>
        public const int MaxInjectionTokens = 500;

        /// <summary>Schema version of memory.json.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Fact cap. Past this, the lowest salience×recency UNPROTECTED fact is evicted.</summary>
        public const int MaxFacts = 200;

        /// <summary>
        /// Soft ceiling on the file (doc 01 §2.2). Exceeding it sheds unprotected facts rather than
        /// refusing to save — a memory file is never worth failing a write over.
        /// </summary>
        public const int SoftMaxBytes = 256 * 1024;

        /// <summary>
        /// Boundaries are always injected, but not without limit: a pathological file full of them
        /// would otherwise swallow the whole prompt tail. Past this many, the most salient are shown
        /// and the rest are acknowledged by a count line.
        /// </summary>
        public const int MaxBoundaryLines = 20;

        /// <summary>Recency decay constant, in days: weight = e^(-days/30).</summary>
        public const double RecencyDecayDays = 30d;

        /// <summary>How long the debounced background save waits for the writes to stop.</summary>
        public static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);

        // Curated profile keys. Constants because MemorySignalWriter, the panel and the injection
        // ordering all have to agree on the spelling, and a typo would silently create a second key.
        public const string KeyPreferredName = "preferredName";
        public const string KeyFirstSeen = "firstSeen";
        public const string KeyLevel = "level";
        public const string KeyStreakDays = "streakDays";
        public const string KeyTotalSessions = "totalSessions";
        public const string KeyArchetype = "archetype";
        public const string KeyFavoriteFeatures = "favoriteFeatures";
        public const string KeyLastSessionRecap = "lastSessionRecap";

        /// <summary>
        /// Injection order of the known profile keys. Stable ordering is a cost lever, not cosmetics:
        /// the memory block sits in the prompt's dynamic tail and a reshuffled line is a fresh string
        /// every call. Unknown keys follow, sorted, for the same reason.
        /// </summary>
        private static readonly string[] ProfileKeyOrder =
        {
            KeyPreferredName, KeyFirstSeen, KeyLevel, KeyStreakDays,
            KeyTotalSessions, KeyArchetype, KeyFavoriteFeatures, KeyLastSessionRecap
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _path;
        private readonly Func<DateTime> _clock;

        /// <summary>
        /// Per-app-session jitter seed. Fact selection is randomised *within the top band* once per
        /// session (doc 01 §2.5) so different days surface different callbacks — but it must be
        /// STABLE inside a session or the prompt tail changes on every call and provider prompt
        /// caching never hits.
        /// </summary>
        private readonly int _sessionSeed;

        private readonly Dictionary<string, object?> _profile = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RelationshipState> _relationship = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _usage = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<MemoryFact> _facts = new();

        private readonly Timer? _saveTimer;
        private readonly MemorySignalWriter? _signals;
        private EventHandler? _processExitHandler;
        private bool _disposed;

        /// <summary>Production constructor: real path, auto-load, app signals mirrored automatically.</summary>
        public MemoryStore() : this(DefaultMemoryPath, null, null, mirrorAppSignals: true) { }

        /// <summary>
        /// Test/diagnostic constructor. Nothing is subscribed to app events, so this is safe to build
        /// headlessly.
        /// </summary>
        /// <param name="memoryPath">Full path of memory.json.</param>
        /// <param name="utcClock">Injectable clock for recency maths.</param>
        /// <param name="sessionSeed">Fixes the per-session selection jitter.</param>
        public MemoryStore(string memoryPath, Func<DateTime>? utcClock = null, int? sessionSeed = null)
            : this(memoryPath, utcClock, sessionSeed, mirrorAppSignals: false) { }

        private MemoryStore(string memoryPath, Func<DateTime>? utcClock, int? sessionSeed, bool mirrorAppSignals)
        {
            _path = string.IsNullOrWhiteSpace(memoryPath) ? DefaultMemoryPath : memoryPath;
            _clock = utcClock ?? (() => DateTime.UtcNow);
            _sessionSeed = sessionSeed ?? Environment.TickCount;

            Load();
            _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);

            if (!mirrorAppSignals) return;

            // The store owns the writer rather than App.OnStartup doing it, so memory works the
            // moment CompanionBrain constructs a default store — there is no separate wiring step to
            // forget, and no call site outside this feature needs to change.
            try
            {
                _signals = new MemorySignalWriter(this);
                _signals.Start();

                // Nothing in the app owns this store's lifetime (CompanionBrain holds it as an
                // IMemoryStore and never disposes it), so without this the last debounce window's
                // worth of signals would be lost on every exit.
                _processExitHandler = (_, _) => { try { SaveNow(); } catch { } };
                AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MemoryStore: app-signal mirroring failed to start");
            }
        }

        /// <summary><c>%LOCALAPPDATA%\ConditioningControlPanel\companion\memory.json</c>.</summary>
        public static string DefaultMemoryPath => Path.Combine(CompanionDirectory, "memory.json");

        /// <summary>The companion's private data folder. Not the assets path, not a mod folder.</summary>
        public static string CompanionDirectory => Path.Combine(App.UserDataPath, "companion");

        /// <summary>Full path of memory.json — surfaced for the privacy panel and diagnostics.</summary>
        public string MemoryPath => _path;

        // ===================== profile =====================

        public IReadOnlyDictionary<string, object?> Profile
        {
            get { lock (_lock) return new Dictionary<string, object?>(_profile, StringComparer.OrdinalIgnoreCase); }
        }

        public void UpdateProfileSignal(string key, object? value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            bool changed;
            lock (_lock)
            {
                _profile.TryGetValue(key, out var existing);
                changed = !ProfileValuesEqual(existing, value);
                if (changed)
                {
                    if (value == null) _profile.Remove(key);
                    else _profile[key] = value;
                }
            }

            // Only a real change costs a write. Level/streak signals are refreshed on every settings
            // notification, and most of those are about something else entirely.
            if (changed) RequestSave();
        }

        // ===================== relationship =====================

        /// <summary>Read-only view of the per-mod relationship counters.</summary>
        public IReadOnlyDictionary<string, RelationshipState> Relationships
        {
            get { lock (_lock) return new Dictionary<string, RelationshipState>(_relationship, StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Counts one user chat turn against the active mod. Deterministic, no LLM: this is the raw
        /// material the Train 4 relationship arc reads. Blank/unknown mod ids fall back to
        /// <c>"default"</c> so the count is never silently dropped.
        /// </summary>
        public void NoteChatTurn(string? modId)
        {
            var key = string.IsNullOrWhiteSpace(modId) ? "default" : modId.Trim();
            lock (_lock)
            {
                var state = _relationship.TryGetValue(key, out var existing) ? existing : RelationshipState.Empty;
                _relationship[key] = state with { ChatTurnsTotal = state.ChatTurnsTotal + 1, LastChat = _clock() };
            }
            RequestSave();
        }

        // ===================== usage counters =====================

        /// <summary>
        /// Read-only view of the feature-usage counters that back <c>favoriteFeatures</c>.
        /// </summary>
        public IReadOnlyDictionary<string, int> FeatureUsage
        {
            get { lock (_lock) return new Dictionary<string, int>(_usage, StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Bumps a feature-usage counter. Callers are expected to debounce (see
        /// <see cref="MemorySignalWriter.FeatureUseCooldown"/>) — flashes fire hundreds of times an
        /// hour and would otherwise drown every other feature in the ranking.
        /// </summary>
        public void NoteFeatureUsed(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature)) return;
            var key = feature.Trim();
            lock (_lock) _usage[key] = (_usage.TryGetValue(key, out var n) ? n : 0) + 1;
            RequestSave();
        }

        // ===================== facts =====================

        public IReadOnlyList<MemoryFact> GetFacts()
        {
            lock (_lock)
            {
                return _facts
                    .OrderByDescending(f => f.Kind == MemoryFactKind.Boundary)
                    .ThenByDescending(f => f.Pinned)
                    .ThenByDescending(f => f.Salience)
                    .ToList();
            }
        }

        public MemoryFact AddFact(string text, MemoryFactKind kind, double salience = 0.5,
            string source = MemoryFact.SourceChat)
        {
            var body = (text ?? string.Empty).Trim();
            var now = _clock();
            var fact = new MemoryFact(
                Id: NewFactId(),
                Text: body,
                Kind: kind,
                Salience: Math.Clamp(salience, 0d, 1d),
                Created: now,
                LastUsed: null,
                Uses: 0,
                Pinned: false,
                Source: string.IsNullOrWhiteSpace(source) ? MemoryFact.SourceChat : source);

            if (body.Length == 0) return fact; // nothing worth storing; caller still gets a valid record

            lock (_lock)
            {
                _facts.Add(fact);
                EnforceFactCap();
            }
            RequestSave();
            return fact;
        }

        public bool UpdateFact(string id, string? text = null, double? salience = null, bool? pinned = null)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock)
            {
                int i = _facts.FindIndex(f => f.Id == id);
                if (i < 0) return false;
                var f = _facts[i];
                _facts[i] = f with
                {
                    Text = text ?? f.Text,
                    Salience = salience.HasValue ? Math.Clamp(salience.Value, 0d, 1d) : f.Salience,
                    Pinned = pinned ?? f.Pinned,
                    // A hand-edited fact is the user telling us it matters; mark the provenance so a
                    // future extractor never silently overwrites it.
                    Source = text != null ? MemoryFact.SourceUserEdited : f.Source
                };
            }
            RequestSave();
            return true;
        }

        public bool ForgetFact(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool removed;
            lock (_lock) removed = _facts.RemoveAll(f => f.Id == id) > 0;
            if (removed) RequestSave();
            return removed;
        }

        public void NoteFactUsed(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_lock)
            {
                int i = _facts.FindIndex(f => f.Id == id);
                if (i < 0) return;
                _facts[i] = _facts[i] with { LastUsed = _clock(), Uses = _facts[i].Uses + 1 };
            }
            RequestSave();
        }

        // ===================== injection =====================

        /// <summary>
        /// Renders the prompt's memory block (doc 01 §2.5) in priority order: profile line →
        /// every boundary → top-K facts by salience×recency.
        ///
        /// <para>Boundaries deliberately ignore <paramref name="tokenBudget"/> (up to
        /// <see cref="MaxBoundaryLines"/>): a remembered "stop teasing me about X" is consent hygiene
        /// and must not lose a budget race to a joke about the user's cat.</para>
        /// </summary>
        public string? GetInjectionBlock(int tokenBudget)
        {
            int budget = Math.Min(Math.Max(tokenBudget, 0), MaxInjectionTokens);
            if (budget <= 0) return null;

            Dictionary<string, object?> profile;
            List<MemoryFact> facts;
            lock (_lock)
            {
                if (_profile.Count == 0 && _facts.Count == 0) return null;
                profile = new Dictionary<string, object?>(_profile, StringComparer.OrdinalIgnoreCase);
                facts = _facts.ToList();
            }

            var sb = new StringBuilder();
            int spent = 0;

            void Append(string line)
            {
                sb.AppendLine(line);
                spent += ChatSession.ApproxTokens(line) + 1; // +1 for the newline
            }

            bool TryAppend(string line)
            {
                if (spent + ChatSession.ApproxTokens(line) + 1 > budget) return false;
                Append(line);
                return true;
            }

            var profileLine = BuildProfileLine(profile);
            if (profileLine != null) TryAppend(profileLine);

            // Boundaries: unconditional, most salient first, capped so a degenerate file cannot eat
            // the whole prompt.
            var boundaries = facts
                .Where(f => f.Kind == MemoryFactKind.Boundary)
                .OrderByDescending(f => f.Salience)
                .ThenBy(f => f.Created)
                .ToList();
            foreach (var f in boundaries.Take(MaxBoundaryLines))
                Append("Boundary (honor this): " + f.Text);
            if (boundaries.Count > MaxBoundaryLines)
                Append($"(+{boundaries.Count - MaxBoundaryLines} more boundaries on file — stay careful.)");

            foreach (var f in RankFacts(facts.Where(f => f.Kind != MemoryFactKind.Boundary)))
            {
                if (!TryAppend($"- {f.Text}")) break;
            }

            var block = sb.ToString().TrimEnd();
            return block.Length == 0 ? null : block;
        }

        /// <summary>
        /// The one-line profile summary, or null when there is nothing deterministic to say (so the
        /// assembler never emits an empty "What you know about them:" line).
        /// </summary>
        internal static string? BuildProfileLine(IReadOnlyDictionary<string, object?> profile)
        {
            if (profile.Count == 0) return null;

            var ordered = new List<string>();
            foreach (var key in ProfileKeyOrder)
            {
                if (!profile.TryGetValue(key, out var value)) continue;
                var text = FormatProfileValue(value);
                if (text.Length > 0) ordered.Add($"{key}={text}");
            }
            foreach (var pair in profile
                         .Where(p => !ProfileKeyOrder.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                         .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var text = FormatProfileValue(pair.Value);
                if (text.Length > 0) ordered.Add($"{pair.Key}={text}");
            }

            return ordered.Count == 0 ? null : "What you know about them: " + string.Join(", ", ordered);
        }

        /// <summary>
        /// salience × e^(-days/30), jittered stably within the session so near-ties reshuffle between
        /// app runs but a genuinely more salient fact never loses to a stale one. Pinned facts sort
        /// ahead of everything: the user pinned them precisely so she would use them.
        /// </summary>
        internal IReadOnlyList<MemoryFact> RankFacts(IEnumerable<MemoryFact> facts)
        {
            var now = _clock();
            return facts
                .OrderByDescending(f => f.Pinned)
                .ThenByDescending(f => Score(f, now) * Jitter(f.Id))
                .ThenBy(f => f.Id, StringComparer.Ordinal) // total order, so ties never wobble
                .ToList();
        }

        /// <summary>salience × e^(-days/30) against <paramref name="now"/>. Never negative.</summary>
        internal static double Score(MemoryFact fact, DateTime now)
        {
            var anchor = fact.LastUsed ?? fact.Created;
            var days = Math.Max(0d, (now - anchor).TotalDays);
            return Math.Max(0d, fact.Salience) * Math.Exp(-days / RecencyDecayDays);
        }

        /// <summary>
        /// Stable per-session multiplier in [0.85, 1.15]. Deterministic in (session seed, fact id),
        /// so every call inside one app run produces the identical block — the whole point, since the
        /// block rides the prompt's dynamic tail and a churning tail is a cache miss every time.
        /// </summary>
        internal double Jitter(string factId)
        {
            unchecked
            {
                int h = _sessionSeed;
                foreach (char c in factId ?? string.Empty) h = h * 31 + c;
                // Splitmix-ish avalanche so adjacent ids don't land on adjacent multipliers.
                uint x = (uint)h;
                x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
                return 0.85 + (x / (double)uint.MaxValue) * 0.30;
            }
        }

        // ===================== wipe =====================

        /// <summary>
        /// Forgets everything: the in-memory model plus <c>memory.json</c>, <c>episodes.json</c> and
        /// <c>session.json</c> on disk (doc 01 §2.4 "Forget everything").
        ///
        /// <para>It also deletes the legacy <c>local_chat_history.json</c>, absorbing
        /// <c>AiServiceStrategy.ClearLocalHistory</c>. That is not tidiness: leaving it behind would
        /// let <see cref="CompanionSessionStore"/>'s one-time import resurrect the wiped conversation
        /// on the next launch, which is the exact opposite of what the button promises.</para>
        /// </summary>
        public void Wipe()
        {
            lock (_lock)
            {
                _profile.Clear();
                _relationship.Clear();
                _usage.Clear();
                _facts.Clear();
            }

            var dir = Path.GetDirectoryName(_path);
            foreach (var file in new[]
                     {
                         _path,
                         dir == null ? null : Path.Combine(dir, "episodes.json"),
                         dir == null ? null : Path.Combine(dir, "session.json"),
                         LegacyLocalHistoryPath()
                     })
            {
                if (string.IsNullOrEmpty(file)) continue;
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "MemoryStore: failed to delete {File} during wipe", file);
                }
            }

            App.Logger?.Information("MemoryStore: memory wiped");
        }

        private string? LegacyLocalHistoryPath()
        {
            // companion\memory.json → the app data root one level up.
            try
            {
                var companionDir = Path.GetDirectoryName(_path);
                var root = companionDir == null ? null : Path.GetDirectoryName(companionDir);
                return root == null ? null : Path.Combine(root, "local_chat_history.json");
            }
            catch { return null; }
        }

        // ===================== persistence =====================

        /// <summary>
        /// Chat-derived facts follow the existing dialogue toggle: with <c>ChatMemoryEnabled</c> off
        /// nothing the user typed reaches disk. Deterministic app signals (level, streak, archetype)
        /// are not dialogue and keep persisting — turning off chat memory should not make her forget
        /// what level you are.
        /// </summary>
        private static bool ChatDerivedPersistenceEnabled =>
            App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled != false;

        /// <summary>Schedules a debounced background save. Cheap and safe to call on every mutation.</summary>
        public void RequestSave()
        {
            if (_disposed) return;
            try { _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Writes memory.json synchronously. Used on shutdown and by tests.</summary>
        public void SaveNow()
        {
            string json;
            try
            {
                lock (_lock) json = SerializeLocked();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MemoryStore: failed to serialize memory");
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MemoryStore: failed to persist memory");
            }
        }

        /// <summary>
        /// Renders the document and sheds unprotected facts until it fits <see cref="SoftMaxBytes"/>.
        /// Caller holds <see cref="_lock"/>.
        /// </summary>
        private string SerializeLocked()
        {
            bool keepChatFacts = ChatDerivedPersistenceEnabled;
            var persistable = _facts
                .Where(f => keepChatFacts || !string.Equals(f.Source, MemoryFact.SourceChat, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var json = Render(persistable);
            if (Encoding.UTF8.GetByteCount(json) <= SoftMaxBytes) return json;

            // Over the soft cap. Drop the weakest unprotected facts a slice at a time; protected
            // (pinned / boundary) facts are never shed, so a store of nothing but those may still
            // exceed the cap — by design, and it cannot happen within MaxFacts of realistic text.
            var now = _clock();
            var shed = persistable
                .Where(f => !IsProtected(f))
                .OrderBy(f => Score(f, now))
                .ToList();

            int removed = 0;
            while (removed < shed.Count)
            {
                removed += Math.Max(1, shed.Count / 10);
                var survivors = persistable.Except(shed.Take(removed)).ToList();
                json = Render(survivors);
                if (Encoding.UTF8.GetByteCount(json) <= SoftMaxBytes)
                {
                    App.Logger?.Warning("MemoryStore: soft size cap hit, shed {Count} fact(s)", removed);
                    return json;
                }
            }

            App.Logger?.Warning("MemoryStore: memory.json still over the soft cap after shedding");
            return json;
        }

        private string Render(IEnumerable<MemoryFact> facts)
        {
            var payload = new PersistedMemory
            {
                Version = SchemaVersion,
                Profile = _profile.ToDictionary(
                    p => p.Key,
                    p => JsonSerializer.SerializeToElement(p.Value),
                    StringComparer.OrdinalIgnoreCase),
                Relationship = _relationship.ToDictionary(
                    r => r.Key,
                    r => new PersistedRelationship { ChatTurnsTotal = r.Value.ChatTurnsTotal, LastChat = r.Value.LastChat },
                    StringComparer.OrdinalIgnoreCase),
                Usage = new Dictionary<string, int>(_usage, StringComparer.OrdinalIgnoreCase),
                Facts = facts.Select(f => new PersistedFact
                {
                    Id = f.Id,
                    Text = f.Text,
                    Kind = f.Kind.ToString(),
                    Salience = f.Salience,
                    Created = f.Created,
                    LastUsed = f.LastUsed,
                    Uses = f.Uses,
                    Pinned = f.Pinned,
                    Source = f.Source
                }).ToList()
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private void Load()
        {
            string json;
            try
            {
                if (!File.Exists(_path)) return;
                json = File.ReadAllText(_path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MemoryStore: could not read memory.json — starting empty");
                return;
            }

            var parsed = ParseMemory(json);
            lock (_lock)
            {
                foreach (var p in parsed.Profile) _profile[p.Key] = p.Value;
                foreach (var r in parsed.Relationship) _relationship[r.Key] = r.Value;
                foreach (var u in parsed.Usage) _usage[u.Key] = u.Value;
                _facts.AddRange(parsed.Facts);
                EnforceFactCap();
            }

            App.Logger?.Information(
                "MemoryStore: loaded {Facts} fact(s), {Signals} profile signal(s)",
                parsed.Facts.Count, parsed.Profile.Count);
        }

        /// <summary>
        /// What a memory.json parsed down to. Pure and internal so the round trip and every corruption
        /// mode are unit-testable without touching %LOCALAPPDATA%.
        /// </summary>
        internal sealed record MemorySnapshot(
            IReadOnlyDictionary<string, object?> Profile,
            IReadOnlyDictionary<string, RelationshipState> Relationship,
            IReadOnlyDictionary<string, int> Usage,
            IReadOnlyList<MemoryFact> Facts)
        {
            public static MemorySnapshot Empty { get; } = new(
                new Dictionary<string, object?>(),
                new Dictionary<string, RelationshipState>(),
                new Dictionary<string, int>(),
                Array.Empty<MemoryFact>());
        }

        /// <summary>
        /// Parses memory.json. Never throws and never returns null: garbage, a truncated write, a
        /// hand-edited file with the wrong types — all of it degrades to "fewer memories", which is
        /// the only acceptable failure mode for a file the companion reads on every prompt.
        /// </summary>
        internal static MemorySnapshot ParseMemory(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return MemorySnapshot.Empty;

            PersistedMemory? doc;
            try
            {
                doc = JsonSerializer.Deserialize<PersistedMemory>(json, JsonOptions);
            }
            catch (JsonException) { return MemorySnapshot.Empty; }
            catch (NotSupportedException) { return MemorySnapshot.Empty; }

            if (doc == null) return MemorySnapshot.Empty;

            var profile = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in doc.Profile ?? new())
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                var value = FromJsonElement(pair.Value);
                if (value != null) profile[pair.Key] = value;
            }

            var relationship = new Dictionary<string, RelationshipState>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in doc.Relationship ?? new())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                relationship[pair.Key] = new RelationshipState(
                    Math.Max(0, pair.Value.ChatTurnsTotal), pair.Value.LastChat);
            }

            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in doc.Usage ?? new())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0) continue;
                usage[pair.Key] = pair.Value;
            }

            var facts = new List<MemoryFact>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in doc.Facts ?? new())
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Text)) continue;
                if (!Enum.TryParse<MemoryFactKind>(f.Kind, ignoreCase: true, out var kind)) continue;

                // A duplicated or missing id would make ForgetFact/pin ambiguous in the panel, so
                // re-mint rather than drop the fact.
                var id = f.Id?.Trim() ?? string.Empty;
                if (id.Length == 0 || !seen.Add(id))
                {
                    id = NewFactId();
                    seen.Add(id);
                }

                facts.Add(new MemoryFact(
                    Id: id,
                    Text: f.Text.Trim(),
                    Kind: kind,
                    Salience: double.IsFinite(f.Salience) ? Math.Clamp(f.Salience, 0d, 1d) : 0.5,
                    Created: f.Created == default ? DateTime.UtcNow : f.Created,
                    LastUsed: f.LastUsed,
                    Uses: Math.Max(0, f.Uses),
                    Pinned: f.Pinned,
                    Source: string.IsNullOrWhiteSpace(f.Source) ? MemoryFact.SourceChat : f.Source));
            }

            return new MemorySnapshot(profile, relationship, usage, facts);
        }

        // ===================== caps =====================

        /// <summary>Pinned and boundary facts are never evicted (doc 01 §2.2). Caller holds the lock.</summary>
        private static bool IsProtected(MemoryFact f) => f.Pinned || f.Kind == MemoryFactKind.Boundary;

        private void EnforceFactCap()
        {
            if (_facts.Count <= MaxFacts) return;

            var now = _clock();
            // Weakest first; protected facts are simply not candidates, so a store that is entirely
            // pinned/boundary stays over the cap rather than breaking the "never evicted" promise.
            var candidates = _facts
                .Where(f => !IsProtected(f))
                .OrderBy(f => Score(f, now))
                .ThenBy(f => f.Created)
                .ToList();

            int over = _facts.Count - MaxFacts;
            int evicted = 0;
            foreach (var f in candidates)
            {
                if (evicted >= over) break;
                _facts.Remove(f);
                evicted++;
            }

            if (evicted > 0)
                App.Logger?.Debug("MemoryStore: evicted {Count} low-value fact(s) at the {Cap} cap", evicted, MaxFacts);
        }

        // ===================== helpers =====================

        private static string NewFactId() => "f-" + Guid.NewGuid().ToString("N")[..12];

        private static bool ProfileValuesEqual(object? a, object? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is IEnumerable<string> ea && b is IEnumerable<string> eb) return ea.SequenceEqual(eb);
            return string.Equals(FormatProfileValue(a), FormatProfileValue(b), StringComparison.Ordinal);
        }

        /// <summary>Culture-invariant rendering, so a comma-decimal locale can't reshape the prompt.</summary>
        internal static string FormatProfileValue(object? value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s.Trim();
                case bool b: return b ? "true" : "false";
                case IEnumerable<string> list: return string.Join("/", list.Where(x => !string.IsNullOrWhiteSpace(x)));
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? string.Empty;
            }
        }

        private static object? FromJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var s = element.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                case JsonValueKind.Number:
                    // Deliberately two statements: a ?: would unify long and double into double and
                    // silently turn every stored level/streak into a floating-point number.
                    if (element.TryGetInt64(out var whole)) return whole;
                    return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Array:
                    var items = element.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();
                    return items.Length == 0 ? null : items;
                default:
                    return null; // objects/null/undefined carry nothing the prompt could render
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _signals?.Dispose(); } catch { /* shutdown is best-effort */ }
            try
            {
                if (_processExitHandler != null) AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
                _processExitHandler = null;
            }
            catch { }
            try { _saveTimer?.Dispose(); } catch { }
            try { SaveNow(); } catch { }
        }

        // ===================== persisted shape =====================

        private sealed class PersistedFact
        {
            public string Id { get; set; } = "";
            public string Text { get; set; } = "";
            public string Kind { get; set; } = nameof(MemoryFactKind.Event);
            public double Salience { get; set; } = 0.5;
            public DateTime Created { get; set; }
            public DateTime? LastUsed { get; set; }
            public int Uses { get; set; }
            public bool Pinned { get; set; }
            public string Source { get; set; } = MemoryFact.SourceChat;
        }

        private sealed class PersistedRelationship
        {
            public int ChatTurnsTotal { get; set; }
            public DateTime? LastChat { get; set; }
        }

        private sealed class PersistedMemory
        {
            public int Version { get; set; } = SchemaVersion;
            public Dictionary<string, JsonElement> Profile { get; set; } = new();
            public Dictionary<string, PersistedRelationship> Relationship { get; set; } = new();
            public Dictionary<string, int> Usage { get; set; } = new();
            public List<PersistedFact> Facts { get; set; } = new();
        }
    }
}
