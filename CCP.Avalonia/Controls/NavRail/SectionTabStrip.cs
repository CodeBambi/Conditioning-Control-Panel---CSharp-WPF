// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/NavRail/SectionTabStrip.xaml(.cs):
// the section page header. Breadcrumb "Section > Page" (the section word returns to that
// section's last tab), the section's pills (filled = the tab on screen), a one-line "Moved"
// note and the 2 px accent line in the section hue. Code-built (no .axaml): every part is
// painted from Core's NavStripRules anyway, so XAML would only hold empty shells.
//
// Same as WPF: one hue per section, every pill wears its own near-hue tint (TabTint), solid
// active fill that slides under the lit pill with a soft static glow, ExtraBold width reserved
// so lighting a pill never moves the row, a tab bar that never scrolls sideways, keyboard
// Left/Right/Home/End/Enter/Space, a tier sign on a locked pill (PillLocked), "Moved" note.
//
// ponytail (not this wave): the "?" help badges + the Just Drop ask-first card
// (SectionTabStrip.Help.cs), the hover lift/scale/glyph wiggle/sheen/burst (SectionTabStrip.Fx.cs).
// The pill PLATE (raised tint gradient + bevelled outline) and the tray fill are here; the
// depth (sunken tray well, hue drop band under each raised pill, face travel, lit pill pressed
// in its socket) is SectionTabStrip.Depth.cs, as WPF.
//
// Glyphs: Segoe MDL2 Assets, as WPF. A glyph the font lacks draws nothing, never a box, which is
// what Linux gets (no Segoe MDL2 there): the pill shows its label only. Noted in the hand-back.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    public sealed partial class SectionTabStrip : UserControl
    {
        private static readonly IBrush FocusRing = NavPaint.Solid(0xCCFFFFFF);
        private static readonly IBrush BadgePlate = NavPaint.Solid(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.85));

        /// <summary>One pill and its own paint (every pill wears its tab's tint).</summary>
        private sealed class PillParts
        {
            public NavTab Tab = null!;
            public Button Pill = null!;
            public Border Ring = null!;
            public Border Face = null!;
            public TextBlock Label = null!;
            public TextBlock? Glyph;
            public uint Tint;
            public bool Locked;
            public IBrush RestText = Brushes.Gainsboro;
            public IBrush RestGlyph = Brushes.Gainsboro;
            public IBrush ActiveGlyph = Brushes.Black;
            public IBrush Outline = Brushes.Transparent;
            public IBrush ActiveRing = Brushes.Transparent;
            public IBrush Plate = Brushes.Transparent;
            public IBrush Hover = Brushes.Transparent;
            public Thickness RestPadding;
            // Depth (SectionTabStrip.Depth.cs): the drop band under the pill, the face's travel.
            public Border? Drop;
            public TranslateTransform? FaceShift;
            public bool Pressed, Hovered;
            public DispatcherTimer? DepthTimer;
            public long DepthStarted;
            public int DepthMs;
            public double DepthFrom;
            public global::ConditioningControlPanel.Motion.Keyframe[] DepthTrack = Array.Empty<global::ConditioningControlPanel.Motion.Keyframe>();
        }

        private readonly List<PillParts> _pills = new();
        private string? _section;
        private string? _activePill;
        private string? _crumbKey;
        private DispatcherTimer? _noteTimer;
        private IBrush _activeText = Brushes.Black;
        private uint _hue = NavStripRules.Lilac;

        // Parts (WPF x:Names kept as field names).
        private readonly StackPanel CrumbPanel;
        private readonly Button CrumbSection;
        private readonly TextBlock CrumbSectionText;
        private readonly TextBlock CrumbSep;
        private readonly TextBlock CrumbPage;
        private readonly Panel TrayHost;
        private readonly Border PillTrack;
        private readonly Border ActiveFill;
        private readonly TranslateTransform ActiveFillShift = new();
        private readonly StackPanel PillRow;
        private readonly Border MovedNote;
        private readonly TextBlock MovedNoteText;
        private readonly Border AccentLine;

        /// <summary>A pill was chosen (click, Enter/Space, or a Left/Right/Home/End move).</summary>
        public event Action<NavTab>? TabRequested;

        /// <summary>The breadcrumb's section word was clicked.</summary>
        public event Action<string>? SectionRequested;

        /// <summary>A pill was built (the host may attach a menu here).</summary>
        public event Action<NavTab, Control>? PillCreated;

        /// <summary>Test seam: forces a motion level instead of asking the head's settings.</summary>
        internal MotionLevel? MotionOverride { get; set; }

        /// <summary>Test seam / host seam: the two canonical gates (null = no account service yet,
        /// which reads as locked so the sign is never hidden by mistake).</summary>
        internal Func<(bool? Premium, bool? Lab)>? AccessProvider { get; set; }

        private MotionLevel Level => MotionOverride ?? NavPaint.Level;

        public SectionTabStrip()
        {
            Focusable = false;

            CrumbSectionText = new TextBlock { FontSize = 14, FontWeight = FontWeight.ExtraBold, VerticalAlignment = VerticalAlignment.Center };
            CrumbSection = new Button
            {
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = CrumbTemplate(),
                Content = CrumbSectionText,
            };
            CrumbSection.Click += (_, e) => { e.Handled = true; if (_section != null) SectionRequested?.Invoke(_section); };
            CrumbSep = new TextBlock { FontSize = 14, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            CrumbSep.Bind(TextBlock.ForegroundProperty, CrumbSep.GetResourceObservable("TextMutedBrush"));
            CrumbPage = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            CrumbPage.Bind(TextBlock.ForegroundProperty, CrumbPage.GetResourceObservable("TextLightBrush"));
            CrumbPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 18, 0),
                Children = { CrumbSection, CrumbSep, CrumbPage },
            };
            CrumbPanel.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) } };

            ActiveFill = new Border
            {
                CornerRadius = new CornerRadius(19),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 0,
                IsHitTestVisible = false,
                IsVisible = false,
                RenderTransform = ActiveFillShift,
            };
            PillRow = new StackPanel { Orientation = Orientation.Horizontal };
            PillRow.AddHandler(KeyDownEvent, PillRow_KeyDown, RoutingStrategies.Tunnel);
            // The track is a tray (sunken): darker than the page wash, its top edge shaded.
            // 38 px pills + 2 x 4 padding + 2 x 1.5 border = 49.
            PillTrack = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(21),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(4),
                MinHeight = 49,
                Child = new Panel { Children = { ActiveFill, PillRow } },
            };
            TrayHost = new Panel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { TrayFloor, TrayWellTop, TrayWellLeft, TrayWellFoot, PillTrack },
            };
            Grid.SetColumn(TrayHost, 1);
            // The well's floor and inner bands are painted per section in PaintDepthPages
            // (SectionTabStrip.Depth.cs): one lamp above, a sunken tray takes it on top and left.

            MovedNoteText = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold };
            MovedNote = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 4),
                Background = NavPaint.Solid(0xE61B1026),
                BorderThickness = new Thickness(1),
                IsVisible = false,
                Opacity = 0,
                IsHitTestVisible = false,
                Child = MovedNoteText,
            };
            Grid.SetColumn(MovedNote, 2);

            var header = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6),
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children = { CrumbPanel, TrayHost, MovedNote },
            };
            AccentLine = new Border { CornerRadius = new CornerRadius(1), Opacity = 0.85 };
            Grid.SetRow(AccentLine, 1);
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,2"),
                Children = { header, AccentLine },
            };

            CrumbSep.Text = SafeLoc("nav_crumb_sep", "›");
            PillRow.SizeChanged += (_, _) => PositionFill(animate: false);
        }

        // ------------------------------------------------------------------ read-only seams

        internal string? Section => _section;
        internal string? ActivePillKey => _activePill;
        internal IReadOnlyList<string> PillKeys => _pills.Select(p => p.Tab.Key).ToArray();
        internal Button? PillFor(string key) => Part(key)?.Pill;
        private PillParts? Part(string key) => _pills.FirstOrDefault(p => p.Tab.Key == key);
        internal string CrumbText => $"{CrumbSectionText.Text} {CrumbSep.Text} {CrumbPage.Text}".Trim();
        internal bool PillLockedFor(string key) => Part(key)?.Locked == true;
        internal string? PillGlyph(string key) => Part(key)?.Glyph?.Text;
        internal IBrush? PillLabelBrush(string key) => Part(key)?.Label.Foreground;
        internal IBrush? PillFaceBrush(string key) => Part(key)?.Face.Background;
        internal double ActiveFillX => ActiveFillShift.X;
        internal double ActiveFillWidth => ActiveFill.Width;
        internal bool ActiveFillShown => ActiveFill.IsVisible;
        internal IBrush? TrackFill => PillTrack.Background;
        internal bool TrackShown => PillTrack.IsVisible && PillTrack.Opacity > 0;
        internal string? MovedNoteShown => MovedNote.IsVisible ? MovedNoteText.Text : null;
        internal double CrumbPageWidth => CrumbPage.Width;

        // ------------------------------------------------------------------ show

        /// <summary>Show the header for a tab. <paramref name="pageLabel"/> overrides the
        /// breadcrumb's page word (Settings passes its current section's label).</summary>
        public void Show(string? section, string? tab, string? pageLabel = null)
        {
            if (!NavStripRules.ShowsHeader(section))
            {
                IsVisible = false;
                _section = section;
                return;
            }
            IsVisible = true;

            bool sectionChanged = !string.Equals(section, _section, StringComparison.OrdinalIgnoreCase);
            _section = section;
            var hue = NavStripRules.Accent(section);

            if (sectionChanged)
            {
                PaintFor(hue);
                FixCrumbWidth(section!);
                BuildPills(section!);
                AccentLine.Background = NavPaint.Horizontal(new[] { (hue, 0.0), (global::ConditioningControlPanel.Fx.Argb.WithAlpha(hue, (byte)0), 1.0) });
                ActiveFill.Background = NavPaint.Solid(hue);
                CrumbSectionText.Foreground = NavPaint.Solid(hue);
            }
            // Hidden, not Collapsed: the track keeps the row's height, so Settings (no pills) has the
            // same header height as every other section. Avalonia has no Hidden: Opacity 0 + no hits.
            bool any = _pills.Count > 0;
            TrayHost.Opacity = any ? 1 : 0;
            TrayHost.IsHitTestVisible = any;

            // Breadcrumb: two levels only; the section word is a link back to its last tab.
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section ?? string.Empty);
            var page = pageLabel ?? SafeLoc(NavStripRules.PageLabelKey(tab), string.Empty);
            var crumbKey = sectionLabel + "|" + page;
            if (crumbKey != _crumbKey)
            {
                CrumbSectionText.Text = sectionLabel;
                CrumbPage.Text = page;
                CrumbSep.IsVisible = !string.IsNullOrEmpty(page);
                if (_crumbKey != null) Crossfade(CrumbPanel);
                _crumbKey = crumbKey;
            }
            ToolTip.SetTip(CrumbSection, sectionLabel);

            SetActive(NavStripRules.ActivePill(tab), animate: !sectionChanged);
        }

        /// <summary>The one-line "Moved: Social > Lobby" note, about three seconds, non-modal.</summary>
        public void ShowMovedNote(string text)
        {
            MovedNoteText.Text = text;
            var hue = NavStripRules.Accent(_section);
            MovedNote.BorderBrush = NavPaint.Solid(hue);
            MovedNoteText.Foreground = NavPaint.Solid(NavStripRules.WithAlpha(hue, NavStripRules.NoteTextAlpha));
            MovedNote.IsVisible = true;
            var level = Level;
            MovedNote.Transitions = level == MotionLevel.Off ? null : new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150),
                },
            };
            MovedNote.Opacity = 1;

            _noteTimer?.Stop();
            _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _noteTimer.Tick += (_, _) =>
            {
                _noteTimer?.Stop();
                if (Level == MotionLevel.Off) { HideNote(); return; }
                MovedNote.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
                };
                MovedNote.Opacity = 0;
                DispatcherTimer.RunOnce(() => { if (MovedNote.Opacity <= 0) HideNote(); }, TimeSpan.FromMilliseconds(320));
            };
            _noteTimer.Start();
        }

        private void HideNote()
        {
            MovedNote.Transitions = null;
            MovedNote.Opacity = 0;
            MovedNote.IsVisible = false;
        }

        /// <summary>The section's pill paint: tray fill and border, active text, all from the
        /// one hue table (NavStripRules.Accent).</summary>
        private void PaintFor(uint hue)
        {
            _activeText = NavPaint.Solid(NavStripRules.ActiveTextOn(hue));
            _hue = hue;
            PillTrack.Background = NavPaint.Solid(NavStripRules.TrackFill(hue));
            PillTrack.BorderBrush = NavPaint.Vertical(NavStripRules.TrackBorderStops(hue));
            PaintDepthPages(hue);
        }

        // ------------------------------------------------------------------ pills

        private void BuildPills(string section)
        {
            PillRow.Children.Clear();
            _pills.Clear();
            _activePill = null;
            ActiveFill.Width = 0;
            ActiveFill.IsVisible = false;

            var access = SafeAccess();
            var tabs = NavStripRules.Pills(section);
            var hue = NavStripRules.Accent(section);
            for (int index = 0; index < tabs.Count; index++)
            {
                var tab = tabs[index];
                var tint = NavStripRules.TabTint(section, tab.Key, index, tabs.Count);
                var parts = new PillParts
                {
                    Tab = tab,
                    Tint = tint,
                    RestText = NavPaint.Solid(NavStripRules.RestTextOn(hue, tint)),
                    RestGlyph = NavPaint.Solid(NavStripRules.RestGlyphOn(hue, tint)),
                    ActiveGlyph = NavPaint.Solid(NavStripRules.ActiveGlyphOn(hue, tint)),
                    Outline = NavPaint.Vertical(NavStripRules.OutlineStops(tint)),
                    ActiveRing = NavPaint.Vertical(NavStripRules.ActiveRingStops(tint)),
                    Plate = NavPaint.Vertical(NavStripRules.PlateStops(tint, NavStripRules.RestFillAlpha)),
                    Hover = NavPaint.Vertical(NavStripRules.PlateStops(tint, NavStripRules.HoverFillAlpha)),
                };
                var label = new TextBlock
                {
                    Text = SafeLoc(tab.LabelKey, tab.Key),
                    FontSize = NavStripRules.PillFontSize,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Foreground = parts.RestText,
                };
                // The active pill turns ExtraBold in WPF's first cut; reserve that width so lighting
                // a pill never widens it and pushes every pill after it sideways.
                label.MinWidth = BoldWidth(label);
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                TextBlock? glyph = null;
                var glyphText = NavStripRules.Glyph(tab.Key);
                if (glyphText != null && GlyphRenders(glyphText))
                {
                    glyph = new TextBlock
                    {
                        Text = glyphText,
                        FontFamily = GlyphFamily,
                        FontSize = NavStripRules.GlyphSize,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                        Foreground = parts.RestGlyph,
                    };
                    content.Children.Add(glyph);
                }
                content.Children.Add(label);

                bool locked = NavStripRules.PillLocked(tab.Tier, access.Premium, access.Lab);
                parts.Locked = locked;
                if (locked)
                {
                    // Locked stays visible: the tier sign sits in the pill, the click still
                    // navigates and the page's own gate explains the lock. Static on chrome.
                    var badge = new TierBadge
                    {
                        MotionOverride = false,
                        Tier = tab.Tier,
                        MaxWidthOverride = NavStripRules.BadgeMaxWidth,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    content.Children.Add(new Border
                    {
                        Width = NavStripRules.BadgePlateWidth,
                        Height = NavStripRules.BadgePlateHeight,
                        Margin = new Thickness(NavStripRules.BadgeGap, 0, -6, 0),
                        CornerRadius = new CornerRadius(7),
                        Background = BadgePlate,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new Panel
                        {
                            Width = NavStripRules.BadgeMaxWidth,
                            Height = NavStripRules.BadgePlateHeight - 2,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children = { badge },
                        },
                    });
                }

                double rightPad = NavStripRules.PillPadding + (locked ? NavStripRules.BadgePadExtra : 0);
                var face = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2 - 1),
                    Padding = new Thickness(NavStripRules.PillPadding, 0, rightPad, 0),
                    MinHeight = NavStripRules.PillHeight - 2,   // + the 1 px ring = a 38 px pill
                    Background = parts.Plate,
                    BorderThickness = new Thickness(NavStripRules.RestFaceThickness),
                    BorderBrush = parts.Outline,
                    Child = content,
                    // Depth: the face travels (lit = sunk, pressed = down); the ring keeps its place.
                    RenderTransform = parts.FaceShift = new TranslateTransform(),
                };
                // Depth: the raised pill's drop band, under the face, on the sheet (it does not
                // travel). Length and paint per state in DepthSettle; the negative foot margin keeps
                // the pill 38 px tall.
                parts.Drop = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false,
                    CornerRadius = new CornerRadius(4),
                    Height = 0,
                };
                var ring = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent,
                    Child = face,
                };
                var pill = new Button
                {
                    Template = PillTemplate,
                    Content = new Panel { Children = { parts.Drop, ring } },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Focusable = true,
                    Tag = tab.Key,
                    Name = "NavPill_" + tab.Key,
                    Margin = new Thickness(PillRow.Children.Count == 0 ? 0 : NavStripRules.PillGap, 0, 0, 0),
                };
                ToolTip.SetTip(pill, PillToolTip(section, tab, label.Text ?? string.Empty, locked));
                ToolTip.SetShowDelay(pill, 500);
                KeyboardNavigation.SetIsTabStop(pill, false);
                global::Avalonia.Automation.AutomationProperties.SetName(pill, label.Text);
                global::Avalonia.Automation.AutomationProperties.SetHelpText(pill, CrumbFor(section, label.Text ?? string.Empty));
                global::Avalonia.Automation.AutomationProperties.SetAutomationId(pill, "NavPill_" + tab.Key);

                var captured = parts;
                pill.PointerEntered += (_, _) => { if (!IsActive(tab.Key)) face.Background = captured.Hover; DepthHover(captured, true); };
                pill.PointerExited += (_, _) => { face.Background = IsActive(tab.Key) ? Brushes.Transparent : captured.Plate; DepthHover(captured, false); };
                pill.PropertyChanged += (_, e) => { if (e.Property == Button.IsPressedProperty) DepthPress(captured, pill.IsPressed); };
                pill.Click += (_, e) => { e.Handled = true; Choose(tab, focus: false); };
                pill.GotFocus += (_, _) => ring.BorderBrush = FocusRing;
                pill.LostFocus += (_, _) => ring.BorderBrush = Brushes.Transparent;

                PillRow.Children.Add(pill);
                parts.Pill = pill;
                parts.Ring = ring;
                parts.Face = face;
                parts.RestPadding = face.Padding;
                parts.Label = label;
                parts.Glyph = glyph;
                _pills.Add(parts);
                DepthSettle(parts, animate: false);
                PillCreated?.Invoke(tab, pill);
            }

            // One tab stop for the whole strip (ARIA tabs): the active pill, else the first.
            if (_pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
        }

        private (bool? Premium, bool? Lab) SafeAccess()
        {
            try { return AccessProvider?.Invoke() ?? (null, null); }
            catch { return (null, null); }
        }

        /// <summary>Repaint only the tier signs (an account change); rebuilds the section's pills.</summary>
        internal void RefreshLocks()
        {
            var section = _section;
            if (section == null || !NavStripRules.ShowsPills(section)) return;
            var active = _activePill;
            BuildPills(section);
            SetActive(active, animate: false);
        }

        /// <summary>A language switch: forget the built section so the next Show rebuilds every
        /// label, the crumb and the separator.</summary>
        internal void RefreshLabels()
        {
            _section = null;
            _crumbKey = null;
            CrumbSep.Text = SafeLoc("nav_crumb_sep", "›");
        }

        private static readonly FontFamily GlyphFamily = new(NavStripRules.GlyphFont);
        private static readonly Dictionary<string, bool> GlyphCache = new();

        /// <summary>True when the glyph font on this machine has the glyph (else the pill shows
        /// no glyph rather than a box). Cached per glyph. Linux has no Segoe MDL2: false.</summary>
        internal static bool GlyphRenders(string glyph)
        {
            lock (GlyphCache)
            {
                if (GlyphCache.TryGetValue(glyph, out var known)) return known;
                bool ok = false;
                try
                {
                    var fm = FontManager.Current;
                    if (fm.TryGetGlyphTypeface(new Typeface(GlyphFamily), out var gt)
                        && string.Equals(gt.FamilyName, NavStripRules.GlyphFont, StringComparison.OrdinalIgnoreCase))
                        ok = glyph.All(ch => gt.CharacterToGlyphMap.TryGetGlyph(ch, out var index) && index != 0);
                }
                catch { ok = false; }
                GlyphCache[glyph] = ok;
                return ok;
            }
        }

        /// <summary>A pill button's template: a transparent hit backing and the content.</summary>
        private static readonly IControlTemplate PillTemplate = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        });

        private static IControlTemplate CrumbTemplate() => new FuncControlTemplate<Button>((b, _) =>
        {
            var bd = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 1),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    VerticalAlignment = VerticalAlignment.Center,
                    [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
                },
            };
            b.PointerEntered += (_, _) => bd.Background = NavPaint.Solid(0x14FFFFFF);
            b.PointerExited += (_, _) => bd.Background = Brushes.Transparent;
            b.GotFocus += (_, _) => bd.BorderBrush = NavPaint.Solid(0xCCFFFFFF);
            b.LostFocus += (_, _) => bd.BorderBrush = Brushes.Transparent;
            return bd;
        });

        /// <summary>"Play > Games": the section and page a pill leads to.</summary>
        internal static string CrumbFor(string section, string page)
        {
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section);
            return $"{sectionLabel} {SafeLoc("nav_crumb_sep", ">")} {page}";
        }

        /// <summary>The pill tooltip says more than its label: where it leads, an optional help
        /// line, and the tier line for a locked page.</summary>
        internal static string PillToolTip(string section, NavTab tab, string label, bool locked)
        {
            var lines = new List<string> { CrumbFor(section, label) };
            var tip = SafeLoc(NavStripRules.HelpKey(tab), string.Empty);
            if (!string.IsNullOrEmpty(tip)) lines.Add(tip);
            if (locked && tab.Tier == 1) lines.Add(SafeLoc("nav_tag_premium_tip", string.Empty));
            else if (locked && tab.Tier >= 2) lines.Add(SafeLoc("nav_tag_lab_tip", string.Empty));
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        /// <summary>
        /// The page word gets one fixed width per section: the longest page name the section can
        /// show (hidden pages and Settings' sections included), so the crumb never pushes the
        /// pills sideways when the page changes.
        /// </summary>
        private void FixCrumbWidth(string section)
        {
            double widest = 0;
            foreach (var t in NavSections.Find(section)?.Tabs ?? Array.Empty<NavTab>())
                widest = Math.Max(widest, TextWidth(SafeLoc(t.LabelKey, t.Key), CrumbPage.FontFamily, CrumbPage.FontSize, FontWeight.SemiBold));
            CrumbPage.Width = widest > 0 ? Math.Ceiling(widest) + 2 : double.NaN;
        }

        private double BoldWidth(TextBlock label) =>
            Math.Ceiling(TextWidth(label.Text ?? string.Empty, FontFamily, label.FontSize, FontWeight.ExtraBold)) + 1;

        private static double TextWidth(string text, FontFamily family, double size, FontWeight weight)
        {
            try
            {
                var probe = new TextBlock { Text = text, FontFamily = family, FontSize = size, FontWeight = weight };
                probe.Measure(Size.Infinity);
                return probe.DesiredSize.Width;
            }
            catch { return 0; }
        }

        private bool IsActive(string key) => string.Equals(_activePill, key, StringComparison.OrdinalIgnoreCase);

        private void Choose(NavTab tab, bool focus)
        {
            // Launchers and windows open something else; the page on screen keeps its pill.
            if (tab.Kind is NavTabKind.Tab or NavTabKind.Zone) SetActive(tab.Key, animate: true);
            // WPF SectionTabStrip.Fx: the chosen pill rings once in the section hue.
            if (tab.Kind is NavTabKind.Tab or NavTabKind.Zone && _section != null)
                NavGlow.Once(PillFor(tab.Key), NavStripRules.Accent(_section), null, "pill-choose");
            if (focus) PillFor(tab.Key)?.Focus();
            TabRequested?.Invoke(tab);
        }

        /// <summary>Test seam: a pill press, exactly as a click raises it.</summary>
        internal void ChooseForTests(string key)
        {
            var p = Part(key);
            if (p != null) Choose(p.Tab, focus: false);
        }

        private void SetActive(string? key, bool animate)
        {
            _activePill = key;
            foreach (var p in _pills)
            {
                bool on = IsActive(p.Tab.Key);
                p.Label.Foreground = on ? _activeText : p.RestText;
                if (p.Glyph != null) p.Glyph.Foreground = on ? p.ActiveGlyph : p.RestGlyph;
                p.Face.BorderBrush = on ? p.ActiveRing : p.Outline;
                // The lit ring is half a pixel heavier; the padding gives it back so the label and
                // the pill's width stay exactly where they were.
                double grow = on ? NavStripRules.ActiveFaceThickness - NavStripRules.RestFaceThickness : 0;
                p.Face.BorderThickness = new Thickness(on ? NavStripRules.ActiveFaceThickness : NavStripRules.RestFaceThickness);
                var pad = p.RestPadding;
                p.Face.Padding = new Thickness(Math.Max(0, pad.Left - grow), pad.Top, Math.Max(0, pad.Right - grow), pad.Bottom);
                p.Face.Background = on ? Brushes.Transparent : (p.Pill.IsPointerOver ? p.Hover : p.Plate);
                KeyboardNavigation.SetIsTabStop(p.Pill, on);
                DepthSettle(p, animate);
            }
            if (key == null && _pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
            PositionFill(animate);
        }

        private void PositionFill(bool animate)
        {
            var pill = _activePill == null ? null : PillFor(_activePill);
            if (pill == null)
            {
                ActiveFill.IsVisible = false;
                return;
            }
            if (pill.Bounds.Width <= 0)
            {
                // First show: the row has not been laid out yet. Place it once layout lands.
                Dispatcher.UIThread.Post(() =>
                {
                    if (pill.Bounds.Width > 0 && PillFor(_activePill ?? string.Empty) == pill) PlaceFill(pill, false);
                }, DispatcherPriority.Background);
                return;
            }
            PlaceFill(pill, animate);
        }

        private void PlaceFill(Control pill, bool animate)
        {
            var at = pill.TranslatePoint(new Point(0, 0), PillRow);
            if (at is null) return;
            ActiveFill.IsVisible = true;
            ActiveFill.Height = pill.Bounds.Height;
            // The lit tab floats: a soft static glow in the hue (0 offset), none at Motion Off.
            var level = Level;
            ActiveFill.BoxShadow = level == MotionLevel.Off
                ? default
                : new BoxShadows(new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = 0,
                    Blur = NavStripRules.ActiveGlowBlur,
                    Color = NavPaint.C(NavStripRules.WithAlpha(_hue, NavStripRules.ActiveGlowOpacity)),
                });

            int ms = animate && ActiveFill.Width > 0 ? NavStripRules.SlideMs(level) : 0;
            if (ms <= 0)
            {
                ActiveFillShift.Transitions = null;
                ActiveFill.Transitions = null;
                ActiveFillShift.X = at.Value.X;
                ActiveFill.Width = pill.Bounds.Width;
                return;
            }
            var dur = TimeSpan.FromMilliseconds(ms);
            var ease = new CubicEaseOut();
            ActiveFillShift.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = dur, Easing = ease } };
            ActiveFill.Transitions = new Transitions { new DoubleTransition { Property = Layoutable.WidthProperty, Duration = dur, Easing = ease } };
            ActiveFillShift.X = at.Value.X;
            ActiveFill.Width = pill.Bounds.Width;
        }

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
            var key = e.Key switch
            {
                Key.Left => StripKey.Left,
                Key.Right => StripKey.Right,
                Key.Home => StripKey.Home,
                Key.End => StripKey.End,
                _ => StripKey.Other,
            };
            int next = NavStripRules.MoveIndex(current, _pills.Count, key);
            if (next < 0) return;
            e.Handled = true;
            var target = _pills[next];
            // Automatic activation for pages; launchers and windows only take focus, so arrowing
            // past "Mods" never opens a dialog.
            if (target.Tab.Kind is NavTabKind.Tab or NavTabKind.Zone) Choose(target.Tab, focus: true);
            else target.Pill.Focus();
        }

        private void Crossfade(Control element)
        {
            var level = Level;
            if (level == MotionLevel.Off) { element.Opacity = 1; return; }
            if (element.Transitions is { Count: > 0 } t && t[0] is DoubleTransition d)
                d.Duration = TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150);
            // From 0.25 back to 1: drop without a transition, then let the transition bring it up.
            var saved = element.Transitions;
            element.Transitions = null;
            element.Opacity = 0.25;
            element.Transitions = saved;
            element.Opacity = 1;
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
    }
}
