using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Session editor window with timeline and drag-drop features.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SessionEditorWindow.xaml.cs. Deviations:
    ///  - <c>TimelineSession</c> / <c>Session</c> / <c>SessionFileService</c> are all WPF-head
    ///    types (804, 200-odd and 300-odd lines), so the editor drives <see cref="EditorSession"/>,
    ///    a nested stand-in carrying only the members this view calls, logic copied verbatim.
    ///    Delete it and switch the field's type when TimelineSession moves to Core.
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>, as Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>MessageBox.Show</c> has no Avalonia equivalent and no package may be added, so the
    ///    four message boxes log instead; the key and argument order are preserved so wiring a
    ///    real dialog later is a one-line swap at each site.
    ///  - Import/Export need <c>OpenFileDialog</c>/<c>SaveFileDialog</c> plus SessionFileService;
    ///    both are stubs. Save/Cancel are fully ported.
    ///  - <c>BtnHelp</c>'s <c>HelpContentService</c> lookup and <c>HelpVideoWindow</c> hand-off is
    ///    a stub; the button opens the coach-mark overlay, which was WPF's own fallback.
    ///  - Mouse -> pointer: <c>DragMove</c> -> <c>BeginMoveDrag</c>, <c>CaptureMouse</c> ->
    ///    <c>e.Pointer.Capture</c>, <c>MouseRightButtonDown</c> -> <c>PointerPressed</c> filtered
    ///    on <c>IsRightButtonPressed</c>, <c>Line.X1/Y1/X2/Y2</c> -> <c>StartPoint/EndPoint</c>,
    ///    <c>ActualWidth</c> -> <c>Bounds.Width</c>, <c>ToolTip =</c> -> <c>ToolTip.SetTip</c>.
    ///  - The <c>pack://</c> branch of the feature-icon loader is dropped (WPF-only URI scheme);
    ///    a real file under the app directory still wins, otherwise the emoji renders - which
    ///    Avalonia does natively in colour, see the porting notes in CLAUDE.md.
    ///  - NEW: the canvases re-render on width change. WPF computed positions from
    ///    <c>ActualWidth</c> in the constructor, when it is still 0, so both canvases were laid
    ///    out against the hardcoded 800px fallback and never corrected.
    /// </summary>
    public partial class SessionEditorWindow : Window
    {
        private readonly EditorSession _session;

        // Timeline icon drag state
        private bool _isTimelineDragging;
        private Border? _draggedTimelineIcon;
        private TimelineEvent? _draggedEvent;
        private Point _dragStartPoint;
        private double _dragStartCanvasLeft;

        // Segment (bar) drag state - moves both start and stop together
        private bool _isSegmentDragging;
        private Rectangle? _draggedBar;
        private TimelineEvent? _draggedStartEvent;
        private TimelineEvent? _draggedStopEvent;
        private int _segmentDragOriginalStartMinute;
        private int _segmentDragOriginalStopMinute;
        private double _segmentDragStartX;

        // Last width the timeline was laid out against, so the SizeChanged re-render does not loop
        // (RenderEvents writes CanvasTimeline.Height, which raises SizeChanged again).
        private double _lastLayoutWidth;

        /// <summary>
        /// Result session after save (null if cancelled)
        /// </summary>
        internal EditorSession? ResultSession { get; private set; }

        /// <summary>Render/design constructor: a two-segment sample session so --render-view draws
        /// real timeline bars, icons and stats rather than an empty track.</summary>
        internal SessionEditorWindow() : this(SampleSession())
        {
            // The five feature columns are taller than the shipped 765px window, so at that size
            // the description box sits below the fold - reachable by scrolling, invisible in a
            // screenshot. The render seat is taller so the proof covers the whole view.
            Height = 900;
        }

        /// <param name="existingSession">Session to edit, or null for a blank one. WPF's public
        /// parameterless overload meant "blank"; here that signature is the render seat, so
        /// production callers pass null explicitly.</param>
        internal SessionEditorWindow(EditorSession? existingSession)
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            _session = existingSession ?? new EditorSession();

            // Handlers live here rather than in markup, per the porting convention.
            PointerPressed += Window_PointerPressed;
            BtnHelp.Click += (_, _) => BtnHelp_Click();
            BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
            BtnClose.Click += (_, _) => Close(false);
            BtnImport.Click += (_, _) => BtnImport_Click();
            BtnExport.Click += (_, _) => BtnExport_Click();
            BtnCancel.Click += (_, _) => BtnCancel_Click();
            BtnSave.Click += (_, _) => BtnSave_Click();
            BtnTutorialClose.Click += (_, _) => TutorialOverlay.IsVisible = false;
            BtnGotIt.Click += (_, _) => TutorialOverlay.IsVisible = false;
            TutorialBackdrop.PointerPressed += (_, e) => { TutorialOverlay.IsVisible = false; e.Handled = true; };
            TutorialContent.PointerPressed += (_, e) => e.Handled = true; // don't close through the card
            SliderDuration.ValueChanged += (_, e) => SliderDuration_ValueChanged(e.NewValue);

            // Canvas-level pointer handlers for smooth dragging
            CanvasTimeline.PointerMoved += CanvasTimeline_PointerMoved;
            CanvasTimeline.PointerReleased += CanvasTimeline_PointerReleased;

            // Drop zone. AllowDrop is set in the markup; the three drag events are attached
            // handlers, so they cannot be assigned as CLR events.
            TimelineDropTarget.AddHandler(DragDrop.DragEnterEvent, Timeline_DragEnter);
            TimelineDropTarget.AddHandler(DragDrop.DragOverEvent, Timeline_DragOver);
            TimelineDropTarget.AddHandler(DragDrop.DragLeaveEvent, Timeline_DragLeave);
            TimelineDropTarget.AddHandler(DragDrop.DropEvent, Timeline_Drop);

            // Positions are a fraction of the canvas width, which is 0 until the first layout pass.
            CanvasTimeline.SizeChanged += (_, e) =>
            {
                if (Math.Abs(e.NewSize.Width - _lastLayoutWidth) < 0.5) return;
                _lastLayoutWidth = e.NewSize.Width;
                RefreshTimeline();
            };

            FeatureSettings.SettingsChanged += OnSettingsChanged;
            FeatureSettings.DeleteRequested += OnDeleteRequested;
            FeatureSettings.CloseRequested += OnPopupCloseRequested;

            // After the wiring, not before it as in WPF: WPF's ValueChanged was attached in markup,
            // so assigning the slider refreshed TxtDurationValue. Here it must already be live.
            // The name placeholder was a {loc:Str} in the WPF markup; it is set here because the
            // user edits this box, and an Avalonia binding would survive their edit and undo it on
            // the next language change. Key copied from the XAML it replaces.
            TxtSessionName.Text = existingSession?.Name ?? Loc.Get("label_my_new_session");

            if (existingSession != null)
            {
                TxtDescription.Text = _session.Description;
                SliderDuration.Value = _session.DurationMinutes;
            }

            InitializeFeatureIcons();
            RefreshTimeline();
            RefreshStats();
        }

        /// <summary>Sample data for the render seat: a 60 minute session with two segments on
        /// different feature rows, so bars, start icons and stop icons are all exercised.</summary>
        private static EditorSession SampleSession()
        {
            var s = new EditorSession { Name = "Sample Session", DurationMinutes = 60 };
            s.AddStopEvent(s.AddStartEvent("spiral", 5), 25);
            s.AddStopEvent(s.AddStartEvent("flash", 30), 50);
            return s;
        }

        #region Window Chrome

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Buttons, the text boxes and the timeline mark the event handled, so this only sees
            // clicks on the chrome - the same reachability WPF's bubbling handler had.
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void BtnHelp_Click()
        {
            // ponytail: needs HelpContentService + HelpVideoWindow, wired when they are ported.
            // WPF preferred the tutorial clip when the topic shipped one and fell back to this
            // overlay; the fallback is what runs until then.
            TutorialOverlay.IsVisible = true;
        }

        #endregion

        #region Feature Icons

        private void InitializeFeatureIcons()
        {
            var features = FeatureDefinition.GetAllFeatures();

            foreach (var feature in features)
            {
                // Brain Drain is a dead feature in sessions — its runtime apply path in
                // SessionEngine is fully commented out (it never did anything when a session
                // ran), so hide it from the creator palette to stop users adding a no-op block
                // (bug #430). The definition itself stays so any legacy session that already
                // has a brain_drain item still resolves/loads without a null lookup.
                if (feature.Id == "brain_drain") continue;

                var panel = GetCategoryPanel(feature.Category);
                if (panel == null) continue;

                var icon = CreateFeatureIcon(feature);
                panel.Children.Add(icon);
            }
        }

        private Panel? GetCategoryPanel(FeatureCategory category)
        {
            return category switch
            {
                FeatureCategory.Audio => AudioFeatures,
                FeatureCategory.Video => VideoFeatures,
                FeatureCategory.Overlays => OverlayFeatures,
                FeatureCategory.Interactive => InteractiveFeatures,
                FeatureCategory.Extras => ExtrasFeatures,
                _ => null
            };
        }

        private Border CreateFeatureIcon(FeatureDefinition feature)
        {
            var border = new Border
            {
                Background = Res("PanelBgBrush"),
                CornerRadius = new CornerRadius(7),
                Width = 68,
                Height = 68,
                Margin = new Thickness(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = feature.Id
            };
            ToolTip.SetTip(border, $"{feature.Name}\n{GetFeatureDescription(feature)}\n\nDrag to timeline to add a segment");

            var grid = new Grid();

            // Try to load PNG image, fallback to emoji
            var content = CreateFeatureIconContent(feature, 60);
            grid.Children.Add(content);

            border.Child = grid;

            // Drag events
            border.PointerPressed += FeatureIcon_PointerPressed;

            return border;
        }

        private string GetFeatureDescription(FeatureDefinition feature)
        {
            return feature.Id switch
            {
                "audio_whispers" => "Plays audio whispers throughout the session",
                "mind_wipe" => "Powerful audio effect for deep immersion",
                "flash" => "Flashes images on screen periodically",
                "mandatory_videos" => "Plays mandatory video clips",
                "subliminal" => "Shows subliminal text messages",
                "bouncing_text" => "Displays bouncing text across the screen",
                "pink_filter" => "Applies a pink color filter overlay",
                "spiral" => "Shows a hypnotic spiral overlay",
                "brain_drain" => "Intense visual distortion effect",
                "bubbles" => "Floating interactive bubbles",
                "lock_cards" => "Interactive lock card challenges",
                "bubble_count" => "Bubble counting mini-game",
                "corner_gif" => "Displays a GIF in the corner",
                _ => "An effect for your session"
            };
        }

        #endregion

        #region Drag and Drop

        /// <summary>The drag payload. WPF stuffed a raw "FeatureId" string into a DataObject;
        /// Avalonia 12 replaced DataObject with DataTransfer and wants a typed DataFormat, so the
        /// format name is declared once here and used on both ends.</summary>
        private static readonly DataFormat<string> FeatureIdFormat =
            DataFormat.CreateStringApplicationFormat("FeatureId");

        /// <summary>
        /// WPF armed a flag here and called DoDragDrop from MouseMove. Avalonia 12's
        /// <c>DoDragDropAsync</c> only accepts <see cref="PointerPressedEventArgs"/>, so the drag
        /// starts on press and the flag plus the move handler are gone.
        /// </summary>
        private async void FeatureIcon_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            var featureId = (sender as Border)?.Tag as string;
            if (featureId == null) return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(FeatureIdFormat, featureId));

            // Before the await, or the press bubbles to Window_PointerPressed and the window
            // manager's move-drag grabs the pointer out from under the drag-and-drop.
            e.Handled = true;

            try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy); }
            catch (Exception ex) { Log.Debug(ex, "session editor: feature drag cancelled"); }
        }

        private void Timeline_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(FeatureIdFormat))
            {
                e.DragEffects = DragDropEffects.Copy;
                // Show visual feedback
                if (sender is Border border)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // Pink
                    border.BorderThickness = new Thickness(2);
                }
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Timeline_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(FeatureIdFormat) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Timeline_DragLeave(object? sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }
        }

        private void Timeline_Drop(object? sender, DragEventArgs e)
        {
            // Reset visual
            if (sender is Border border)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }

            if (!e.DataTransfer.Contains(FeatureIdFormat))
                return;

            var featureId = e.DataTransfer.TryGetValue(FeatureIdFormat);
            if (featureId == null) return;

            // Calculate minute from drop position
            var position = e.GetPosition(CanvasTimeline);
            var startMinute = PositionToMinute(position.X);

            // Create a segment with default duration (10 min or remaining time)
            var defaultDuration = Math.Min(10, _session.DurationMinutes - startMinute);
            if (defaultDuration < 1) defaultDuration = 1;
            var endMinute = Math.Min(startMinute + defaultDuration, _session.DurationMinutes);

            // If there's an overlap, auto-place after the last segment of this feature
            if (_session.IsOverlapping(featureId, startMinute, endMinute))
            {
                var lastEndMinute = _session.GetLastSegmentEndMinute(featureId);
                if (lastEndMinute >= 0)
                {
                    // Place 1 minute after the last segment
                    startMinute = lastEndMinute + 1;
                    endMinute = Math.Min(startMinute + defaultDuration, _session.DurationMinutes);

                    // Check if we still have room
                    if (startMinute >= _session.DurationMinutes)
                    {
                        Warn("title_timeline_full", Loc.Get("msg_no_more_room_in_the_timeline_for_this_effect"));
                        return;
                    }

                    // Ensure we have at least 1 minute duration
                    if (endMinute <= startMinute)
                    {
                        endMinute = Math.Min(startMinute + 1, _session.DurationMinutes);
                    }
                }
            }

            // Add start event
            var startEvt = _session.AddStartEvent(featureId, startMinute);

            // Add paired stop event
            _session.AddStopEvent(startEvt, endMinute);

            // Icon stays in "start" mode (green) - user can drop again to add another segment
            // No need to track "pending" state anymore

            RefreshTimeline();
            RefreshStats();
        }

        #endregion

        #region Timeline Rendering

        private void RefreshTimeline()
        {
            RenderMarkers();
            RenderEvents();

            // Hide hint if there are events
            TxtTimelineHint.IsVisible = !_session.Events.Any();
        }

        private void RenderMarkers()
        {
            CanvasMarkers.Children.Clear();

            var duration = _session.DurationMinutes;
            var width = CanvasMarkers.Bounds.Width > 0 ? CanvasMarkers.Bounds.Width : 800;

            // Calculate interval (aim for 5-10 markers, more for longer durations)
            int interval = duration <= 30 ? 5 : (duration <= 60 ? 10 : (duration <= 120 ? 15 : 30));

            for (int min = 0; min <= duration; min += interval)
            {
                var x = MinuteToPosition(min, width);

                // Marker line
                var line = new Line
                {
                    StartPoint = new Point(x, 15),
                    EndPoint = new Point(x, 20),
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    StrokeThickness = 1
                };
                CanvasMarkers.Children.Add(line);

                // Marker text - show hours:minutes for durations over 60 min
                string markerText;
                if (duration > 60)
                {
                    int hours = min / 60;
                    int mins = min % 60;
                    markerText = hours > 0 ? $"{hours}:{mins:D2}" : mins.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    markerText = min.ToString(CultureInfo.InvariantCulture);
                }

                var text = new TextBlock
                {
                    Text = markerText,
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 10
                };
                Canvas.SetLeft(text, x - 8);
                Canvas.SetTop(text, 0);
                CanvasMarkers.Children.Add(text);
            }
        }

        private const int TimelineRowHeight = 45;

        private void RenderEvents()
        {
            CanvasTimeline.Children.Clear();
            CanvasTimeline.Children.Add(TxtTimelineHint);

            var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

            // Build stable feature row assignments (alphabetically by feature ID for consistency)
            var featureIds = _session.Events
                .Select(e => e.FeatureId)
                .Distinct()
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            var featureRows = new Dictionary<string, int>();
            for (int i = 0; i < featureIds.Count; i++)
            {
                featureRows[featureIds[i]] = i;
            }

            // Update canvas height to fit all rows
            var requiredHeight = Math.Max(35, featureIds.Count * TimelineRowHeight + 15);
            CanvasTimeline.Height = requiredHeight;

            // Render each start event with its paired stop
            foreach (var evt in _session.Events.Where(e => e.EventType == TimelineEventType.Start).OrderBy(e => e.Minute))
            {
                var feature = FeatureDefinition.GetById(evt.FeatureId);
                if (feature == null) continue;

                if (!featureRows.TryGetValue(evt.FeatureId, out var row))
                    continue;

                var rowY = 4 + row * TimelineRowHeight;

                var startX = MinuteToPosition(evt.Minute, width);
                var stopEvt = _session.GetPairedStopEvent(evt);
                var endX = stopEvt != null
                    ? MinuteToPosition(stopEvt.Minute, width)
                    : MinuteToPosition(_session.DurationMinutes, width);

                // Draw connection bar (pink duration bar) - draggable to move segment
                var bar = new Rectangle
                {
                    Width = Math.Max(endX - startX, 10),
                    Height = 11,
                    Fill = new SolidColorBrush(Color.FromArgb(100, 255, 105, 180)),
                    RadiusX = 3,
                    RadiusY = 3,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = evt.Id // Store start event ID
                };
                ToolTip.SetTip(bar, $"{feature.Name}\n{evt.Minute} - {stopEvt?.Minute ?? _session.DurationMinutes} min\n\nDrag to move • Right-click to edit");
                bar.PointerPressed += SegmentBar_PointerPressed;
                Canvas.SetLeft(bar, startX);
                Canvas.SetTop(bar, rowY + 2);
                CanvasTimeline.Children.Add(bar);

                // Start icon
                var startIcon = CreateTimelineIcon(evt, feature, true);
                Canvas.SetLeft(startIcon, startX - 19);
                Canvas.SetTop(startIcon, rowY - 15);
                CanvasTimeline.Children.Add(startIcon);

                // Stop icon if exists
                if (stopEvt != null)
                {
                    var stopIcon = CreateTimelineIcon(stopEvt, feature, false);
                    Canvas.SetLeft(stopIcon, endX - 19);
                    Canvas.SetTop(stopIcon, rowY - 15);
                    CanvasTimeline.Children.Add(stopIcon);
                }
            }
        }

        private Border CreateTimelineIcon(TimelineEvent evt, FeatureDefinition feature, bool isStart)
        {
            var border = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent, // Removed green/red color
                Cursor = new Cursor(StandardCursorType.SizeWestEast), // indicates draggable
                Tag = evt.Id,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };
            ToolTip.SetTip(border, $"{feature.Name} - {(isStart ? "Start" : "Stop")} at {evt.Minute} min\nDrag to move • Right-click to edit");

            // Try to load PNG image, fallback to emoji
            border.Child = CreateFeatureIconContent(feature, 32);

            // Left button repositions, right button opens the settings popup
            border.PointerPressed += TimelineIcon_PointerPressed;

            return border;
        }

        private Control CreateFeatureIconContent(FeatureDefinition feature, double size)
        {
            // Try to load PNG image with rounded corners. WPF also tried a pack:// resource when
            // the file was missing; that URI scheme is WPF-only, so only the file path remains.
            if (!string.IsNullOrEmpty(feature.ImagePath))
            {
                try
                {
                    // System.IO.Path spelled out: Avalonia.Controls.Shapes.Path is also in scope.
                    var normalizedPath = feature.ImagePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
                    var filePath = System.IO.Path.Combine(AppContext.BaseDirectory, normalizedPath);
                    if (File.Exists(filePath))
                    {
                        // Rectangle with an ImageBrush, for the rounded corners
                        return new Rectangle
                        {
                            Width = size,
                            Height = size,
                            RadiusX = size * 0.15, // 15% corner radius
                            RadiusY = size * 0.15,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Fill = new ImageBrush
                            {
                                Source = new Bitmap(filePath),
                                Stretch = Stretch.UniformToFill
                            }
                        };
                    }
                }
                catch (Exception ex) { Log.Debug(ex, "session editor: feature image {Path} unreadable", feature.ImagePath); }
            }

            // Fallback to emoji
            return new TextBlock
            {
                Text = feature.Icon,
                FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void TimelineIcon_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border) return;

            var eventId = border.Tag as string;
            if (eventId == null) return;

            var evt = _session.Events.FirstOrDefault(ev => ev.Id == eventId);
            if (evt == null) return;

            var props = e.GetCurrentPoint(border).Properties;

            if (props.IsRightButtonPressed)
            {
                ShowFeatureSettingsPopup(evt);
                e.Handled = true;
                return;
            }

            if (!props.IsLeftButtonPressed) return;

            // Start potential drag
            _draggedTimelineIcon = border;
            _draggedEvent = evt;
            _dragStartPoint = e.GetPosition(CanvasTimeline);
            _dragStartCanvasLeft = Canvas.GetLeft(border);
            _isTimelineDragging = false; // Not yet - need to move first

            // Capture on the canvas for smooth tracking even when the pointer moves fast
            e.Pointer.Capture(CanvasTimeline);
            e.Handled = true;
        }

        private void CanvasTimeline_PointerMoved(object? sender, PointerEventArgs e)
        {
            // Handle icon dragging
            if (_draggedTimelineIcon != null && _draggedEvent != null)
            {
                HandleIconDrag(e);
                return;
            }

            // Handle segment bar dragging
            if (_draggedBar != null && _draggedStartEvent != null)
            {
                HandleSegmentDrag(e);
            }
        }

        private void HandleIconDrag(PointerEventArgs e)
        {
            // Only process if we have a valid drag state
            if (_draggedTimelineIcon == null || _draggedEvent == null)
                return;

            var currentPos = e.GetPosition(CanvasTimeline);
            var delta = currentPos.X - _dragStartPoint.X;

            // Start dragging if moved more than 5 pixels
            if (!_isTimelineDragging && Math.Abs(delta) > 5)
            {
                _isTimelineDragging = true;
            }

            if (_isTimelineDragging)
            {
                // Calculate new position
                var newLeft = _dragStartCanvasLeft + delta;
                newLeft = Math.Max(0, Math.Min(newLeft, CanvasTimeline.Bounds.Width - 20));

                // Move the icon visually
                Canvas.SetLeft(_draggedTimelineIcon, newLeft);

                // Calculate and validate new minute
                var newMinute = PositionToMinute(newLeft + 10);
                ApplyTimelineDrag(_draggedEvent, newMinute);

                // Update the associated bar visually
                UpdateBarVisual(_draggedEvent);
            }
        }

        private void HandleSegmentDrag(PointerEventArgs e)
        {
            // Only process if we have a valid drag state
            if (_draggedBar == null || _draggedStartEvent == null)
                return;

            var currentX = e.GetPosition(CanvasTimeline).X;
            var deltaX = currentX - _segmentDragStartX;

            if (!_isSegmentDragging && Math.Abs(deltaX) > 5)
            {
                _isSegmentDragging = true;
                _draggedBar.Opacity = 0.7;
            }

            if (_isSegmentDragging)
            {
                var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

                // Calculate minute delta
                var originalStartX = MinuteToPosition(_segmentDragOriginalStartMinute, width);
                var newStartX = originalStartX + deltaX;
                var newStartMinute = PositionToMinute(newStartX);

                // Calculate the duration to preserve
                var duration = _segmentDragOriginalStopMinute - _segmentDragOriginalStartMinute;

                // Clamp so segment stays in bounds
                newStartMinute = Math.Max(0, newStartMinute);
                if (_draggedStopEvent != null)
                {
                    newStartMinute = Math.Min(newStartMinute, _session.DurationMinutes - duration);
                }

                var newStopMinute = newStartMinute + duration;

                if (_session.IsOverlapping(_draggedStartEvent.FeatureId, newStartMinute, newStopMinute, _draggedStartEvent.Id))
                {
                    // Overlap detected, do not apply change.
                    return;
                }

                // Apply new minutes
                _draggedStartEvent.Minute = newStartMinute;
                if (_draggedStopEvent != null)
                    _draggedStopEvent.Minute = newStopMinute;

                // Update visuals directly
                var startX = MinuteToPosition(newStartMinute, width);
                var endX = MinuteToPosition(newStopMinute, width);

                Canvas.SetLeft(_draggedBar, startX);

                // Find and move the icons too
                foreach (var child in CanvasTimeline.Children)
                {
                    if (child is Border icon)
                    {
                        var iconEventId = icon.Tag as string;
                        if (iconEventId == _draggedStartEvent.Id)
                        {
                            Canvas.SetLeft(icon, startX - 10);
                        }
                        else if (_draggedStopEvent != null && iconEventId == _draggedStopEvent.Id)
                        {
                            Canvas.SetLeft(icon, endX - 10);
                        }
                    }
                }
            }
        }

        private void UpdateBarVisual(TimelineEvent evt)
        {
            // Find the bar associated with this event
            var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;

            TimelineEvent? startEvt;
            TimelineEvent? stopEvt;

            if (evt.EventType == TimelineEventType.Start)
            {
                startEvt = evt;
                stopEvt = _session.GetPairedStopEvent(evt);
            }
            else
            {
                // Find the start event for this stop
                startEvt = _session.Events.FirstOrDefault(e =>
                    e.EventType == TimelineEventType.Start && e.PairedEventId == evt.Id);
                stopEvt = evt;
            }

            if (startEvt == null) return;

            // Find the bar with this start event ID
            foreach (var child in CanvasTimeline.Children)
            {
                if (child is Rectangle bar && bar.Tag as string == startEvt.Id)
                {
                    var startX = MinuteToPosition(startEvt.Minute, width);
                    var endX = stopEvt != null
                        ? MinuteToPosition(stopEvt.Minute, width)
                        : MinuteToPosition(_session.DurationMinutes, width);

                    Canvas.SetLeft(bar, startX);
                    bar.Width = Math.Max(endX - startX, 10);
                    break;
                }
            }
        }

        private void CanvasTimeline_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);

            if (_isTimelineDragging)
            {
                RefreshTimeline();
                RefreshStats();
            }

            if (_isSegmentDragging)
            {
                if (_draggedBar != null) _draggedBar.Opacity = 1.0;
                RefreshTimeline();
                RefreshStats();
            }

            ResetTimelineDrag();
            ResetSegmentDrag();
        }

        #region Segment Bar Dragging

        private void SegmentBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Rectangle bar) return;

            var startEventId = bar.Tag as string;
            if (startEventId == null) return;

            var startEvt = _session.Events.FirstOrDefault(ev => ev.Id == startEventId);
            if (startEvt == null) return;

            var props = e.GetCurrentPoint(bar).Properties;

            if (props.IsRightButtonPressed)
            {
                ShowFeatureSettingsPopup(startEvt);
                e.Handled = true;
                return;
            }

            if (!props.IsLeftButtonPressed) return;

            var stopEvt = _session.GetPairedStopEvent(startEvt);

            _draggedBar = bar;
            _draggedStartEvent = startEvt;
            _draggedStopEvent = stopEvt;
            _segmentDragOriginalStartMinute = startEvt.Minute;
            _segmentDragOriginalStopMinute = stopEvt?.Minute ?? _session.DurationMinutes;
            _segmentDragStartX = e.GetPosition(CanvasTimeline).X;
            _isSegmentDragging = false;

            // Capture on the canvas for smooth tracking
            e.Pointer.Capture(CanvasTimeline);
            e.Handled = true;
        }

        private void ResetSegmentDrag()
        {
            _isSegmentDragging = false;
            _draggedBar = null;
            _draggedStartEvent = null;
            _draggedStopEvent = null;
        }

        #endregion

        private void ApplyTimelineDrag(TimelineEvent evt, int newMinute)
        {
            // Clamp to valid range
            newMinute = Math.Max(0, Math.Min(newMinute, _session.DurationMinutes));

            int startMinute, endMinute;

            if (evt.EventType == TimelineEventType.Start)
            {
                var stopEvt = _session.GetPairedStopEvent(evt);
                if (stopEvt == null) return;
                startMinute = newMinute;
                endMinute = stopEvt.Minute;

                // For start events, ensure it doesn't go past its stop event
                if (startMinute >= endMinute)
                {
                    startMinute = Math.Max(0, endMinute - 1);
                }
            }
            else // Stop event
            {
                var startEvt = _session.Events.FirstOrDefault(e => e.EventType == TimelineEventType.Start && e.PairedEventId == evt.Id);
                if (startEvt == null) return;
                startMinute = startEvt.Minute;
                endMinute = newMinute;

                // For stop events, ensure it doesn't go before its start event
                if (endMinute <= startMinute)
                {
                    endMinute = Math.Min(_session.DurationMinutes, startMinute + 1);
                }
            }

            if (_session.IsOverlapping(evt.FeatureId, startMinute, endMinute, evt.Id))
            {
                // Overlap detected, don't apply the change. The UI will snap back in RefreshTimeline.
                return;
            }

            evt.Minute = newMinute;
        }

        private void ResetTimelineDrag()
        {
            _isTimelineDragging = false;
            _draggedTimelineIcon = null;
            _draggedEvent = null;
        }

        // Padding to prevent icons from being clipped at edges
        private const double TimelinePadding = 45;

        private double MinuteToPosition(int minute, double width)
        {
            // Add padding on both sides so icons at 0 and end aren't clipped
            var usableWidth = width - (TimelinePadding * 2);
            return TimelinePadding + (minute / (double)_session.DurationMinutes) * usableWidth;
        }

        private int PositionToMinute(double x)
        {
            var width = CanvasTimeline.Bounds.Width > 0 ? CanvasTimeline.Bounds.Width : 800;
            var usableWidth = width - (TimelinePadding * 2);
            var adjustedX = x - TimelinePadding;
            var minute = (int)Math.Round((adjustedX / usableWidth) * _session.DurationMinutes);
            return Math.Max(0, Math.Min(minute, _session.DurationMinutes));
        }

        #endregion

        #region Settings Popup

        private void ShowFeatureSettingsPopup(TimelineEvent evt)
        {
            // Load event into popup. WPF passed the whole TimelineSession purely so the popup could
            // reach its two phrase lists; the ported popup takes those lists directly.
            FeatureSettings.LoadEvent(evt, _session.DurationMinutes,
                                      _session.SubliminalPhrases, _session.BouncingTextPhrases);

            // Show popup
            SettingsPopup.IsOpen = true;
        }

        private void OnSettingsChanged(object? sender, TimelineEvent evt)
        {
            RefreshTimeline();
            RefreshStats();
        }

        private void OnDeleteRequested(object? sender, TimelineEvent evt)
        {
            SettingsPopup.IsOpen = false;

            _session.RemoveEvent(evt);

            RefreshTimeline();
            RefreshStats();
        }

        private void OnPopupCloseRequested(object? sender, EventArgs e)
        {
            SettingsPopup.IsOpen = false;
        }

        #endregion

        #region Stats

        private void RefreshStats()
        {
            var xp = _session.CalculateXP();
            var (difficultyText, difficultyColor) = _session.GetDifficulty();

            TxtXP.Text = Loc.GetF("session_xp_amount", xp);
            TxtDifficulty.Text = difficultyText;
            TxtDifficulty.Foreground = new SolidColorBrush(Color.Parse(difficultyColor));
            TxtDuration.Text = Loc.GetF("session_duration_min", _session.DurationMinutes);

            // WPF let the markup placeholder stand until the slider first moved; that placeholder
            // is gone, so the value label is written on every stats pass instead.
            TxtDurationValue.Text = Loc.GetF("session_duration_min", _session.DurationMinutes);
        }

        private void SliderDuration_ValueChanged(double newValue)
        {
            if (_session == null) return;

            _session.DurationMinutes = (int)newValue;
            TxtDurationValue.Text = Loc.GetF("session_duration_min", _session.DurationMinutes);

            // Clamp any events that exceed the new duration
            foreach (var evt in _session.Events.Where(ev => ev.Minute > _session.DurationMinutes).ToList())
            {
                evt.Minute = _session.DurationMinutes;
            }

            // Remove zero-length segments (start and stop clamped to same minute)
            var collapsedStarts = _session.Events
                .Where(e => e.EventType == TimelineEventType.Start && e.PairedEventId != null)
                .Where(start =>
                {
                    var stop = _session.Events.FirstOrDefault(e => e.Id == start.PairedEventId);
                    return stop != null && start.Minute >= stop.Minute;
                })
                .ToList();
            foreach (var start in collapsedStarts)
            {
                var stop = _session.Events.FirstOrDefault(e => e.Id == start.PairedEventId);
                _session.Events.Remove(start);
                if (stop != null) _session.Events.Remove(stop);
            }

            RefreshTimeline();
            RefreshStats();
        }

        #endregion

        #region Buttons

        private void BtnImport_Click()
        {
            // ponytail: needs SessionFileService + a StorageProvider file picker, wired when the
            // service moves to Core. WPF filtered on *.session.json, validated the file, mapped it
            // through TimelineSession.FromSession and reported with msg_invalid_session_file /
            // msg_failed_to_import_session / msg_imported_session.
            Log.Information("session editor: import is not wired on this head yet");
        }

        private void BtnExport_Click()
        {
            // ponytail: needs SessionFileService.ExportSession + GetExportFileName and a save
            // picker, wired when the service moves to Core (msg_session_exported_to on success).
            _session.Name = TxtSessionName.Text ?? string.Empty;
            _session.Description = TxtDescription.Text ?? string.Empty;
            Log.Information("session editor: export is not wired on this head yet");
        }

        private void BtnCancel_Click()
        {
            ResultSession = null;
            Close(false);
        }

        private void BtnSave_Click()
        {
            // Update session from UI
            _session.Name = TxtSessionName.Text ?? string.Empty;
            _session.Description = TxtDescription.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_session.Name))
            {
                Warn("title_validation_error", Loc.Get("msg_please_enter_a_session_name"));
                return;
            }

            ResultSession = _session;
            Close(true);
        }

        #endregion

        /// <summary>Stand-in for WPF's <c>MessageBox.Show(..., MessageBoxImage.Warning)</c>: Avalonia
        /// ships no message box and no package may be added here. Keys and argument order are
        /// preserved so each call site becomes a real dialog in one line later.
        /// ponytail: needs a shared warning dialog, wired when one lands on this head.</summary>
        private static void Warn(string titleKey, string message)
            => Log.Warning("session editor [{Title}]: {Message}", Loc.Get(titleKey), message);

        private IBrush? Res(string key) => this.TryFindResource(key, out var v) ? v as IBrush : null;

        /// <summary>
        /// The slice of <c>ConditioningControlPanel.Models.TimelineSession</c> this editor calls,
        /// logic copied verbatim from the WPF head. That type is 804 lines and pulls in Session,
        /// SessionSettings and thirteen per-feature mappers, none of which are in Core yet - and
        /// none of which the editor itself touches.
        ///
        /// ponytail: local stand-in for TimelineSession; delete it and retype the _session field
        /// when TimelineSession moves to Core.
        /// </summary>
        internal sealed class EditorSession
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = "New Session";
            public string Icon { get; set; } = "✨";
            public string Description { get; set; } = "";
            public int DurationMinutes { get; set; } = 30;
            public List<TimelineEvent> Events { get; set; } = new();
            public List<string> SubliminalPhrases { get; set; } = new();
            public List<string> BouncingTextPhrases { get; set; } = new();

            public bool HasFeature(string featureId)
                => Events.Any(e => e.FeatureId == featureId && e.EventType == TimelineEventType.Start);

            public List<TimelineEvent> GetStartEvents(string featureId)
                => Events.Where(e => e.FeatureId == featureId && e.EventType == TimelineEventType.Start).ToList();

            public TimelineEvent? GetPairedStopEvent(TimelineEvent startEvent)
            {
                if (startEvent.EventType != TimelineEventType.Start || string.IsNullOrEmpty(startEvent.PairedEventId))
                    return null;
                return Events.FirstOrDefault(e => e.Id == startEvent.PairedEventId);
            }

            /// <summary>End minute of the last segment for a feature, or -1 when it has none.</summary>
            public int GetLastSegmentEndMinute(string featureId)
            {
                var lastEndMinute = -1;

                foreach (var startEvt in Events.Where(e => e.FeatureId == featureId && e.EventType == TimelineEventType.Start))
                {
                    var stopEvt = GetPairedStopEvent(startEvt);
                    if (stopEvt != null && stopEvt.Minute > lastEndMinute)
                    {
                        lastEndMinute = stopEvt.Minute;
                    }
                }

                return lastEndMinute;
            }

            /// <summary>Maximum opacity/intensity setting for a feature.</summary>
            public int GetMaxValue(string featureId, string settingKey)
            {
                var startEvents = GetStartEvents(featureId);
                if (startEvents.Count == 0) return 0;

                int maxValue = 0;
                foreach (var evt in startEvents)
                {
                    var value = evt.GetSetting<int>(settingKey, 0);
                    if (value > maxValue) maxValue = value;

                    // Also check end value for ramping
                    if (evt.EndValue.HasValue && evt.EndValue.Value > maxValue)
                        maxValue = evt.EndValue.Value;
                }
                return maxValue;
            }

            /// <summary>XP reward for this session, rounded to the nearest 50.</summary>
            public int CalculateXP()
            {
                // Base XP: 10 per minute
                int baseXP = DurationMinutes * 10;

                // Feature bonus based on distinct features (each feature counts once)
                int featureBonus = 0;
                var countedFeatures = new HashSet<string>();
                foreach (var evt in Events.Where(e => e.EventType == TimelineEventType.Start))
                {
                    if (!countedFeatures.Add(evt.FeatureId)) continue;
                    var definition = FeatureDefinition.GetById(evt.FeatureId);
                    if (definition != null)
                    {
                        featureBonus += definition.XPBonus;
                    }
                }

                // Round to nearest 50
                return (int)(Math.Round((baseXP + featureBonus) / 50.0) * 50);
            }

            /// <summary>Difficulty label and colour. WPF split this across CalculateDifficulty,
            /// GetDifficultyText and GetDifficultyColor - three passes over the same score, and a
            /// SessionDifficulty enum that only ever fed those two switches.</summary>
            public (string Text, string Color) GetDifficulty()
            {
                int score = 0;

                // Duration factor: +1 per 15 minutes
                score += DurationMinutes / 15;

                // Count distinct active features
                score += Events
                    .Where(e => e.EventType == TimelineEventType.Start)
                    .Select(e => e.FeatureId)
                    .Distinct()
                    .Count();

                // Heavy features add more weight (each feature counted once)
                var countedFeatures = new HashSet<string>();
                foreach (var evt in Events.Where(e => e.EventType == TimelineEventType.Start))
                {
                    if (!countedFeatures.Add(evt.FeatureId)) continue;
                    var definition = FeatureDefinition.GetById(evt.FeatureId);
                    if (definition != null)
                    {
                        score += definition.DifficultyWeight;
                    }
                }

                // High intensity settings add more difficulty
                if (HasFeature("spiral") && GetMaxValue("spiral", "opacity") > 20) score += 1;
                if (HasFeature("flash") && GetMaxValue("flash", "opacity") > 60) score += 1;
                if (HasFeature("brain_drain") && GetMaxValue("brain_drain", "intensity") > 10) score += 1;

                return score switch
                {
                    <= 4 => ("⭐ Easy", "#90EE90"),         // Light green
                    <= 8 => ("⭐⭐ Medium", "#FFD700"),      // Gold
                    <= 12 => ("⭐⭐⭐ Hard", "#FFA500"),      // Orange
                    _ => ("💀 Extreme", "#FF6347")          // Tomato red
                };
            }

            /// <summary>Add a start event to the timeline.</summary>
            public TimelineEvent AddStartEvent(string featureId, int minute, Dictionary<string, object>? settings = null)
            {
                var evt = new TimelineEvent
                {
                    FeatureId = featureId,
                    Minute = minute,
                    EventType = TimelineEventType.Start,
                    Settings = settings ?? new Dictionary<string, object>()
                };

                // Apply default settings from feature definition
                var definition = FeatureDefinition.GetById(featureId);
                if (definition != null)
                {
                    foreach (var settingDef in definition.Settings)
                    {
                        if (!evt.Settings.ContainsKey(settingDef.Key) && settingDef.Default != null)
                        {
                            evt.Settings[settingDef.Key] = settingDef.Default;
                        }
                    }
                }

                Events.Add(evt);
                return evt;
            }

            /// <summary>Add a stop event paired to a start event.</summary>
            public TimelineEvent AddStopEvent(TimelineEvent startEvent, int minute)
            {
                var evt = new TimelineEvent
                {
                    FeatureId = startEvent.FeatureId,
                    Minute = minute,
                    EventType = TimelineEventType.Stop,
                    PairedEventId = startEvent.Id
                };

                startEvent.PairedEventId = evt.Id;
                Events.Add(evt);
                return evt;
            }

            /// <summary>Remove an event and its paired event.</summary>
            public void RemoveEvent(TimelineEvent evt)
            {
                if (!string.IsNullOrEmpty(evt.PairedEventId))
                {
                    var paired = Events.FirstOrDefault(e => e.Id == evt.PairedEventId);
                    if (paired != null)
                    {
                        Events.Remove(paired);
                    }
                }

                // Also remove any events that reference this one
                foreach (var r in Events.Where(e => e.PairedEventId == evt.Id).ToList())
                {
                    r.PairedEventId = null;
                }

                Events.Remove(evt);
            }

            /// <summary>True when [startMinute, endMinute) overlaps another segment of the same
            /// feature.</summary>
            public bool IsOverlapping(string featureId, int startMinute, int endMinute, string? excludeEventId = null)
            {
                var featureStartEvents = Events.Where(e =>
                    e.FeatureId == featureId &&
                    e.EventType == TimelineEventType.Start &&
                    e.Id != excludeEventId &&
                    (e.PairedEventId != excludeEventId || excludeEventId == null));

                foreach (var startEvt in featureStartEvents)
                {
                    var stopEvt = GetPairedStopEvent(startEvt);
                    if (stopEvt != null)
                    {
                        // Check for overlap: (StartA < EndB) and (EndA > StartB)
                        if (startMinute < stopEvt.Minute && endMinute > startEvt.Minute)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
