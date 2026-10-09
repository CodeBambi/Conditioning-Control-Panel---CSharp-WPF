// PORTED from WPF 7.1.5 Controls/Friends/FriendsDrawer.cs `_leash` (the LeashDrawerSection pinned
// above the list, kept between repaints). FriendsDrawer.cs calls MountLeash once with the slot
// where WPF adds `_leash`; this file is the only place the leash section joins the drawer.
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    /// <summary>The leash section, once mounted (also the source of a friend card's Offer chip).</summary>
    internal LeashDrawerSection? LeashSection { get; private set; }

    partial void MountLeash(Panel slot)
    {
        if (LeashSection != null) return;
        LeashSection = new LeashDrawerSection(owner: () => OwnerWindow?.Invoke());
        slot.Children.Add(LeashSection);
    }
}
