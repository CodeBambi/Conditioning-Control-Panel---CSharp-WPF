using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Draw-without-replacement picker ("shuffle bag") over a pool somebody else owns.
    ///
    /// Why this exists: the flash picker used to call <c>Random.Next(pool.Count)</c> for every
    /// image, which is a draw WITH replacement. Over 1000 draws from a 1000-file folder that
    /// surfaces only ~632 distinct files, some of them three or four times, and roughly a third
    /// of the folder never shows up at all - which is exactly what a user with a large library
    /// reports as "I keep seeing the same fifty gifs". A shuffle bag deals the whole pool in a
    /// random order, hands it out one at a time, and only reshuffles once the pool is spent, so
    /// N draws from an N-file pool touch every file exactly once.
    ///
    /// Memory: the bag keeps an int per pool entry plus one reference to the item it handed out
    /// last. It never holds decoded images and never copies the pool.
    ///
    /// The pool is passed in on every draw rather than stored, because the caller rebuilds its
    /// list from disk (and prunes deselected assets out of it) whenever the library changes. A
    /// change in pool size reshuffles on its own; when the pool changes WITHOUT changing size,
    /// the owner calls <see cref="Invalidate"/>.
    ///
    /// Not thread-safe: callers hold their own lock (FlashService uses <c>_lockObj</c>).
    /// </summary>
    internal sealed class ShuffleBag<T>
    {
        private readonly Random _random;
        private int[] _order = Array.Empty<int>();
        private int _cursor;            // next slot of _order to hand out
        private int _builtFor = -1;     // pool size the current order was dealt for
        private bool _hasLast;
        private T? _last;               // last item handed out, for the reshuffle boundary guard

        public ShuffleBag(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>Draws left before the bag reshuffles. Zero means the next draw reshuffles.</summary>
        public int Remaining => _builtFor < 0 ? 0 : Math.Max(0, _order.Length - _cursor);

        /// <summary>
        /// Forces a reshuffle on the next draw. Call this when the pool contents changed but the
        /// count did not (a rescan that swapped one file for another, a mod or folder switch).
        /// </summary>
        public void Invalidate()
        {
            _builtFor = -1;
            _cursor = 0;
        }

        /// <summary>
        /// Hands out the next item. Returns false only for an empty pool.
        /// </summary>
        public bool TryNext(IReadOnlyList<T> pool, out T item)
        {
            item = default!;
            if (pool == null || pool.Count == 0) return false;

            if (_builtFor != pool.Count || _cursor >= _order.Length) Deal(pool);

            int index = _order[_cursor++];
            if (index >= pool.Count)
            {
                // The pool shrank under us without an Invalidate. Re-deal rather than throw.
                Deal(pool);
                index = _order[_cursor++];
            }

            item = pool[index];
            _last = item;
            _hasLast = true;
            return true;
        }

        private void Deal(IReadOnlyList<T> pool)
        {
            int n = pool.Count;
            if (_order.Length != n) _order = new int[n];
            for (int i = 0; i < n; i++) _order[i] = i;

            // Fisher-Yates, matching VideoService.ShuffleList.
            for (int i = n - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }

            // A fresh bag must not open on the item the old one closed on: back-to-back repeats
            // across the seam are the one thing a shuffle bag is supposed to make impossible, and
            // they are the most visible kind of repeat there is.
            if (n > 1 && _hasLast && EqualityComparer<T>.Default.Equals(pool[_order[0]], _last!))
            {
                int swap = 1 + _random.Next(n - 1);
                (_order[0], _order[swap]) = (_order[swap], _order[0]);
            }

            _builtFor = n;
            _cursor = 0;
        }
    }
}
