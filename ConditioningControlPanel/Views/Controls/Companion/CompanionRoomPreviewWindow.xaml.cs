using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Debug preview harness for <see cref="CompanionRoomView"/>. See the XAML header for how it is
    /// opened and why nothing in the app opens it by itself.
    /// </summary>
    public partial class CompanionRoomPreviewWindow : Window
    {
        /// <summary>Window width used by the strip's narrow toggle — under the shelf's threshold.</summary>
        private const double NarrowWidth = 1000;

        /// <summary>…and the width it goes back to. Both sit clear of the hysteresis band.</summary>
        private const double WideWidth = 1240;

        private readonly Dictionary<string, Button> _stateButtons =
            new(StringComparer.OrdinalIgnoreCase);

        public CompanionRoomPreviewWindow()
        {
            InitializeComponent();
            BuildStateStrip();
            ShowVariant(DefaultVariantKey);
        }

        /// <summary>The page state the harness opens on.</summary>
        public const string DefaultVariantKey = "default";

        /// <summary>The key currently on screen. Also rendered into the strip for the driver.</summary>
        public string CurrentVariantKey { get; private set; } = DefaultVariantKey;

        /// <summary>The composed page, for a driver that wants to poke it directly.</summary>
        public CompanionRoomView RoomView => Room;

        /// <summary>The viewmodel behind the page right now.</summary>
        public MockCompanionRoomVm? CurrentRoom => Room.DataContext as MockCompanionRoomVm;

        // =====================================================================================
        //  entry points
        // =====================================================================================

        /// <summary>
        /// Builds, shows and returns the harness. Must be called on a UI thread (the app's
        /// dispatcher, or an STA thread of your own).
        ///
        /// <para><paramref name="variantKey"/> is one of <see cref="MockCompanionRoomVm.Variants"/>;
        /// an unknown key is ignored and the artboard opens, because a harness that refuses to open
        /// over a typo helps nobody.</para>
        /// </summary>
        public static CompanionRoomPreviewWindow Launch(string? variantKey = null)
        {
            var window = new CompanionRoomPreviewWindow();
            if (!string.IsNullOrWhiteSpace(variantKey)) window.ShowVariant(variantKey!);
            window.Show();
            return window;
        }

        /// <summary>
        /// Swaps the page state. No-op on an unknown key — the current page stays exactly as it is
        /// rather than blanking, so a mistyped key in a driver script is visible but harmless.
        /// </summary>
        public bool ShowVariant(string? variantKey)
        {
            var vm = MockCompanionRoomVm.Get(variantKey);
            if (vm == null) return false;

            Room.ViewModel = vm;
            CurrentVariantKey = variantKey!;
            CurrentLabel.Text = variantKey!;

            foreach (var pair in _stateButtons)
            {
                bool active = string.Equals(pair.Key, variantKey, StringComparison.OrdinalIgnoreCase);
                pair.Value.Opacity = active ? 1.0 : 0.55;
                pair.Value.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            }
            return true;
        }

        // =====================================================================================
        //  the strip
        // =====================================================================================

        /// <summary>
        /// One button per page state, built from the variant table rather than typed out, so a state
        /// added to <see cref="MockCompanionRoomVm"/> appears here without anyone remembering to.
        /// </summary>
        private void BuildStateStrip()
        {
            foreach (var key in MockCompanionRoomVm.Variants.Keys)
            {
                var button = new Button
                {
                    Content = Humanize(key),
                    Tag = key,
                    Margin = new Thickness(0, 2, 8, 2),
                    Style = TryFindResource("CmpChipButtonStyle") as Style
                };
                AutomationProperties.SetAutomationId(button, "CtabPreview_" + key);
                AutomationProperties.SetName(button, key);
                button.Click += StateButton_Click;

                _stateButtons[key] = button;
                StateStrip.Children.Add(button);
            }
        }

        /// <summary>"freeTier" → "free tier". Display only; the key is what automation uses.</summary>
        private static string Humanize(string key)
        {
            var chars = new List<char>(key.Length + 2);
            foreach (char c in key)
            {
                if (char.IsUpper(c)) { chars.Add(' '); chars.Add(char.ToLowerInvariant(c)); }
                else chars.Add(c);
            }
            return new string(chars.ToArray());
        }

        private void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string key }) ShowVariant(key);
        }

        /// <summary>
        /// Flips the window across the shelf's collapse threshold, which is the one piece of this
        /// page a still screenshot cannot show you.
        /// </summary>
        private void WidthToggle_Click(object sender, RoutedEventArgs e)
            => Width = Width > NarrowWidth + 1 ? NarrowWidth : WideWidth;
    }
}
