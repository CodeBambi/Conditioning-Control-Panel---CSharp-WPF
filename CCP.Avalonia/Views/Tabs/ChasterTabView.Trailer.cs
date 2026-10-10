// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: the trailer (:1314-1397).
// One popup, re-aimed at whichever row is hovered: the row's picture, its flavour line, its tier
// sign, the plain explanation with the live figure in it, and the figure itself. It opens 150 ms
// after the pointer settles on a row, stays while the pointer is on the card, and closes 140 ms
// after it leaves both. It sits BELOW the row (a popup over its own trigger flickers).
// The picture drifts and pans while the card is up, and the app's own floating figure lifts off the plate
// every 1.7 s (FxTrailerStart, ChasterTabView.Fx.cs). The printed figure under it stays (this head's own).
// not ported: the WPF vignette scene (ChasterTrailerView, a WebView2 page); the picture is the scene,
// as WPF shows it without a browser.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private static readonly TimeSpan TrailerOpenDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan TrailerCloseGrace = TimeSpan.FromMilliseconds(140);

        private readonly DispatcherTimer _trailerOpen = new() { Interval = TrailerOpenDelay };
        private readonly DispatcherTimer _trailerClose = new() { Interval = TrailerCloseGrace };
        private ToggleButton? _trailerRow;
        private bool _trailerShown;
        private bool _overTrailer;

        private void TrailerInit()
        {
            _trailerOpen.Tick += (_, _) => { _trailerOpen.Stop(); OpenTrailer(); };
            _trailerClose.Tick += (_, _) => { _trailerClose.Stop(); if (!_overTrailer) HideTrailer(); };
            // WPF (:99): a page that leaves the screen takes its trailer with it.
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) HideTrailer(); };
            DetachedFromVisualTree += (_, _) => HideTrailer();
        }

        private void Row_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is not ToggleButton row) return;
            _trailerClose.Stop();
            _trailerRow = row;
            if (_trailerShown) OpenTrailer();
            else { _trailerOpen.Stop(); _trailerOpen.Start(); }
        }

        private void Row_PointerExited(object? sender, PointerEventArgs e)
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        private void Trailer_PointerEntered(object? sender, PointerEventArgs e)
        {
            _overTrailer = true;
            _trailerClose.Stop();
        }

        private void Trailer_PointerExited(object? sender, PointerEventArgs e)
        {
            _overTrailer = false;
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        /// <summary>The id the trailer is aimed at while it is up, for the tests. Null when closed.
        /// Kept apart from the popup's own IsOpen: without a window behind it a Popup may refuse
        /// to open, and the dressing must still be right.</summary>
        internal string? TrailerId => _trailerShown ? _trailerRow?.Tag as string : null;

        /// <summary>Aim the one popup at the hovered row and dress it for that row's price.</summary>
        internal void OpenTrailer(ToggleButton? row = null)
        {
            row ??= _trailerRow;
            if (row?.Tag is not string id || TabPrices.Find(id) is not { } price) return;
            _trailerRow = row;
            try
            {
                TrailerArt.Source = TabMenuCopy.ArtFor(id) is { } art ? Helpers.ModArt.TryLoad(art, 640) : null;
                TxtTrailerFlavour.Text = Loc.Get(TabMenuCopy.FlavourKey(id));
                TxtTrailerWhy.Text = TabMenuCopy.Why(id, Loc.Get, ShownSeconds(id));
                var tier = TabMenuCopy.BadgeTier(price.Gate);
                TrailerBadgeHost.Child = tier > 0 ? new TierBadge { Tier = tier, MaxWidthOverride = 64 } : null;
                // The figure the row books right now, in its board's colour; rows with no one figure print none.
                var shown = TabPriceEdit.Shown(price, Overrides);
                TxtTrailerFigure.Text = TabMenuCopy.HasFixedFigure(id) ? TabPageText.Price(shown, Loc.Get("chaster_each")) : "";
                TxtTrailerFigure.Foreground = new SolidColorBrush(shown.Seconds > 0 ? CostColour : EarnColour);
                Trailer.PlacementTarget = row;
                _trailerShown = true;
                FxTrailerStart(shown);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer"); }
            try { Trailer.IsOpen = true; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer open"); }
        }

        internal void HideTrailer()
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _overTrailer = false;
            if (!_trailerShown) return;
            _trailerShown = false;
            FxTrailerStop();
            try { Trailer.IsOpen = false; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer close"); }
        }
    }
}
