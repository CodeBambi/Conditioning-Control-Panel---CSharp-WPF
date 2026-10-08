using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Tabs/CompanionTabView.xaml.cs. The tab
    /// owns the room and, v2 being on for everyone, stands the <see cref="ConversationPage"/>
    /// (Companion &gt; Chat) over it: the room is collapsed and stays in the tree as the owner of the
    /// live zones the Companion section pages adopt (Personality, Permissions, Links, AI).
    ///
    /// <para><b>Still not here, and why.</b> WPF news up a <c>CompanionRoomRuntimeVm</c> and
    /// re-publishes ~110 control names for the MainWindow partials (the compat seam). This head's
    /// zones seed their own viewmodels and no partial writes those names, so the page hands the
    /// conversation the two viewmodels it needs (the hero's and the engine's) straight from the
    /// zones, and the passthroughs stay absent rather than faked.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
        public CompanionTabView()
        {
            InitializeComponent();
            if (CompanionExperience.IsV2Enabled
                && Room.HeroZone.ViewModel is { } hero
                && Room.EngineZone.DataContext is EngineRoomVm engine)
            {
                Room.IsVisible = false;
                Conversation = new ConversationPage(hero, engine, () => Room.HeroZone.ApplyAvatarArt());
                PageHost.Children.Add(Conversation);
            }
        }

        /// <summary>The live room (collapsed under the conversation in v2): the zones' owner.</summary>
        internal CompanionRoomView RoomView => Room;

        /// <summary>Companion &gt; Chat. Null only when v2 is off (never, in this build).</summary>
        internal ConversationPage? Conversation { get; }
    }
}
