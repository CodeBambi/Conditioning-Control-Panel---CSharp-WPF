using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/EnhancementsTabView.xaml.cs.
    ///
    /// <para>The WPF original is one handler wide: the skill tree's wheel redirect. It ports for
    /// real - it touches nothing but the ScrollViewer it is attached to.</para>
    ///
    /// <para>The tree canvas and the secret rail are MainWindow.Enhancements.cs's DrawSkillTree /
    /// PopulateSecretSkills over Core's <c>Models.SkillDefinition.All</c>, with ownership read
    /// through Core <c>SkillTreeRules.HasSkill</c>. Purchasing is unavailable: PurchaseSkillAsync
    /// is a server call behind account auth (not ported), so every unowned node draws locked and
    /// nothing is clickable.</para>
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

            Repaint();
        }

        private System.ComponentModel.INotifyPropertyChanged? _settings;

        // WPF repaints from SkillTreeService events; the Core signal is AppSettings.PropertyChanged.
        // UnlockedSkills only raises on assignment, not on an in-place Add.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _settings = CoreSettings.Current;
            _settings.PropertyChanged += OnSettingsChanged;
            Repaint();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSettings.SkillPoints) or nameof(AppSettings.UnlockedSkills))
                global::Avalonia.Threading.Dispatcher.UIThread.Post(Repaint);
        }

        private void Repaint()
        {
            SkillTreeCanvas.Children.Clear();
            SecretSkills.Children.Clear();
            PaintTree();
            PaintSecretRail();
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
        //  DrawSkillTree / PopulateSecretSkills, read-only
        // =====================================================================================

        /// <summary>DrawSkillTree's layout, verbatim: header at x=5, root at 570, columns 270
        /// apart, the three paths 160 apart, the analytics chain on the middle row.</summary>
        internal static Dictionary<string, (double X, double Y)> NodePositions()
        {
            const double startX = 570, colSpacing = 270, rowSpacing = 160;
            var pos = new Dictionary<string, (double X, double Y)> { ["pink_hours"] = (startX, rowSpacing) };
            void Row(double y, int firstCol, params string[] ids)
            {
                for (int i = 0; i < ids.Length; i++) pos[ids[i]] = (startX + colSpacing * (firstCol + i), y);
            }
            Row(0, 1, "ditzy_data", "hive_mind", "trophy_case", "popular_girl", "quest_refresh", "better_quests");
            Row(rowSpacing, 1, "sparkle_boost_1", "sparkle_boost_2", "lucky_bimbo", "sparkle_boost_3", "lucky_bubbles", "pink_rush");
            Row(rowSpacing * 2, 1, "good_girl_streak", "milestone_rewards", "oopsie_insurance", "streak_power", "reroll_addict", "perfect_bimbo_week");
            Row(rowSpacing, 7, "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo");
            return pos;
        }

        private void PaintTree()
        {
            var settings = CoreSettings.Current;
            var positions = NodePositions();
            var tree = SkillDefinition.All.Where(s => !s.IsSecret && positions.ContainsKey(s.Id)).ToList();

            // DrawConnectionLines' pairs are exactly each node's prerequisite edge. Lines first,
            // so they sit behind the nodes.
            foreach (var skill in tree.Where(s => s.PrerequisiteId != null && positions.ContainsKey(s.PrerequisiteId)))
            {
                var (px, py) = positions[skill.PrerequisiteId!];
                var (cx, cy) = positions[skill.Id];
                SkillTreeCanvas.Children.Add(Connector(px + NodeWidth, py + NodeHeight / 2, cx, cy + NodeHeight / 2,
                    SkillTreeRules.HasSkill(settings, skill.Id), SkillTreeRules.HasSkill(settings, skill.PrerequisiteId!)));
            }

            SkillTreeCanvas.Children.Add(Place(Header(), 5, 0));
            foreach (var skill in tree)
                SkillTreeCanvas.Children.Add(Place(Node(skill, SkillTreeRules.HasSkill(settings, skill.Id)), positions[skill.Id].X, positions[skill.Id].Y));
        }

        private static Control Place(Control c, double left, double top)
        {
            Canvas.SetLeft(c, left);
            Canvas.SetTop(c, top);
            return c;
        }

        /// <summary>DrawConnectionLines' colours, minus the purchasable accent (purchasing is not ported).</summary>
        private static Control Connector(double x1, double y1, double x2, double y2, bool childOwned, bool parentOwned) => new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(childOwned ? Color.FromRgb(100, 255, 150)
                : parentOwned ? Color.Parse(CoreMods.AccentColorHex) : Color.FromRgb(60, 60, 80)),
            StrokeThickness = childOwned ? 3 : 2,
            Opacity = childOwned || parentOwned ? 1.0 : 0.3,
        };

        /// <summary>The 500dip stats header CreateSkillTreeHeader draws at the start of the canvas.</summary>
        private static Control Header()
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
            info.Children.Add(Text(CoreSettings.Current.SkillPoints.ToString("N0"), Color.Parse(CoreMods.AccentColorHex), 24, bold: true));
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

        /// <summary>CreateSkillNode: art row (tier gradient; ModResourceResolver art is head-side),
        /// name, and the cost badge - gold FOREVER when owned, locked otherwise.</summary>
        internal static Control Node(SkillDefinition skill, bool owned)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("86,20,3,28") };
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x44)),
                Child = new TextBlock { Text = skill.Icon, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Opacity = owned ? 1 : 0.5,
            });

            var name = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 28, 45)),
                Child = new TextBlock
                {
                    Text = CoreMods.MakeModAware(skill.LocalizedName),
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                    FontSize = 9.5,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetRow(name, 1);
            grid.Children.Add(name);

            var cost = new Border
            {
                Background = new SolidColorBrush(owned ? Color.FromRgb(255, 200, 80) : Color.FromRgb(40, 35, 50)),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = owned ? $"💎{skill.Cost} {Loc.Get("label_skill_permanent")}" : $"🔒 {skill.Cost}",
                    Foreground = new SolidColorBrush(owned ? Color.FromRgb(20, 20, 30) : Color.FromRgb(120, 120, 130)),
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetRow(cost, 3);
            grid.Children.Add(cost);

            var node = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Tag = skill.Id,
                BorderThickness = new Thickness(owned ? 2 : 1),
                BorderBrush = new SolidColorBrush(owned ? Color.FromRgb(100, 255, 150) : Color.FromRgb(60, 50, 70)),
                Child = grid,
            };
            ToolTip.SetTip(node, CoreMods.MakeModAware(skill.LocalizedDescription));
            return node;
        }

        /// <summary>PopulateSecretSkills. Every unowned secret draws hidden; revealing a met
        /// requirement (SkillTreeRules.IsSecretSkillAvailable, now in Core) is not drawn yet.</summary>
        private void PaintSecretRail()
        {
            var settings = CoreSettings.Current;
            foreach (var skill in SkillDefinition.All.Where(s => s.IsSecret))
                SecretSkills.Children.Add(HiddenSecretCard(SkillTreeRules.HasSkill(settings, skill.Id)
                    ? skill.LocalizedName : skill.LocalizedSecretRequirementDesc, SkillTreeRules.HasSkill(settings, skill.Id)));
        }

        private static Control HiddenSecretCard(string requirement, bool owned = false)
        {
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            body.Children.Add(new TextBlock
            {
                Text = owned ? "✨" : "🔒",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            text.Children.Add(Text(Loc.Get(owned ? "label_skill_permanent" : "label_secret_skill_hidden"), Color.FromRgb(0x99, 0x32, 0xCC), 11, bold: true));
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
                Opacity = owned ? 1.0 : 0.6,
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
