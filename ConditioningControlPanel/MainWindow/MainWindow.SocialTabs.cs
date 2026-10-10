using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    /// <summary>
    /// SOCIAL lane pages (nav rework 2026-10-06, BRIEF contract 1). Friends and Leash are not
    /// XAML children of MainWindow: they register through the tab registry and are built on their
    /// first ShowTab. Both host the existing drawer parts (FriendsDrawer in page mode, and
    /// LeashDrawerSection); the rail chip and its popup stay as the quick surface.
    /// Also arms the Social rail badge ("N open"), whose lease follows the panel's visibility.
    /// </summary>
    public partial class MainWindow
    {
        partial void RegisterSocialTabs()
        {
            RegisterNavTab(new NavTabHost("friends", () => new FriendsTabView(),
                OnShown: v => (v as FriendsTabView)?.OnShown(),
                OnHidden: v => (v as FriendsTabView)?.OnHidden()));

            RegisterNavTab(new NavTabHost("leash", () => new LeashTabView(),
                OnShown: v => (v as LeashTabView)?.OnShown()));

            HookLobbyBadge();
        }
    }
}
