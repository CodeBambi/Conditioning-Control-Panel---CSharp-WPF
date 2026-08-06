using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Train 1 SHELL implementation of <see cref="IMemoryStore"/>: in-memory only, no disk, no LLM.
    ///
    /// <para>It is a working store rather than a bag of no-ops — CRUD really works and
    /// <see cref="GetInjectionBlock"/> really renders whatever it holds within budget — so the brain
    /// and its tests exercise the real code path. What it deliberately does NOT do is persist
    /// (<c>companion/memory.json</c>), mirror app signals automatically, decay salience or evict:
    /// that is the memory agent's branch, and LLM extraction is Train 4.</para>
    ///
    /// <para>Because nothing is loaded at construction, a stock Train 1 build injects nothing —
    /// <see cref="GetInjectionBlock"/> returns null and the prompt tail is unchanged from today.
    /// That is the intended kill-switch-free default: no behaviour change until memory ships.</para>
    /// </summary>
    public sealed class MemoryStore : IMemoryStore
    {
        /// <summary>Hard ceiling on the injected block regardless of the budget a caller asks for.</summary>
        public const int MaxInjectionTokens = 500;

        private readonly object _lock = new();
        private readonly Dictionary<string, object?> _profile = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<MemoryFact> _facts = new();

        public IReadOnlyDictionary<string, object?> Profile
        {
            get { lock (_lock) return new Dictionary<string, object?>(_profile, StringComparer.OrdinalIgnoreCase); }
        }

        public void UpdateProfileSignal(string key, object? value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (_lock)
            {
                if (value == null) _profile.Remove(key);
                else _profile[key] = value;
            }
        }

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
            var fact = new MemoryFact(
                Id: "f-" + Guid.NewGuid().ToString("N")[..12],
                Text: body,
                Kind: kind,
                Salience: Math.Clamp(salience, 0d, 1d),
                Created: DateTime.UtcNow,
                LastUsed: null,
                Uses: 0,
                Pinned: false,
                Source: string.IsNullOrWhiteSpace(source) ? MemoryFact.SourceChat : source);

            if (body.Length == 0) return fact; // nothing worth storing; caller still gets a valid record
            lock (_lock) _facts.Add(fact);
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
                return true;
            }
        }

        public bool ForgetFact(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock) return _facts.RemoveAll(f => f.Id == id) > 0;
        }

        public void NoteFactUsed(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_lock)
            {
                int i = _facts.FindIndex(f => f.Id == id);
                if (i < 0) return;
                _facts[i] = _facts[i] with { LastUsed = DateTime.UtcNow, Uses = _facts[i].Uses + 1 };
            }
        }

        public string? GetInjectionBlock(int tokenBudget)
        {
            int budget = Math.Min(Math.Max(tokenBudget, 0), MaxInjectionTokens);
            if (budget <= 0) return null;

            Dictionary<string, object?> profile;
            List<MemoryFact> facts;
            lock (_lock)
            {
                if (_profile.Count == 0 && _facts.Count == 0) return null;
                profile = new Dictionary<string, object?>(_profile);
                facts = _facts.ToList();
            }

            var sb = new StringBuilder();
            int spent = 0;

            bool TryAppend(string line)
            {
                int cost = ChatSession.ApproxTokens(line) + 1; // +1 for the newline
                if (spent + cost > budget) return false;
                sb.AppendLine(line);
                spent += cost;
                return true;
            }

            if (profile.Count > 0)
            {
                var pairs = profile.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(p => $"{p.Key}={p.Value}");
                TryAppend("What you know about them: " + string.Join(", ", pairs));
            }

            // Boundaries first and unconditionally-attempted — they are the one class that must never
            // be crowded out by a chattier joke fact.
            foreach (var f in facts.Where(f => f.Kind == MemoryFactKind.Boundary))
            {
                if (!TryAppend("Boundary (honor this): " + f.Text)) break;
            }

            foreach (var f in facts.Where(f => f.Kind != MemoryFactKind.Boundary)
                         .OrderByDescending(f => f.Pinned)
                         .ThenByDescending(f => f.Salience))
            {
                if (!TryAppend($"- {f.Text}")) break;
            }

            var block = sb.ToString().TrimEnd();
            return block.Length == 0 ? null : block;
        }

        public void Wipe()
        {
            lock (_lock)
            {
                _profile.Clear();
                _facts.Clear();
            }
        }
    }
}
