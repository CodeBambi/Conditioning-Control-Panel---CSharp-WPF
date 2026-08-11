using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z3 — What she knows about you. Her diary, and the app's trust surface.
    ///
    /// <para>Deliberately NOT paywalled: the profile strip is deterministic and local, so a free
    /// user sees the whole panel. It is the user-inspectable, user-deletable story.</para>
    ///
    /// <para>The wall is never blank. A fresh account still renders the profile strip (level and
    /// streak exist from minute one) plus one ghost card, and pre-Train-4 the wall ends with a
    /// dormant promise card rather than stopping dead.</para>
    /// </summary>
    public interface IMemoryDiaryVm : INotifyPropertyChanged
    {
        /// <summary>"SHE CAN SEE:" — the strip's leading label.</summary>
        string ProfileStripLabel { get; }

        /// <summary>Gold read-only chips: Level 41 · Streak 12 days · 87 sessions · Archetype…</summary>
        IReadOnlyList<IProfileStatVm> ProfileStats { get; }

        /// <summary>The kind chips: all · boundaries · jokes · preferences · goals · moments.</summary>
        IReadOnlyList<IFactFilterVm> Filters { get; }

        /// <summary>
        /// The wall, already filtered by the selected chip and already sorted
        /// (boundary ▸ pinned ▸ salience ▸ dormant). The view does no ordering of its own.
        /// </summary>
        IReadOnlyList<IMemoryFactVm> Facts { get; }

        /// <summary>Currently selected filter key; setting it re-projects <see cref="Facts"/>.</summary>
        string SelectedFilterKey { get; set; }

        /// <summary>No real facts yet — the wall shows the ghost card instead.</summary>
        bool IsEmpty { get; }
        /// <summary>"tell me things and I'll keep them~".</summary>
        string EmptyCopy { get; }

        /// <summary>Footer left: "her memory lives on this machine only".</summary>
        string StorageNote { get; }
        /// <summary>The "where?" link label.</summary>
        string StorageLinkLabel { get; }
        string ForgetEverythingLabel { get; }

        /// <summary>Opens the companion\ folder in Explorer.</summary>
        ICommand OpenStorageFolderCommand { get; }
        /// <summary>Confirm dialog in her voice, then the wipe. Absorbs the old Reset Memory button.</summary>
        ICommand ForgetEverythingCommand { get; }
    }
}
