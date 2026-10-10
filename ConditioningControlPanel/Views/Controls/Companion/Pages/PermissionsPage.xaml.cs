using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Pages
{
    /// <summary>
    /// Companion > Permissions: hosts the room's live AiPermissionsGrid ("PermissionsZone"), the
    /// grid MainWindow.Patreon.cs writes through CompanionTab.ChkAllow* and SyncLabEffectPermsUI.
    /// </summary>
    public partial class PermissionsPage : UserControl
    {
        private readonly MainWindow? _owner;

        public PermissionsPage() : this(null) { }

        internal PermissionsPage(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();
        }

        internal void OnShown()
        {
            if (CompanionPageHost.Room(_owner)?.FindName("PermissionsZone") is AiPermissionsGrid grid)
                CompanionPageHost.Adopt(grid, PermissionsHost);
            CompanionPageHost.Tab(_owner)?.Vm.Sync();
        }

        private void OpenTakeover_Click(object sender, RoutedEventArgs e) => _owner?.ShowTab("bambitakeover");
    }
}
