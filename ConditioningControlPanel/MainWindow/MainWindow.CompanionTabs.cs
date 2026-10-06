using ConditioningControlPanel.Views.Controls.Companion.Pages;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Companion section's own pages (nav rework, contract 1). Each one is the visible home of
    /// options the v2 chat page left inside the collapsed room (commit 7043dfe85); see
    /// CompanionPagesTests for the list each page has to carry.
    /// </summary>
    public partial class MainWindow
    {
        partial void RegisterCompanionTabs()
        {
            RegisterNavTab(new NavTabHost("personality", () => new PersonalityPage(this),
                OnShown: v => ((PersonalityPage)v).OnShown()));
            RegisterNavTab(new NavTabHost("permissions", () => new PermissionsPage(this),
                OnShown: v => ((PermissionsPage)v).OnShown()));
            RegisterNavTab(new NavTabHost("companionlinks", () => new LinksPage(this),
                OnShown: v => ((LinksPage)v).OnShown()));
        }
    }
}
