using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Pages
{
    /// <summary>
    /// Companion > Links: "Videos it can play" is the room's own WorkshopLibraryCell (the one the
    /// v2 Videos sheet used to show), "Links it knows" is <see cref="KnowledgeLinksEditor"/>, the
    /// same editor CompanionPromptEditorDialog hosts.
    /// </summary>
    public partial class LinksPage : UserControl
    {
        private readonly MainWindow? _owner;

        public LinksPage() : this(null) { }

        internal LinksPage(MainWindow? owner)
        {
            _owner = owner;
            InitializeComponent();
            // A bounded list inside the page ScrollViewer: pass unusable wheel notches up so the
            // page scrolls over "No links yet" too. The video pool cell wires its own list.
            CompanionWheelRelay.Attach(KnowledgeLinks.LstKnowledgeLinks);
        }

        /// <summary>Runs every time the page is shown: adopt the live cell, reload both lists.</summary>
        internal void OnShown()
        {
            if (CompanionPageHost.Tab(_owner) is { } tab)
            {
                CompanionPageHost.Adopt(tab.Vm.Shelf.Library, VideosHost);
                _owner?.RefreshVideoLinkPool();
            }
            KnowledgeLinks.Load();
        }
    }
}
