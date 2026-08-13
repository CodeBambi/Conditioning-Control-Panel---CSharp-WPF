using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Exclusives "Velvet Vault" tab shell. All roster/card logic lives in
    /// MainWindow.Exclusives.cs (house pattern: views delegate to MainWindow);
    /// this code-behind only owns the backdrop and geometry chores.
    /// </summary>
    public partial class ExclusivesTabView : UserControl
    {
        /// <summary>
        /// The room: a neutral black/grey/neon-blue cyber-vault plate, so every mod's FxTheme
        /// accent washes over it without clashing. A plain gradient (the XAML background) is the
        /// fallback if nothing resolves.
        ///
        /// <para>This used to be a loose Content file read from
        /// <c>AppContext.BaseDirectory\assets\exclusives\</c>, which put it outside
        /// <c>Resources\</c> and therefore outside every mod's reach - a .ccpmod shipping
        /// <c>resources/exclusives/vault_backdrop.png</c> was silently ignored. It now lives in
        /// <c>Resources\exclusives\</c> as a build-action Resource and resolves through
        /// <see cref="ModResourceResolver"/> like the rest of the app's mod-skinnable art.</para>
        /// </summary>
        private const string BackdropResource = "exclusives/vault_backdrop.png";

        /// <summary>
        /// Decode cap for the plate. The shipped art is 1376 wide - i.e. essentially native - and
        /// the room paints the full tab width, so this is a ceiling for a mod that ships a 4K
        /// backdrop rather than a downscale of ours.
        /// </summary>
        private const int BackdropDecodeWidth = 1400;

        public ExclusivesTabView()
        {
            InitializeComponent();
            LoadBackdrop();

            // WPF's ClipToBounds is rectangular; rounded corners need explicit
            // clip geometry that tracks the element's size.
            // 12, not 14: the spotlight card wears the round-4 velvet edge (2px), so the
            // host inside it has the card's inner radius, not its outer one.
            RoundClipOnResize(SpotArtHost, 12);

            // The tab is permanently mounted, so the room has to follow the active mod itself -
            // nothing else repaints it. Hooked on Loaded / unhooked on Unloaded (the neighbours'
            // idiom, e.g. Features/BubblePopFeatureControl) so this never outlives the view.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        /// <summary>ModChanged may be raised off the UI thread; marshal before touching the Image.</summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
            => Dispatcher.BeginInvoke(new Action(LoadBackdrop));

        private void LoadBackdrop()
        {
            try
            {
                // Null means neither the mod nor the embedded copy could be decoded: keep whatever
                // is already painted (the XAML gradient on first load) rather than blanking the room.
                var art = ModResourceResolver.ResolveImageDecoded(BackdropResource, BackdropDecodeWidth);
                if (art != null) VaultBackdrop.Source = art;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Exclusives backdrop load failed: {E}", ex.Message);
            }
        }

        /// <summary>Keeps a rounded-rect Clip on <paramref name="host"/> matched to its size.</summary>
        internal static void RoundClipOnResize(FrameworkElement host, double radius)
        {
            host.SizeChanged += (_, e) =>
            {
                try
                {
                    host.Clip = new RectangleGeometry(
                        new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
                }
                catch { }
            };
        }

        private void Spotlight_Click(object sender, RoutedEventArgs e) => OpenSpotlight();

        private void Spotlight_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenSpotlight();

        private void OpenSpotlight()
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenExclusiveSpotlight();
        }
    }
}
