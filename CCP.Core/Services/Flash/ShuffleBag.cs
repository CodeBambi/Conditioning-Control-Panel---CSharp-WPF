using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Flash
{
    /// <summary>
    /// Deals a picture pool in a shuffled order so every entry comes up once before any comes up
    /// twice (#627). The flash picker used to draw with replacement, so a 1000-file folder showed
    /// about 630 distinct files per 1000 flashes and a third of it almost never.
    ///
    /// <para>Keyed on SOURCE identity (the disk path, or pack id plus entry name), never on a
    /// decrypted temp path: a pack entry decrypts to a fresh temp file on every draw.</para>
    ///
    /// <para>The pool is passed on every call because the caller owns it and edits it in place
    /// (deselection prune, refresh, clear). A changed pool (other list, other count) is re-dealt,
    /// keeping what this cycle already showed, so a refresh mid-cycle does not restart coverage.
    /// A new cycle never opens on the entry the last one closed on. Holds an index array and the
    /// keys dealt this cycle, never a copy of the items. Not thread-safe: the caller locks.</para>
    /// </summary>
    internal sealed class ShuffleBag<T>
    {
        private readonly Func<T, string> _key;
        private readonly Random _rng;
        private readonly HashSet<string> _dealt = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<T>? _pool;
        private int _poolCount = -1;
        private int[] _order = Array.Empty<int>();
        private int _next;
        private string? _lastKey;

        internal ShuffleBag(Func<T, string> key, Random rng)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Entries still to come in this cycle (for tests and logs).</summary>
        internal int Remaining => Math.Max(0, _order.Length - _next);

        /// <summary>Forget the cycle: the next draw starts a fresh shuffle.</summary>
        internal void Reset()
        {
            _dealt.Clear();
            _pool = null;
            _poolCount = -1;
            _order = Array.Empty<int>();
            _next = 0;
        }

        /// <summary>The next entry of <paramref name="pool"/>, or false when the pool is empty.</summary>
        internal bool TryNext(IReadOnlyList<T> pool, out T item)
        {
            item = default!;
            if (pool == null || pool.Count == 0) return false;

            if (!ReferenceEquals(pool, _pool) || pool.Count != _poolCount)
                Deal(pool, keepCycle: true);

            // Skip indices that fell out of range or were dealt already under another index
            // (the same key listed twice); a spent order opens a new cycle.
            for (int spins = 0; spins < 2; spins++)
            {
                while (_next < _order.Length)
                {
                    int idx = _order[_next++];
                    if ((uint)idx >= (uint)pool.Count) continue;
                    var candidate = pool[idx];
                    var k = _key(candidate) ?? string.Empty;
                    if (!_dealt.Add(k)) continue;
                    _lastKey = k;
                    item = candidate;
                    return true;
                }
                Deal(pool, keepCycle: false);
            }
            return false;
        }

        private void Deal(IReadOnlyList<T> pool, bool keepCycle)
        {
            _pool = pool;
            _poolCount = pool.Count;
            if (!keepCycle) _dealt.Clear();

            var order = new List<int>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
                if (!keepCycle || !_dealt.Contains(_key(pool[i]) ?? string.Empty)) order.Add(i);

            if (order.Count == 0 && keepCycle)
            {
                // Everything in the new pool was already shown this cycle: start the next one.
                _dealt.Clear();
                for (int i = 0; i < pool.Count; i++) order.Add(i);
            }

            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // No back-to-back repeat across a cycle boundary.
            if (order.Count > 1 && _lastKey != null
                && string.Equals(_key(pool[order[0]]), _lastKey, StringComparison.OrdinalIgnoreCase))
            {
                int j = 1 + _rng.Next(order.Count - 1);
                (order[0], order[j]) = (order[j], order[0]);
            }

            _order = order.ToArray();
            _next = 0;
        }
    }

    /// <summary>
    /// Folder listing rule for the flash pool (#627): a file belongs to the pass for its own
    /// extension, once. Windows matches "*.jpe" against x.jpeg as a prefix, so without this those
    /// files sat in the pool twice.
    /// </summary>
    internal static class MediaListing
    {
        internal static bool Keep(string file, string ext, ISet<string> listed) =>
            string.Equals(System.IO.Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)
            && listed.Add(file);
    }
}
