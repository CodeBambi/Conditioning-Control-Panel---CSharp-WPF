using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The pill help (owner, 2026-10-07): "add a ? button over each chip, so people can hover
    /// before clicking to know what it does", and "a prompt on Just Drop that asks if they want
    /// to open it, since it opens in a new window".
    ///
    /// <para>Each pill sits in a host Grid with a small "?" badge on its top-right corner. The
    /// badge is a SIBLING of the pill button, never inside it, so hovering or clicking the badge
    /// never presses, squishes or navigates the pill. Its negative margins keep the host exactly
    /// the pill's size: the strip does not grow by a pixel.</para>
    ///
    /// <para>A pill whose kind is Window (NavStripRules.AsksBeforeOpening) shows a small card under
    /// itself instead of opening at once; only "Open it" raises TabRequested.</para>
    /// </summary>
    public partial class SectionTabStrip
    {
        /// <summary>The badge's diameter, and how far it pokes past the pill's top and right edges.</summary>
        internal const double HelpBadgeSize = 17;
        internal const double HelpBadgeOverTop = 5;
        internal const double HelpBadgeOverRight = 4;

        private readonly Dictionary<string, FrameworkElement> _helpBadges = new(StringComparer.Ordinal);
        private Popup? _confirm;
        private NavTab? _confirmTab;

        /// <summary>The "what is this" line for a pill: its own nav_help key, else the older
        /// label_tip line, else nothing.</summary>
        internal static string HelpText(NavTab tab) =>
            SafeLoc(NavStripRules.HelpKey(tab), SafeLoc(tab.LabelKey + "_tip", string.Empty));

        /// <summary>The pill plus its "?" badge, in one host that is exactly the pill's size.</summary>
        private FrameworkElement HostWithHelp(string section, NavTab tab, Button pill, string label, Color tint, Thickness gap)
        {
            var host = new Grid { Margin = gap, VerticalAlignment = VerticalAlignment.Center };
            host.Children.Add(pill);

            var help = HelpText(tab);
            if (string.IsNullOrEmpty(help)) return host;

            var rest = Freeze(new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.92)));
            var lit = Freeze(new SolidColorBrush(NavStripRules.Mix(tint, Colors.White, 0.15)));
            var ring = Freeze(new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.Mix(tint, Colors.White, 0.25), 0.95)));
            var mark = new TextBlock
            {
                Text = "?",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            };
            var badge = new Border
            {
                Width = HelpBadgeSize,
                Height = HelpBadgeSize,
                CornerRadius = new CornerRadius(HelpBadgeSize / 2),
                Background = rest,
                BorderBrush = ring,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -HelpBadgeOverTop, -HelpBadgeOverRight, 0),
                Cursor = Cursors.Help,
                Child = mark,
                ToolTip = HelpCard(section, tab, label, help),
            };
            ToolTipService.SetInitialShowDelay(badge, 60);
            ToolTipService.SetShowDuration(badge, 60000);
            ToolTipService.SetPlacement(badge, PlacementMode.Bottom);
            System.Windows.Automation.AutomationProperties.SetName(badge, "? " + label);
            System.Windows.Automation.AutomationProperties.SetHelpText(badge, help);
            System.Windows.Automation.AutomationProperties.SetAutomationId(badge, "NavPillHelp_" + tab.Key);

            badge.MouseEnter += (_, _) => { badge.Background = lit; mark.Foreground = new SolidColorBrush(NavStripRules.DarkInk); };
            badge.MouseLeave += (_, _) => { badge.Background = rest; mark.Foreground = Brushes.White; };
            // A click on the badge is a request to read, not to go: open the card, swallow the click.
            badge.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (badge.ToolTip is ToolTip tip) { tip.PlacementTarget = badge; tip.IsOpen = true; }
            };

            host.Children.Add(badge);
            _helpBadges[tab.Key] = badge;
            return host;
        }

        /// <summary>The hover card: the page name, what it does, and the tier line for a locked page.</summary>
        private static ToolTip HelpCard(string section, NavTab tab, string label, string help)
        {
            var body = new StackPanel { MaxWidth = 300 };
            body.Children.Add(new TextBlock
            {
                Text = CrumbFor(section, label),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock
            {
                Text = help,
                FontSize = 12.5,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            string? tier = !NavStripRules.PillLocked(tab.Tier) ? null
                : tab.Tier == 1 ? SafeLoc("nav_tag_premium_tip", string.Empty)
                : SafeLoc("nav_tag_lab_tip", string.Empty);
            if (!string.IsNullOrEmpty(tier))
                body.Children.Add(new TextBlock
                {
                    Text = tier,
                    FontSize = 11.5,
                    Opacity = 0.75,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
            return new ToolTip { Content = body };
        }

        // ---- "this opens a window" ---------------------------------------------------------

        private void AskBeforeOpening(NavTab tab)
        {
            var target = (FrameworkElement?)PillFor(tab.Key)?.Parent ?? PillFor(tab.Key);
            if (target == null) { TabRequested?.Invoke(tab); return; }

            CloseConfirm();
            _confirmTab = tab;
            var label = SafeLoc(tab.LabelKey, tab.Key);
            var hue = _hue;

            var title = new TextBlock
            {
                Text = string.Format(SafeLoc("nav_open_window_title", "Open {0}?"), label),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
            };
            var body = new TextBlock
            {
                Text = SafeLoc("nav_open_window_body", "It opens in its own window."),
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 12),
                Foreground = Freeze(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF))),
                TextWrapping = TextWrapping.Wrap,
            };
            var yes = ConfirmButton(SafeLoc("nav_open_window_yes", "Open it"), filled: true, hue);
            var no = ConfirmButton(SafeLoc("nav_open_window_no", "Not now"), filled: false, hue);
            System.Windows.Automation.AutomationProperties.SetAutomationId(yes, "NavOpenWindowYes");
            System.Windows.Automation.AutomationProperties.SetAutomationId(no, "NavOpenWindowNo");
            yes.Click += (_, _) => AnswerConfirm(open: true);
            no.Click += (_, _) => AnswerConfirm(open: false);
            no.Margin = new Thickness(8, 0, 0, 0);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);

            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 12),
                MinWidth = 240,
                MaxWidth = 340,
                Background = Freeze(new SolidColorBrush(Color.FromArgb(0xF5, 0x1B, 0x10, 0x26))),
                BorderBrush = Freeze(new SolidColorBrush(NavStripRules.WithAlpha(hue, 0.85))),
                BorderThickness = new Thickness(1.5),
                Child = new StackPanel { Children = { title, body, buttons } },
            };

            _confirm = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 8,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = Level == MotionLevel.Off ? PopupAnimation.None : PopupAnimation.Fade,
                Child = card,
            };
            _confirm.Closed += (_, _) => { _confirm = null; _confirmTab = null; };
            _confirm.IsOpen = true;
            yes.Focus();
        }

        private void AnswerConfirm(bool open)
        {
            var tab = _confirmTab;
            CloseConfirm();
            if (open && tab != null) TabRequested?.Invoke(tab);
        }

        private void CloseConfirm()
        {
            if (_confirm != null) _confirm.IsOpen = false;
            _confirm = null;
            _confirmTab = null;
        }

        private static Button ConfirmButton(string text, bool filled, Color hue)
        {
            var bg = filled ? Freeze(new SolidColorBrush(hue)) : Freeze(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));
            var fg = filled ? Freeze(new SolidColorBrush(NavStripRules.ActiveTextOn(hue))) : (Brush)Brushes.White;

            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetValue(Border.PaddingProperty, new Thickness(14, 5, 14, 6));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "Bd"));
            template.Triggers.Add(hover);

            return new Button
            {
                Template = template,
                Background = bg,
                Cursor = Cursors.Hand,
                Content = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg },
            };
        }

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: the "?" badge drawn over a pill (null when the pill has no help line).</summary>
        internal FrameworkElement? HelpBadgeFor(string key) => _helpBadges.TryGetValue(key, out var b) ? b : null;

        /// <summary>Test seam: is the "opens a window" card up, and for which pill.</summary>
        internal string? ConfirmingKey => _confirmTab?.Key;

        /// <summary>Test seam: choose a pill as a click would.</summary>
        internal void ChooseForTests(string key)
        {
            if (Part(key) is { } p) Choose(p.Tab, focus: false);
        }

        /// <summary>Test seam: answer the "opens a window" card.</summary>
        internal void AnswerConfirmForTests(bool open) => AnswerConfirm(open);
    }
}
