using System;
using ConditioningControlPanel.Models.Dashboard;

namespace ConditioningControlPanel.Services.Dashboard
{
    /// <summary>
    /// What the picker must do with a pick before it touches the layout. Two of the four are
    /// silent and two raise the three-way ask.
    /// </summary>
    public enum PickPrompt
    {
        /// <summary>Target slot is empty. Place it and be done.</summary>
        Place,

        /// <summary>Target slot holds one splittable FX and the pick is another. Ask
        /// Replace / Split / Cancel.</summary>
        AskReplaceOrSplit,

        /// <summary>Target slot is taken by something that cannot share it - a door, a tile that
        /// is already split, or a pick that cannot be a half. Ask Replace / Cancel.</summary>
        AskReplaceOnly,

        /// <summary>The feature is already on the wall, so this is a move, not a new tile.
        /// Silent by design: the user can see both ends of it happen.</summary>
        Move,
    }

    /// <summary>
    /// The edit-mode half of the slot rules: when the pencil shows, what a pick on a given slot
    /// means, and what a reset writes. Pure - no WPF type, no settings object, no window - so the
    /// tests bind to it rather than to a rendered wall, and so Phase F's rolodex can reuse the
    /// same decisions without reimplementing them behind a bridge.
    ///
    /// <para><see cref="DashboardLayoutRule"/> still owns the mutation. This only decides what to
    /// ASK first; every accepted answer goes back through <c>Place</c>.</para>
    /// </summary>
    public static class DashboardPickerRule
    {
        /// <summary>
        /// The pencil is hidden, not disabled, while a session is running. A disabled pencil is a
        /// thing to argue with; an absent one is just not the moment. The ribbon over the mosaic
        /// is already saying why, so the tile says nothing (plan 6.1, memory feedback_minimal_ui).
        /// </summary>
        public static bool ShowPencil(bool sessionLocked) => !sessionLocked;

        /// <summary>
        /// What a click on <paramref name="key"/> means for <paramref name="slot"/>.
        ///
        /// <para>Order is the whole rule. "Already on the wall" is asked FIRST because a move is
        /// never a replacement question: the feature the user clicked is coming here either way,
        /// and asking about the tile it lands on would be asking about a tile they are also about
        /// to vacate. After that an empty slot places, and an occupied one asks - with Split on
        /// the table only when the occupant is a single splittable FX and the pick is one too.</para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Slot outside 0..8.</exception>
        public static PickPrompt Decide(DashboardLayout layout, int slot, string? key)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (slot < 0 || slot >= DashboardLayout.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Dashboard slot must be 0..8.");

            var row = FeatureCatalog.Find(key);
            if (row == null) return PickPrompt.AskReplaceOnly;   // Place refuses it; never silently.

            if (IsOnTheWall(layout, row.Key)) return PickPrompt.Move;

            var target = layout.Slots[slot];
            if (target.IsEmpty) return PickPrompt.Place;

            return CanOfferSplit(layout, slot, row.Key) ? PickPrompt.AskReplaceOrSplit : PickPrompt.AskReplaceOnly;
        }

        /// <summary>
        /// True when Split is a real option on this slot for this key: the slot holds exactly one
        /// occupant, that occupant can be a half, and so can the pick. An already-split tile is
        /// full - a third feature in one cell is not a tile, it is a mosaic of its own.
        /// </summary>
        public static bool CanOfferSplit(DashboardLayout layout, int slot, string? key)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (slot < 0 || slot >= DashboardLayout.SlotCount) return false;

            var target = layout.Slots[slot];
            if (target.IsEmpty || target.IsSplit) return false;

            var row = FeatureCatalog.Find(key);
            if (row == null) return false;
            if (string.Equals(target.Primary, row.Key, StringComparison.OrdinalIgnoreCase)) return false;

            return FeatureCatalog.CanSplit(target.Primary) && FeatureCatalog.CanSplit(row.Key);
        }

        /// <summary>
        /// What "Reset to default" writes: the shipped wall, still marked touched. Touched is not
        /// a record of whether the layout differs from the default - it is a record of whether the
        /// user has ever had an opinion, and choosing the default back is an opinion. The cloud
        /// adopt reads it, so clearing it here would let another machine's layout land on someone
        /// who just said they wanted this one.
        /// </summary>
        public static (string Wire, bool Touched) Reset()
            => (DashboardLayoutRule.ToWire(DashboardLayout.Default()), true);

        /// <summary>What an accepted edit writes to settings. Same pair, from the live layout.</summary>
        public static (string Wire, bool Touched) Commit(DashboardLayout layout)
            => (DashboardLayoutRule.ToWire(layout), true);

        private static bool IsOnTheWall(DashboardLayout layout, string key)
        {
            for (int i = 0; i < DashboardLayout.SlotCount; i++)
            {
                var s = layout.Slots[i];
                if (string.Equals(s.Primary, key, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s.Secondary, key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
