using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models.AiEnrichment;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Persistent, sliding-window chat memory shared by AI providers.
    /// Stores only user/assistant dialogue turns (never system or enrichment blocks).
    /// Providers keep system/enrichment ephemeral and send only the most recent
    /// <see cref="MaxSendPairs"/> turns to the model.
    /// </summary>
    public sealed class AiChatMemory
    {
        private readonly string _storagePath;
        private readonly Func<bool> _isEnabled;
        private readonly int _maxPersistedPairs;
        private readonly List<AiMessage> _turns = new();
        private readonly object _lock = new();

        public AiChatMemory(string storageKey, Func<bool> isEnabled, int maxPersistedPairs = 50)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
                throw new ArgumentException("Storage key is required", nameof(storageKey));
            _storagePath = Path.Combine(App.UserDataPath, $"{storageKey}_chat_history.json");
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            _maxPersistedPairs = Math.Max(0, maxPersistedPairs);
        }

        /// <summary>
        /// Number of user/assistant turns loaded from disk at construction. Used by
        /// providers that want to signal when persistent memory actually surfaces.
        /// </summary>
        public int RestoredTurnCount { get; private set; }

        /// <summary>
        /// Loads persisted turns from disk. Safe to call multiple times; clears first.
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                _turns.Clear();
                RestoredTurnCount = 0;

                if (!_isEnabled()) return;

                try
                {
                    if (!File.Exists(_storagePath)) return;
                    var json = File.ReadAllText(_storagePath);
                    var turns = JsonSerializer.Deserialize<List<PersistedTurn>>(json);
                    if (turns == null) return;

                    foreach (var t in turns)
                    {
                        if (string.IsNullOrEmpty(t.Role) || string.IsNullOrEmpty(t.Content)) continue;
                        if (t.Role != "user" && t.Role != "assistant") continue;
                        _turns.Add(new AiMessage(t.Role, t.Content));
                    }

                    RestoredTurnCount = _turns.Count;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AiChatMemory: failed to load history from {Path}", _storagePath);
                }
            }
        }

        /// <summary>
        /// Persists the in-memory turn list to disk, trimmed to <see cref="_maxPersistedPairs"/>.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                if (!_isEnabled()) return;

                try
                {
                    var dialogue = _turns
                        .Where(m => m.Role == "user" || m.Role == "assistant")
                        .Where(m => !string.IsNullOrEmpty(m.Content)
                                    && !m.Content.Contains("[CONTEXT BLOCK — NOT DIALOGUE]"))
                        .Select(m => new PersistedTurn { Role = m.Role, Content = m.Content })
                        .ToList();

                    int maxMessages = _maxPersistedPairs * 2;
                    if (dialogue.Count > maxMessages)
                    {
                        dialogue = dialogue.Skip(dialogue.Count - maxMessages).ToList();
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
                    var json = JsonSerializer.Serialize(dialogue, new JsonSerializerOptions { WriteIndented = false });
                    File.WriteAllText(_storagePath, json);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AiChatMemory: failed to save history to {Path}", _storagePath);
                }
            }
        }

        /// <summary>
        /// Clears in-memory history and deletes the on-disk file.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _turns.Clear();
                RestoredTurnCount = 0;
                try { if (File.Exists(_storagePath)) File.Delete(_storagePath); }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AiChatMemory: failed to delete history file {Path}", _storagePath);
                }
            }
        }

        public void AddUserTurn(string content)
        {
            lock (_lock) _turns.Add(new AiMessage("user", content));
        }

        public void AddAssistantTurn(string content)
        {
            lock (_lock) _turns.Add(new AiMessage("assistant", content));
        }

        /// <summary>
        /// Removes the most recent user turn if it is unanswered (used when the request
        /// fails before an assistant reply is produced).
        /// </summary>
        public void RemoveLastUserTurn()
        {
            lock (_lock)
            {
                if (_turns.Count > 0 && _turns[^1].Role == "user")
                    _turns.RemoveAt(_turns.Count - 1);
            }
        }

        /// <summary>
        /// Removes the most recent assistant turn (used when output moderation blocks
        /// the reply so the rejected exchange is not remembered).
        /// </summary>
        public void RemoveLastAssistantTurn()
        {
            lock (_lock)
            {
                if (_turns.Count > 0 && _turns[^1].Role == "assistant")
                    _turns.RemoveAt(_turns.Count - 1);
            }
        }

        /// <summary>
        /// Builds the message list to send to the model: system, optional enrichment,
        /// then up to <paramref name="maxSendPairs"/> recent user/assistant turns.
        /// The caller is expected to append the current user message before sending.
        /// </summary>
        public List<AiMessage> BuildSendMessages(
            AiMessage systemMessage,
            AiMessage? enrichmentMessage,
            int maxSendPairs)
        {
            lock (_lock)
            {
                var messages = new List<AiMessage> { systemMessage };
                if (enrichmentMessage != null)
                    messages.Add(enrichmentMessage);

                maxSendPairs = Math.Max(0, maxSendPairs);
                int take = Math.Min(_turns.Count, maxSendPairs * 2);
                if (take > 0)
                    messages.AddRange(_turns.Skip(_turns.Count - take));

                return messages;
            }
        }

        private sealed class PersistedTurn
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }
    }
}
