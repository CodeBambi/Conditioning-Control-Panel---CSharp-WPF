using System;

namespace ConditioningControlPanel.Models.Dashboard
{
    /// <summary>
    /// One movable cell of the Home mosaic. <see cref="Secondary"/> non-null means a split tile
    /// (two FX halves); a slot is a feature key and nothing more - no per-slot settings.
    /// </summary>
    public sealed class DashboardSlot
    {
        public string? Primary { get; set; }
        public string? Secondary { get; set; }
        public bool IsEmpty => string.IsNullOrEmpty(Primary) && string.IsNullOrEmpty(Secondary);
        public bool IsSplit => !string.IsNullOrEmpty(Primary) && !string.IsNullOrEmpty(Secondary);
    }

    /// <summary>
    /// The persisted shape: nine movable slots, in the grid order the renderer walks. The fixed
    /// cells (logo, Vault, ? BOX) are not in here and never move.
    /// </summary>
    public sealed class DashboardLayout
    {
        public const int SlotCount = 9;

        private DashboardSlot[] _slots = NewSlots();

        /// <summary>Always exactly <see cref="SlotCount"/> entries; a short or long array is normalized on set.</summary>
        public DashboardSlot[] Slots
        {
            get => _slots;
            set
            {
                var next = NewSlots();
                for (int i = 0; value != null && i < SlotCount && i < value.Length; i++)
                    next[i] = value[i] ?? new DashboardSlot();
                _slots = next;
            }
        }

        /// <summary>Today's wall, so a user who never opens the picker sees zero change.</summary>
        public static DashboardLayout Default()
        {
            var l = new DashboardLayout();
            Set(0, "flash", null); Set(1, "video", "bubblecount"); Set(2, "subliminal", null);
            Set(3, "bouncingtext", null); Set(4, "justdrop", null); Set(5, "spiral", "pinkfilter");
            Set(6, "mindwipe", "braindrain"); Set(7, "bubbles", null); Set(8, "lockcard", null);
            return l;

            void Set(int i, string p, string? s) { l.Slots[i].Primary = p; l.Slots[i].Secondary = s; }
        }

        /// <summary>True when this layout is slot-for-slot the shipped wall.</summary>
        public bool IsDefault
        {
            get
            {
                var d = Default();
                for (int i = 0; i < SlotCount; i++)
                    if (!Same(Slots[i].Primary, d.Slots[i].Primary) ||
                        !Same(Slots[i].Secondary, d.Slots[i].Secondary)) return false;
                return true;

                static bool Same(string? a, string? b)
                    => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static DashboardSlot[] NewSlots()
        {
            var slots = new DashboardSlot[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = new DashboardSlot();
            return slots;
        }
    }
}
