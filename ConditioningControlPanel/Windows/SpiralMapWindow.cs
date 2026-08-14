using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;

// NAMESPACE: root, NOT ConditioningControlPanel.Windows, matching all 41 other files in
// this folder. A `ConditioningControlPanel.Windows` namespace shadows the global WinRT
// `Windows` root inside every file in this assembly — ScreenOcrService's Windows.Graphics
// reference stops compiling the moment one exists. Do not "tidy" this into a folder
// namespace.
namespace ConditioningControlPanel
{
    /// <summary>
    /// THE EXPANDED SPIRAL — the same `/embed/spiral` canvas at ?mode=map, in its own
    /// window (CONTRACTS §9: "the 80px rail miniature and the expanded map are the same
    /// canvas", "click-to-expand opens the map in the same surface").
    ///
    /// ITS OWN WINDOW, DELIBERATELY. The map is the large surface, and a large WebView2
    /// inside a tab is the worst version of the airspace problem this app keeps running
    /// into (VatGlassCanvas's header; SettingsTabView's browser card). A top-level HWND
    /// has no airspace problem: nothing clips it, nothing animates over it, and closing
    /// it takes the browser with it. It is also the shape every other hosted page in the
    /// app already uses.
    ///
    /// SINGLETON, AND OPAQUE. One map at a time — a second click focuses the open one
    /// rather than starting a second browser. AllowsTransparency stays false: a WebView2
    /// does not paint inside a layered window, and this app has the render-thread scar
    /// tissue to prove it.
    ///
    /// FAILS SOFT LIKE THE RAIL: if the embed cannot start, the window closes itself
    /// rather than standing there as an empty rectangle. There is no map without the
    /// canvas, and a blank window is worse than no window.
    /// </summary>
    public sealed class SpiralMapWindow : Window
    {
        private static SpiralMapWindow? _open;

        private readonly SpiralEmbedView _embed;

        private SpiralMapWindow()
        {
            Title = "The Spiral";
            Width = 900;
            Height = 700;
            MinWidth = 480;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AllowsTransparency = false;
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));

            _embed = new SpiralEmbedView("map");
            _embed.Failed += (_, _) =>
            {
                try { Close(); } catch { /* already going */ }
            };

            Content = new Grid { Children = { _embed } };

            Loaded += (_, _) =>
            {
                _embed.Start();
                _embed.PostState(App.Descent?.Current);
            };

            // The map is a view of the same block the rail draws; a sync landing while it
            // is open should move the dot rather than wait for a reopen.
            if (App.Descent != null) App.Descent.BlockChanged += OnBlockChanged;

            Closed += (_, _) =>
            {
                if (App.Descent != null) App.Descent.BlockChanged -= OnBlockChanged;
                _embed.Dispose();
                if (ReferenceEquals(_open, this)) _open = null;
            };
        }

        private void OnBlockChanged(object? sender, EventArgs e)
        {
            // DescentService already marshalled to the UI thread before raising.
            try { _embed.PostState(App.Descent?.Current); }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] map state: {E}", ex.Message); }
        }

        /// <summary>Open the map, or focus the one already open.</summary>
        public static void ShowMap()
        {
            try
            {
                if (_open != null)
                {
                    _open.Activate();
                    return;
                }

                var w = new SpiralMapWindow();
                var main = Application.Current?.MainWindow;
                if (main != null && !ReferenceEquals(main, w)) w.Owner = main;
                _open = w;
                w.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Spiral] ShowMap: {E}", ex.Message);
            }
        }
    }
}
