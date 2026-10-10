// PORTED from WPF 7.1.5 MainWindow/MainWindow.CompanionTabs.cs: the Companion section's own pages,
// registered through the tab registry (MainShellWindow.TabNavigation.cs). Each one is the visible
// home of options the v2 conversation page left inside the collapsed room. Registering them retires
// the four Companion pages (TabNavigation keeps no stand-in for them any more).
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        partial void RegisterCompanionTabs()
        {
            RegisterNavTab(new NavTabHost("personality", () => new PersonalityPage(this),
                OnShown: v => ((PersonalityPage)v).OnShown()));
            RegisterNavTab(new NavTabHost("permissions", () => new PermissionsPage(this),
                OnShown: v => ((PermissionsPage)v).OnShown()));
            RegisterNavTab(new NavTabHost("companionlinks", () => new LinksPage(this),
                OnShown: v => ((LinksPage)v).OnShown()));
            RegisterNavTab(new NavTabHost("companionai", () => new AiPage(this),
                OnShown: v => ((AiPage)v).OnShown(), OnHidden: v => ((AiPage)v).OnHidden()));
        }
    }
}
