using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    // The webcam engine bar's live half: tracking monitor, debug cursor, status pill and counters
    // (WPF MainWindow.LabTab.cs: RefreshWebcamMonitorList :1442, CmbWebcamMonitor_SelectionChanged :1496,
    // ChkWebcamDebugCursor_Changed :1307, UpdateLabTrackerUi :327, EnsureWebcamDebugSubscribed :667).
    public partial class DevicesSettingsSection
    {
        private bool _webcamMonitorPopulating;
        private bool _trackerSubscribed;
        private int _debugBlinks;
        private string _debugFace = "-";
        private GazeSide? _debugGaze;
        private string? _lastStateKey;

        /// <summary>WPF FillMonitorCombo: "Primary" first (survives a monitor reorder), then one row per screen.</summary>
        internal void RefreshWebcamMonitorList()
        {
            _webcamMonitorPopulating = true;
            try
            {
                var saved = CoreSettings.Current.WebcamCalibrationScreen ?? "Primary";
                CmbWebcamMonitor.Items.Clear();
                CmbWebcamMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("webcam_monitor_primary"), Tag = "Primary" });
                int target = 0;
                var all = TopLevel.GetTopLevel(this)?.Screens?.All;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var name = Platform.WebcamScreen.NameOf(all[i], i);
                        CmbWebcamMonitor.Items.Add(new ComboBoxItem
                        {
                            Content = string.Format(Loc.Get("webcam_monitor_item_fmt"), i + 1, name, all[i].Bounds.Width, all[i].Bounds.Height),
                            Tag = name,
                        });
                        if (string.Equals(name, saved, StringComparison.OrdinalIgnoreCase)) target = i + 1;
                    }
                CmbWebcamMonitor.SelectedIndex = target;
            }
            catch (Exception ex) { Log.Debug("Settings/Devices: monitor list failed: {Error}", ex.Message); }
            finally { _webcamMonitorPopulating = false; }
        }

        private void CmbWebcamMonitor_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (CmbWebcamMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not string name) return;
            var s = CoreSettings.Current;
            if (string.Equals(s.WebcamCalibrationScreen, name, StringComparison.OrdinalIgnoreCase)) return;
            s.WebcamCalibrationScreen = name;
            CoreSettings.Save();
            AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
        }

        private void ChkWebcamDebugCursor_Changed(object? sender, RoutedEventArgs e)
        {
            if (ChkWebcamDebugCursor.IsChecked == true)
            {
                GazeDebugCursor.Show("debug-toggle", this);
                AppendWebcamDebugLog("Debug cursor enabled. Tracking must be running + calibrated for the dot to appear.");
            }
            else
            {
                GazeDebugCursor.Hide("debug-toggle");
                AppendWebcamDebugLog("Debug cursor hidden.");
            }
        }

        private void SubscribeTracker()
        {
            if (_trackerSubscribed) return;
            _trackerSubscribed = true;
            var t = Platform.WebcamTracker.Instance;
            t.StateChanged += OnTrackerState;
            t.OnStartupProgress += OnTrackerProgress;
            t.OnFaceFound += OnTrackerFaceFound;
            t.OnFaceLost += OnTrackerFaceLost;
            t.OnBlink += OnTrackerBlink;
            t.OnGazeSide += OnTrackerGazeSide;
            ChkWebcamDebugCursor.IsChecked = GazeDebugCursor.IsShown;
            PaintTrackerState(log: false);
        }

        private void UnsubscribeTracker()
        {
            if (!_trackerSubscribed) return;
            _trackerSubscribed = false;
            var t = Platform.WebcamTracker.Instance;
            t.StateChanged -= OnTrackerState;
            t.OnStartupProgress -= OnTrackerProgress;
            t.OnFaceFound -= OnTrackerFaceFound;
            t.OnFaceLost -= OnTrackerFaceLost;
            t.OnBlink -= OnTrackerBlink;
            t.OnGazeSide -= OnTrackerGazeSide;
        }

        private void OnTrackerState() => PaintTrackerState(log: true);
        private void OnTrackerProgress(double p, string status) => PaintTrackerState(log: false);
        private void OnTrackerFaceFound() { _debugFace = "yes"; PaintCounters(); AppendWebcamDebugLog("Face FOUND"); PaintTrackerState(log: false); }
        private void OnTrackerFaceLost() { _debugFace = "lost"; PaintCounters(); AppendWebcamDebugLog("Face LOST"); PaintTrackerState(log: false); }
        private void OnTrackerBlink() { _debugBlinks++; PaintCounters(); AppendWebcamDebugLog($"Blink #{_debugBlinks}"); }

        // Logged on change only: gaze side fires every frame and would drown the blinks (WPF).
        private void OnTrackerGazeSide(GazeSide side)
        {
            if (_debugGaze == side) return;
            _debugGaze = side;
            PaintCounters();
            AppendWebcamDebugLog($"Gaze → {side}");
        }

        private void PaintCounters() =>
            TxtWebcamDebugCounters.Text = Loc.GetF("webcam_debug_counters", _debugFace, _debugBlinks, _debugGaze is { } g ? g.ToString() : "-");

        /// <summary>WPF UpdateLabTrackerUi + the state half of EnsureWebcamDebugSubscribed: the pill's text
        /// in the user's language, a green dot and border while live, the Start button's label.</summary>
        internal void PaintTrackerState(bool log)
        {
            var t = Platform.WebcamTracker.Instance;
            var key = t.StateKey;
            TxtWebcamDebugStatus.Text = Loc.Get(key);
            bool live = t.IsRunning;
            if (this.TryFindResource(live ? "SuccessGreenBrush" : "TextMutedBrush", out var dot) && dot is IBrush db) LabTrackerDot.Fill = db;
            if (this.TryFindResource(live ? "SuccessGreenBrush" : "PanelAccentBrush", out var edge) && edge is IBrush eb) LabTrackerPill.BorderBrush = eb;
            RefreshWebcamStartLabel();
            if (log && key != _lastStateKey && (key == "rf_webcam_stopped" || key == "rf_webcam_error" || key == "rf_webcam_tracking"))
                AppendWebcamDebugLog($"State → {Loc.Get(key)}");
            _lastStateKey = key;
        }
    }
}
