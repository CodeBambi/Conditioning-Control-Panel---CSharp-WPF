// PORTED from WPF 7.1.5 Controls/NavRail/SectionTabStrip.Help.cs (owner, 2026-10-07: "add a ?
// button over each chip, so people can hover before clicking to know what it does", and "a
// prompt on Just Drop that asks if they want to open it, since it opens in a new window").
//
// Each pill sits in a host Panel with a small "?" badge on its top-right corner. The badge is a
// SIBLING of the pill button, never inside it, so hovering or clicking the badge never presses,
// lifts or navigates the pill. Its negative margins keep the host exactly the pill's size: the
// strip does not grow by a pixel.
//
// A pill whose kind is Window (NavStripRules.AsksBeforeOpening) shows a small card under itself
// instead of opening at once; only "Open it" raises TabRequested.
//
// Avalonia notes: the hover card opens BELOW the badge (a tooltip under the pointer reports
// PointerExited and flickers); the ask-first card fades in (WPF PopupAnimation.Fade) with an
// opacity transition clamped 0..1 and closes at once, as a light-dismiss popup does.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Nav;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    public sealed partial class SectionTabStrip
    {
        /// <summary>The badge's diameter, and how far it pokes past the pill's top and right edges.</summary>
        internal const double HelpBadgeSize = 17;
        internal const double HelpBadgeOverTop = 5;
        internal const double HelpBadgeOverRight = 4;

        private readonly Dictionary<string, Border> _helpBadges = new(StringComparer.Ordinal);
        private Popup? _confirm;
        private NavTab? _confirmTab;

        /// <summary>The "what is this" line for a pill: its own nav_help key, else the older
        /// label_tip line, else nothing.</summary>
        internal static string HelpText(NavTab tab) =>
            SafeLoc(NavStripRules.HelpKey(tab), SafeLoc(tab.LabelKey + "_tip", string.Empty));

        /// <summary>The pill plus its "?" badge, in one host that is exactly the pill's size.
        /// The pill's gap margin moves to the host.</summary>
        private Control HostWithHelp(string section, NavTab tab, Button pill, string label, uint tint, bool locked)
        {
            var host = new Panel { Margin = pill.Margin, VerticalAlignment = VerticalAlignment.Center };
            pill.Margin = default;
            host.Children.Add(pill);

            var help = HelpText(tab);
            if (string.IsNullOrEmpty(help)) return host;

            var rest = NavPaint.Solid(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.92));
            var lit = NavPaint.Solid(NavStripRules.Mix(tint, 0xFFFFFFFF, 0.15));
            var ring = NavPaint.Solid(NavStripRules.WithAlpha(NavStripRules.Mix(tint, 0xFFFFFFFF, 0.25), 0.95));
            var darkInk = NavPaint.Solid(NavStripRules.DarkInk);
            var mark = new TextBlock
            {
                Text = "?",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
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
                Cursor = new Cursor(StandardCursorType.Help),
                Child = mark,
                Name = "NavPillHelp_" + tab.Key,
            };
            ToolTip.SetTip(badge, HelpCard(section, tab, label, help, locked));
            ToolTip.SetShowDelay(badge, 60);
            ToolTip.SetPlacement(badge, PlacementMode.Bottom);
            AutomationProperties.SetName(badge, "? " + label);
            AutomationProperties.SetHelpText(badge, help);
            AutomationProperties.SetAutomationId(badge, "NavPillHelp_" + tab.Key);

            badge.PointerEntered += (_, _) => { badge.Background = lit; mark.Foreground = darkInk; };
            badge.PointerExited += (_, _) => { badge.Background = rest; mark.Foreground = Brushes.White; };
            // A click on the badge is a request to read, not to go: open the card, swallow the click.
            badge.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(badge).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                try { ToolTip.SetIsOpen(badge, true); }
                catch (Exception ex) { Log.Debug("SectionTabStrip help open: {E}", ex.Message); }
            };

            host.Children.Add(badge);
            _helpBadges[tab.Key] = badge;
            return host;
        }

        /// <summary>The hover card: the page name, what it does, and the tier line for a locked page.</summary>
        private static Control HelpCard(string section, NavTab tab, string label, string help, bool locked)
        {
            var body = new StackPanel { MaxWidth = 300 };
            body.Children.Add(new TextBlock
            {
                Text = CrumbFor(section, label),
                FontWeight = FontWeight.Bold,
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
            string? tier = !locked ? null
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
            return body;
        }

        // ---- "this opens a window" ---------------------------------------------------------

        private void AskBeforeOpening(NavTab tab)
        {
            Control? target = PillFor(tab.Key)?.Parent as Control ?? PillFor(tab.Key);
            if (target == null) { TabRequested?.Invoke(tab); return; }

            CloseConfirm();
            var label = SafeLoc(tab.LabelKey, tab.Key);
            var hue = _hue;

            var title = new TextBlock
            {
                Text = string.Format(SafeLoc("nav_open_window_title", "Open {0}?"), label),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
            };
            var body = new TextBlock
            {
                Text = SafeLoc("nav_open_window_body", "It opens in its own window."),
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 12),
                Foreground = NavPaint.Solid(0xD9FFFFFF),
                TextWrapping = TextWrapping.Wrap,
            };
            var yes = ConfirmButton(SafeLoc("nav_open_window_yes", "Open it"), filled: true, hue);
            var no = ConfirmButton(SafeLoc("nav_open_window_no", "Not now"), filled: false, hue);
            yes.Name = "NavOpenWindowYes";
            no.Name = "NavOpenWindowNo";
            AutomationProperties.SetAutomationId(yes, "NavOpenWindowYes");
            AutomationProperties.SetAutomationId(no, "NavOpenWindowNo");
            yes.Click += (_, _) => AnswerConfirm(open: true);
            no.Click += (_, _) => AnswerConfirm(open: false);
            no.Margin = new Thickness(8, 0, 0, 0);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);

            bool fade = Level != MotionLevel.Off;
            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 12),
                MinWidth = 240,
                MaxWidth = 340,
                Background = NavPaint.Solid(0xF51B1026),
                BorderBrush = NavPaint.Solid(NavStripRules.WithAlpha(hue, 0.85)),
                BorderThickness = new Thickness(1.5),
                Opacity = fade ? 0 : 1,
                Child = new StackPanel { Children = { title, body, buttons } },
            };
            if (fade)
                card.Transitions = new Transitions
                {
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(Level == MotionLevel.Reduced ? 75 : 150) },
                };

            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 8,
                IsLightDismissEnabled = true,
                Child = card,
            };
            popup.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_confirm, popup)) return;
                _confirm = null;
                _confirmTab = null;
                TrayHost.Children.Remove(popup);
            };
            _confirm = popup;
            _confirmTab = tab;
            try
            {
                TrayHost.Children.Add(popup);   // a Popup needs a place in the tree; it takes no room
                popup.IsOpen = true;
                card.Opacity = 1;               // the IN: 0 -> 1 through the transition
                yes.Focus();
            }
            catch (Exception ex) { Log.Debug("SectionTabStrip.AskBeforeOpening: {E}", ex.Message); }
        }

        private void AnswerConfirm(bool open)
        {
            var tab = _confirmTab;
            CloseConfirm();
            if (open && tab != null) TabRequested?.Invoke(tab);
        }

        private void CloseConfirm()
        {
            var popup = _confirm;
            _confirm = null;
            _confirmTab = null;
            if (popup == null) return;
            try { popup.IsOpen = false; } catch { /* never opened (no window) */ }
            TrayHost.Children.Remove(popup);
        }

        private static Button ConfirmButton(string text, bool filled, uint hue)
        {
            var bg = filled ? NavPaint.Solid(hue) : NavPaint.Solid(0x1FFFFFFF);
            var fg = filled ? NavPaint.Solid(NavStripRules.ActiveTextOn(hue)) : Brushes.White;
            var button = new Button
            {
                Background = bg,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = fg },
                Template = new FuncControlTemplate<Button>((b, _) => new Border
                {
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(14, 5, 14, 6),
                    [!Border.BackgroundProperty] = b[!BackgroundProperty],
                    Child = new global::Avalonia.Controls.Presenters.ContentPresenter
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        [!global::Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = b[!ContentProperty],
                    },
                }),
            };
            // WPF's IsMouseOver trigger: the plate dims to 85% under the pointer.
            button.PointerEntered += (_, _) => button.Opacity = 0.85;
            button.PointerExited += (_, _) => button.Opacity = 1;
            return button;
        }

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: the "?" badge drawn over a pill (null when the pill has no help line).</summary>
        internal Border? HelpBadgeFor(string key) => _helpBadges.TryGetValue(key, out var b) ? b : null;

        /// <summary>Test seam: is the "opens a window" card up, and for which pill.</summary>
        internal string? ConfirmingKey => _confirmTab?.Key;

        /// <summary>Test seam: answer the "opens a window" card.</summary>
        internal void AnswerConfirmForTests(bool open) => AnswerConfirm(open);
    }
}
