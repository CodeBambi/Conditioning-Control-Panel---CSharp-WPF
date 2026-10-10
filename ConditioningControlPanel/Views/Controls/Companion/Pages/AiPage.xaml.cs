using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion.V2;

namespace ConditioningControlPanel.Views.Controls.Companion.Pages
{
    /// <summary>
    /// Companion > AI: hosts the room's live EngineRoomDrawer ("EngineZone": off / cloud / local /
    /// custom, model, sampler, test), the Workshop Behaviour + Triggers cells (how often it talks),
    /// and MemoryDiaryView ("MemoryZone") with the preferred name editor and recap.
    /// </summary>
    public partial class AiPage : UserControl
    {
        private readonly MainWindow? _owner;

        public AiPage() : this(null) { }

        internal AiPage(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();
        }

        /// <summary>Runs every time the page is shown: adopt both live zones, fresh name + recap.</summary>
        internal void OnShown()
        {
            App.Brain?.EnsureCurrentAccount();
            NameHost.Content = new PreferredNameEditor();
            RecapHost.Content = new ConversationRecap();
            if (CompanionPageHost.Tab(_owner) is not { } tab) return;
            tab.Vm.Sync();
            CompanionPageHost.Adopt(tab.Vm.Shelf.Behavior, BehaviorHost);
            CompanionPageHost.Adopt(tab.Vm.Shelf.Triggers, TriggersHost);
            var room = CompanionPageHost.Room(_owner);
            // The room binds each zone to {Binding Engine} / {Binding Memory}; the page has no room context.
            if (room?.FindName("EngineZone") is EngineRoomDrawer engine)
            {
                engine.DataContext = tab.Vm.Engine;
                tab.Vm.Engine.IsExpanded = true;
                CompanionPageHost.Adopt(engine, EngineHost);
            }
            if (room?.FindName("MemoryZone") is MemoryDiaryView diary)
            {
                diary.DataContext = tab.Vm.Memory;
                CompanionPageHost.Adopt(diary, MemoryHost);
            }
        }

        internal void OnHidden()
        {
            if (MemoryHost.Content is MemoryDiaryView diary) diary.ForgetConfirm.Disarm();
        }
    }
}
