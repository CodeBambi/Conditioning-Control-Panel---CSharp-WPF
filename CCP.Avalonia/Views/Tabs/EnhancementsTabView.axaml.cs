using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/EnhancementsTabView.xaml.cs.
    ///
    /// <para>The WPF original is one handler wide: the skill tree's wheel redirect. It ports for
    /// real - it touches nothing but the ScrollViewer it is attached to.</para>
    ///
    /// <para>What is NOT here is the tab's content. Both the tree canvas and the secret rail are
    /// filled by MainWindow.Enhancements.cs (DrawSkillTree / PopulateSecretSkills) off
    /// <c>App.SkillTree</c> and <c>Models.SkillDefinition.All</c>, neither of which is on this head.
    /// This file paints a SAMPLE of each instead, with the real loc keys, so the 460dip board and
    /// the 90dip rail do not render as two unexplained blanks in the render proof.
    /// ponytail: needs ConditioningControlPanel/Services/SkillTreeService.cs and
    /// ConditioningControlPanel/Models/SkillTree.cs (SkillDefinition.All), both still in the WPF
    /// head. The one number that IS live is the sparkle-point count, which is a plain setting.</para>
    /// </summary>
    public partial class EnhancementsTabView : UserControl
    {
        /// <summary>Node box, verbatim from MainWindow.Enhancements.cs (NodeWidth/NodeHeight).</summary>
        private const double NodeWidth = 156, NodeHeight = 139;

        /// <summary>Rail card box, verbatim from MainWindow.Enhancements.cs.</summary>
        private const double SecretCardWidth = 180, SecretCardHeight = 56;

        public EnhancementsTabView()
        {
            InitializeComponent();

            // Tunneling, like WPF's Preview- pair, so the redirect wins before the ScrollViewer's
            // own handler consumes the wheel.
            SkillTreeScroller.AddHandler(PointerWheelChangedEvent, OnSkillTreeWheel, RoutingStrategies.Tunnel);

            PaintSampleTree();
            PaintSampleSecretRail();
        }

        /// <summary>
        /// Redirects vertical wheel to horizontal scrolling for the skill tree — EXCEPT when the
        /// wheel is over a nested vertically-scrollable region (the tree-header panel with its
        /// stats/analytics expanders). There we yield so the inner viewer scrolls vertically;
        /// otherwise its content below the canvas fold would be unreachable.
        ///
        /// <para>The step is 60px, not WPF's <c>Delta * 0.5</c>: WPF reports ±120 per notch and
        /// Avalonia reports ±1, so keeping the multiplier would have scrolled half a pixel.</para>
        /// </summary>
        private void OnSkillTreeWheel(object? sender, PointerWheelEventArgs e)
        {
            const double stepPerNotch = 60;

            for (var el = e.Source as Visual; el is not null && el != SkillTreeScroller; el = el.GetVisualParent())
            {
                if (el is ScrollViewer inner && inner.Extent.Height > inner.Viewport.Height)
                    return; // let the inner viewer take the wheel (vertical scroll)
            }

            var offset = SkillTreeScroller.Offset;
            SkillTreeScroller.Offset = new Vector(offset.X - e.Delta.Y * stepPerNotch, offset.Y);
            e.Handled = true;
        }

        // =====================================================================================
        //  sample content — see the class summary
        // =====================================================================================

        /// <summary>
        /// The header block plus the root node and the first node of each of the three paths, at
        /// the coordinates DrawSkillTree uses (header at x=5, root at 570, columns 270 apart, rows
        /// 160 apart), with the connection lines drawn behind them.
        /// </summary>
        private void PaintSampleTree()
        {
            const double startX = 570, colSpacing = 270, rowSpacing = 160;
            double rootY = rowSpacing;

            var branches = new (string Key, double X, double Y)[]
            {
                ("skill_ditzy_data_name",       startX + colSpacing, 0),
                ("skill_sparkle_boost_1_name",  startX + colSpacing, rowSpacing),
                ("skill_good_girl_streak_name", startX + colSpacing, rowSpacing * 2),
            };

            // Lines first, so they sit behind the nodes.
            foreach (var b in branches)
                SkillTreeCanvas.Children.Add(Connector(
                    startX + NodeWidth, rootY + NodeHeight / 2,
                    b.X, b.Y + NodeHeight / 2));

            SkillTreeCanvas.Children.Add(Place(SampleHeader(), 5, 0));
            SkillTreeCanvas.Children.Add(Place(SampleNode("skill_pink_hours_name", owned: true), startX, rootY));
            foreach (var b in branches)
                SkillTreeCanvas.Children.Add(Place(SampleNode(b.Key, owned: false), b.X, b.Y));
        }

        private static Control Place(Control c, double left, double top)
        {
            Canvas.SetLeft(c, left);
            Canvas.SetTop(c, top);
            return c;
        }

        private static Control Connector(double x1, double y1, double x2, double y2) => new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x3A, 0x5C)),
            StrokeThickness = 2,
        };

        /// <summary>The 500dip stats header CreateSkillTreeHeader draws at the start of the canvas.</summary>
        private static Control SampleHeader()
        {
            var stack = new StackPanel();

            stack.Children.Add(Text("✨ " + Loc.Get("label_enhancement_tree_title"),
                Color.FromRgb(0xFF, 0x69, 0xB4), 22, bold: true));
            stack.Children.Add(Text(Loc.Get("label_enhancement_tree_subtitle"),
                Color.FromRgb(0xB0, 0xB0, 0xB0), 11, italic: true, top: 4));
            stack.Children.Add(Text(Loc.Get("label_enhancement_tree_warning"),
                Color.FromRgb(0x88, 0xAA, 0xCC), 10, italic: true, top: 2));

            var points = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            points.Children.Add(new TextBlock
            {
                Text = "💎",
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            var info = new StackPanel();
            info.Children.Add(Text(Loc.Get("label_sparkle_points"), Color.FromRgb(0xB0, 0xB0, 0xB0), 10));
            // The live count, as CreateSkillTreeHeader reads it. Spending is what needs
            // SkillTreeService; the balance is a plain setting and reads correctly today.
            info.Children.Add(Text($"{CoreSettings.Current.SkillPoints}", Color.FromRgb(0xFF, 0x69, 0xB4), 24, bold: true));
            points.Children.Add(info);

            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x4A)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 15, 0, 0),
                Child = points,
            });

            return new Border
            {
                Width = 500,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x3C)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 8, 15, 15),
                Child = stack,
            };
        }

        private static Control SampleNode(string nameKey, bool owned)
        {
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) };
            body.Children.Add(new TextBlock
            {
                Text = owned ? "⭐" : "💠",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var name = Text(Loc.Get(nameKey), owned ? Color.FromRgb(0x64, 0xFF, 0x96) : Color.FromRgb(0xE6, 0xE6, 0xF0), 12, bold: true);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.TextAlignment = TextAlignment.Center;
            name.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(name);

            return new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x1E, 0x36)),
                BorderThickness = new Thickness(owned ? 2 : 1),
                BorderBrush = new SolidColorBrush(owned ? Color.FromRgb(0x64, 0xFF, 0x96) : Color.FromRgb(0x3C, 0x32, 0x46)),
                Child = body,
            };
        }

        /// <summary>
        /// Three hidden secret cards, exactly what PopulateSecretSkills draws while none of the
        /// three requirements is met: the padlock, the withheld-name label, and the hint.
        /// </summary>
        private void PaintSampleSecretRail()
        {
            foreach (var id in new[] { "night_shift", "early_bird_bimbo", "eternal_doll" })
                SecretSkills.Children.Add(HiddenSecretCard(Loc.Get($"rf_skill_{id}_req")));
        }

        private static Control HiddenSecretCard(string requirement)
        {
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            body.Children.Add(new TextBlock
            {
                Text = "🔒",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            text.Children.Add(Text(Loc.Get("label_secret_skill_hidden"), Color.FromRgb(0x99, 0x32, 0xCC), 11, bold: true));
            var hint = Text(requirement, Color.FromRgb(0x80, 0x80, 0x80), 8, top: 1);
            hint.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(hint);
            body.Children.Add(text);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 20, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 60, 100)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Width = SecretCardWidth,
                Height = SecretCardHeight,
                Margin = new Thickness(0, 3, 10, 3),
                Padding = new Thickness(8, 6, 8, 6),
                Opacity = 0.6,
                Child = body,
            };
        }

        private static TextBlock Text(string text, Color colour, double size,
                                      bool bold = false, bool italic = false, double top = 0) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(colour),
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Margin = new Thickness(0, top, 0, 0),
        };
    }
}
