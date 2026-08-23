using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel
{
    /// <summary>
    /// MainWindow as a haunted room. Read Services/Possession/POSSESSION.md first.
    ///
    /// <para>This partial is ONLY the stage: the two canvases the ghosts live on, the registry of
    /// controls that opted in via <c>poss:Possession.Role</c>, and the one boolean that says whether
    /// the room can be haunted right now. All the choreography (which effect, when, how loud) is the
    /// director's - a host that also picked effects would have to be re-implemented for every other
    /// room we haunt later (dashboard wall, Settings palette, Lock Card).</para>
    /// </summary>
    public partial class MainWindow : IPossessionHost
    {
        // ==== the stage =================================================================
        // Both canvases hang off RootGrid, which is the ONE element in this window with no rows,
        // no columns and no Viewbox above it - a child added here covers the whole window and needs
        // no Grid.Row bookkeeping. PlayLockdownActivationAnimation already parks its crimson flash
        // the same way, so this is the established seat for a window-wide overlay.
        //
        // WHY that matters for coordinates: the real UI lives INSIDE a Viewbox (DesignCanvas is
        // authored at 1585x901 and scaled to fit), so an element's own ActualWidth is design units,
        // not screen pixels. PointOf below goes through TranslatePoint, which walks the Viewbox's
        // scale for us and hands back true window coordinates. Anything drawn on the ghost layer
        // must therefore take its SIZE from the same transform, never from the victim's
        // ActualWidth/Height directly.

        private Canvas? _possessionGhostLayer;
        private Canvas? _possessionRubbleFloor;
        private bool _possessionHostHooked;

        /// <summary>Live target objects, keyed by the element they describe. The director hangs
        /// per-target cooldowns and the "currently possessed" flag off these, and Targets is rebuilt
        /// on every read - so the objects have to OUTLIVE the list or every refresh would forget that
        /// the Stop button was haunted twelve seconds ago. Weak on the key so a control that gets
        /// unloaded and collected takes its entry with it.</summary>
        private readonly ConditionalWeakTable<FrameworkElement, PossessionTarget> _possessionTargets = new();

        /// <summary>Called from InitializeLockdown (MainWindow.Lab.cs) rather than from a Loaded
        /// handler in the shared MainWindow.xaml.cs: the lockdown wiring is where this layer belongs,
        /// and it keeps the whole host inside the two files Possession owns.</summary>
        private void InitializePossessionHost()
        {
            if (_possessionHostHooked) return;
            _possessionHostHooked = true;

            try
            {
                Loaded += (_, _) =>
                {
                    try
                    {
                        EnsurePossessionLayers();
                        Services.Possession.PossessionPointer.Attach(this);
                        App.Possession?.AttachHost(this);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Possession: failed to attach the main window host");
                    }
                };

                Closed += (_, _) =>
                {
                    try { App.Possession?.DetachHost(this); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "Possession: failed to detach the main window host"); }
                };

                // The window may already be up when lockdown wiring runs (the tray path re-shows it).
                if (IsLoaded)
                {
                    EnsurePossessionLayers();
                    Services.Possession.PossessionPointer.Attach(this);
                    App.Possession?.AttachHost(this);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: host initialization failed");
            }
        }

        /// <summary>Idempotent: safe to call from Loaded (which fires again after a re-parent), from
        /// the GhostLayer getter, and from a director that attaches late.</summary>
        private void EnsurePossessionLayers()
        {
            if (RootGrid == null) return;

            if (_possessionGhostLayer == null || !RootGrid.Children.Contains(_possessionGhostLayer))
            {
                _possessionGhostLayer = new Canvas
                {
                    // Never eats a click: everything the ghosts draw is theatre over a UI that must
                    // stay usable underneath (POSSESSION.md - the X may dodge, it must stay clickable
                    // where it lands).
                    IsHitTestVisible = false,
                    // False so a letter falling out of a title can travel past the layer's own bounds
                    // on its way to the rubble floor.
                    ClipToBounds = false,
                    Background = null,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Panel.SetZIndex(_possessionGhostLayer, 9000);
                RootGrid.Children.Add(_possessionGhostLayer);
            }

            if (_possessionRubbleFloor == null || !RootGrid.Children.Contains(_possessionRubbleFloor))
            {
                _possessionRubbleFloor = new Canvas
                {
                    Height = 28,
                    IsHitTestVisible = false,
                    ClipToBounds = false,
                    Background = null,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                // One under the ghost layer: rubble is where pieces COME TO REST, so a live ghost
                // still passing overhead has to draw on top of it.
                Panel.SetZIndex(_possessionRubbleFloor, 8999);
                RootGrid.Children.Add(_possessionRubbleFloor);
            }

            HookPossessionInvalidation();
        }

        /// <summary>
        /// The auto-tag cache goes stale whenever the room changes shape: a tab switch, a card that
        /// expands, a list that fills in. LayoutUpdated is the one signal that catches all of them
        /// without this file having to know about the nav rail, the tab keys or any view.
        ///
        /// <para>It also fires on every animation frame, which is why it only ever sets a FLAG - the
        /// rebuild itself is rate-limited in GetPossessionTargets (PossessionRebuildFloor). Marking
        /// dirty is a field write; doing the walk here would put a visual-tree crawl on the compositor
        /// path of a window that is, at that exact moment, animating a ghost.</para>
        /// </summary>
        private void HookPossessionInvalidation()
        {
            if (_possessionLayoutHooked) return;
            _possessionLayoutHooked = true;
            try
            {
                LayoutUpdated += (_, _) =>
                {
                    try
                    {
                        var now = DateTime.Now;
                        if (now - _possessionLayoutSeenAt < TimeSpan.FromMilliseconds(250)) return;
                        _possessionLayoutSeenAt = now;
                        _possessionCacheDirty = true;
                    }
                    catch { }
                };
            }
            catch (Exception ex) { App.Logger?.Debug(ex, "Possession: layout invalidation hook failed"); }
        }

        // ==== IPossessionHost ===========================================================

        Window IPossessionHost.Window => this;

        Canvas IPossessionHost.GhostLayer
        {
            get
            {
                EnsurePossessionLayers();
                return _possessionGhostLayer ??= new Canvas { IsHitTestVisible = false };
            }
        }

        Canvas IPossessionHost.RubbleFloor
        {
            get
            {
                EnsurePossessionLayers();
                return _possessionRubbleFloor ??= new Canvas { IsHitTestVisible = false };
            }
        }

        // ==== A1 auto-tag (wave 2) ======================================================
        // The first live lockdown had ELEVEN possessable controls in the whole window, because every
        // other hand tag sat on the Lockdown card - which is hidden while a lockdown runs. Hand-tagging
        // the rest was never going to scale: the window is thousands of elements across a dozen tabs,
        // and a curated list rots the moment anyone edits a view. So the host now INFERS a role from
        // the control type and keeps the hand tags as overrides (an author who says "this is a card"
        // always wins). The blocklist that curation used to give us for free is now explicit:
        // Possession.Exclude, the never-touch names below, and the leaf rule.
        //
        // THE LEAF RULE, which is what keeps this cheap and sane: once an element resolves to an
        // interactive role we do not descend into it. WPF's visual tree walks straight through control
        // TEMPLATES, so a ScrollBar would otherwise contribute two RepeatButtons and a Thumb, a ComboBox
        // its toggle button, and every button its caption TextBlock. Stopping at the control means the
        // deck sees "a button", which is what the user sees too.

        private List<PossessionTarget>? _possessionTargetCache;
        private DateTime _possessionCacheAt = DateTime.MinValue;
        private bool _possessionCacheDirty = true;
        private DateTime _possessionLayoutSeenAt = DateTime.MinValue;
        private bool _possessionLayoutHooked;

        /// <summary>A rebuild is at most this often even while the layout is churning (a live haunt
        /// animates transforms, which raises LayoutUpdated on every frame).</summary>
        private static readonly TimeSpan PossessionRebuildFloor = TimeSpan.FromMilliseconds(750);

        /// <summary>And at least this often, so a tab switch that somehow raised no layout pass still
        /// gets picked up before the next haunt.</summary>
        private static readonly TimeSpan PossessionCacheMaxAge = TimeSpan.FromSeconds(10);

        /// <summary>Never a victim, subtree and all. The timer VALUE and the secret exit box are
        /// POSSESSION.md hard rules; the Emergency Exit button is the friction door (EMERGENCY_EXIT.md)
        /// and has to stay exactly where the user last saw it; the gate is the premium wall; the rung
        /// readout and pips are the feature's own instrumentation - haunting the dial that tells you how
        /// haunted you are is a joke that reads as a bug.</summary>
        private static readonly HashSet<string> PossessionNeverNames = new(StringComparer.Ordinal)
        {
            "TxtLockdownTimer", "TxtLockdownExit", "BtnEmergencyExit", "EERoot",
            "LockdownGate", "TxtPossessionRung", "PossessionPips",
        };

        /// <summary>Labels are the one role that can run to the hundreds on a dense tab, and a deck full
        /// of them buries the buttons and cards that carry the joke. Keep the ones nearest the cursor.</summary>
        private const int MaxAutoLabels = 24;

        private const double MinTargetPx = 8;

        /// <summary>
        /// Rebuilt lazily rather than on every read: the walk is a few hundred elements with a transform
        /// per candidate, which is cheap (measured at Debug, typically well under 5 ms) but not free, and
        /// the reactive ghosts (B15) read this on mouse events rather than once a minute. The TARGET
        /// OBJECTS are cached in _possessionTargets regardless, so cooldowns and IsLive survive every
        /// rebuild; only the walk is redone.
        /// </summary>
        IReadOnlyList<PossessionTarget> IPossessionHost.Targets => GetPossessionTargets();

        internal IReadOnlyList<PossessionTarget> GetPossessionTargets()
        {
            try
            {
                var now = DateTime.Now;
                var age = now - _possessionCacheAt;
                var stale = _possessionTargetCache == null
                            || age > PossessionCacheMaxAge
                            || (_possessionCacheDirty && age > PossessionRebuildFloor);
                if (!stale) return _possessionTargetCache!;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var found = new List<PossessionTarget>();
                var labels = new List<PossessionTarget>();

                var root = Content as DependencyObject ?? RootGrid;
                if (root != null) WalkPossession(root, found, labels, 0, 17);

                if (labels.Count > MaxAutoLabels) TrimLabels(labels);
                found.AddRange(labels);

                _possessionTargetCache = found;
                _possessionCacheAt = now;
                _possessionCacheDirty = false;
                sw.Stop();
                App.Logger?.Debug("Possession: auto-tag walk found {Count} targets in {Ms:F1} ms",
                    found.Count, sw.Elapsed.TotalMilliseconds);
                return found;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: target walk failed");
                return _possessionTargetCache ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>();
            }
        }

        /// <summary>Keep the labels nearest the cursor (the haunt should happen where the user is
        /// looking); with no cursor reading yet, keep the biggest, which are the headings.</summary>
        private void TrimLabels(List<PossessionTarget> labels)
        {
            try
            {
                var origin = Services.Possession.PossessionPointer.Position;
                bool haveCursor = origin.X > 0 || origin.Y > 0;

                double Score(PossessionTarget t)
                {
                    try
                    {
                        if (!TryWindowBounds(t.Element, out var r)) return double.MaxValue;
                        if (!haveCursor) return -(r.Width * r.Height);         // biggest first
                        var dx = r.X + r.Width / 2 - origin.X;
                        var dy = r.Y + r.Height / 2 - origin.Y;
                        return dx * dx + dy * dy;                              // nearest first
                    }
                    catch { return double.MaxValue; }
                }

                labels.Sort((a, b) => Score(a).CompareTo(Score(b)));
            }
            catch { }

            if (labels.Count > MaxAutoLabels) labels.RemoveRange(MaxAutoLabels, labels.Count - MaxAutoLabels);
        }

        /// <summary>What a subtree contributed, so a Border can decide whether it is a CARD: a card holds
        /// something you can use, and when cards nest we keep the innermost one (the outer one is a
        /// column, and sagging a whole column reads as a layout bug rather than a haunt).</summary>
        private readonly record struct PossessionSubtree(bool Interactive, bool Card);

        private PossessionSubtree WalkPossession(DependencyObject node, List<PossessionTarget> into,
                                                 List<PossessionTarget> labels, int depth, int pathHash)
        {
            if (node == null || depth > 64) return default;
            if (ReferenceEquals(node, _possessionGhostLayer) || ReferenceEquals(node, _possessionRubbleFloor))
                return default;

            FrameworkElement? fe = node as FrameworkElement;
            if (fe != null)
            {
                // A collapsed tab is a whole subtree of invisible controls; stopping here rather than
                // filtering leaf by leaf is what keeps the walk cheap.
                if (!fe.IsVisible) return default;
                if (Services.Possession.Possession.GetExclude(fe)) return default;
                if (!string.IsNullOrEmpty(fe.Name) && PossessionNeverNames.Contains(fe.Name)) return default;
            }

            // Hand tags win, always: an author who wrote poss:Possession.Role said something the type
            // system cannot.
            var handRole = fe != null ? Services.Possession.Possession.GetRole(fe) : PossessionRole.None;
            var role = handRole;
            if (fe != null && role == PossessionRole.None) role = InferLeafRole(fe);

            var leaf = role != PossessionRole.None && role != PossessionRole.Card && role != PossessionRole.Scroll;
            var interactive = IsInteractiveRole(role);

            PossessionSubtree below = default;
            if (!leaf)
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    var h = unchecked(pathHash * 31 + i * 7 + (child?.GetType().Name.Length ?? 0));
                    var r = WalkPossession(child, into, labels, depth + 1, h);
                    if (r.Interactive) below = below with { Interactive = true };
                    if (r.Card) below = below with { Card = true };
                }
            }

            if (fe == null) return below;

            // The card heuristic, applied bottom-up so "innermost wins" is decidable.
            if (role == PossessionRole.None && below.Interactive && !below.Card && IsCardBorder(fe))
                role = PossessionRole.Card;

            if (role != PossessionRole.None && (handRole != PossessionRole.None || PassesSizeAndPlacement(fe)))
            {
                var t = TargetFor(fe, role, pathHash);
                if (t != null)
                {
                    if (role == PossessionRole.Label && handRole == PossessionRole.None) labels.Add(t);
                    else into.Add(t);
                    if (role == PossessionRole.Card) below = below with { Card = true };
                }
            }

            return new PossessionSubtree(interactive || below.Interactive, below.Card);
        }

        private PossessionTarget? TargetFor(FrameworkElement fe, PossessionRole role, int pathHash)
        {
            try
            {
                if (_possessionTargets.TryGetValue(fe, out var existing)) return existing;
                var target = new PossessionTarget
                {
                    Element = fe,
                    Role = role,
                    // x:Name first: stable across rebuilds AND across a layout change that reorders
                    // siblings. The path hash is the fallback and only has to hold for one lockdown.
                    Key = !string.IsNullOrEmpty(fe.Name) ? fe.Name : role + "#" + pathHash.ToString("x8"),
                    DisplayName = DisplayNameFor(fe, role),
                };
                _possessionTargets.Add(fe, target);
                return target;
            }
            catch { return null; }
        }

        /// <summary>Role from the control TYPE. Returns None for anything we do not want in the deck;
        /// Card is decided by the caller because it needs the subtree.</summary>
        private static PossessionRole InferLeafRole(FrameworkElement fe)
        {
            // ToggleButton first: CheckBox, RadioButton and every switch-styled toggle in this app are
            // ButtonBase too, and "a toggle crumbles to ash when clicked" is a different joke from
            // "the button moved".
            if (fe is ToggleButton) return PossessionRole.Toggle;
            if (fe is ButtonBase)
            {
                // A RepeatButton that is part of a ScrollBar/Slider template is chrome, not a control;
                // the leaf rule normally hides it, but a bare ScrollBar can still expose one.
                if (fe is RepeatButton && IsInsideScrollBar(fe)) return PossessionRole.None;
                return PossessionRole.Button;
            }
            if (fe is Slider) return PossessionRole.Slider;
            if (fe is ComboBox) return PossessionRole.Combo;
            if (fe is TextBox or PasswordBox) return PossessionRole.TextBox;
            if (fe is ProgressBar) return PossessionRole.Progress;
            if (fe is ScrollViewer sv)
            {
                double scrollable;
                try { scrollable = sv.ScrollableHeight; } catch { scrollable = 0; }
                return scrollable > 0 ? PossessionRole.Scroll : PossessionRole.None;
            }
            if (fe is Image img)
                return (img.ActualWidth >= 48 && img.ActualHeight >= 48) ? PossessionRole.Image : PossessionRole.None;
            if (fe is TextBlock tb)
            {
                double size;
                try { size = tb.FontSize; } catch { size = 0; }
                if (size >= 18) return PossessionRole.Title;
                var text = tb.Text;
                if (string.IsNullOrWhiteSpace(text)) return PossessionRole.None;
                var trimmed = text.Trim();
                return trimmed.Length is >= 3 and <= 40 ? PossessionRole.Label : PossessionRole.None;
            }
            return PossessionRole.None;
        }

        private static bool IsInsideScrollBar(DependencyObject? node)
        {
            try
            {
                for (int i = 0; node != null && i < 6; i++)
                {
                    if (node is ScrollBar) return true;
                    node = VisualTreeHelper.GetParent(node);
                }
            }
            catch { }
            return false;
        }

        private static bool IsInteractiveRole(PossessionRole role) => role is PossessionRole.Button
            or PossessionRole.Toggle or PossessionRole.Slider or PossessionRole.Combo or PossessionRole.TextBox;

        /// <summary>The documented card heuristic: a rounded Border that PAINTS something, is big enough
        /// to read as a panel, and holds at least one control. Every card in this app is drawn exactly
        /// that way (the CardBorder style family in MainWindow.xaml), and the "no qualifying card inside
        /// me" test in the caller keeps us on the innermost one.</summary>
        private static bool IsCardBorder(FrameworkElement fe)
        {
            try
            {
                if (fe is not Border b) return false;
                if (b.Background == null) return false;
                var cr = b.CornerRadius;
                if (cr.TopLeft <= 0 && cr.TopRight <= 0 && cr.BottomLeft <= 0 && cr.BottomRight <= 0) return false;
                return b.ActualHeight >= 60 && b.ActualWidth >= 60;
            }
            catch { return false; }
        }

        /// <summary>Big enough to see move, enabled, hit-testable, and actually inside the window. Sizes
        /// are taken in WINDOW pixels, not the element's own ActualWidth: the UI lives in a Viewbox over
        /// a 1585x901 design canvas, so design units and screen pixels are different currencies.</summary>
        private bool PassesSizeAndPlacement(FrameworkElement fe)
        {
            try
            {
                if (!fe.IsEnabled || !fe.IsHitTestVisible) return false;
                if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return false;
                if (!TryWindowBounds(fe, out var r)) return false;
                if (r.Width < MinTargetPx || r.Height < MinTargetPx) return false;

                var w = ActualWidth;
                var h = ActualHeight;
                if (w <= 0 || h <= 0) return false;
                // Fully off-screen (a virtualised row scrolled out, a panel parked outside the clip).
                return r.Right > 0 && r.Bottom > 0 && r.Left < w && r.Top < h;
            }
            catch { return false; }
        }

        /// <summary>The element's rectangle in WINDOW coordinates (Viewbox scale included).</summary>
        internal bool TryWindowBounds(FrameworkElement fe, out Rect bounds)
        {
            bounds = default;
            try
            {
                var t = fe.TransformToVisual(this);
                bounds = t.TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                return !bounds.IsEmpty && !double.IsNaN(bounds.X) && !double.IsNaN(bounds.Y);
            }
            catch { return false; }
        }

        /// <summary>Possession.Name, then a string Content, then a ToolTip, then AutomationProperties.Name,
        /// then the role's own noun. Written the way the bark lines need it ("oops, the Start button
        /// moved") - see the note on Possession.NameProperty for why this is not a loc key.</summary>
        private static string DisplayNameFor(FrameworkElement fe, PossessionRole role)
        {
            try
            {
                var hand = Services.Possession.Possession.GetName(fe);
                if (!string.IsNullOrWhiteSpace(hand)) return hand;

                var text = Tidy(fe is ContentControl cc ? cc.Content as string : null)
                           ?? Tidy(fe is TextBlock tb ? tb.Text : null)
                           ?? Tidy(fe.ToolTip as string)
                           ?? Tidy(AutomationProperties.GetName(fe));

                if (string.IsNullOrEmpty(text)) return FallbackDisplayName(role);

                return role switch
                {
                    PossessionRole.Button => "the " + text + " button",
                    PossessionRole.Toggle => "the " + text + " toggle",
                    PossessionRole.TabHeader => "the " + text + " tab",
                    PossessionRole.Slider => "the " + text + " slider",
                    PossessionRole.Combo => "the " + text + " dropdown",
                    PossessionRole.Card => "the " + text + " card",
                    PossessionRole.Title => "the " + text + " heading",
                    PossessionRole.Label => "the " + text + " label",
                    _ => "the " + text,
                };
            }
            catch { return FallbackDisplayName(role); }
        }

        /// <summary>One line, no runs of whitespace, short enough to drop into a spoken sentence.</summary>
        private static string? Tidy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ", StringComparison.Ordinal);
            if (s.Length is < 2 or > 32) return null;
            return s;
        }

        /// <summary>Last resort so the warden never says "oops, the moved". Untranslated by design -
        /// see the note on Possession.NameProperty.</summary>
        private static string FallbackDisplayName(PossessionRole role) => role switch
        {
            PossessionRole.Button => "that button",
            PossessionRole.Card => "that card",
            PossessionRole.Toggle => "that toggle",
            PossessionRole.Title => "the title",
            PossessionRole.Label => "that label",
            PossessionRole.TabHeader => "that tab",
            PossessionRole.Timer => "the timer",
            PossessionRole.Slider => "that slider",
            PossessionRole.Combo => "that dropdown",
            PossessionRole.Image => "that picture",
            PossessionRole.Scroll => "that list",
            PossessionRole.Progress => "that bar",
            PossessionRole.TextBox => "that box",
            _ => "something",
        };

        Point IPossessionHost.PointOf(FrameworkElement element)
        {
            if (element == null) return new Point(0, 0);

            var layer = _possessionGhostLayer;
            if (layer == null)
            {
                EnsurePossessionLayers();
                layer = _possessionGhostLayer;
                if (layer == null) return new Point(0, 0);
            }

            // The ghost layer is a SIBLING of the Viewbox, not an ancestor of the victim, so
            // TransformToAncestor throws for anything inside the design canvas. Tried first anyway
            // because a future host may well parent its layer above the content, and TranslatePoint
            // (which handles the sibling case, Viewbox scale and all) is the fallback that actually
            // runs today.
            try { return element.TransformToVisual(layer).Transform(new Point(0, 0)); }
            catch { }

            try { return element.TranslatePoint(new Point(0, 0), layer); }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "Possession: PointOf failed for {Name}", element.Name);
                return new Point(0, 0);
            }
        }

        /// <summary>
        /// "Can the room be haunted right this second." Deliberately cheap and deliberately
        /// conservative: a false here only costs one skipped pick, while a true during a fullscreen
        /// takeover means an ember ripple painted on a window nobody can see, and a bark naming a
        /// button the user is not looking at. Live effects keep running either way - this gates
        /// STARTING, not finishing (POSSESSION.md).
        /// </summary>
        bool IPossessionHost.IsUsable
        {
            get
            {
                try
                {
                    if (!IsLoaded || WindowState == WindowState.Minimized || !IsVisible) return false;

                    // A3 (wave 2): a playing video is NOT a reason to stop. The old rule paused the
                    // haunt for the whole of any video, which in a session-heavy lockdown meant most of
                    // it - the owner's first live run felt empty largely because of this line. The room
                    // is haunted whether or not something is playing in it; what still stops us is a
                    // window that has TAKEN OVER the screen, because an ember ripple painted on a
                    // window nobody can see is a bark about a button nobody is looking at.
                    if (DtrhHostService.IsActive) return false;
                    if (LoomHostService.IsActive) return false;
                    if (ArcademyHostService.IsActive) return false;
                    // The Lock Card owns the user's attention (and their input) while it is up.
                    if (LockCardWindow.IsAnyOpen()) return false;

                    return true;
                }
                catch
                {
                    // A throwing usability check must read as "not now", never as "go ahead".
                    return false;
                }
            }
        }
    }
}
