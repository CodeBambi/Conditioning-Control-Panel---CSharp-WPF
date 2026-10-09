using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>
    /// What the "clip" art view needs: the clip on disk, its poster (may be null), and a way to
    /// tell the provider the clip really played, which moves the rotation on.
    /// </summary>
    public sealed record ShowcaseClipArt(string ClipId, string VideoPath, string? PosterPath, Action<string>? Played);

    /// <summary>
    /// The premium showcase: at most ONE card, a feature above the viewer's plan with its silent
    /// clip. Prime viewers get nothing here (a tip takes the slot). The clips come from a
    /// manifest on a GitHub release, downloaded on first need and cached; until the clip whose
    /// turn it is sits checked on disk, the provider offers no card and raises
    /// <see cref="Changed"/> once it does. Clips go round in a per-install order, one per cycle:
    /// the rotation moves when a clip actually plays.
    /// </summary>
    public sealed class ShowcaseProvider : IBillboardProvider
    {
        public const string ManifestUrl =
            "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/showcase-clips/showcase.json";

        /// <summary>How long a fetched manifest stands before the next look.</summary>
        public static readonly TimeSpan ManifestFreshFor = TimeSpan.FromHours(6);

        /// <summary>After a failed fetch, wait this long before trying again.</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

        private readonly object _gate = new();
        private readonly ShowcaseCache _cache;
        private readonly string _statePath;
        private readonly Func<string, string?> _text;
        private readonly Func<DateTime> _utcNow;
        private readonly SynchronizationContext? _ui;

        private bool _started;
        private ShowcaseState _state = new();
        private ShowcaseManifest _manifest = ShowcaseManifest.Empty;
        private IReadOnlyList<string> _order = Array.Empty<string>();
        private DateTime _nextFetchUtc = DateTime.MinValue;
        private int _fetching;

        public ShowcaseProvider()
            : this(new ShowcaseCache(Path.Combine(App.UserDataPath, "showcase"), new Uri(ManifestUrl)), LocText, () => DateTime.UtcNow)
        {
        }

        internal ShowcaseProvider(ShowcaseCache cache, Func<string, string?> text, Func<DateTime> utcNow)
        {
            _cache = cache;
            _statePath = Path.Combine(cache.Root, "state.json");
            _text = text;
            _utcNow = utcNow;
            _ui = SynchronizationContext.Current;
        }

        public string Id => "showcase";

        public event EventHandler? Changed;

        public IEnumerable<BillboardCardSpec> Current(BillboardContext context)
        {
            EnsureStarted();
            MaybeRefreshManifest();

            if (context.Tier == BillboardTier.Prime) return Array.Empty<BillboardCardSpec>();

            ShowcaseClip? clip;
            lock (_gate)
            {
                // Only clips the app has copy for may take a turn, or the rotation could park on one it can never show.
                var known = _manifest.Clips.Where(c => HasCopy(c.Id));
                var eligible = ShowcaseRules.EligibleIds(known, context.Tier);
                var nextId = ShowcaseRules.NextClip(_order, eligible, _state.LastShown);
                clip = nextId == null ? null : _manifest.Clips.FirstOrDefault(c => c.Id == nextId);
            }
            if (clip == null) return Array.Empty<BillboardCardSpec>();

            if (!_cache.IsReady(clip))
            {
                Fetch(clip);
                return Array.Empty<BillboardCardSpec>();
            }

            var art = new ShowcaseClipArt(clip.Id, _cache.ClipPath(clip), _cache.ReadyPoster(clip), OnPlayed);
            var card = ShowcaseRules.BuildCard(clip, art, _text);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        }

        /// <summary>The showcase's only button is a Tab action the deck runs itself.</summary>
        public void Invoke(string actionTarget) { }

        // ---- rotation -------------------------------------------------------------------

        /// <summary>
        /// The clip view calls this the first time its clip starts playing. The rotation moves past
        /// that clip (persisted) and the next clip is fetched now so it is ready for the next cycle.
        /// Changed is deliberately NOT raised: the deck picks the next clip at its next rebuild.
        /// </summary>
        internal void OnPlayed(string clipId)
        {
            ShowcaseClip? prefetch = null;
            lock (_gate)
            {
                if (string.Equals(_state.LastShown, clipId, StringComparison.Ordinal)) return;
                _state.LastShown = clipId;
                _state.Save(_statePath);

                // Prefetch for the widest audience the next turn could serve; a clip the viewer
                // cannot see costs one download and is never shown to them.
                var known = _manifest.Clips.Where(c => HasCopy(c.Id));
                var eligible = ShowcaseRules.EligibleIds(known, BillboardTier.Free);
                var nextId = ShowcaseRules.NextClip(_order, eligible, clipId);
                prefetch = nextId == null ? null : _manifest.Clips.FirstOrDefault(c => c.Id == nextId);
            }
            if (prefetch != null && !_cache.IsReady(prefetch)) _ = _cache.EnsureAsync(prefetch);
        }

        // ---- manifest -----------------------------------------------------------------------

        private void EnsureStarted()
        {
            if (_started) return;
            lock (_gate)
            {
                if (_started) return;
                _started = true;
                _state = ShowcaseState.Load(_statePath);
                ApplyManifest(ShowcaseManifestParser.Parse(_cache.ReadManifestFromDisk()));
                var fetched = _state.ManifestFetchedUtc;
                _nextFetchUtc = fetched.HasValue && _manifest.Clips.Count > 0
                    ? fetched.Value + ManifestFreshFor
                    : DateTime.MinValue;
            }
        }

        private void ApplyManifest(ShowcaseManifest manifest)
        {
            _manifest = manifest;
            _order = ShowcaseRules.InstallOrder(manifest.Clips.Select(c => c.Id), _state.Seed);
        }

        private void MaybeRefreshManifest()
        {
            if (_utcNow() < _nextFetchUtc) return;
            if (Interlocked.Exchange(ref _fetching, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    var json = await _cache.FetchManifestAsync().ConfigureAwait(false);
                    var now = _utcNow();
                    if (json == null)
                    {
                        _nextFetchUtc = now + RetryAfter;
                        return;
                    }
                    var parsed = ShowcaseManifestParser.Parse(json);
                    bool changed;
                    lock (_gate)
                    {
                        changed = !SameClips(_manifest, parsed);
                        ApplyManifest(parsed);
                        _state.ManifestFetchedUtc = now;
                        _state.Save(_statePath);
                        _nextFetchUtc = now + ManifestFreshFor;
                    }
                    _cache.Prune(parsed.Clips);
                    if (changed) RaiseChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref _fetching, 0);
                }
            });
        }

        private static bool SameClips(ShowcaseManifest a, ShowcaseManifest b) =>
            a.Clips.Count == b.Clips.Count && a.Clips.Zip(b.Clips).All(p => p.First == p.Second);

        private void Fetch(ShowcaseClip clip)
        {
            _cache.EnsureAsync(clip).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result) RaiseChanged();
            }, TaskScheduler.Default);
        }

        private void RaiseChanged()
        {
            void Raise() { try { Changed?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { App.Logger?.Warning("Showcase Changed handler threw: {Message}", ex.Message); } }
            if (_ui != null) _ui.Post(_ => Raise(), null);
            else Raise();
        }

        // ---- copy ------------------------------------------------------------------------------

        private bool HasCopy(string clipId) =>
            !string.IsNullOrWhiteSpace(_text(ShowcaseRules.TitleKey(clipId)));

        /// <summary>The app's string for a key, or null when the language files do not have it.</summary>
        private static string? LocText(string key)
        {
            var value = Loc.Get(key);
            return string.Equals(value, key, StringComparison.Ordinal) ? null : value;
        }
    }
}
