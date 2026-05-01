using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Views.Deeper
{
    // Multi-select / rubber-band / clipboard / undo plumbing for the Deeper editor.
    // Keeps the high-touch state and helpers in one file so the existing single-
    // select code paths (SelectRegion/SelectHaptic/SelectEffect/SelectRule) only
    // need a one-line hook to participate.
    public partial class DeeperEditorWindow
    {
        // -- Multi-select state ------------------------------------------------
        // Holds Region | HapticEvent | TimelineItem (effects only — Rules don't
        // have a timeline visual the user can rubber-band, they live in the side
        // list). The single _selectedXxx fields remain the *primary* selection
        // (drives which side-panel editor is shown); _selectionSet drives only
        // the visual highlight + bulk operations (delete/copy/cut).
        internal readonly HashSet<object> _selectionSet = new();

        // -- Rubber-band drag state -------------------------------------------
        private System.Windows.Shapes.Rectangle? _rubberBandRect;
        private Point _rbStartCanvasPt;
        private const double RubberBandThresholdPx = 3.0;

        // -- Clipboard ---------------------------------------------------------
        // Process-wide so the user can copy in one editor window and paste in
        // another (e.g. open two enhancements side-by-side).
        internal static class DeeperClipboard
        {
            public static List<TimelineItem> Items = new();
            public static List<Region> Regions = new();
            public static List<HapticEvent> Haptics = new();
            public static double AnchorSeconds;
            public static bool HasContent =>
                Items.Count > 0 || Regions.Count > 0 || Haptics.Count > 0;
        }

        // -- Undo / Redo (snapshot-based) -------------------------------------
        // Snapshots are JSON of the entire Enhancement; for a typical project
        // (~10-100KB) and cap of 50 entries this is comfortably under any RAM
        // concern and avoids the full bookkeeping of a command pattern.
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private const int UndoCap = 50;
        private bool _suppressUndoSnapshot;
        // Set by drag MouseMove handlers the first tick a mutation actually
        // happens, cleared on MouseUp. Lets click-without-drag stay a no-op
        // for undo while drag-to-mutate produces exactly one entry.
        internal bool _dragSnapshotPushed;

        // -- Multi-select helpers ---------------------------------------------

        /// <summary>
        /// Drive selection in response to a mouse click on a timeline item.
        /// Ctrl held = toggle the item's membership in the selection set
        /// (without changing the primary anchor). Plain click = collapse the
        /// set to just this item.
        /// </summary>
        private void HandleSelectionClick(object item)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl)
            {
                if (!_selectionSet.Add(item)) _selectionSet.Remove(item);
            }
            else
            {
                _selectionSet.Clear();
                _selectionSet.Add(item);
            }
        }

        internal bool IsInSelectionSet(object? item) => item != null && _selectionSet.Contains(item);

        // -- Rubber-band drag --------------------------------------------------

        private void StartRubberBand(Point canvasPt)
        {
            _rbStartCanvasPt = canvasPt;
            _rubberBandRect = null; // lazy-create once we exceed the threshold
        }

        private void UpdateRubberBand(Point canvasPt)
        {
            // Defer the visual creation until the user has actually moved past
            // the threshold; otherwise a short click flashes a tiny rectangle.
            var dx = Math.Abs(canvasPt.X - _rbStartCanvasPt.X);
            var dy = Math.Abs(canvasPt.Y - _rbStartCanvasPt.Y);
            if (_rubberBandRect == null && dx < RubberBandThresholdPx && dy < RubberBandThresholdPx) return;

            if (_rubberBandRect == null)
            {
                _rubberBandRect = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x68, 0xF0, 0xFF)),
                    Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xB0, 0xE0, 0xFF)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 },
                    IsHitTestVisible = false
                };
                Panel.SetZIndex(_rubberBandRect, 50);
                TimelineCanvas.Children.Add(_rubberBandRect);
            }

            var x = Math.Min(_rbStartCanvasPt.X, canvasPt.X);
            var y = Math.Min(_rbStartCanvasPt.Y, canvasPt.Y);
            var w = Math.Abs(canvasPt.X - _rbStartCanvasPt.X);
            var h = Math.Abs(canvasPt.Y - _rbStartCanvasPt.Y);
            Canvas.SetLeft(_rubberBandRect, x);
            Canvas.SetTop(_rubberBandRect, y);
            _rubberBandRect.Width = w;
            _rubberBandRect.Height = h;
        }

        /// <summary>
        /// Returns true if the rubber-band actually drew (i.e. the user dragged
        /// past the threshold). On true, the selection set is replaced with the
        /// items inside; on false, the caller should treat the gesture as a
        /// plain click (existing scrub-and-deselect behavior).
        /// </summary>
        private bool FinishRubberBand(Point canvasPt)
        {
            if (_rubberBandRect == null) return false;

            try
            {
                TimelineCanvas.Children.Remove(_rubberBandRect);
                var canvasW = TimelineCanvas.ActualWidth;
                var canvasH = TimelineCanvas.ActualHeight;
                if (canvasW <= 0 || canvasH <= 0 || _totalSeconds <= 0) return true;

                double xMin = Math.Max(0, Math.Min(_rbStartCanvasPt.X, canvasPt.X));
                double xMax = Math.Min(canvasW, Math.Max(_rbStartCanvasPt.X, canvasPt.X));
                double yMin = Math.Max(0, Math.Min(_rbStartCanvasPt.Y, canvasPt.Y));
                double yMax = Math.Min(canvasH, Math.Max(_rbStartCanvasPt.Y, canvasPt.Y));

                double tMin = (xMin / canvasW) * _totalSeconds;
                double tMax = (xMax / canvasW) * _totalSeconds;

                _selectionSet.Clear();

                // Lane Y mapping mirrors the rebuild methods so the hit math
                // stays in lockstep with what the user sees:
                //   Regions: top lane           y in [0, h/2)
                //   Haptics: bottom-half lane   y in [h/2 + 2, h - 2)
                //   Effect segments/dots:       y in [h - 22, h)
                double regionLaneTop = 0;
                double regionLaneBottom = canvasH / 2.0;
                double hapticLaneTop = canvasH / 2.0 + 2;
                double hapticLaneBottom = canvasH - 4;
                double effectLaneTop = canvasH - 22;
                double effectLaneBottom = canvasH;

                bool RangesOverlap(double a1, double a2, double b1, double b2) => a1 < b2 && a2 > b1;

                if (RangesOverlap(yMin, yMax, regionLaneTop, regionLaneBottom))
                {
                    foreach (var r in _enhancement.Regions)
                    {
                        if (r == null) continue;
                        if (RangesOverlap(tMin, tMax, r.Start, r.End))
                            _selectionSet.Add(r);
                    }
                }
                if (RangesOverlap(yMin, yMax, hapticLaneTop, hapticLaneBottom))
                {
                    foreach (var track in _enhancement.HapticTracks)
                    {
                        if (track?.Events == null) continue;
                        foreach (var ev in track.Events)
                        {
                            if (ev == null) continue;
                            if (RangesOverlap(tMin, tMax, ev.Start, ev.Start + ev.Duration))
                                _selectionSet.Add(ev);
                        }
                    }
                }
                if (RangesOverlap(yMin, yMax, effectLaneTop, effectLaneBottom))
                {
                    foreach (var item in _enhancement.TimelineItems)
                    {
                        if (item == null || item.Kind != TimelineItemKind.Effect) continue;
                        if (item.EffectType == EffectTypes.Haptic) continue; // legacy lane
                        var dur = Math.Max(0, item.Duration);
                        if (RangesOverlap(tMin, tMax, item.Start, item.Start + dur))
                            _selectionSet.Add(item);
                    }
                }

                // Promote the first selected item to be the primary anchor so
                // the side panel shows something useful.
                _selectedRegion = _selectionSet.OfType<Region>().FirstOrDefault();
                _selectedHaptic = _selectionSet.OfType<HapticEvent>().FirstOrDefault();
                if (_selectedHaptic != null)
                {
                    _selectedHapticTrack = _enhancement.HapticTracks.FirstOrDefault(t => t?.Events?.Contains(_selectedHaptic) == true);
                }
                _selectedEffect = _selectionSet.OfType<TimelineItem>().FirstOrDefault();
                _selectedRule = null;
                UpdateSelectedSidePanel();

                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
            }
            finally
            {
                _rubberBandRect = null;
            }
            return true;
        }

        // -- Bulk delete -------------------------------------------------------

        internal void DeleteSelection()
        {
            if (_selectionSet.Count == 0) return;
            PushUndoSnapshot();
            try
            {
                foreach (var sel in _selectionSet.ToList())
                {
                    switch (sel)
                    {
                        case Region r:
                            _enhancement.Regions.Remove(r);
                            // Detach any rule that pointed at this region.
                            foreach (var rule in _enhancement.Rules)
                            {
                                if (rule.RegionConstraint == r.Id) rule.RegionConstraint = null;
                            }
                            // Remove the paired Rule-kind TimelineItem the loader
                            // projected for this region, otherwise BackProject on
                            // save resurrects the deleted region from the orphan.
                            if (!string.IsNullOrEmpty(r.Id))
                            {
                                _enhancement.TimelineItems.RemoveAll(ti =>
                                    ti != null
                                    && ti.Kind == TimelineItemKind.Rule
                                    && ti.Id == r.Id);
                            }
                            break;
                        case HapticEvent ev:
                            foreach (var track in _enhancement.HapticTracks)
                                if (track?.Events != null && track.Events.Remove(ev)) break;
                            break;
                        case TimelineItem ti:
                            _enhancement.TimelineItems.Remove(ti);
                            break;
                    }
                }
                _selectionSet.Clear();
                _selectedRegion = null;
                _selectedHaptic = null;
                _selectedHapticTrack = null;
                _selectedEffect = null;
                _selectedRule = null;
                UpdateSelectedSidePanel();
                MarkDirty();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                RefreshRulesList();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: DeleteSelection error: {Error}", ex.Message);
            }
        }

        // -- Clipboard --------------------------------------------------------

        internal void CopySelection()
        {
            if (_selectionSet.Count == 0) return;
            try
            {
                DeeperClipboard.Items.Clear();
                DeeperClipboard.Regions.Clear();
                DeeperClipboard.Haptics.Clear();

                double anchor = double.MaxValue;

                foreach (var sel in _selectionSet)
                {
                    switch (sel)
                    {
                        case Region r:
                            DeeperClipboard.Regions.Add(DeepClone(r));
                            anchor = Math.Min(anchor, r.Start);
                            break;
                        case HapticEvent ev:
                            DeeperClipboard.Haptics.Add(DeepClone(ev));
                            anchor = Math.Min(anchor, ev.Start);
                            break;
                        case TimelineItem ti:
                            DeeperClipboard.Items.Add(DeepClone(ti));
                            anchor = Math.Min(anchor, ti.Start);
                            break;
                    }
                }

                DeeperClipboard.AnchorSeconds = double.IsInfinity(anchor) ? 0 : anchor;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: CopySelection error: {Error}", ex.Message);
            }
        }

        internal void CutSelection()
        {
            if (_selectionSet.Count == 0) return;
            CopySelection();
            DeleteSelection();
        }

        internal void PasteFromClipboard()
        {
            if (!DeeperClipboard.HasContent) return;
            PushUndoSnapshot();
            try
            {
                // Anchor-relative paste: the earliest item in the original
                // selection lands at the playhead; everything else preserves
                // its offset from that anchor.
                double pasteAt = _currentSeconds;
                double anchor = DeeperClipboard.AnchorSeconds;

                _selectionSet.Clear();

                foreach (var src in DeeperClipboard.Regions)
                {
                    var clone = DeepClone(src);
                    clone.Id = TimelineItem.NewId();
                    var len = Math.Max(0, src.End - src.Start);
                    var newStart = Math.Max(0, (src.Start - anchor) + pasteAt);
                    if (_totalSeconds > 0) newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - len));
                    clone.Start = newStart;
                    clone.End = newStart + len;
                    _enhancement.Regions.Add(clone);
                    _selectionSet.Add(clone);
                }

                foreach (var src in DeeperClipboard.Haptics)
                {
                    var clone = DeepClone(src);
                    var newStart = Math.Max(0, (src.Start - anchor) + pasteAt);
                    if (_totalSeconds > 0)
                        newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - clone.Duration));
                    clone.Start = newStart;
                    var track = _enhancement.HapticTracks.FirstOrDefault();
                    if (track == null)
                    {
                        track = new HapticTrack { Id = "primary" };
                        _enhancement.HapticTracks.Add(track);
                    }
                    track.Events.Add(clone);
                    _selectionSet.Add(clone);
                }

                foreach (var src in DeeperClipboard.Items)
                {
                    var clone = DeepClone(src);
                    clone.Id = TimelineItem.NewId();
                    var dur = Math.Max(0, clone.Duration);
                    var newStart = Math.Max(0, (src.Start - anchor) + pasteAt);
                    if (_totalSeconds > 0)
                        newStart = Math.Min(newStart, Math.Max(0, _totalSeconds - dur));
                    clone.Start = newStart;
                    _enhancement.TimelineItems.Add(clone);
                    _selectionSet.Add(clone);
                }

                // Promote one of the pasted items to primary so the side panel updates.
                _selectedRegion = _selectionSet.OfType<Region>().FirstOrDefault();
                _selectedHaptic = _selectionSet.OfType<HapticEvent>().FirstOrDefault();
                if (_selectedHaptic != null)
                {
                    _selectedHapticTrack = _enhancement.HapticTracks.FirstOrDefault(t => t?.Events?.Contains(_selectedHaptic) == true);
                }
                _selectedEffect = _selectionSet.OfType<TimelineItem>().FirstOrDefault();
                UpdateSelectedSidePanel();
                MarkDirty();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                ScheduleValidation();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: PasteFromClipboard error: {Error}", ex.Message);
            }
        }

        // -- Undo / Redo ------------------------------------------------------

        /// <summary>
        /// Snapshot the current Enhancement onto the undo stack. Call BEFORE the
        /// mutation so undo restores the pre-mutation state. Suppressed during
        /// undo/redo replay so we don't push the just-restored state back on.
        /// </summary>
        /// <summary>
        /// Push a snapshot exactly once per drag gesture. Drag handlers call this
        /// on every MouseMove tick; the second+ call is a cheap no-op.
        /// </summary>
        internal void PushDragSnapshotOnce()
        {
            if (_dragSnapshotPushed) return;
            PushUndoSnapshot();
            _dragSnapshotPushed = true;
        }

        internal void PushUndoSnapshot()
        {
            if (_suppressUndoSnapshot) return;
            try
            {
                var json = JsonConvert.SerializeObject(_enhancement, EnhancementSerializer.JsonReadSettingsForClone());
                _undo.Push(json);
                while (_undo.Count > UndoCap)
                {
                    // Stack lacks Dequeue — copy, drop the oldest (bottom), rebuild.
                    var arr = _undo.ToArray(); // top first
                    _undo.Clear();
                    int keep = Math.Min(arr.Length, UndoCap);
                    for (int i = keep - 1; i >= 0; i--) _undo.Push(arr[i]);
                    break;
                }
                _redo.Clear();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: PushUndoSnapshot error: {Error}", ex.Message);
            }
        }

        internal void Undo()
        {
            if (_undo.Count == 0) return;
            try
            {
                var current = JsonConvert.SerializeObject(_enhancement, EnhancementSerializer.JsonReadSettingsForClone());
                _redo.Push(current);
                var snapshot = _undo.Pop();
                ApplyHistorySnapshot(snapshot);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: Undo error: {Error}", ex.Message);
            }
        }

        internal void Redo()
        {
            if (_redo.Count == 0) return;
            try
            {
                var current = JsonConvert.SerializeObject(_enhancement, EnhancementSerializer.JsonReadSettingsForClone());
                _undo.Push(current);
                var snapshot = _redo.Pop();
                ApplyHistorySnapshot(snapshot);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: Redo error: {Error}", ex.Message);
            }
        }

        // Common Undo/Redo body: capture selection by id, swap in the snapshot,
        // then re-resolve the selection against the new object identities so
        // the side-panel editor stays open on the same item the user was
        // editing instead of collapsing to "no selection".
        private void ApplyHistorySnapshot(string snapshot)
        {
            var sel = CaptureSelectionIds();
            _suppressUndoSnapshot = true;
            try
            {
                var restored = JsonConvert.DeserializeObject<Enhancement>(snapshot, EnhancementSerializer.JsonReadSettingsForClone())
                              ?? new Enhancement();
                _enhancement = restored;
                SelectNothing();
                MarkDirty();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
                RefreshRulesList();
                RestoreSelectionIds(sel);
                ScheduleValidation();
            }
            finally { _suppressUndoSnapshot = false; }
        }

        // Object identity is lost across the JSON round-trip in ApplyHistorySnapshot,
        // so we capture id-based handles here and re-resolve them against the
        // restored Enhancement's TimelineItems / Regions / HapticTracks.
        private struct SelectionIds
        {
            public List<string> Regions;
            public List<string> TimelineItems;
            // (TrackId, Start, Duration) — HapticEvent has no id field but the
            // tuple is unique within an enhancement in practice.
            public List<(string TrackId, double Start, double Duration)> HapticEvents;
            public string? PrimaryRegionId;
            public string? PrimaryTimelineItemId;
        }

        private SelectionIds CaptureSelectionIds()
        {
            var sel = new SelectionIds
            {
                Regions = new List<string>(),
                TimelineItems = new List<string>(),
                HapticEvents = new List<(string, double, double)>(),
                PrimaryRegionId = _selectedRegion?.Id,
                PrimaryTimelineItemId = _selectedEffect?.Id,
            };
            foreach (var item in _selectionSet)
            {
                switch (item)
                {
                    case Region r when !string.IsNullOrEmpty(r.Id):
                        sel.Regions.Add(r.Id);
                        break;
                    case TimelineItem ti when !string.IsNullOrEmpty(ti.Id):
                        sel.TimelineItems.Add(ti.Id);
                        break;
                    case HapticEvent ev:
                        var trackId = _enhancement.HapticTracks
                            .FirstOrDefault(t => t?.Events?.Contains(ev) == true)?.Id ?? "";
                        sel.HapticEvents.Add((trackId, ev.Start, ev.Duration));
                        break;
                }
            }
            return sel;
        }

        private void RestoreSelectionIds(SelectionIds sel)
        {
            try
            {
                if (sel.Regions != null)
                {
                    foreach (var id in sel.Regions)
                    {
                        var r = _enhancement.Regions.FirstOrDefault(x => x?.Id == id);
                        if (r != null) _selectionSet.Add(r);
                    }
                }
                if (sel.TimelineItems != null)
                {
                    foreach (var id in sel.TimelineItems)
                    {
                        var ti = _enhancement.TimelineItems.FirstOrDefault(x => x?.Id == id);
                        if (ti != null) _selectionSet.Add(ti);
                    }
                }
                if (sel.HapticEvents != null)
                {
                    foreach (var (trackId, start, dur) in sel.HapticEvents)
                    {
                        var track = _enhancement.HapticTracks.FirstOrDefault(t => t?.Id == trackId);
                        if (track?.Events == null) continue;
                        var ev = track.Events.FirstOrDefault(e => e != null
                            && Math.Abs(e.Start - start) < 0.0005
                            && Math.Abs(e.Duration - dur) < 0.0005);
                        if (ev != null) _selectionSet.Add(ev);
                    }
                }
                if (!string.IsNullOrEmpty(sel.PrimaryRegionId))
                    _selectedRegion = _enhancement.Regions.FirstOrDefault(r => r?.Id == sel.PrimaryRegionId);
                if (!string.IsNullOrEmpty(sel.PrimaryTimelineItemId))
                    _selectedEffect = _enhancement.TimelineItems.FirstOrDefault(t => t?.Id == sel.PrimaryTimelineItemId);
                UpdateSelectedSidePanel();
                RebuildRegionVisuals();
                RebuildHapticVisuals();
                RebuildEffectVisuals();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: RestoreSelectionIds error: {Error}", ex.Message);
            }
        }

        // -- Select all -------------------------------------------------------

        internal void SelectAllOnTimeline()
        {
            _selectionSet.Clear();
            foreach (var r in _enhancement.Regions) if (r != null) _selectionSet.Add(r);
            foreach (var track in _enhancement.HapticTracks)
            {
                if (track?.Events == null) continue;
                foreach (var ev in track.Events) if (ev != null) _selectionSet.Add(ev);
            }
            foreach (var ti in _enhancement.TimelineItems)
            {
                if (ti == null || ti.Kind != TimelineItemKind.Effect) continue;
                if (ti.EffectType == EffectTypes.Haptic) continue;
                _selectionSet.Add(ti);
            }
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RebuildEffectVisuals();
        }

        // -- Deep clone helper ------------------------------------------------

        private static T DeepClone<T>(T src)
        {
            var json = JsonConvert.SerializeObject(src, EnhancementSerializer.JsonReadSettingsForClone());
            return JsonConvert.DeserializeObject<T>(json, EnhancementSerializer.JsonReadSettingsForClone())!;
        }
    }
}
