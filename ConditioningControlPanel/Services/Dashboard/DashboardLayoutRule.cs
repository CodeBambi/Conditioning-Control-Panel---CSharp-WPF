using System;
using System.Collections.Generic;
using System.Text;
using ConditioningControlPanel.Models.Dashboard;

namespace ConditioningControlPanel.Services.Dashboard
{
    /// <summary>
    /// What <see cref="DashboardLayoutRule.Place"/> did. Unchanged and the two Refused values all
    /// leave the layout exactly as it was; the difference is only whether the caller asked for
    /// something illegal (Refused) or for something it already had (Unchanged).
    /// </summary>
    public enum PlaceOutcome
    {
        Placed, Replaced, Split, MovedFrom, Unchanged, RefusedNotSplittable, RefusedUnknownKey,
    }

    /// <summary>
    /// Every rule the Home slot grid obeys, with no WPF type in sight: the picker, the renderer,
    /// the settings round trip and the cloud adopt all bind to this, and the tests bind to it
    /// instead of to a window. The invariant worth naming: a feature is on the wall at most once,
    /// so placing one that is already somewhere is a move, not a copy.
    /// </summary>
    public static class DashboardLayoutRule
    {
        /// <summary>The server sanitizer's byte cap; anything longer is not a layout we wrote.</summary>
        private const int MaxWireLength = 256;

        /// <summary>
        /// Make any layout - a hand-edited settings file, an older client's wire string, a newer
        /// one's - safe to render: unknown keys blanked, a Secondary that cannot be a split half
        /// dropped, duplicates kept only at their first appearance, and a layout with nothing left
        /// in it promoted to the default rather than shown as nine holes. Never mutates the input.
        /// </summary>
        public static DashboardLayout Sanitize(DashboardLayout? raw)
        {
            if (raw == null) return DashboardLayout.Default();

            var clean = new DashboardLayout();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < DashboardLayout.SlotCount; i++)
            {
                var primary = Keep(raw.Slots[i].Primary, seen);
                var secondary = Keep(raw.Slots[i].Secondary, seen);

                // An orphaned second half is promoted rather than thrown away.
                if (primary == null && secondary != null) { primary = secondary; secondary = null; }

                // Only two FX make a split tile. A door never has a second half, and never is one.
                if (secondary != null &&
                    !(FeatureCatalog.CanSplit(primary) && FeatureCatalog.CanSplit(secondary)))
                    secondary = null;

                clean.Slots[i].Primary = primary;
                clean.Slots[i].Secondary = secondary;
            }

            return IsEmptyLayout(clean) ? DashboardLayout.Default() : clean;

            static string? Keep(string? key, HashSet<string> seen)
            {
                var row = FeatureCatalog.Find(key);
                return row != null && seen.Add(row.Key) ? row.Key : null;
            }
        }

        /// <summary>
        /// Put <paramref name="key"/> in <paramref name="slot"/>. Precedence: an unknown key
        /// refuses; a key dropped on the slot it is already in is Unchanged; a split the pair
        /// cannot form refuses; a feature already on the wall reports MovedFrom even when it also
        /// displaced an occupant, because the move is the surprising half and the picker's replace
        /// prompt has been answered by then; otherwise an occupied target is Replaced and an empty
        /// one is Placed.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Slot outside 0..8.</exception>
        public static PlaceOutcome Place(DashboardLayout layout, int slot, string key, bool split)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            RequireSlot(slot);

            var row = FeatureCatalog.Find(key);
            if (row == null) return PlaceOutcome.RefusedUnknownKey;

            var target = layout.Slots[slot];

            // Dropping a key back where it already is means nothing, and it has to be answered
            // HERE, before the move arithmetic: Remove() would lift this key off its own slot and
            // promote its partner into the hole, and the write that follows would then flatten the
            // promotion - destroying the other half of the split the user was only re-picking.
            // True of either half, and of a split re-drop, which is the same nothing.
            if (Is(target.Primary, row.Key) || Is(target.Secondary, row.Key))
                return PlaceOutcome.Unchanged;

