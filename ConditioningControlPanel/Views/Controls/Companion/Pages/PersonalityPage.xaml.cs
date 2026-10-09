using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Views.Controls.Companion.Pages
{
    /// <summary>
    /// Companion > Personality. Hosts, live: the CompanionPickerCard (the pick), the room's
    /// MakeHerYoursView ("PersonalityZone": preset chips, re-interview, adjust), the Workshop
    /// Community cell. The Advanced row is the prompt editor's door again
    /// (MainWindow.BtnCustomizeCompanion_Click). Behaviour + Triggers live on <see cref="AiPage"/>.
    /// </summary>
    public partial class PersonalityPage : UserControl
    {
        private readonly MainWindow? _owner;
        private CompanionPickerCard? _picker;

        public PersonalityPage() : this(null) { }

        internal PersonalityPage(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();
        }

        internal void OnShown()
        {
            _picker ??= new CompanionPickerCard
            {
                MorePersonalityOptions = window =>
                {
                    Services.Companion.PersonalityStudio.Show(window);
                    CompanionPageHost.Tab(_owner)?.Vm.Sync();
                }
            };
            if (!ReferenceEquals(PickerHost.Content, _picker)) PickerHost.Content = _picker;
            else _picker.Refresh();

            if (CompanionPageHost.Tab(_owner) is { } tab)
            {
                if (CompanionPageHost.Room(_owner)?.FindName("PersonalityZone") is FrameworkElement presets)
                {
                    CompanionPageHost.Adopt(presets, PresetsHost);
                    // The zone inherited the room's DataContext; outside the room it needs its own.
                    presets.DataContext = tab.Vm.Personality;
                }
                CompanionPageHost.Adopt(tab.Vm.Shelf.Community, CommunityHost);
                tab.Vm.Sync();
            }
        }

        private void OpenPromptEditor_Click(object sender, RoutedEventArgs e)
        {
            _owner?.BtnCustomizeCompanion_Click(sender, e);
            CompanionPageHost.Tab(_owner)?.Vm.Sync();
        }
    }
}
