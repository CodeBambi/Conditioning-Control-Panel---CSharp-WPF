using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// Mission 3 (Deeper Player redesign) additions.
    ///
    /// New surfaces:
    /// - Header status pill (Empty / Loaded / Live).
    /// - File context strip — folds the old AudioFileRow + enh-file-row +
    ///   TxtEnhSource into one strip with a Change popover for the four
    ///   file-loading actions.
    /// - "Now: [region]" overlay on the media pane (top-right).
    /// - Mini-timeline read-out (regions + rule pins + playhead).
    /// - Structured event log (filter pills All / Actions / Engine / Errors;
    ///   collapse + clear; per-row icon coloring by category).
    /// - "Open in editor" jump from the header (routes through MainWindow's
    ///   OpenDeeperEditor — focuses an existing editor if one is open for
    ///   the same project).
    ///
    /// All x:Names on file-loading buttons + audio path + TxtEnhPath etc.
    /// were preserved by the XAML rewrite so existing Click handlers and
    /// ShowMediaPaneFor/UpdateHostUi paths in EnhancementPlayerWindow.xaml.cs
    /// keep working unchanged. This partial only ADDS behavior.
    /// </summary>
    public partial class EnhancementPlayerWindow
    {
        // ====================================================================
        // Structured event log model
        // ====================================================================

        /// <summary>
        /// One row in the player's event log. Bound to LstEvents via a
        /// CollectionViewSource filter — the active filter pill decides which
        /// categories pass.
        ///
        /// Spec called for the rule label to be colored by trigger family
        /// (amber/violet/teal). The engine's ActionLogged stream is a plain
        /// formatted string without a rule id, so we can't reliably map a
        /// firing back to its rule without engine changes. For v1 we
        /// classify strictly by source (ActionLogged → Action, Diagnostic →
        /// Engine, LoadFailed → Error) and skip the per-rule color tint.
        /// Documented in mission-3-player-redesign.md.
        /// </summary>
        public enum EventLogCategory { Action, Engine, Error }

        public sealed class EventLogEntry
        {
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public EventLogCategory Category { get; init; }
            public string Description { get; init; } = "";
            public string? RuleId { get; init; }
            public string? RuleLabel { get; init; }

            public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");

            public string IconGlyph => Category switch
            {
                EventLogCategory.Action => "⚡",
                EventLogCategory.Engine => "⚙",
                EventLogCategory.Error => "⚠",
                _ => "•",
            };

            public Brush IconBrush => Category switch
            {
                EventLogCategory.Action => (Brush)Application.Current.FindResource("DeeperAccentBrush"),
                EventLogCategory.Engine => (Brush)Application.Current.FindResource("TextMutedBrush"),
                EventLogCategory.Error => (Brush)Application.Current.FindResource("DangerBrush"),
                _ => Brushes.Gray,
            };

            // Action rows render flat; Engine + Error get a faint tinted
            // background so they don't compete with effect-firing rows.
            public Brush RowBgBrush => Category switch
            {
                EventLogCategory.Engine => (Brush)Application.Current.FindResource("DeeperLaneHeaderBrush"),
                EventLogCategory.Error => (Brush)Application.Current.FindResource("DeeperLaneHeaderBrush"),
                _ => Brushes.Transparent,
            };

            public Brush RuleLabelBrush =>
                (Brush)Application.Current.FindResource("DeeperAccentSoftBrush");

            // "Open in editor" link only makes sense when we know which rule
            // fired. Hidden in v1 since the engine stream doesn't carry the
            // rule id back — see mission report.
            public Visibility OpenInEditorVisibility =>
                string.IsNullOrEmpty(RuleId) ? Visibility.Collapsed : Visibility.Visible;
        }

        private readonly ObservableCollection<EventLogEntry> _logEntries = new();
        private ICollectionView? _logEntriesView;
        private string _activeFilter = "all"; // all | action | engine | error
        private const int MaxLogEntries = 30;
        private bool _eventLogCollapsed;
        private double _eventLogExpandedHeight = 140;

        /// <summary>
        /// Called from the constructor (after InitializeComponent) once XAML
        /// is live. Sets up the filtered view, wires the live status pill,
        /// and primes counts. Safe to call multiple times — short-circuits
        /// if _logEntriesView already exists.
        /// </summary>
        private void InitializeMission3()
        {
            if (_logEntriesView != null) return;

            _logEntriesView = CollectionViewSource.GetDefaultView(_logEntries);
            _logEntriesView.Filter = LogEntryFilter;
            LstEvents.ItemsSource = _logEntriesView;

            UpdateFilterCounts();
            UpdateStatusPill();
        }

        private bool LogEntryFilter(object o)
        {
            if (o is not EventLogEntry e) return false;
            return _activeFilter switch
            {
                "action" => e.Category == EventLogCategory.Action,
                "engine" => e.Category == EventLogCategory.Engine,
                "error" => e.Category == EventLogCategory.Error,
                _ => true,
            };
        }

        // ====================================================================
        // Event ingestion (replaces AppendEvent's string-only path)
        // ====================================================================

        private void IngestActionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            AppendLogEntry(new EventLogEntry
            {
                Category = EventLogCategory.Action,
                Description = line,
            });
        }

        private void IngestDiagnosticLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            // Best-effort: lines that loudly say "error" / "fail" land in
            // the Error bucket even when they came via the Diagnostic event.
            var lower = line.ToLowerInvariant();
            var cat = (lower.Contains("error") || lower.Contains("fail") || lower.Contains("rejected"))
                ? EventLogCategory.Error
                : EventLogCategory.Engine;
            AppendLogEntry(new EventLogEntry { Category = cat, Description = line });
        }

        private void IngestErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            AppendLogEntry(new EventLogEntry { Category = EventLogCategory.Error, Description = line });
        }

        private void AppendLogEntry(EventLogEntry entry)
        {
            _logEntries.Insert(0, entry);
            while (_logEntries.Count > MaxLogEntries)
                _logEntries.RemoveAt(_logEntries.Count - 1);
            UpdateFilterCounts();
        }

        private void UpdateFilterCounts()
        {
            try
            {
                int all = _logEntries.Count;
                int action = 0, engine = 0, error = 0;
                foreach (var e in _logEntries)
                {
                    switch (e.Category)
                    {
                        case EventLogCategory.Action: action++; break;
                        case EventLogCategory.Engine: engine++; break;
                        case EventLogCategory.Error: error++; break;
                    }
                }
                if (TxtFilterCountAll != null) TxtFilterCountAll.Text = all.ToString();
                if (TxtFilterCountActions != null) TxtFilterCountActions.Text = action.ToString();
                if (TxtFilterCountEngine != null) TxtFilterCountEngine.Text = engine.ToString();
                if (TxtFilterCountErrors != null) TxtFilterCountErrors.Text = error.ToString();
            }
            catch { }
        }

        // ====================================================================
        // Filter pill clicks (single-select)
        // ====================================================================

        private void EventFilterPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;
            // Force single-select: ignore unchecks (re-check the clicked
            // pill if the user tried to deselect the active filter), and
            // uncheck the other three.
            if (clicked.IsChecked != true) { clicked.IsChecked = true; return; }
            _activeFilter = (clicked.Tag as string ?? "all").ToLowerInvariant();
            foreach (var pill in new[] { PillFilterAll, PillFilterActions, PillFilterEngine, PillFilterErrors })
            {
                if (pill == null || ReferenceEquals(pill, clicked)) continue;
                pill.IsChecked = false;
            }
            _logEntriesView?.Refresh();
        }

        private void BtnClearEvents_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            UpdateFilterCounts();
        }

        private void BtnCollapseEventLog_Click(object sender, RoutedEventArgs e)
        {
            if (EventScroll == null) return;
            _eventLogCollapsed = !_eventLogCollapsed;
            if (_eventLogCollapsed)
            {
                _eventLogExpandedHeight = EventScroll.MaxHeight > 0 ? EventScroll.MaxHeight : 140;
                EventScroll.MaxHeight = 0;
                EventScroll.Visibility = Visibility.Collapsed;
                BtnCollapseEventLog.Content = "▴";
            }
            else
            {
                EventScroll.MaxHeight = _eventLogExpandedHeight;
                EventScroll.Visibility = Visibility.Visible;
                BtnCollapseEventLog.Content = "▾";
            }
        }

        private void EventOpenInEditor_Click(object sender, RoutedEventArgs e)
        {
            // RuleId carried via the row's Tag binding. Hidden by default
            // in v1 (no rule id flows from the engine string format) — but
            // wired so the moment we DO start carrying rule ids the link
            // works without further UI changes.
            try
            {
                var ruleId = (sender as Button)?.Tag as string;
                JumpToEditorForCurrentEnhancement(ruleId);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Player: open-in-editor jump from event log failed: {Error}", ex.Message);
            }
        }

        // ====================================================================
        // Header: Open in editor + status pill + Change popover
        // ====================================================================

        private void BtnOpenInEditor_Click(object sender, RoutedEventArgs e)
        {
            JumpToEditorForCurrentEnhancement(ruleId: null);
        }

        /// <summary>
        /// Routes to the editor for the currently-loaded enhancement.
        ///
        /// If we know a real disk path (.ccpenh.json), route through
        /// MainWindow.OpenDeeperFile (the same plumbing the hub library
        /// row's click uses). Otherwise the enhancement is in-memory only
        /// (editor-preview launch from the editor itself); the editor that
        /// owns us is already open and is the right target, so just focus
        /// it.
        ///
        /// Existing-window dedupe: if any DeeperEditorWindow is open whose
        /// loaded file path matches ours, activate it instead of spawning
        /// a duplicate.
        /// </summary>
        private void JumpToEditorForCurrentEnhancement(string? ruleId)
        {
            var enh = _host?.LoadedEnhancement;
            if (enh == null) return;
            var path = _host?.LoadedFilePath;

            // In-memory only path: the editor-preview tag we set ("editor-
            // preview" or "embedded:foo.mp4" etc) won't be a real file
            // path. Just focus this window's owner if it's an editor.
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (Owner is DeeperEditorWindow editor)
                {
                    try { editor.Activate(); } catch { }
                }
                return;
            }

            // Dedupe: walk all open windows, pick the matching editor.
            foreach (Window w in Application.Current.Windows)
            {
                if (w is DeeperEditorWindow ed && string.Equals(ed.LoadedFilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    try { ed.Activate(); } catch { }
                    return;
                }
            }

            // No matching editor open — route through the main window's
            // standard OpenDeeperFile path (also refreshes the library
            // list on close).
            try
            {
                if (Application.Current.MainWindow is MainWindow mw)
                {
                    mw.OpenDeeperEditorFromPlayer(path);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Player: open-in-editor route failed");
            }
        }

        private void BtnChange_Click(object sender, RoutedEventArgs e)
        {
            if (ChangePopup == null) return;
            ChangePopup.IsOpen = !ChangePopup.IsOpen;
        }

        /// <summary>
        /// Status pill state machine. Called from UpdateHostUi after the
        /// enhancement load + from UiTimer_Tick when play/pause state
        /// might have shifted.
        /// </summary>
        private void UpdateStatusPill()
        {
            try
            {
                if (StatusPill == null || StatusPillText == null) return;
                var enh = _host?.LoadedEnhancement;
                if (enh == null)
                {
                    StatusPillText.Text = Loc.Get("deeper_player_pill_empty");
                    StatusPillText.Foreground = (Brush)Application.Current.FindResource("TextMutedBrush");
                    StatusPill.Background = (Brush)Application.Current.FindResource("DeeperAccentTransparent20Brush");
                    StatusPill.BorderBrush = (Brush)Application.Current.FindResource("DeeperAccentTransparent40Brush");
                    return;
                }

                bool isPlaying = (_videoSource?.IsPlaying ?? false) || _player.IsPlaying;
                if (isPlaying)
                {
                    StatusPillText.Text = Loc.Get("deeper_player_pill_live");
                    var accent = (Brush)Application.Current.FindResource("DeeperAccentBrush");
                    StatusPillText.Foreground = Brushes.White;
                    StatusPill.Background = accent;
                    StatusPill.BorderBrush = accent;
                }
                else
                {
                    StatusPillText.Text = Loc.Get("deeper_player_pill_loaded");
                    var soft = (Brush)Application.Current.FindResource("DeeperAccentSoftBrush");
                    StatusPillText.Foreground = soft;
                    StatusPill.Background = (Brush)Application.Current.FindResource("DeeperAccentTransparent20Brush");
                    StatusPill.BorderBrush = soft;
                }
            }
            catch { }
        }

        // ====================================================================
        // File context strip — name/icon/meta-line update
        // ====================================================================

        /// <summary>
        /// Called from UpdateHostUi after TxtEnhPath/TxtEnhMetadata are set.
        /// Computes the display name, media-type icon, and the inline
        /// creator·source·counts line for the file context strip.
        /// </summary>
        private void RefreshFileContextStrip(Enhancement? enh, string? path)
        {
            if (TxtEnhName == null) return;

            if (enh == null)
            {
                TxtEnhName.Text = Loc.Get("deeper_player_no_enh");
                TxtEnhMetadata.Text = "";
                if (TxtEnhPath != null) TxtEnhPath.Visibility = Visibility.Collapsed;
                if (MediaTypeIcon != null) MediaTypeIcon.Text = "🎵";
                if (MediaTypeIconBg != null)
                    MediaTypeIconBg.Background = (Brush)Application.Current.FindResource("DeeperHubAudioBadgeBgBrush");
                if (SourcePill != null) SourcePill.Visibility = Visibility.Collapsed;
                if (BtnOpenInEditor != null) BtnOpenInEditor.Visibility = Visibility.Collapsed;
                return;
            }

            var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? "(untitled)" : enh.Metadata!.Name;
            TxtEnhName.Text = name;

            var isVideo = string.Equals(enh.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase);
            if (MediaTypeIcon != null) MediaTypeIcon.Text = isVideo ? "🎬" : "🎵";
            if (MediaTypeIconBg != null)
                MediaTypeIconBg.Background = (Brush)Application.Current.FindResource(
                    isVideo ? "DeeperHubVideoBadgeBgBrush" : "DeeperHubAudioBadgeBgBrush");

            // Inline meta line: creator · sourceGlyph source · counts.
            // Counts come from the loaded enhancement — they're cheap.
            var creator = enh.Metadata?.Creator;
            var (srcGlyph, srcText) = DescribeMediaSource(enh);
            int regions = enh.Regions?.Count ?? 0;
            int rules = enh.Rules?.Count ?? 0;
            int haptics = 0;
            if (enh.HapticTracks != null)
                foreach (var t in enh.HapticTracks) haptics += t?.Events?.Count ?? 0;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(creator)) parts.Add(creator!);
            if (!string.IsNullOrWhiteSpace(srcText)) parts.Add($"{srcGlyph} {srcText}");
            parts.Add(string.Format(Loc.Get("deeper_player_meta_counts_fmt"), regions, rules, haptics));
            TxtEnhMetadata.Text = string.Join("  ·  ", parts);

            // TxtEnhPath kept hidden by default — the popover surfaces it
            // when the user wants the raw path. Tooltip on the name shows
            // it as a discoverable detail.
            if (TxtEnhPath != null)
            {
                TxtEnhPath.Text = path ?? "";
                TxtEnhPath.Visibility = Visibility.Collapsed;
            }
            ToolTipService.SetToolTip(TxtEnhName, path ?? "");

            if (SourcePill != null) SourcePill.Visibility = Visibility.Visible;
            if (BtnOpenInEditor != null) BtnOpenInEditor.Visibility = Visibility.Visible;
        }

        private static (string Glyph, string Text) DescribeMediaSource(Enhancement enh)
        {
            var src = enh.MediaSource;
            if (string.IsNullOrWhiteSpace(src)) return ("⚠", Loc.Get("deeper_player_source_missing"));
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { return ("🌐", new Uri(src).Host); } catch { return ("🌐", src); }
            }
            if (File.Exists(src)) return ("✓", System.IO.Path.GetFileName(src));
            return ("⚠", System.IO.Path.GetFileName(src));
        }

        // ====================================================================
        // Mini-timeline read-out (regions + rule pins + playhead)
        // ====================================================================

        // Cached enhancement reference so we don't reach through _host every
        // tick. Refreshed in UpdateHostUi via OnEnhancementLoadedForMini.
        private Enhancement? _miniEnhancement;
        // Cached total duration for time→x conversion. For audio: _player.DurationMs.
        // For video: _videoSource.GetDurationSeconds(). Refreshed each tick.
        private double _miniTotalSeconds;

        private void OnEnhancementLoadedForMini(Enhancement? enh)
        {
            _miniEnhancement = enh;
            if (MiniTimelinePanel == null) return;
            if (enh == null)
            {
                MiniTimelinePanel.Visibility = Visibility.Collapsed;
                return;
            }
            MiniTimelinePanel.Visibility = Visibility.Visible;
            RebuildMiniTimeline();
        }

        private void MiniTimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RebuildMiniTimeline();
        }

        private void RebuildMiniTimeline()
        {
            try
            {
                if (MiniTimelineCanvas == null) return;
                MiniTimelineCanvas.Children.Clear();
                var enh = _miniEnhancement;
                if (enh == null) return;
                var w = MiniTimelineCanvas.ActualWidth;
                var h = MiniTimelineCanvas.ActualHeight;
                if (w <= 0 || h <= 0) return;

                // Use enhancement's effective max time as the denominator.
                // We prefer playback duration when known, else the latest
                // region/rule/haptic end so even a short clip with rules
                // spanning past the end stays visible.
                var total = ComputeMiniTotalSeconds(enh);
                if (total <= 0) return;
                _miniTotalSeconds = total;

                // Region bands — full height, color from metadata.
                if (enh.Regions != null)
                {
                    foreach (var r in enh.Regions)
                    {
                        if (r == null) continue;
                        double rs = Math.Max(0, r.Start);
                        double re = Math.Max(rs, r.End);
                        if (re <= 0) continue;
                        var x1 = (rs / total) * w;
                        var x2 = (re / total) * w;
                        var bw = Math.Max(2, x2 - x1);
                        var brush = ParseHexBrush(r.Color, fallbackResourceKey: "DeeperAccentBrush");
                        var fill = brush is SolidColorBrush sb
                            ? new SolidColorBrush(Color.FromArgb(110, sb.Color.R, sb.Color.G, sb.Color.B))
                            : (Brush)Application.Current.FindResource("DeeperAccentTransparent40Brush");
                        var rect = new Rectangle
                        {
                            Width = bw,
                            Height = h - 2,
                            Fill = fill,
                            Stroke = brush,
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2,
                            ToolTip = string.IsNullOrEmpty(r.Label) ? r.Id : r.Label,
                        };
                        Canvas.SetLeft(rect, x1);
                        Canvas.SetTop(rect, 1);
                        MiniTimelineCanvas.Children.Add(rect);

                        // Label printed on the band when there's room.
                        if (bw >= 40 && !string.IsNullOrEmpty(r.Label))
                        {
                            var tb = new TextBlock
                            {
                                Text = r.Label,
                                Foreground = Brushes.White,
                                FontSize = 9,
                                FontWeight = FontWeights.SemiBold,
                                IsHitTestVisible = false,
                            };
                            Canvas.SetLeft(tb, x1 + 4);
                            Canvas.SetTop(tb, (h - 12) / 2.0);
                            MiniTimelineCanvas.Children.Add(tb);
                        }
                    }
                }

                // Rule pins — TimeReached only. Other rule types are
                // represented by their constraint region's band.
                if (enh.Rules != null)
                {
                    foreach (var rule in enh.Rules)
                    {
                        var trig = rule?.Trigger;
                        if (trig is not TimeReachedTrigger tr) continue;
                        var t = Math.Max(0, tr.Time);
                        if (t > total) continue;
                        var x = (t / total) * w;
                        // 1.5px dashed orange line — matches the editor's
                        // rule pin language.
                        var line = new Line
                        {
                            X1 = x, X2 = x, Y1 = 1, Y2 = h - 1,
                            Stroke = Brushes.Orange,
                            StrokeThickness = 1.5,
                            StrokeDashArray = new DoubleCollection { 2, 2 },
                            IsHitTestVisible = false,
                        };
                        MiniTimelineCanvas.Children.Add(line);
                        // Small flag at top — 5x4 triangle.
                        var flag = new Polygon
                        {
                            Points = new PointCollection
                            {
                                new Point(x - 3, 1),
                                new Point(x + 3, 1),
                                new Point(x, 5),
                            },
                            Fill = Brushes.Orange,
                            IsHitTestVisible = false,
                        };
                        MiniTimelineCanvas.Children.Add(flag);
                    }
                }

                // Playhead — solid accent line + small triangle marker on top.
                var ph = new Line
                {
                    X1 = 0, X2 = 0, Y1 = 0, Y2 = h,
                    Stroke = (Brush)Application.Current.FindResource("DeeperAccentBrush"),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                ph.Tag = "playhead";
                MiniTimelineCanvas.Children.Add(ph);

                UpdateMiniPlayheadX();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Player: mini-timeline build failed: {Error}", ex.Message);
            }
        }

        private void UpdateMiniPlayheadX()
        {
            if (MiniTimelineCanvas == null || _miniEnhancement == null) return;
            var w = MiniTimelineCanvas.ActualWidth;
            if (w <= 0 || _miniTotalSeconds <= 0) return;
            double currentSec;
            if (_videoSource != null)
                currentSec = _videoSource.GetCurrentTimeSeconds();
            else
                currentSec = _player.CurrentTimeMs / 1000.0;
            var x = Math.Clamp(currentSec / _miniTotalSeconds, 0, 1) * w;

            foreach (var child in MiniTimelineCanvas.Children)
            {
                if (child is Line l && (l.Tag as string) == "playhead")
                {
                    l.X1 = x; l.X2 = x;
                    break;
                }
            }
        }

        private void MiniTimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Audio-mode seek. Video mode seeking through the JS bridge is
            // race-prone right after navigation; skip for v1 (see report).
            try
            {
                if (_miniEnhancement == null) return;
                var w = MiniTimelineCanvas.ActualWidth;
                if (w <= 0 || _miniTotalSeconds <= 0) return;
                if (_videoSource != null) return;
                if (_player.DurationMs <= 0) return;
                var frac = Math.Clamp(e.GetPosition(MiniTimelineCanvas).X / w, 0, 1);
                _player.Seek(frac * _player.DurationMs / 1000.0);
                UpdateMiniPlayheadX();
            }
            catch { }
        }

        private static double ComputeMiniTotalSeconds(Enhancement enh)
        {
            // Total = max(any region.End, any rule TimeReached time, any
            // haptic event end). Falls back to 60s if nothing has a time
            // (genuinely empty new enhancement).
            double max = 0;
            if (enh.Regions != null)
                foreach (var r in enh.Regions)
                    if (r != null) max = Math.Max(max, r.End);
            if (enh.Rules != null)
                foreach (var rule in enh.Rules)
                    if (rule?.Trigger is TimeReachedTrigger tr) max = Math.Max(max, tr.Time);
            if (enh.HapticTracks != null)
                foreach (var t in enh.HapticTracks)
                    if (t?.Events != null)
                        foreach (var ev in t.Events)
                            if (ev != null) max = Math.Max(max, ev.Start + ev.Duration);
            return max > 0 ? max : 60.0;
        }

        // ====================================================================
        // "Now: [region]" overlay
        // ====================================================================

        private void RefreshNowRegionOverlay()
        {
            try
            {
                if (NowRegionPanel == null) return;
                var enh = _miniEnhancement;
                if (enh?.Regions == null || enh.Regions.Count == 0)
                {
                    NowRegionPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                double currentSec = _videoSource != null
                    ? _videoSource.GetCurrentTimeSeconds()
                    : _player.CurrentTimeMs / 1000.0;
                Region? hit = null;
                foreach (var r in enh.Regions)
                {
                    if (r == null) continue;
                    if (currentSec >= r.Start && currentSec <= r.End) { hit = r; break; }
                }
                if (hit == null)
                {
                    NowRegionPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                TxtNowRegion.Text = string.IsNullOrEmpty(hit.Label) ? hit.Id : hit.Label;
                var brush = ParseHexBrush(hit.Color, fallbackResourceKey: "DeeperAccentBrush");
                NowRegionSwatch.Fill = brush;
                NowRegionPanel.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private static Brush ParseHexBrush(string? hex, string fallbackResourceKey)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(c);
                }
            }
            catch { }
            return (Brush)Application.Current.FindResource(fallbackResourceKey);
        }
    }
}
