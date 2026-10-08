// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/Pages/{PersonalityPage,
// PermissionsPage,LinksPage,AiPage}.xaml(.cs): the Companion section's own pages, each the visible
// home of options the v2 conversation page collapsed away with the old room. Code only on this head
// (each WPF XAML is a ScrollViewer, a capped StackPanel, a title and a few cards); the chrome is
// CompanionPageHost's.
//
// Deviations:
//   - Personality: WPF's CompanionPickerCard (avatar look + personality preset with a live preview)
//     is not on this head. The "who" card hosts the room's live Workshop Roster cell (which
//     companion) instead; the preset chips, re-interview and adjust are the live MakeHerYoursView.
//     ponytail: CompanionPickerCard (AvatarTubeWindow.PickableAvatarSets + PersonalityService
//     presets + TubeFitDialog) when the picker is ported.
//   - Links: WPF also calls MainWindow.RefreshVideoLinkPool; this head's Library cell seeds itself.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
using static ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages.CompanionPageHost;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// Companion &gt; Personality: the ONE home for the personality. Who (the roster), presets /
    /// re-interview / adjust (the room's MakeHerYoursView, "PersonalityZone"), the full prompt editor
    /// (Advanced) and community prompts. Every control here is the live one, hosted, never copied.
    /// Behaviour + Triggers live on <see cref="AiPage"/> (7.1.3: folded here, nobody found them).
    /// </summary>
    public sealed class PersonalityPage : UserControl
    {
        private readonly Windows.MainShellWindow? _owner;
        internal ContentControl PickerHost { get; } = Host("PickerHost");
        internal ContentControl PresetsHost { get; } = Host("PresetsHost", new Thickness(0, 0, 0, 16));
        internal ContentControl CommunityHost { get; } = Host("CommunityHost");
        internal Button BtnOpenPromptEditor { get; }

        public PersonalityPage() : this(null) { }

        internal PersonalityPage(Windows.MainShellWindow? owner)
        {
            _owner = owner;
            BtnOpenPromptEditor = PageButton("companion_page_personality_open_editor", OpenPromptEditor_Click);
            BtnOpenPromptEditor.Name = "BtnOpenPromptEditor";
            Grid.SetColumn(BtnOpenPromptEditor, 1);
            var advancedText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            advancedText.Children.Add(CardTitle("companion_page_personality_advanced"));
            advancedText.Children.Add(CardNote("companion_page_personality_advanced_note", 13, new Thickness(0, 2, 12, 0)));
            var advanced = Card(new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { advancedText, BtnOpenPromptEditor } });
            advanced.Name = "AdvancedRow";

            var community = new StackPanel();
            var communityTitle = CardTitle("companion_page_personality_community");
            communityTitle.Margin = new Thickness(0, 0, 0, 8);
            community.Children.Add(communityTitle);
            community.Children.Add(CommunityHost);

            var who = new StackPanel();
            var whoTitle = CardTitle("companion_workshop_cell_roster");
            whoTitle.Margin = new Thickness(0, 0, 0, 8);
            who.Children.Add(whoTitle);
            who.Children.Add(PickerHost);

            Content = Page(1100,
                PageTitle("nav_tab_personality"),
                PageSubtitle("companion_page_personality_intro"),
                Card(who),
                PresetsHost,
                advanced,
                Card(community));
        }

        /// <summary>Runs every time the page is shown: adopt the live zone and cells.</summary>
        internal void OnShown()
        {
            var owner = _owner ?? ShellOf(this);
            if (Shelf(owner) is { } shelf)
            {
                Adopt(shelf.Roster, PickerHost);
                Adopt(shelf.Community, CommunityHost);
            }
            if (Room(owner)?.FindControl<MakeHerYoursView>("PersonalityZone") is { } presets)
                Adopt(presets, PresetsHost);
        }

        private async void OpenPromptEditor_Click(object? sender, RoutedEventArgs e)
        {
            try { CoreBark.NotifyUiAction("customize_companion"); } catch { }
            try { await new CompanionPromptEditorDialog().ShowDialogSafe(TopLevel.GetTopLevel(this) as Window); }
            catch (Exception ex) { Log.Warning(ex, "[Companion] prompt editor failed to open"); }
        }
    }

    /// <summary>
    /// Companion &gt; Permissions: hosts the room's live AiPermissionsGrid ("PermissionsZone"), the one
    /// list of what the AI may do. Takeover keeps its own effect list and points here; this page
    /// points back to it.
    /// </summary>
    public sealed class PermissionsPage : UserControl
    {
        private readonly Windows.MainShellWindow? _owner;
        internal ContentControl PermissionsHost { get; } = Host("PermissionsHost");
        internal Button LinkOpenTakeover { get; }

        public PermissionsPage() : this(null) { }

        internal PermissionsPage(Windows.MainShellWindow? owner)
        {
            _owner = owner;
            // WPF: a Run then an inline Hyperlink. A flat link-styled button keeps the click target
            // real (an Avalonia inline has no Click) and the words on one line.
            LinkOpenTakeover = new Button
            {
                Name = "LinkOpenTakeover",
                Content = Loc(new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), FontSize = 12 }, "companion_page_open_takeover"),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 0, 0, 0),
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            LinkOpenTakeover.Click += (_, _) => (_owner ?? ShellOf(this))?.ShowTab("bambitakeover");
            var foot = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foot.Children.Add(CardNote("companion_page_permissions_takeover", 12, new Thickness(0)));
            foot.Children.Add(LinkOpenTakeover);

            Content = Page(1240,
                PageTitle("nav_tab_permissions"),
                PageSubtitle("companion_page_permissions_intro"),
                PermissionsHost,
                foot);
        }

        internal void OnShown()
        {
            if (Room(_owner ?? ShellOf(this))?.FindControl<AiPermissionsGrid>("PermissionsZone") is { } grid)
                Adopt(grid, PermissionsHost);
        }
    }

    /// <summary>
    /// Companion &gt; Links: "Videos it can play" is the room's own WorkshopLibraryCell (the mod's
    /// video pool the AI picks from), "Links it knows" is <see cref="KnowledgeLinksEditor"/>, the
    /// global knowledge base every personality knows. Both are the live controls.
    /// </summary>
    public sealed class LinksPage : UserControl
    {
        private readonly Windows.MainShellWindow? _owner;
        internal ContentControl VideosHost { get; } = Host("VideosHost");
        internal KnowledgeLinksEditor KnowledgeLinks { get; } = new() { Name = "KnowledgeLinks", SaveImmediately = true };

        public LinksPage() : this(null) { }

        internal LinksPage(Windows.MainShellWindow? owner)
        {
            _owner = owner;
            var videos = new StackPanel();
            videos.Children.Add(CardHeading("companion_page_links_videos"));
            videos.Children.Add(CardNote("companion_page_links_videos_note"));
            videos.Children.Add(VideosHost);
            var known = new StackPanel();
            known.Children.Add(CardHeading("companion_page_links_known"));
            known.Children.Add(CardNote("companion_page_links_known_note"));
            known.Children.Add(KnowledgeLinks);

            Content = Page(980,
                PageTitle("nav_tab_companionlinks"),
                PageSubtitle("companion_page_links_intro"),
                Card(videos),
                Card(known, new Thickness(0)));
        }

        /// <summary>Runs every time the page is shown: adopt the live cell, reload the link list.</summary>
        internal void OnShown()
        {
            if (Shelf(_owner ?? ShellOf(this)) is { } shelf) Adopt(shelf.Library, VideosHost);
            KnowledgeLinks.Load();
        }
    }

    /// <summary>
    /// Companion &gt; AI: hosts the room's live EngineRoomDrawer ("EngineZone": off / cloud / local /
    /// custom, model, sampler, test), the Workshop Behaviour + Triggers cells (how often it talks,
    /// an OPEN card, never folded: 7.1.3), and MemoryDiaryView ("MemoryZone") with the preferred
    /// name editor and the recap. One pill, not two: two ran the Companion bar past 1469 px.
    /// </summary>
    public sealed class AiPage : UserControl
    {
        private readonly Windows.MainShellWindow? _owner;
        internal ContentControl EngineHost { get; } = Host("EngineHost");
        internal ContentControl BehaviorHost { get; } = Host("BehaviorHost", new Thickness(0, 0, 0, 12));
        internal ContentControl TriggersHost { get; } = Host("TriggersHost");
        internal ContentControl NameHost { get; } = Host("NameHost");
        internal ContentControl RecapHost { get; } = Host("RecapHost");
        internal ContentControl MemoryHost { get; } = Host("MemoryHost");

        public AiPage() : this(null) { }

        internal AiPage(Windows.MainShellWindow? owner)
        {
            _owner = owner;
            var title = PageTitle("label_ai_badge");
            title.Margin = new Thickness(0, 0, 0, 16);
            var behaviour = new StackPanel();
            behaviour.Children.Add(CardHeading("companion_page_personality_behaviour"));
            behaviour.Children.Add(CardNote("companion_page_personality_behaviour_note"));
            behaviour.Children.Add(BehaviorHost);
            behaviour.Children.Add(TriggersHost);
            var memory = new StackPanel();
            memory.Children.Add(CardHeading("companion_v2_memory"));
            memory.Children.Add(CardNote("companion_v2_memory_note"));
            memory.Children.Add(NameHost);
            memory.Children.Add(RecapHost);
            memory.Children.Add(MemoryHost);

            Content = Page(980,
                title,
                EngineHost,
                Card(behaviour, new Thickness(0, 16, 0, 0)),
                Card(memory, new Thickness(0, 16, 0, 0)));
        }

        /// <summary>Runs every time the page is shown: adopt both live zones and the two cells,
        /// a fresh name editor and recap.</summary>
        internal void OnShown()
        {
            var owner = _owner ?? ShellOf(this);
            App.Brain?.EnsureCurrentAccount();
            NameHost.Content = new PreferredNameEditor();
            RecapHost.Content = new ConversationRecap();
            if (Shelf(owner) is { } shelf)
            {
                Adopt(shelf.Behavior, BehaviorHost);
                Adopt(shelf.Triggers, TriggersHost);
            }
            var room = Room(owner);
            if (room?.FindControl<EngineRoomDrawer>("EngineZone") is { } engine)
            {
                if (engine.DataContext is EngineRoomVm vm) { vm.IsExpanded = true; vm.Sync(); }
                Adopt(engine, EngineHost);
            }
            if (room?.FindControl<MemoryDiaryView>("MemoryZone") is { } diary)
            {
                diary.ViewModel?.Sync();
                Adopt(diary, MemoryHost);
            }
        }

        internal void OnHidden()
        {
            if (MemoryHost.Content is MemoryDiaryView diary) diary.ForgetConfirm.Disarm();
        }
    }
}
