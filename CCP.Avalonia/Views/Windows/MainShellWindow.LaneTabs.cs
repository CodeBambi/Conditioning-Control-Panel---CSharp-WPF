// PORTED from WPF 7.1.5 MainWindow/MainWindow.TabNavigation.cs:142-158 (RegisterLaneNavTabs and the
// four partial hooks). The section lanes register their pages from their own partial files; this
// file only calls them, once.
//
// Deviation: WPF calls RegisterLaneNavTabs() from the MainWindow constructor. This head's
// constructor and TabNavigation.cs belong to other lanes, so the call rides OnInitialized, which
// Avalonia raises from the EndInit of AvaloniaXamlLoader.Load(this): after the XAML exists, before
// anything can ShowTab. A host that never loads XAML (none today) would not register; the
// guard below makes a second call (a later explicit one from the constructor) harmless.
using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Implemented by the SOCIAL / COMPANION / REHOME / PREMIUM lanes in their own partial files.
        partial void RegisterSocialTabs();
        partial void RegisterCompanionTabs();
        partial void RegisterRehomeTabs();
        partial void RegisterPremiumTab();

        private bool _laneTabsRegistered;

        /// <summary>Once per window: every lane's RegisterNavTab calls.</summary>
        internal void RegisterLaneNavTabs()
        {
            if (_laneTabsRegistered) return;
            _laneTabsRegistered = true;
            try { RegisterSocialTabs(); } catch (Exception ex) { Log.Warning(ex, "RegisterSocialTabs failed"); }
            try { RegisterCompanionTabs(); } catch (Exception ex) { Log.Warning(ex, "RegisterCompanionTabs failed"); }
            try { RegisterRehomeTabs(); } catch (Exception ex) { Log.Warning(ex, "RegisterRehomeTabs failed"); }
            try { RegisterPremiumTab(); } catch (Exception ex) { Log.Warning(ex, "RegisterPremiumTab failed"); }
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            RegisterLaneNavTabs();
        }
    }
}
