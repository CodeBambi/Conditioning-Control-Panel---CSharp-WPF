using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The padlock at the foot of the nav rail. Opens Circe's tab (<c>ShowTab("chaster")</c>).
    ///
    /// <para>It took the slot the spiral medallion had (owner, 2026-09-21): the Spiral Room is
    /// still one click away from the profile, and this page had no door at all. Always visible,
    /// linked or not, so the feature can be found; the page itself says what it is to a player
    /// who has never heard of Chaster.</para>
    ///
    /// <para>Code-built and vector on purpose, the same as the chip it replaced: it is NOT a door
    /// medallion, so it has no NavDoorMap row, no mod art slot (it looks the same under every mod)
    /// and none of the 40px door Viewbox markup NavRailFlyoutTests counts.</para>
    /// </summary>
    public sealed class ChasterRailChip : Grid
    {
        public const string TabKey = "chaster";

        private const double ChipHeight = 56;
        private const double BadgeSize = 40;

        // A padlock in a 24 box: the shackle, then the body with a keyhole cut out of it.
        private const string ShackleData = "M7.5,11 V8 a4.5,4.5 0 0 1 9,0 V11";
        private const string BodyData = "M5,11 h14 a1.5,1.5 0 0 1 1.5,1.5 v7 a1.5,1.5 0 0 1 -1.5,1.5 h-14 a1.5,1.5 0 0 1 -1.5,-1.5 v-7 a1.5,1.5 0 0 1 1.5,-1.5 Z "
                                        + "M12,14 a1.4,1.4 0 0 0 -0.7,2.6 v1.6 h1.4 v-1.6 a1.4,1.4 0 0 0 -0.7,-2.6 Z";

        public ChasterRailChip()
        {
            Height = ChipHeight;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent; // the whole 56px row takes the click, not just the ring

            var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xCB));
            var lockArt = new Canvas { Width = 24, Height = 24 };
            lockArt.Children.Add(new Path
            {
                Data = Geometry.Parse(ShackleData),
                Stroke = ink,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
            lockArt.Children.Add(new Path { Data = Geometry.Parse(BodyData), Fill = ink, });

            Children.Add(new Border
            {
                Width = BadgeSize,
                Height = BadgeSize,
                CornerRadius = new CornerRadius(BadgeSize / 2),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Viewbox { Width = 22, Height = 22, Child = lockArt },
            });

            SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[chaster_title]")
            {
                Source = LocalizationManager.Instance,
                Mode = System.Windows.Data.BindingMode.OneWay,
            });
            MouseLeftButtonUp += (_, _) => Open();
        }

        private static void Open()
        {
            try
            {
                // MainWindow can be the launcher window, and is null in the tray.
                var main = App.MainWindowRef ?? Application.Current?.MainWindow as ConditioningControlPanel.MainWindow;
                main?.ShowTab(TabKey);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip: {E}", ex.Message); }
        }
    }
}
