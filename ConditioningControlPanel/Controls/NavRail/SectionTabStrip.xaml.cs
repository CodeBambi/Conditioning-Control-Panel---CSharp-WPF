using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The pure half of the section page header (nav rework 2026-10-06): which pills a section
    /// draws, which pill a tab lights, the section hue, keyboard wrap and the last-tab memory.
    /// No WPF state, so SectionTabStripTests can pin it without a window.
    /// </summary>
    public static class NavStripRules
    {
        /// <summary>Home is the dashboard (no strip); Settings keeps its own left pill column.</summary>
        public static bool ShowsPills(string? section) =>
            section != null && section != NavSections.Home && section != NavSections.Settings;

        /// <summary>The header (breadcrumb row) shows everywhere but Home.</summary>
        public static bool ShowsHeader(string? section) =>
            section != null && section != NavSections.Home;

        /// <summary>The pills a section draws, in table order, hidden tabs skipped.</summary>
        public static IReadOnlyList<NavTab> Pills(string? section)
        {
            if (!ShowsPills(section)) return Array.Empty<NavTab>();
            return NavSections.Find(section)?.Tabs.Where(t => !t.Hidden).ToArray() ?? Array.Empty<NavTab>();
        }

        /// <summary>
        /// The pill a tab key lights. A pill key lights itself; the permanent alias "lab" lights
        /// Games; pages that live inside a Play zone light that zone (Graded Intake and Lockdown
        /// sit in Sessions, Blink Trainer in Eyes). Null when no pill owns the page (Spiral Room).
        /// </summary>
        public static string? ActivePill(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant();
            switch (key)
            {
                case "lab": return "play";
                case "gradedintake":
                case "lockdown": return "playsessions";
                case "blinktrainer": return "playeyes";
            }
            var section = NavSections.SectionForTab(key);
            return Pills(section).Any(p => p.Key == key) ? key : null;
        }

        /// <summary>The label key a breadcrumb shows for a tab (its own row in the table).</summary>
        public static string? PageLabelKey(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant() == "lab" ? "play" : tab.ToLowerInvariant();
            foreach (var t in NavSections.AllTabs)
                if (t.Key == key) return t.LabelKey;
            return null;
        }

        /// <summary>Keyboard move inside the strip: Left/Right wrap, Home/End jump. -1 = not a strip key.</summary>
        public static int MoveIndex(int current, int count, Key key)
        {
            if (count <= 0) return -1;
            return key switch
            {
                Key.Left => current <= 0 ? count - 1 : current - 1,
                Key.Right => current < 0 || current >= count - 1 ? 0 : current + 1,
                Key.Home => 0,
                Key.End => count - 1,
                _ => -1,
            };
        }

        // Section hues. Four families only, static (commerce-neutral) tokens, never gold (T1),
        // cyan (T2), red (Circe / danger) or mint (credit): SectionTabStripTests pins the gaps.
        public static readonly Color Lilac = Color.FromRgb(0xB7, 0x9C, 0xFF);       // Home, Library
        public static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);        // Studio, Companion (SectionHueGeneral)
        public static readonly Color VioletBlue = Color.FromRgb(0x8A, 0x7D, 0xFF);  // Play, Social
        public static readonly Color Coral = Color.FromRgb(0xFF, 0x9A, 0x6B);       // You

        public static Color Accent(string? section) => section switch
        {
            NavSections.Studio or NavSections.Companion => Pink,
            NavSections.Play or NavSections.Social => VioletBlue,
            NavSections.You => Coral,
            _ => Lilac,   // Home, Library, Settings
        };

        /// <summary>Last-tab memory: section -> tab, stored as JSON in AppSettings.NavLastTabBySection.</summary>
        public static Dictionary<string, string> ParseLastTabs(string? json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return map;
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json!);
                if (raw != null)
                    foreach (var (k, v) in raw)
                        if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v)) map[k] = v;
            }
            catch (JsonException) { }
            return map;
        }

        /// <summary>The JSON with one section's last tab set. Unchanged JSON when nothing moved.</summary>
        public static string WithLastTab(string? json, string section, string tab)
        {
            var map = ParseLastTabs(json);
            if (map.TryGetValue(section, out var had) && string.Equals(had, tab, StringComparison.OrdinalIgnoreCase))
                return json ?? string.Empty;
            map[section] = tab;
            return JsonSerializer.Serialize(map.OrderBy(p => p.Key, StringComparer.Ordinal)
                                               .ToDictionary(p => p.Key, p => p.Value));
        }

        /// <summary>The tab a section returns to: its remembered tab when the table still owns it,
        /// otherwise its default.</summary>
        public static string? LastTabFor(string? json, string section)
        {
            var def = NavSections.DefaultTab(section);
            if (ParseLastTabs(json).TryGetValue(section, out var tab)
                && string.Equals(NavSections.SectionForTab(tab), section, StringComparison.OrdinalIgnoreCase)
                && !NavSections.Redirects.ContainsKey(tab))
                return tab;
            return def;
        }

        /// <summary>Slide duration for the active fill: 180 ms, halved at Reduced, 0 at Off.</summary>
        public static int SlideMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 90,
            _ => 180,
        };
    }

    /// <summary>
    /// The section page header: breadcrumb, pill strip, "Moved" note and accent line. One
    /// instance, mounted by MainWindow above the page views and synced from ShowTab.
    /// </summary>
    public partial class SectionTabStrip : UserControl
    {
        private static readonly SolidColorBrush ActiveText = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x10, 0x26)));
        private static readonly SolidColorBrush HoverTint = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
        private static readonly SolidColorBrush FocusRing = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));

        private readonly List<(NavTab Tab, Button Pill, Border Face, TextBlock Label)> _pills = new();
        private string? _section;
        private string? _activePill;
        private string? _crumbKey;
        private DispatcherTimer? _noteTimer;

        /// <summary>A pill was chosen (click, Enter/Space, or a Left/Right/Home/End move).</summary>
        public event Action<NavTab>? TabRequested;

        /// <summary>The breadcrumb's section word was clicked.</summary>
        public event Action<string>? SectionRequested;

        /// <summary>A pill was built (the host attaches its pin menu here).</summary>
        public event Action<NavTab, FrameworkElement>? PillCreated;

        /// <summary>Test seam: forces a motion level instead of asking MotionFx.</summary>
        internal MotionLevel? MotionOverride { get; set; }

        public SectionTabStrip()
        {
            InitializeComponent();
            CrumbSep.Text = SafeLoc("nav_crumb_sep", "›");
            PillRow.PreviewKeyDown += PillRow_PreviewKeyDown;
            SizeChanged += (_, _) => PositionFill(animate: false);
        }

        internal string? Section => _section;
        internal string? ActivePillKey => _activePill;
        internal IReadOnlyList<string> PillKeys => _pills.Select(p => p.Tab.Key).ToArray();
        internal Button? PillFor(string key) => _pills.FirstOrDefault(p => p.Tab.Key == key).Pill;
        internal string CrumbText => $"{CrumbSectionText.Text} {CrumbSep.Text} {CrumbPage.Text}".Trim();

        private MotionLevel Level => MotionOverride ?? MotionFx.Level;

        /// <summary>Show the header for a tab. <paramref name="pageLabel"/> overrides the
        /// breadcrumb's page word (Settings passes its current section's label).</summary>
        public void Show(string? section, string? tab, string? pageLabel = null)
        {
            if (!NavStripRules.ShowsHeader(section))
            {
                Visibility = Visibility.Collapsed;
                _section = section;
                return;
            }
            Visibility = Visibility.Visible;

            bool sectionChanged = !string.Equals(section, _section, StringComparison.OrdinalIgnoreCase);
            _section = section;
            var accent = new SolidColorBrush(NavStripRules.Accent(section));
            accent.Freeze();

            if (sectionChanged)
            {
                FixCrumbWidth(section!);
                BuildPills(section!, accent);
                AccentLine.Background = new LinearGradientBrush(
                    NavStripRules.Accent(section), Color.FromArgb(0, 0, 0, 0), 0);
                ActiveFill.Background = accent;
                CrumbSectionText.Foreground = accent;
            }
            // Hidden, not Collapsed: the track keeps the row's height, so Settings (no pills) has the
            // same header height as every other section.
            PillTrack.Visibility = _pills.Count > 0 ? Visibility.Visible : Visibility.Hidden;

            // Breadcrumb: two levels only; the section word is a link back to its last tab.
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section ?? string.Empty);
            var page = pageLabel ?? SafeLoc(NavStripRules.PageLabelKey(tab), string.Empty);
            var crumbKey = sectionLabel + "|" + page;
            if (crumbKey != _crumbKey)
            {
                CrumbSectionText.Text = sectionLabel;
                CrumbPage.Text = page;
                CrumbSep.Visibility = string.IsNullOrEmpty(page) ? Visibility.Collapsed : Visibility.Visible;
                if (_crumbKey != null) Crossfade(CrumbPanel);
                _crumbKey = crumbKey;
            }
            CrumbSection.ToolTip = sectionLabel;

            SetActive(NavStripRules.ActivePill(tab), animate: !sectionChanged);
        }

        /// <summary>The one-line "Moved: Social › Lobby" note, about three seconds, non-modal.</summary>
        public void ShowMovedNote(string text)
        {
            MovedNoteText.Text = text;
            MovedNote.BorderBrush = new SolidColorBrush(NavStripRules.Accent(_section));
            MovedNote.Visibility = Visibility.Visible;
            var level = Level;
            if (level == MotionLevel.Off) MovedNote.Opacity = 1;
            else MovedNote.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150)));

            _noteTimer?.Stop();
            _noteTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3.2) };
            _noteTimer.Tick += (_, _) =>
            {
                _noteTimer?.Stop();
                if (Level == MotionLevel.Off) { HideNote(); return; }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => HideNote();
                MovedNote.BeginAnimation(OpacityProperty, fade);
            };
            _noteTimer.Start();
        }

        private void HideNote()
        {
            MovedNote.BeginAnimation(OpacityProperty, null);
            MovedNote.Opacity = 0;
            MovedNote.Visibility = Visibility.Collapsed;
        }

        // =====================================================================================
        //  pills
        // =====================================================================================

        private void BuildPills(string section, Brush accent)
        {
            PillRow.Children.Clear();
            _pills.Clear();
            _activePill = null;
            ActiveFill.Width = 0;
            ActiveFill.Visibility = Visibility.Collapsed;

            foreach (var tab in NavStripRules.Pills(section))
            {
                var label = new TextBlock
                {
                    Text = SafeLoc(tab.LabelKey, tab.Key),
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush?)TryFindResource("TextSecondaryBrush") ?? Brushes.Gainsboro,
                };
                // The active pill turns ExtraBold: reserve that width now, or lighting a pill
                // would widen it and push every pill after it sideways.
                label.MinWidth = BoldWidth(label);
                label.TextAlignment = TextAlignment.Center;
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(label);
                if (tab.Tier > 0)
                {
                    // Locked stays visible: the tier sign sits in the pill, the click still
                    // navigates and the page's own gate explains the lock. Static on chrome.
                    var badge = new TierBadge
                    {
                        MotionOverride = false,   // first: Tier would start the hum
                        Tier = tab.Tier,
                        MaxWidthOverride = 38,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    content.Children.Add(new Grid
                    {
                        Width = 38,
                        Height = 18,
                        Margin = new Thickness(6, 0, -4, 0),
                        ClipToBounds = false,
                        Children = { badge },
                    });
                }

                var face = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(14, 6, 14, 6),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Child = content,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                // A real Button (UIA: a named, invokable control) wearing the pill face. The
                // button captures the mouse on press, so the release always reaches it: a press
                // that squished the face away from the pointer, a focus hand-back after a dialog
                // or a window activation no longer swallows the first click. The transparent
                // backing keeps the whole unsquished rectangle hit-testable.
                var pill = new Button
                {
                    Template = PillTemplate,
                    Content = face,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    FocusVisualStyle = null,
                    Tag = tab.Key,
                    ToolTip = PillToolTip(section, tab, label.Text),
                };
                ToolTipService.SetInitialShowDelay(pill, 500);
                KeyboardNavigation.SetIsTabStop(pill, false);
                System.Windows.Automation.AutomationProperties.SetName(pill, label.Text);
                System.Windows.Automation.AutomationProperties.SetHelpText(pill, CrumbFor(section, label.Text));
                System.Windows.Automation.AutomationProperties.SetAutomationId(pill, "NavPill_" + tab.Key);

                pill.MouseEnter += (_, _) => { if (!IsActive(tab.Key)) face.Background = HoverTint; };
                pill.MouseLeave += (_, _) => { face.Background = Brushes.Transparent; if (!pill.IsPressed) MotionFx.PressSquish(face, false); };
                pill.PreviewMouseLeftButtonDown += (_, _) => MotionFx.PressSquish(face, true);
                pill.LostMouseCapture += (_, _) => MotionFx.PressSquish(face, false);
                pill.Click += (_, e) =>
                {
                    e.Handled = true;
                    MotionFx.PressSquish(face, false);
                    Choose(tab, focus: false);
                };
                pill.GotKeyboardFocus += (_, _) => face.BorderBrush = FocusRing;
                pill.LostKeyboardFocus += (_, _) => face.BorderBrush = Brushes.Transparent;

                PillRow.Children.Add(pill);
                _pills.Add((tab, pill, face, label));
                PillCreated?.Invoke(tab, pill);
            }

            // One tab stop for the whole strip (ARIA tabs): the active pill, else the first.
            if (_pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
        }

        /// <summary>Template for a pill button: a transparent hit backing and the face.</summary>
        private static readonly ControlTemplate PillTemplate = BuildPillTemplate();

        private static ControlTemplate BuildPillTemplate()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            grid.AppendChild(presenter);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = grid };
            t.Seal();
            return t;
        }

        /// <summary>"Play > Games": the section and page a pill leads to.</summary>
        internal static string CrumbFor(string section, string page)
        {
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section);
            return $"{sectionLabel} {SafeLoc("nav_crumb_sep", ">")} {page}";
        }

        /// <summary>The pill tooltip says more than its label: where it leads, an optional
        /// "label key + _tip" line, and the tier line for a locked page.</summary>
        internal static string PillToolTip(string section, NavTab tab, string label)
        {
            var lines = new List<string> { CrumbFor(section, label) };
            var tip = SafeLoc(tab.LabelKey + "_tip", string.Empty);
            if (!string.IsNullOrEmpty(tip)) lines.Add(tip);
            if (tab.Tier == 1) lines.Add(SafeLoc("nav_tag_premium_tip", string.Empty));
            else if (tab.Tier >= 2) lines.Add(SafeLoc("nav_tag_lab_tip", string.Empty));
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        /// <summary>
        /// The page word gets one fixed width per section: the longest page name the section can
        /// show (hidden pages and Settings' sections included). The crumb then never pushes the
        /// pills sideways when the page changes, so a second click at the same spot hits the
        /// same pill.
        /// </summary>
        private void FixCrumbWidth(string section)
        {
            double widest = 0;
            try
            {
                var typeface = new Typeface(CrumbPage.FontFamily, CrumbPage.FontStyle, FontWeights.SemiBold, CrumbPage.FontStretch);
                double dpi = 1.0;
                try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                foreach (var t in NavSections.Find(section)?.Tabs ?? Array.Empty<NavTab>())
                {
                    var text = SafeLoc(t.LabelKey, t.Key);
                    var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, typeface, CrumbPage.FontSize, Brushes.White, dpi);
                    widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FixCrumbWidth({Section}): {E}", section, ex.Message); }
            CrumbPage.Width = widest > 0 ? Math.Ceiling(widest) + 2 : double.NaN;
        }

        /// <summary>A pill label's width at the active (ExtraBold) weight.</summary>
        private double BoldWidth(TextBlock label)
        {
            try
            {
                double dpi = 1.0;
                try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                var typeface = new Typeface(label.FontFamily, label.FontStyle, FontWeights.ExtraBold, label.FontStretch);
                var ft = new FormattedText(label.Text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, label.FontSize, Brushes.White, dpi);
                return Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 1;
            }
            catch { return 0; }
        }

        /// <summary>Test seam: the fixed width of the breadcrumb's page word.</summary>
        internal double CrumbPageWidth => CrumbPage.Width;

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
            foreach (var (tab, pill, face, label) in _pills)
            {
                bool on = IsActive(tab.Key);
                label.Foreground = on ? ActiveText
                    : (Brush?)TryFindResource("TextSecondaryBrush") ?? Brushes.Gainsboro;
                label.FontWeight = on ? FontWeights.ExtraBold : FontWeights.SemiBold;
                if (on) face.Background = Brushes.Transparent;
                KeyboardNavigation.SetIsTabStop(pill, on);
                System.Windows.Automation.AutomationProperties.SetItemStatus(pill, on ? "selected" : string.Empty);
            }
            if (key == null && _pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
            PositionFill(animate);
        }

        private void PositionFill(bool animate)
        {
            var pill = _activePill == null ? null : PillFor(_activePill);
            if (pill == null)
            {
                ActiveFill.Visibility = Visibility.Collapsed;
                return;
            }
            if (!pill.IsMeasureValid || pill.ActualWidth <= 0)
            {
                // First show: the row has not been laid out yet. Place it once layout lands.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (pill.ActualWidth > 0 && PillFor(_activePill ?? string.Empty) == pill) PlaceFill(pill, false);
                }));
                return;
            }
            PlaceFill(pill, animate);
        }

        private void PlaceFill(FrameworkElement pill, bool animate)
        {
            double x;
            try { x = pill.TranslatePoint(new Point(0, 0), PillRow).X; }
            catch (InvalidOperationException) { return; }
            ActiveFill.Visibility = Visibility.Visible;
            ActiveFill.Height = pill.ActualHeight;

            int ms = animate && ActiveFill.Width > 0 ? NavStripRules.SlideMs(Level) : 0;
            if (ms <= 0)
            {
                ActiveFillShift.BeginAnimation(TranslateTransform.XProperty, null);
                ActiveFill.BeginAnimation(WidthProperty, null);
                ActiveFillShift.X = x;
                ActiveFill.Width = pill.ActualWidth;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(ms);
            ActiveFillShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(x, dur) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            ActiveFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(pill.ActualWidth, dur) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        }

        private void PillRow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_pills.Count == 0) return;
            int current = _pills.FindIndex(p => p.Pill.IsKeyboardFocused);
            if (current < 0) current = _pills.FindIndex(p => IsActive(p.Tab.Key));

            if (e.Key is Key.Enter or Key.Space)
            {
                if (current >= 0) Choose(_pills[current].Tab, focus: true);
                e.Handled = true;
                return;
            }
            int next = NavStripRules.MoveIndex(current, _pills.Count, e.Key);
            if (next < 0) return;
            e.Handled = true;
            var target = _pills[next];
            // Automatic activation for pages (they are built); launchers and windows only take
            // focus, so arrowing past "Mods" never opens a dialog.
            if (target.Tab.Kind is NavTabKind.Tab or NavTabKind.Zone) Choose(target.Tab, focus: true);
            else target.Pill.Focus();
        }

        // =====================================================================================
        //  breadcrumb
        // =====================================================================================

        private void CrumbSection_Click(object sender, RoutedEventArgs e)
        {
            if (_section != null) SectionRequested?.Invoke(_section);
        }

        private void Crossfade(UIElement element)
        {
            var level = Level;
            if (level == MotionLevel.Off) { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; return; }
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1,
                TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150)));
        }

        private static string SafeLoc(string? key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            try
            {
                var s = Loc.Get(key!);
                return string.IsNullOrEmpty(s) || s == key ? fallback : s;
            }
            catch { return fallback; }
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
