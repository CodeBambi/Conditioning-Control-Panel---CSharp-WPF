using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Mirrors deterministic app state into the companion's memory profile (doc 01 §2.2,
    /// "Deterministic app facts are free").
    ///
    /// <para>Level, streak, session count, quiz archetype, which features the user actually touches —
    /// the process already knows all of it, and <c>BarkService</c> has been reading the same fields
    /// for its rule conditions for a year. Copying them into <c>memory.json</c> costs zero tokens and
    /// buys most of the "she knows me" feeling; the LLM extractor that mines the *conversation* is
    /// Train 4 and is deliberately absent here.</para>
    ///
    /// <para><b>Cheap.</b> Writes are debounced through <see cref="MemoryStore.RequestSave"/> (2s) and
    /// the profile refresh is change-detecting, so the hundreds of settings notifications an active
    /// session produces collapse into a handful of disk writes. Feature counters are cooldown-gated
    /// (<see cref="FeatureUseCooldown"/>) because a flash fires hundreds of times an hour and would
    /// otherwise drown every other feature in the "favourite" ranking.</para>
    ///
    /// <para><b>Never load-bearing.</b> Every subscription is null-guarded and every callback is
    /// wrapped: a service that failed to construct costs a signal, never a crash. Nothing here
    /// touches the UI, so no dispatcher hop is needed.</para>
    /// </summary>
    public sealed class MemorySignalWriter : IDisposable
    {
        /// <summary>
        /// Minimum gap between two counted uses of the same feature. Turns "the flash service fired
        /// 400 times" into "they engaged with flashes in N distinct stretches", which is what
        /// "favourite feature" actually means.
        /// </summary>
        public static readonly TimeSpan FeatureUseCooldown = TimeSpan.FromMinutes(5);

        /// <summary>How many features the profile line names.</summary>
        public const int MaxFavoriteFeatures = 3;

        /// <summary>A feature needs at least this many counted uses before it can be a "favourite".</summary>
        public const int FavoriteFeatureFloor = 3;

        // Feature ids. Short and stable — they are written to disk and rendered into the prompt.
        public const string FeatureFlash = "flash";
        public const string FeatureVideo = "video";
        public const string FeatureSubliminal = "subliminal";
        public const string FeatureBubbles = "bubbles";
        public const string FeatureBrainDrain = "braindrain";
        public const string FeatureMindWipe = "mindwipe";
        public const string FeatureMantra = "mantra";

        /// <summary>
        /// AppSettings properties worth a profile refresh. Everything else notifying is noise —
        /// AppSettings raises PropertyChanged for hundreds of unrelated members.
        /// </summary>
        private static readonly HashSet<string> WatchedSettings = new(StringComparer.Ordinal)
        {
            nameof(AppSettings.PlayerLevel),
            nameof(AppSettings.CurrentStreak),
            nameof(AppSettings.TotalSessions),
            nameof(AppSettings.LatestQuizArchetype)
        };

        private readonly MemoryStore _store;
        private readonly Func<DateTime> _clock;
        private readonly List<Action> _unsubscribe = new();
        private readonly Dictionary<string, DateTime> _lastCounted = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _started;
        private bool _disposed;
        private bool _mantraWired;

        public MemorySignalWriter(MemoryStore store, Func<DateTime>? utcClock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = utcClock ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// The head's signal sources (WPF: App.Progression, the feature services and App.Companion;
        /// Avalonia: ProgressionBank and its overlays). Each calls <see cref="Wire{THandler}"/> with
        /// <see cref="SafeRefresh"/>, <see cref="NoteFeatureUse"/> or <see cref="NoteChatTurn"/>.
        /// Unseeded, only the settings mirror runs.
        /// </summary>
        public static volatile Action<MemorySignalWriter>? SourcesHook;

        /// <summary>
        /// Sources that do not exist yet at <see cref="Start"/> (WPF: App.Mantra is built ~200 lines
        /// after the brain). Returns true once it has wired them, so later calls are no-ops.
        /// </summary>
        public static volatile Func<MemorySignalWriter, bool>? DeferredSourcesHook;

        /// <summary>
        /// Takes an immediate snapshot of app state, then subscribes to the events that can change it.
        /// Idempotent; safe to call before the services it wants exist (they are simply skipped).
        /// </summary>
        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;

            RefreshProfile();
            WireSettings();
            try { SourcesHook?.Invoke(this); }
            catch (Exception ex) { Log.Debug("MemorySignalWriter: signal sources failed: {Error}", ex.Message); }
            WireDeferredSources();

            Log.Debug("MemorySignalWriter: mirroring {Count} app signal source(s)", _unsubscribe.Count);
        }

        /// <summary>
        /// Wires the signal sources that do not exist yet at <see cref="Start"/> time.
        ///
        /// <para><see cref="Start"/> runs inside <c>new MemoryStore()</c> inside
        /// <c>new CompanionBrain(Ai)</c>, which WPF <c>App.OnStartup</c> builds ~200 lines before
        /// <c>App.Mantra</c>, and <see cref="Start"/> is idempotent-by-flag — so without a second pass
        /// the mantra counter is never subscribed for the whole process lifetime. Called once from the
        /// end of <c>OnStartup</c>; idempotent, so a second call costs nothing.</para>
        /// </summary>
        public void WireDeferredSources()
        {
            if (_disposed || _mantraWired) return;
            try { _mantraWired = DeferredSourcesHook?.Invoke(this) == true; }
            catch (Exception ex) { Log.Debug("MemorySignalWriter: deferred sources failed: {Error}", ex.Message); }
            if (_mantraWired) Log.Debug("MemorySignalWriter: deferred sources wired");
        }

        /// <summary>Unsubscribes everything. Safe to call twice, or without a prior <see cref="Start"/>.</summary>
        public void Stop()
        {
            List<Action> handlers;
            lock (_lock)
            {
                handlers = _unsubscribe.ToList();
                _unsubscribe.Clear();
            }

            foreach (var off in handlers)
            {
                try { off(); }
                catch (Exception ex) { Log.Debug("MemorySignalWriter: unsubscribe failed: {Error}", ex.Message); }
            }
            _started = false;
            _mantraWired = false;
        }

        // ===================== wiring =====================

        /// <summary>
        /// Subscribes with a matching unsubscriber recorded up front, so <see cref="Stop"/> can never
        /// drift out of sync with <see cref="Start"/> — the leak this pattern prevents is a settings
        /// object holding a dead writer for the life of the process.
        /// </summary>
        public void Wire<THandler>(Action<THandler> add, Action<THandler> remove, THandler handler)
        {
            try
            {
                add(handler);
                lock (_lock) _unsubscribe.Add(() => remove(handler));
            }
            catch (Exception ex)
            {
                Log.Debug("MemorySignalWriter: could not wire a signal: {Error}", ex.Message);
            }
        }

        private void WireSettings()
        {
            var settings = CurrentSettings();
            if (settings == null) return;

            PropertyChangedEventHandler handler = (_, e) =>
            {
                try
                {
                    if (e?.PropertyName != null && !WatchedSettings.Contains(e.PropertyName)) return;
                    RefreshProfile();
                }
                catch (Exception ex)
                {
                    Log.Debug("MemorySignalWriter: settings signal failed: {Error}", ex.Message);
                }
            };
            Wire<PropertyChangedEventHandler>(h => settings.PropertyChanged += h, h => settings.PropertyChanged -= h, handler);
        }

        /// <summary>WPF App.Settings?.Current: null until a head seeds CoreSettings (never the fallback).</summary>
        private static AppSettings? CurrentSettings() => CoreSettings.HasProvider ? CoreSettings.Current : null;

        /// <summary>
        /// The per-mod chat counter (doc 01 §2.2 relationship block), fed from the app's "the user just
        /// talked to her" signal, so the count is identical whether the reply came from the brain or the
        /// legacy path.
        /// </summary>
        public void NoteChatTurn(string? modId)
        {
            try { if (!CompanionExperience.IsV2Enabled) _store.NoteChatTurn(modId); }
            catch (Exception ex) { Log.Debug("MemorySignalWriter: chat-turn signal failed: {Error}", ex.Message); }
        }

        public void SafeRefresh()
        {
            try { RefreshProfile(); }
            catch (Exception ex) { Log.Debug("MemorySignalWriter: profile refresh failed: {Error}", ex.Message); }
        }

        // ===================== the actual mirroring =====================

        /// <summary>
        /// Recomputes every deterministic profile signal and pushes it into the store. Individual
        /// writes are change-detecting inside <see cref="MemoryStore.UpdateProfileSignal"/>, so an
        /// unchanged refresh costs nothing and schedules no save.
        /// </summary>
        public void RefreshProfile()
        {
            var signals = BuildProfileSignals(
                CurrentSettings(),
                _store.FeatureUsage,
                _clock(),
                ExistingFirstSeen());

            foreach (var pair in signals)
                _store.UpdateProfileSignal(pair.Key, pair.Value);
        }

        private object? ExistingFirstSeen()
            => _store.Profile.TryGetValue(MemoryStore.KeyFirstSeen, out var v) ? v : null;

        /// <summary>
        /// Pure signal computation: settings + usage counters in, profile keys out. Kept static and
        /// internal so the mapping is unit-testable without an app instance — which matters because a
        /// wrong key here is invisible (the prompt just quietly stops mentioning your streak).
        /// </summary>
        /// <param name="existingFirstSeen">
        /// The already-stored firstSeen, if any. It is a latch: the first day we ever wrote a profile
        /// is the anniversary the companion celebrates, and recomputing it would silently reset that.
        /// </param>
        internal static IReadOnlyList<KeyValuePair<string, object?>> BuildProfileSignals(
            AppSettings? settings,
            IReadOnlyDictionary<string, int> usage,
            DateTime utcNow,
            object? existingFirstSeen)
        {
            var result = new List<KeyValuePair<string, object?>>();

            void Set(string key, object? value) => result.Add(new KeyValuePair<string, object?>(key, value));

            // firstSeen latches on the first write and never moves again.
            var firstSeen = existingFirstSeen as string;
            if (string.IsNullOrWhiteSpace(firstSeen))
                firstSeen = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Set(MemoryStore.KeyFirstSeen, firstSeen);

            if (settings != null)
            {
                var username = settings.UserDisplayName?.Trim();
                Set(MemoryStore.KeyUsername, string.IsNullOrWhiteSpace(username) ? null : username);
                Set(MemoryStore.KeyLevel, (long)settings.PlayerLevel);
                Set(MemoryStore.KeyStreakDays, (long)settings.CurrentStreak);
                Set(MemoryStore.KeyTotalSessions, (long)settings.TotalSessions);

                var archetype = settings.LatestQuizArchetype?.Trim();
                // null clears the key — an unset archetype must not linger as "archetype=" noise.
                Set(MemoryStore.KeyArchetype, string.IsNullOrWhiteSpace(archetype) ? null : archetype);
            }

            var favorites = TopFeatures(usage);
            Set(MemoryStore.KeyFavoriteFeatures, favorites.Count == 0 ? null : favorites.ToArray());

            return result;
        }

        /// <summary>
        /// The <c>favoriteFeatures</c> ranking: most-counted first, ties broken by name so the profile
        /// line is byte-stable between calls (a churning line is a prompt-cache miss).
        /// Features below <see cref="FavoriteFeatureFloor"/> uses are ignored — one accidental video
        /// is not a favourite.
        /// </summary>
        internal static IReadOnlyList<string> TopFeatures(IReadOnlyDictionary<string, int> usage, int max = MaxFavoriteFeatures)
        {
            if (usage == null || usage.Count == 0 || max <= 0) return Array.Empty<string>();
            return usage
                .Where(p => p.Value >= FavoriteFeatureFloor && !string.IsNullOrWhiteSpace(p.Key))
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Select(p => p.Key)
                .ToList();
        }

        /// <summary>
        /// Counts one use of a feature, at most once per <see cref="FeatureUseCooldown"/>.
        /// Returns true when the counter actually moved — the return value exists for the tests, the
        /// production callers ignore it.
        /// </summary>
        public bool NoteFeatureUse(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature) || _disposed) return false;

            var now = _clock();
            lock (_lock)
            {
                if (_lastCounted.TryGetValue(feature, out var last) && now - last < FeatureUseCooldown)
                    return false;
                _lastCounted[feature] = now;
            }

            try
            {
                _store.NoteFeatureUsed(feature);
                // A new favourite only becomes visible once the profile line is rebuilt.
                RefreshProfile();
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("MemorySignalWriter: feature signal failed: {Error}", ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
