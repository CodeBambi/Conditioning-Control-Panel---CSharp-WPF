using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/AvailableSubjectsTabView.xaml.cs.
    ///
    /// <para>The WPF code-behind owns no logic of its own: every handler looks up the hosting
    /// MainWindow and forwards. Two of those forwards ARE ported, because what they forwarded to
    /// is canvas tuning rather than a service - the ambient fog's layers and intensity, copied
    /// from MainWindow.SubjectsFx (two puffs, 0.40). The rest are stubs.</para>
    ///
    /// <para>Dropped: AvailableSubjectsScroller_PreviewMouseWheel. It existed only to turn a
    /// vertical wheel notch into horizontal scrolling on a scroller whose vertical axis is
    /// disabled; Avalonia's ScrollViewer already does that, so there is nothing to undo.</para>
    /// </summary>
    public partial class AvailableSubjectsTabView : UserControl
    {
        /// <summary>Sparse on purpose - MainWindow.SubjectsFx: the air behind a roster you read
        /// must never compete with a name.</summary>
        private const int SubjectsFogPuffs = 2;
        private const double SubjectsFogIntensity = 0.40;

        private readonly AmbientFxCanvas _ambientFx;

        public AvailableSubjectsTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _ambientFx = this.FindControl<AmbientFxCanvas>("SubjectsAmbientFx")!;

            LoadPlaceholderRoster();

            // WPF hooked IsVisibleChanged and let MainWindow start/park the canvas through
            // RegisterTabFx. There is no tab host on this head, so the view starts its own fog
            // and stops it on unload - the canvas self-gates on motion and window focus anyway.
            Loaded += OnTabLoaded;
            Unloaded += OnTabUnloaded;
        }

        private void OnTabLoaded(object? sender, RoutedEventArgs e)
            => _ambientFx.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.FogDrift,
                FogPuffs = SubjectsFogPuffs,
                Intensity = SubjectsFogIntensity,
            });

        private void OnTabUnloaded(object? sender, RoutedEventArgs e) => _ambientFx.Stop();

        // ------------------------------------------------------------------
        // Placeholder roster
        // ------------------------------------------------------------------

        /// <summary>
        /// Four sample subjects, one per branch the card template carries: a plain row, one with
        /// status text, one with tags, and a claimed row (dimmed to 0.6 with a disabled "Taken"
        /// button) - so the render proof exercises the markup rather than one happy path.
        ///
        /// ponytail: needs AvailableSubjectsService + DirectoryEntry, both pinned to the WPF head
        /// (the service polls over App.Http and marshals onto System.Windows.Application).
        /// Wired when they move to Core; SubjectRow mirrors DirectoryEntry's shape so the swap is
        /// a rename.
        /// </summary>
        private void LoadPlaceholderRoster()
        {
            var rows = new List<SubjectRow>
            {
                new() { UnifiedId = "u1", DisplayName = "velvet_hush", Level = 41, Tier = "full",
                        StatusText = "Deep in a spiral session. Say hello.",
                        Tags = new List<string> { "obedient", "voice", "long sessions" } },
                new() { UnifiedId = "u2", DisplayName = "Nyx", Level = 27, Tier = "standard",
                        Tags = new List<string> { "haptics", "mantras" } },
                new() { UnifiedId = "u3", DisplayName = "spiral.doll", Level = 19, Tier = "light",
                        StatusText = "Taking it slow today.", Claimed = true },
                new() { UnifiedId = "u4", DisplayName = "blink", Level = 8, Tier = "light" },
            };

            this.FindControl<ItemsControl>("AvailableSubjectsList")!.ItemsSource = rows;
        }

        // ------------------------------------------------------------------
        // Stubs for what MainWindow owned
        // ------------------------------------------------------------------
        // ponytail: BtnBecomeASubject_Click needs App.Patreon (free users go to Patreon, premium
        // users to the Remote Control tab) and BtnConnectSubject_Click needs
        // AvailableSubjectsService.TryClaimAsync plus MainWindow.ShowTab. Both are WPF-head
        // services; wired when they move to Core, so the buttons are inert rather than wrong.

        private void BtnBecomeASubject_Click(object? sender, RoutedEventArgs e) { }

        private void BtnConnectSubject_Click(object? sender, RoutedEventArgs e) { }
    }

    /// <summary>
    /// One roster card's data. The Avalonia-side stand-in for
    /// Services.AvailableSubjectsService.DirectoryEntry, whose derived members - TierLabel,
    /// HasTags, HasStatusText, IsConnectEnabled, ConnectButtonText, CardOpacity - are copied
    /// verbatim, including the two hard-coded English strings ("Connect"/"Taken"): they are
    /// literals in the original, not loc keys, and inventing keys for them here would put a
    /// string in the catalogue that the real model cannot use.
    /// </summary>
    public sealed class SubjectRow
    {
        public string UnifiedId { get; set; } = "";
        public string DisplayName { get; set; } = "Anonymous";
        public int Level { get; set; }
        public List<string> Tags { get; set; } = new();
        public string StatusText { get; set; } = "";
        public string Tier { get; set; } = "light";
        public bool Claimed { get; set; }

        /// <summary>The level as text, so the "Lv " Run pair needs no converter.</summary>
        public string LevelDisplay => Level.ToString(CultureInfo.CurrentCulture);

        public string TierLabel => Tier switch
        {
            "light" => "LIGHT",
            "standard" => "STANDARD",
            "full" => "FULL",
            _ => Tier.ToUpperInvariant()
        };

        public bool HasTags => Tags != null && Tags.Count > 0;
        public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
        public bool IsConnectEnabled => !Claimed;
        public string ConnectButtonText => Claimed ? "Taken" : "Connect";
        public double CardOpacity => Claimed ? 0.6 : 1.0;
    }
}
