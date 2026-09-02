using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Debug preview harness for <see cref="CompanionRoomView"/>. See the XAML header for how it is
    /// opened and why nothing in the app opens it by itself.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionRoomPreviewWindow.xaml.cs.
    /// The strip, the current-state label and the width toggle cross as they are. What does not:
    /// <c>MockCompanionRoomVm</c> and <c>ICompanionRoomVm</c> live in the WPF head, so
    /// <see cref="ShowVariant"/> re-labels the strip but cannot yet swap the page's state.</para>
    /// </summary>
    public partial class CompanionRoomPreviewWindow : Window
    {
        /// <summary>Window width used by the strip's narrow toggle — under the shelf's threshold.</summary>
        private const double NarrowWidth = 1000;

        /// <summary>…and the width it goes back to. Both sit clear of the hysteresis band.</summary>
        private const double WideWidth = 1240;

        // ponytail: mirrors MockCompanionRoomVm.Variants' keys; read the dictionary once the mock moves
        private static readonly string[] Variants = { "default", "freeTier", "dormant", "empty", "drained", "disabled" };

        private readonly Dictionary<string, Button> _stateButtons = new(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Builds, shows and returns the harness. Must be called on the UI thread.</summary>
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
            if (variantKey == null || !_stateButtons.ContainsKey(variantKey)) return false;

            // ponytail: Room.ViewModel = MockCompanionRoomVm.Get(key) once the mock moves off the head
            CurrentVariantKey = variantKey;
            CurrentLabel.Text = variantKey;

            foreach (var pair in _stateButtons)
            {
                bool active = string.Equals(pair.Key, variantKey, StringComparison.OrdinalIgnoreCase);
                pair.Value.Opacity = active ? 1.0 : 0.55;
                pair.Value.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
            }
            return true;
        }

        /// <summary>One button per page state, built from the variant table rather than typed out.</summary>
        private void BuildStateStrip()
        {
            this.TryFindResource("CmpChipButtonStyle", out var chip);
            foreach (var key in Variants)
            {
                var button = new Button
                {
                    // A TextBlock, not a string: "freeTier" is safe but a Button parses "_" as an access key.
                    Content = new TextBlock { Text = Humanize(key) },
                    Tag = key,
                    Margin = new Thickness(0, 2, 8, 2),
                    Theme = chip as ControlTheme
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

        private void StateButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string key }) ShowVariant(key);
        }

        /// <summary>Flips the window across the shelf's collapse threshold.</summary>
        private void WidthToggle_Click(object? sender, RoutedEventArgs e)
            => Width = Width > NarrowWidth + 1 ? NarrowWidth : WideWidth;
    }
}
