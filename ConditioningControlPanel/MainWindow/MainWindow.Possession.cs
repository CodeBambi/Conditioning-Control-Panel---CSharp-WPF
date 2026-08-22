using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>
        /// Rebuilt on every read on purpose. The director asks once per pick (tens of seconds apart at
        /// the fastest cadence), and the alternative - a cached list kept in sync with tab switches,
        /// mod swaps and the nav rail - is a whole invalidation problem bought for nothing. The TARGET
        /// OBJECTS are cached (see _possessionTargets), so cooldowns and IsLive survive the rebuild;
        /// only the walk is redone.
        /// </summary>
        IReadOnlyList<PossessionTarget> IPossessionHost.Targets
        {
            get
            {
                var found = new List<PossessionTarget>();
                try
                {
                    var seenPerRole = new Dictionary<PossessionRole, int>();
                    CollectPossessionTargets(this, found, seenPerRole, 0);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Possession: target walk failed");
                }
                return found;
            }
        }

        private void CollectPossessionTargets(DependencyObject node, List<PossessionTarget> into,
                                              Dictionary<PossessionRole, int> seenPerRole, int depth)
        {
            // The visual tree of this window is deep (nav rail -> doors -> panels -> tab views ->
            // cards); the guard is a cheap insurance against a templated control that manages to
            // present itself as its own child rather than a real depth limit anyone should hit.
            if (node == null || depth > 64) return;

            // The ghost layer's own contents are props, not victims - never possess a ghost.
            if (ReferenceEquals(node, _possessionGhostLayer) || ReferenceEquals(node, _possessionRubbleFloor))
                return;

            if (node is FrameworkElement fe)
            {
                // A collapsed tab is a whole subtree of invisible controls; stopping here rather than
                // filtering leaf by leaf is what keeps the walk cheap.
                if (!fe.IsVisible) return;

                var role = Services.Possession.Possession.GetRole(fe);
                if (role != PossessionRole.None && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                {
                    seenPerRole.TryGetValue(role, out var index);
                    seenPerRole[role] = index + 1;

                    if (!_possessionTargets.TryGetValue(fe, out var target))
                    {
                        var name = Services.Possession.Possession.GetName(fe);
                        target = new PossessionTarget
                        {
                            Element = fe,
                            Role = role,
                            // x:Name first: it is stable across rebuilds AND across a layout change
                            // that reorders siblings, which the role+index fallback is not. The
                            // fallback only has to be stable enough for one lockdown.
                            Key = !string.IsNullOrEmpty(fe.Name) ? fe.Name : role + "#" + index,
                            DisplayName = !string.IsNullOrWhiteSpace(name) ? name : FallbackDisplayName(role),
                        };
                        _possessionTargets.Add(fe, target);
                    }

                    into.Add(target);
                }
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                CollectPossessionTargets(VisualTreeHelper.GetChild(node, i), into, seenPerRole, depth + 1);
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
                    if (App.Video?.IsPlaying == true) return false;

                    // The content rooms stay clean (owner decision 5): while one of these owns the
                    // screen, the main window is either behind it or is the thing it took over.
                    if (DtrhHostService.IsActive) return false;
                    if (LoomHostService.IsActive) return false;
                    if (ArcademyHostService.IsActive) return false;

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
