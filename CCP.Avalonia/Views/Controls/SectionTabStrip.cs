// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.xaml(.cs) (nav rework
// 2026-10-06): the section page header - breadcrumb "Section > Page" (the section word returns to
// that section's last tab), the section's pills (filled = the tab on screen), the accent line.
// The rules are Core's NavStripTable, the table Core's NavSections, so both heads draw one truth.
//
// Pill paint (polish waves 2/3/7/9, sync6-nav-polish-b): the tray, the per-tab tints, the raised
// plates with their bevelled rim, the active ring and glow, the sliding active fill (NavStripRules).
//
// ponytail: not yet here (lane sync6-nav-polish-c): the depth plates and drop bands
// (SectionTabStrip.Depth.cs, PaintDepthPages), the hover lift / choose sparks / idle sheen
// (SectionTabStrip.Fx.cs), the leading MDL2 glyphs (absent on Linux; WPF also drops a glyph its font lacks), the "Moved" note, the "?" help
// badges and Just Drop's ask-first, the right-click pin menu.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    public sealed class SectionTabStrip : UserControl
    {
        private static readonly IBrush FocusRing = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush BadgePlate = new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.85));

        /// <summary>One pill's parts and its paint (WPF PillParts :141).</summary>
        internal sealed class PillParts
        {
            public NavTab Tab = null!;
            public Button Pill = null!;
            public Border Ring = null!, Face = null!;
            public TextBlock Label = null!;
            public Color Tint;
            public IBrush RestText = Brushes.Gainsboro, Outline = Brushes.Transparent, ActiveRing = Brushes.Transparent;
            public IBrush Plate = Brushes.Transparent, Hover = Brushes.Transparent;
            public Thickness RestPadding;
        }

        private readonly TextBlock _crumbSectionText = new() { FontSize = 14, FontWeight = FontWeight.ExtraBold };
        private readonly TextBlock _crumbSep = new() { FontSize = 14, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
        private readonly TextBlock _crumbPage = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _crumbSection;
        private readonly StackPanel _pillRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        private readonly Border _track;
        private readonly Border _accentLine = new() { Height = 2, CornerRadius = new CornerRadius(1), Opacity = 0.85 };
        // The lit tab's solid hue, sliding under the pills (WPF ActiveFill + PlaceFill :1018).
        private readonly Border _activeFill = new()
        {
            CornerRadius = new CornerRadius(19), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Width = 0, IsVisible = false, IsHitTestVisible = false,
        };
        private readonly List<PillParts> _pills = new();
        private string? _section, _tab, _activePill;
        private IBrush _accent = Brushes.Transparent, _activeText = Brushes.Black;
        private Color _hue;

        /// <summary>A pill was chosen (click, Enter/Space, or a Left/Right/Home/End move).</summary>
        public event Action<NavTab>? TabRequested;

        /// <summary>A pill was built (WPF PillCreated): the host attaches its pin menu.</summary>
        public event Action<NavTab, Button>? PillCreated;

        /// <summary>The breadcrumb's section word was clicked.</summary>
        public event Action<string>? SectionRequested;

        /// <summary>Tier lock answer, set by the host (WPF NavStripRules.PillLocked).</summary>
        public Func<int, bool> PillLocked { get; set; } = tier => tier > 0;

        /// <summary>Tabs this head can open; a pill it cannot is not drawn (no dead clicks).</summary>
        public Func<NavTab, bool> CanOpen { get; set; } = _ => true;

        /// <summary>The Settings page word, re-read on a rebuild so a language switch renames it.</summary>
        public Func<string?>? SettingsPageLabel { get; set; }

        internal string? Section => _section;
        internal string? ActivePillKey => _activePill;
        internal IReadOnlyList<string> PillKeys => _pills.Select(p => p.Tab.Key).ToArray();
        internal Button? PillFor(string key) => Part(key)?.Pill;
        internal PillParts? Part(string key) => _pills.FirstOrDefault(p => p.Tab.Key == key);
        internal Border ActiveFill => _activeFill;
        internal Border Track => _track;
        internal MotionLevel? MotionOverride { get; set; }
        private MotionLevel Level => MotionOverride ?? CoreSettings.Current.MotionLevel;
        internal string CrumbText => $"{_crumbSectionText.Text} {_crumbSep.Text} {_crumbPage.Text}".Trim();
        internal bool PillsShown => _track.IsVisible && _track.Opacity > 0;

        public SectionTabStrip()
        {
            IsVisible = false;
            _crumbSection = new Button
            {
                Content = _crumbSectionText, Padding = new Thickness(4, 1), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Template = PlainTemplate(),
            };
            _crumbSection.Click += (_, e) => { e.Handled = true; if (_section != null) SectionRequested?.Invoke(_section); };
            var crumb = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 18, 0), Children = { _crumbSection, _crumbSep, _crumbPage },
            };
            _track = new Border
            {
                CornerRadius = new CornerRadius(21), Padding = new Thickness(4), MinHeight = 49,
                BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = new Panel { Children = { _activeFill, _pillRow } },
            };
            _pillRow.LayoutUpdated += (_, _) => PositionFill(animate: false, onlyIfMoved: true);
            _pillRow.AddHandler(KeyDownEvent, PillRow_KeyDown, RoutingStrategies.Tunnel);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetColumn(_track, 1);
            row.Children.Add(crumb);
            row.Children.Add(_track);
            Content = new StackPanel { Children = { row, _accentLine } };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        // WPF Rebind (P09): code-set labels are rebuilt on a language switch.
        private void OnLanguageChanged(object? sender, EventArgs e) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Rebuild());

        /// <summary>Rebuilds the pills (language switch, a tier change).</summary>
        public void Rebuild()
        {
            var section = _section;
            _section = null;
            Show(section, _tab, section == NavSections.Settings ? SettingsPageLabel?.Invoke() : null);
        }

        /// <summary>Show the header for a tab. <paramref name="pageLabel"/> overrides the
        /// breadcrumb's page word (Settings passes its current section's label). WPF Show.</summary>
        public void Show(string? section, string? tab, string? pageLabel = null)
        {
            _tab = tab;
            bool sectionChanged = !string.Equals(section, _section, StringComparison.OrdinalIgnoreCase);
            if (!NavStripTable.ShowsHeader(section))
            {
                IsVisible = false;
                _section = section;
                return;
            }
            IsVisible = true;

            if (!string.Equals(section, _section, StringComparison.OrdinalIgnoreCase))
            {
                _section = section;
                var hue = Hue(section);
                _hue = hue;
                _accent = new SolidColorBrush(hue);
                _activeText = new SolidColorBrush(NavStripRules.ActiveTextOn(hue));
                _activeFill.Background = _accent;
                // The tray: darker than the page wash, its top edge shaded so the pills sit IN it (WPF PaintFor :276).
                _track.Background = new SolidColorBrush(NavStripRules.TrackFill(hue));
                _track.BorderBrush = NavStripRules.TrackBorderBrush(hue);
                _crumbSectionText.Foreground = _accent;
                _accentLine.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(hue, 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
                };
                FixCrumbWidth(section!);
                BuildPills(section!);
            }
            // Hidden, not collapsed: the track keeps the row's height, so Settings (no pills) has
            // the same header height as every other section.
            _track.Opacity = _pills.Count > 0 ? 1 : 0;
            _track.IsHitTestVisible = _pills.Count > 0;

            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section ?? string.Empty);
            var page = pageLabel ?? SafeLoc(NavStripTable.PageLabelKey(tab), string.Empty);
            _crumbSectionText.Text = sectionLabel;
            _crumbSep.Text = SafeLoc("nav_crumb_sep", "›");
            _crumbSep.IsVisible = !string.IsNullOrEmpty(page);
            _crumbPage.Text = page;
            ToolTip.SetTip(_crumbSection, sectionLabel);

            SetActive(NavStripTable.ActivePill(tab), animate: !sectionChanged);
        }

        internal static Color Hue(string? section)
        {
            var rgb = NavStripTable.AccentRgb(section);
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        /// <summary>WPF FixCrumbWidth: the page word gets one fixed width per section (its longest
        /// page name), so the pills never move sideways when the page changes.</summary>
        private void FixCrumbWidth(string section)
        {
            double widest = 0;
            try
            {
                var typeface = new Typeface(_crumbPage.FontFamily, FontStyle.Normal, FontWeight.SemiBold);
                foreach (var t in NavSections.Find(section)?.Tabs ?? Array.Empty<NavTab>())
                    widest = Math.Max(widest, new FormattedText(SafeLoc(t.LabelKey, t.Key),
                        System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        typeface, _crumbPage.FontSize, Brushes.White).WidthIncludingTrailingWhitespace);
            }
            catch (Exception ex) { Serilog.Log.Debug("FixCrumbWidth({Section}): {E}", section, ex.Message); }
            _crumbPage.Width = widest > 0 ? Math.Ceiling(widest) + 2 : double.NaN;
        }

        private void BuildPills(string section)
        {
            _pillRow.Children.Clear();
            _pills.Clear();
            _activePill = null;
            _activeFill.Width = 0;
            _activeFill.IsVisible = false;
            var tabs = NavStripTable.Pills(section);
            var hue = Hue(section);
            for (int index = 0; index < tabs.Count; index++)
            {
                var tab = tabs[index];
                if (!CanOpen(tab)) continue;
                // WPF :665: the tab's own near-hue paints its outline, hover and the active ring; the
                // active FILL and the label rule stay the section's. The index is WPF's (every pill),
                // so a pill this head hides does not shift its neighbours' colours.
                var tint = NavStripRules.TabTint(section, index, tabs.Count);
                var parts = new PillParts
                {
                    Tab = tab, Tint = tint,
                    RestText = new SolidColorBrush(NavStripRules.RestTextOn(hue, tint)),
                    Outline = NavStripRules.OutlineBrush(tint),
                    ActiveRing = NavStripRules.ActiveRingBrush(tint),
                    Plate = NavStripRules.PlateBrush(tint, NavStripRules.RestFillAlpha),
                    Hover = NavStripRules.PlateBrush(tint, NavStripRules.HoverFillAlpha),
                };
                var labelText = SafeLoc(tab.LabelKey, tab.Key);
                var label = new TextBlock
                {
                    Text = labelText, FontSize = NavStripRules.PillFontSize, FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, Foreground = parts.RestText,
                };
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { label } };
                bool locked = PillLocked(tab.Tier);
                // Locked stays visible: the tier sign sits in the pill, the click still navigates
                // and the page's own gate explains the lock. Static on chrome.
                // A dark plate under the sign, so a neon tier sign reads the same on the solid hue
                // of the active pill as on the dark track (WPF :720).
                if (locked)
                    content.Children.Add(new Border
                    {
                        Width = NavStripRules.BadgePlateWidth, Height = NavStripRules.BadgePlateHeight,
                        Margin = new Thickness(NavStripRules.BadgeGap, 0, -6, 0), CornerRadius = new CornerRadius(7),
                        Background = BadgePlate, VerticalAlignment = VerticalAlignment.Center,
                        Child = new TierBadge
                        {
                            MotionOverride = false, Tier = tab.Tier, MaxWidthOverride = NavStripRules.BadgeMaxWidth,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        },
                    });
                // The face: a raised plate in the tab's tint inside a bevelled rim; the 1 px ring
                // around it is the focus ring, so border + ring never grow the pill (WPF :744).
                double rightPad = NavStripRules.PillPadding + (locked ? NavStripRules.BadgePadExtra : 0);
                var face = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2 - 1),
                    Padding = new Thickness(NavStripRules.PillPadding, 0, rightPad, 0),
                    MinHeight = NavStripRules.PillHeight - 2,
                    Background = parts.Plate, BorderThickness = new Thickness(NavStripRules.RestFaceThickness),
                    BorderBrush = parts.Outline, Child = content,
                };
                var ring = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2), BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent, Child = face,
                };
                // A real named Button (UIA: invokable, AutomationId NavPill_<key>), so the click
                // always reaches it (WPF 56b707252).
                var pill = new Button
                {
                    Name = "NavPill_" + tab.Key, Content = ring, Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand), Focusable = true, Tag = tab.Key,
                    Template = PlainTemplate(),
                };
                ToolTip.SetTip(pill, PillToolTip(section, tab, labelText, locked));
                global::Avalonia.Automation.AutomationProperties.SetName(pill, labelText);
                global::Avalonia.Automation.AutomationProperties.SetAutomationId(pill, "NavPill_" + tab.Key);
                pill.PointerEntered += (_, _) => { if (!IsActive(tab.Key)) face.Background = parts.Hover; };
                pill.PointerExited += (_, _) => { if (!IsActive(tab.Key)) face.Background = parts.Plate; };
                pill.GotFocus += (_, _) => ring.BorderBrush = FocusRing;
                pill.LostFocus += (_, _) => ring.BorderBrush = Brushes.Transparent;
                pill.Click += (_, e) => { e.Handled = true; Choose(tab, focus: false); };
                _pillRow.Children.Add(pill);
                parts.Pill = pill;
                parts.Ring = ring;
                parts.Face = face;
                parts.Label = label;
                parts.RestPadding = face.Padding;
                _pills.Add(parts);
                PillCreated?.Invoke(tab, pill);
            }
        }

        /// <summary>WPF PillToolTip: where it leads, then the tier line for a locked page.</summary>
        internal static string PillToolTip(string section, NavTab tab, string label, bool locked)
        {
            var lines = new List<string>
            {
                $"{SafeLoc(NavSections.Find(section)?.LabelKey, section)} {SafeLoc("nav_crumb_sep", ">")} {label}",
            };
            if (locked && tab.Tier == 1) lines.Add(SafeLoc("nav_tag_premium_tip", string.Empty));
            else if (locked && tab.Tier >= 2) lines.Add(SafeLoc("nav_tag_lab_tip", string.Empty));
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        private bool IsActive(string key) => string.Equals(_activePill, key, StringComparison.OrdinalIgnoreCase);

        private void Choose(NavTab tab, bool focus)
        {
            // Launchers and windows open something else; the page on screen keeps its pill.
            if (tab.Kind is NavTabKind.Tab or NavTabKind.Zone) SetActive(tab.Key, animate: true);
            if (focus) PillFor(tab.Key)?.Focus();
            TabRequested?.Invoke(tab);
        }

        private void SetActive(string? key, bool animate)
        {
            _activePill = key;
            foreach (var p in _pills)
            {
                bool on = IsActive(p.Tab.Key);
                // WPF SetActive :983: active = ink on the solid hue the fill slides under (the plate
                // steps aside) and a near-white gloss ring in the tint; rest = the label on a raised
                // plate inside the bevelled rim. The heavier lit rim takes its half pixel back from
                // the padding, so nothing moves.
                p.Label.Foreground = on ? _activeText : p.RestText;
                p.Face.BorderBrush = on ? p.ActiveRing : p.Outline;
                double grow = on ? NavStripRules.ActiveFaceThickness - NavStripRules.RestFaceThickness : 0;
                p.Face.BorderThickness = new Thickness(on ? NavStripRules.ActiveFaceThickness : NavStripRules.RestFaceThickness);
                var pad = p.RestPadding;
                p.Face.Padding = new Thickness(Math.Max(0, pad.Left - grow), pad.Top, Math.Max(0, pad.Right - grow), pad.Bottom);
                p.Face.Background = on ? Brushes.Transparent : (p.Pill.IsPointerOver ? p.Hover : p.Plate);
                // One Tab stop for the strip (ARIA tabs): the active pill, else the first. WPF :991.
                p.Pill.IsTabStop = on || (key == null && p.Pill == _pills[0].Pill);
                global::Avalonia.Automation.AutomationProperties.SetItemStatus(p.Pill, on ? "selected" : string.Empty);
            }
            PositionFill(animate);
        }

        /// <summary>WPF SlideMs (:123): 180 ms, halved at Reduced, 0 at Off.</summary>
        internal static int SlideMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 90,
            _ => 180,
        };

        private double _fillX = double.NaN, _fillW;

        /// <summary>WPF PositionFill/PlaceFill (:999, :1018): the solid hue slides (cubic ease-out)
        /// under the lit pill and glows softly in the hue (none at Motion Off). Before the first
        /// layout the pill has no bounds; LayoutUpdated places it once they land.</summary>
        private void PositionFill(bool animate, bool onlyIfMoved = false)
        {
            var p = _activePill == null ? null : Part(_activePill);
            if (p == null) { _activeFill.IsVisible = false; _fillX = double.NaN; return; }
            var b = p.Pill.Bounds;
            if (b.Width <= 0) return;
            if (onlyIfMoved && b.X == _fillX && b.Width == _fillW && _activeFill.IsVisible) return;
            var level = Level;
            int ms = animate && _activeFill.IsVisible && _activeFill.Width > 0 ? SlideMs(level) : 0;
            if (ms > 0)
            {
                var d = TimeSpan.FromMilliseconds(ms);
                _activeFill.Transitions = new global::Avalonia.Animation.Transitions
                {
                    new global::Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = d, Easing = new global::Avalonia.Animation.Easings.CubicEaseOut() },
                    new global::Avalonia.Animation.ThicknessTransition { Property = MarginProperty, Duration = d, Easing = new global::Avalonia.Animation.Easings.CubicEaseOut() },
                };
            }
            else _activeFill.Transitions = null;
            _fillX = b.X;
            _fillW = b.Width;
            _activeFill.IsVisible = true;
            _activeFill.Height = b.Height;
            _activeFill.Margin = new Thickness(b.X, 0, 0, 0);
            _activeFill.Width = b.Width;
            _activeFill.BoxShadow = level == MotionLevel.Off
                ? default
                : new BoxShadows(new BoxShadow { Blur = NavStripRules.ActiveGlowBlur, Color = NavStripRules.WithAlpha(_hue, NavStripRules.ActiveGlowOpacity) });
        }

        /// <summary>WPF PillRow_PreviewKeyDown: Enter/Space choose, Left/Right wrap, Home/End jump.
        /// Pages activate as focus moves; launchers and windows only take focus.</summary>
        private void PillRow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_pills.Count == 0) return;
            int current = _pills.FindIndex(p => p.Pill.IsFocused);
            if (current < 0) current = _pills.FindIndex(p => IsActive(p.Tab.Key));
            if (e.Key is Key.Enter or Key.Space)
            {
                if (current >= 0) Choose(_pills[current].Tab, focus: true);
                e.Handled = true;
                return;
            }
            int next = NavStripTable.MoveIndex(current, _pills.Count, e.Key.ToString());
            if (next < 0) return;
            e.Handled = true;
            var target = _pills[next];
            if (target.Tab.Kind is NavTabKind.Tab or NavTabKind.Zone) Choose(target.Tab, focus: true);
            else target.Pill.Focus();
        }

        // A button that draws only its content (the face is the paint).
        private static FuncControlTemplate<Button> PlainTemplate() => new((b, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
            [!ContentPresenter.PaddingProperty] = b[!TemplatedControl.PaddingProperty],
        });

        private static string SafeLoc(string? key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            var s = Loc.Get(key);
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }
    }
}
