using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>What came back off disk when the brain started.</summary>
    /// <param name="Turns">Dialogue turns, oldest first. Empty when memory is off or nothing was stored.</param>
    /// <param name="ImportedFromLegacy">
    /// True when these turns came from the one-time <c>local_chat_history.json</c> import rather than
    /// from <c>companion/session.json</c>.
    /// </param>
    public sealed record CompanionSessionSnapshot(IReadOnlyList<CompanionTurn> Turns, bool ImportedFromLegacy)
    {
        public static CompanionSessionSnapshot Empty { get; } =
            new(Array.Empty<CompanionTurn>(), false);
    }

    /// <summary>
    /// Where the companion's conversation lives between launches.
    /// </summary>
    public interface ICompanionSessionStore
    {
        /// <summary>Reads the stored conversation. Never throws; a broken file reads as empty.</summary>
        CompanionSessionSnapshot Load();

        /// <summary>
        /// Writes the dialogue. Callers pass ONLY turns that have cleared output moderation —
        /// the P2/H5 invariant is that a refused turn never reaches disk.
        /// </summary>
        void Save(IReadOnlyList<CompanionTurn> dialogueTurns);

        /// <summary>Deletes the stored conversation.</summary>
        void Wipe();
    }

    /// <summary>
    /// File-backed conversation store: <c>%LOCALAPPDATA%\ConditioningControlPanel\companion\session.json</c>.
    ///
    /// <para>Supersedes <c>local_chat_history.json</c>, which was local-Ollama-only. On first run the
    /// legacy file is imported once so local-AI users keep their memories; because the import
    /// immediately writes session.json, the next launch loads that instead and the import never
    /// repeats. The legacy file is left in place (harmless, and a safety net if a user rolls back).</para>
    ///
    /// <para>Only <see cref="TurnKind.UserChat"/> / <see cref="TurnKind.AssistantChat"/> are persisted:
    /// ambient events are stale the moment they're over, bark echoes replay from bark_rules.json, and
    /// system notes are housekeeping.</para>
    ///
    /// <para>Every path is gated by the existing <c>ChatMemoryEnabled</c> setting — off means nothing
    /// is read and nothing is written, exactly as <c>LocalAiService</c> behaved.</para>
    /// </summary>
    public sealed class CompanionSessionStore : ICompanionSessionStore
    {
        /// <summary>Schema version of session.json.</summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// Persisted dialogue cap, in messages. 100 = the 50 user/assistant pairs
        /// <c>LocalAiService</c> kept, so an imported history is never truncated on arrival.
        /// </summary>
        public const int MaxPersistedTurns = 100;

        private readonly string _sessionPath;
        private readonly string _legacyPath;

        /// <summary>
        /// Serialises writers. Persistence is fire-and-forget (<c>CompanionBrain.PersistAsync</c>
        /// spawns a Task per reply) and <c>Flush</c> runs on the shutdown thread, so two writes to the
        /// same path can overlap; the loser throws and that turn is lost.
        /// </summary>
        private readonly object _writeLock = new();

        public CompanionSessionStore(string? sessionPath = null, string? legacyPath = null)
        {
            _sessionPath = sessionPath ?? Path.Combine(App.UserDataPath, "companion", "session.json");
            _legacyPath = legacyPath ?? Path.Combine(App.UserDataPath, "local_chat_history.json");
        }

        /// <summary>Full path of session.json — surfaced for the privacy panel and diagnostics.</summary>
        public string SessionPath => _sessionPath;

        private static bool MemoryEnabled =>
            App.Settings?.Current?.CompanionPrompt?.ChatMemoryEnabled != false;

        // ---------- persisted shape ----------

        private sealed class PersistedTurn
        {
            public string Kind { get; set; } = nameof(TurnKind.UserChat);
            public string Text { get; set; } = "";
            public string? Mood { get; set; }
            public DateTime Utc { get; set; }
        }

        private sealed class PersistedSession
        {
            public int Version { get; set; } = SchemaVersion;
            public List<PersistedTurn> Turns { get; set; } = new();
        }

        /// <summary>Legacy <c>local_chat_history.json</c> element — a bare role/content pair.</summary>
        private sealed class LegacyTurn
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        // ---------- load ----------

        public CompanionSessionSnapshot Load()
        {
            if (!MemoryEnabled)
            {
                App.Logger?.Debug("CompanionSessionStore: chat memory disabled — starting with an empty session");
                return CompanionSessionSnapshot.Empty;
            }

            try
            {
                if (File.Exists(_sessionPath))
                {
                    var turns = ParseSession(File.ReadAllText(_sessionPath));
                    App.Logger?.Information("CompanionSessionStore: restored {Count} turn(s) from session.json", turns.Count);
                    return new CompanionSessionSnapshot(turns, ImportedFromLegacy: false);
                }

                if (File.Exists(_legacyPath))
                {
                    var imported = ImportLegacyHistory(File.ReadAllText(_legacyPath));
                    if (imported.Count > 0)
                    {
                        App.Logger?.Information(
                            "CompanionSessionStore: imported {Count} turn(s) from local_chat_history.json (one-time)",
                            imported.Count);
                        // Write straight away so the import is genuinely one-time even if this run
                        // never produces a reply worth saving.
                        Save(imported);
                        return new CompanionSessionSnapshot(imported, ImportedFromLegacy: true);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSessionStore: failed to load companion session");
            }

            return CompanionSessionSnapshot.Empty;
        }

        /// <summary>
        /// Parses session.json. Pure and internal so the round trip is testable without touching
        /// %LOCALAPPDATA%. Unknown kinds, blank text and non-dialogue entries are dropped rather than
        /// throwing — a partially corrupt file should cost memories, not the chat box.
        /// </summary>
        internal static List<CompanionTurn> ParseSession(string json)
        {
            var result = new List<CompanionTurn>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            PersistedSession? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PersistedSession>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return result; }

            if (parsed?.Turns == null) return result;

            foreach (var t in parsed.Turns)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                if (!Enum.TryParse<TurnKind>(t.Kind, ignoreCase: true, out var kind)) continue;
                if (kind is not (TurnKind.UserChat or TurnKind.AssistantChat)) continue;

                // Replies persisted before the 0806 sigil fix carry «... said aloud: "..."» shells;
                // heal them on the way in so old files stop re-teaching the model the pattern.
                var text = kind == TurnKind.AssistantChat
                    ? Services.AiTextHygiene.UnwrapSpokenSigil(t.Text)
                    : t.Text;
                if (text.Length == 0) continue;

                result.Add(CompanionTurn.Create(kind, text, t.Mood,
                    utc: t.Utc == default ? DateTime.UtcNow : t.Utc));
            }

            return Trim(result);
        }

        /// <summary>
        /// Converts the legacy local-Ollama history (a flat <c>[{role, content}]</c> array) into
        /// typed turns. Pure and internal so the migration is unit-tested — this runs exactly once
        /// per user and there is no second chance to get it right.
        /// Only user/assistant entries survive; the enrichment "[CONTEXT BLOCK — NOT DIALOGUE]"
        /// preamble is context, not conversation, and is dropped exactly as
        /// <c>LocalAiService.IsDialogueTurn</c> dropped it.
        /// </summary>
        internal static List<CompanionTurn> ImportLegacyHistory(string json)
        {
            var result = new List<CompanionTurn>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            List<LegacyTurn>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<LegacyTurn>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return result; }

            if (parsed == null) return result;

            foreach (var t in parsed)
            {
                if (string.IsNullOrWhiteSpace(t.Role) || string.IsNullOrWhiteSpace(t.Content)) continue;
                if (t.Content.Contains("[CONTEXT BLOCK — NOT DIALOGUE]", StringComparison.Ordinal)) continue;

                TurnKind kind;
                if (string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase)) kind = TurnKind.UserChat;
                else if (string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase)) kind = TurnKind.AssistantChat;
                else continue; // system / anything else is rebuilt fresh per request

                result.Add(CompanionTurn.Create(kind, t.Content));
            }

            return Trim(result);
        }

        /// <summary>Keeps the most recent <see cref="MaxPersistedTurns"/>, dropping from the front.</summary>
        internal static List<CompanionTurn> Trim(List<CompanionTurn> turns)
        {
            if (turns.Count <= MaxPersistedTurns) return turns;
            return turns.Skip(turns.Count - MaxPersistedTurns).ToList();
        }

        // ---------- save ----------

        public void Save(IReadOnlyList<CompanionTurn> dialogueTurns)
        {
            if (!MemoryEnabled) return;

            try
            {
                var dialogue = (dialogueTurns ?? Array.Empty<CompanionTurn>())
                    .Where(t => t != null && t.IsDialogue && !string.IsNullOrWhiteSpace(t.Text))
                    .ToList();
                dialogue = Trim(dialogue);

                var payload = new PersistedSession
                {
                    Version = SchemaVersion,
                    Turns = dialogue.Select(t => new PersistedTurn
                    {
                        Kind = t.Kind.ToString(),
                        Text = t.Text,
                        Mood = t.Mood,
                        Utc = t.Utc
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
                lock (_writeLock)
                {
                    var dir = Path.GetDirectoryName(_sessionPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    // Temp-then-move: a truncated session.json parses as empty, so a crash mid-write
                    // costs the user the whole conversation with no error and no recovery path.
                    MemoryStore.AtomicWrite(_sessionPath, json);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSessionStore: failed to persist companion session");
            }
        }

        public void Wipe()
        {
            try
            {
                lock (_writeLock)
                {
                    if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
                    // A .tmp left behind by a crashed write is the same conversation on disk.
                    var tmp = _sessionPath + ".tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                App.Logger?.Information("CompanionSessionStore: session wiped");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionSessionStore: failed to wipe companion session");
            }
        }
    }
}