            if (split)
            {
                // A split needs a free second half and two halves that may BE halves. Both sides
                // are asked through CanSplit rather than one of them being spot-checked for Kind:
                // the rule is "ungated FX", and a Kind test alone would let a tier-locked FX into
                // half a cell where its lockband and rim cannot follow. The pick cannot be the
                // occupant itself - one feature on both halves - but the guard above said so.
                if (target.Secondary != null
                    || target.Primary == null
                    || !FeatureCatalog.CanSplit(target.Primary)
                    || !FeatureCatalog.CanSplit(row.Key))
                    return PlaceOutcome.RefusedNotSplittable;

                Remove(layout, row.Key);
                target.Secondary = row.Key;
                return PlaceOutcome.Split;
            }

            var from = IndexOf(layout, row.Key);
            var occupied = !target.IsEmpty;

            Remove(layout, row.Key);
            target.Primary = row.Key;
            target.Secondary = null;

            if (from >= 0 && from != slot) return PlaceOutcome.MovedFrom;
            return occupied ? PlaceOutcome.Replaced : PlaceOutcome.Placed;
        }

        /// <summary>Empty a slot. An empty slot renders as a hole, which is a legal layout.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Slot outside 0..8.</exception>
        public static void Clear(DashboardLayout layout, int slot)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            RequireSlot(slot);
            layout.Slots[slot].Primary = null;
            layout.Slots[slot].Secondary = null;
        }

        /// <summary>
        /// The settings and cloud encoding: nine comma-separated fields, a pipe joining a split,
        /// an empty field for an empty slot. Under 200 bytes for every layout the picker can build.
        /// </summary>
        public static string ToWire(DashboardLayout? layout)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < DashboardLayout.SlotCount; i++)
            {
                if (i > 0) sb.Append(',');
                var s = layout?.Slots[i];
                if (s == null || string.IsNullOrEmpty(s.Primary)) continue;
                sb.Append(s.Primary);
                if (!string.IsNullOrEmpty(s.Secondary)) sb.Append('|').Append(s.Secondary);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The other end of <see cref="ToWire"/>, sanitized on the way out so a caller can hand the
        /// result straight to the renderer. Anything unreadable answers with the default wall, and
        /// so does a string that clears every slot - the same promotion Sanitize makes.
        /// </summary>
        public static DashboardLayout FromWire(string? wire)
        {
            if (string.IsNullOrWhiteSpace(wire) || wire!.Length > MaxWireLength)
                return DashboardLayout.Default();

            var raw = new DashboardLayout();
            var fields = wire.Split(',');
            for (int i = 0; i < DashboardLayout.SlotCount && i < fields.Length; i++)
            {
                var field = fields[i].Trim();
                if (field.Length == 0) continue;
                var halves = field.Split('|');
                raw.Slots[i].Primary = halves[0].Trim();
                if (halves.Length > 1) raw.Slots[i].Secondary = halves[1].Trim();
            }
            return Sanitize(raw);
        }

        private static void RequireSlot(int slot)
        {
            if (slot < 0 || slot >= DashboardLayout.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Dashboard slot must be 0..8.");
        }

        private static bool IsEmptyLayout(DashboardLayout l)
        {
            for (int i = 0; i < DashboardLayout.SlotCount; i++) if (!l.Slots[i].IsEmpty) return false;
            return true;
        }

        private static int IndexOf(DashboardLayout l, string key)
        {
            for (int i = 0; i < DashboardLayout.SlotCount; i++)
                if (Is(l.Slots[i].Primary, key) || Is(l.Slots[i].Secondary, key)) return i;
            return -1;
        }

        /// <summary>Take a key off the wall wherever it sits, promoting a widowed second half.</summary>
        private static void Remove(DashboardLayout l, string key)
        {
            for (int i = 0; i < DashboardLayout.SlotCount; i++)
            {
                var s = l.Slots[i];
                if (Is(s.Secondary, key)) s.Secondary = null;
                if (Is(s.Primary, key)) { s.Primary = s.Secondary; s.Secondary = null; }
            }
        }

        private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
