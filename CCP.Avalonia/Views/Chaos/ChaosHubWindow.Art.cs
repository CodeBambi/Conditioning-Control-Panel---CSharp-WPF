// PORTED from WPF 7.1.5 ConditioningControlPanel/Chaos/ChaosHubWindow.xaml.cs: the hub's real art.
//   ArtIcon (:1434), the upgrade / boon row icons (:398, :583), the loadout tiles' art and keyhole
//   (LoadoutTile, TileUnknownArt :940), the how-to card image (:1904), and the menu flipbook
//   (LoadMenuFrames :2032, BuildFlipSeq, StartFlipbook, AdvanceFlip, CrossfadeTo, MenuArt_Click).
//
// Every slot asks ChaosArt first and falls back to what this head drew before (a glyph, a collapsed
// box, the gradient), so a build with no art folder looks exactly as it did.
//
// The flipbook is WPF's plain path (two stacked pictures, the top one fading in over 550 ms), not its
// Skia scene: the per-frame fx masks, the fog and the logo wordmark (LoadMenuFx, MenuLogoFx) are not
// ported. It runs only while the menu's art panel is on screen and ambient loops are allowed; each
// step is a one-shot timer plus a finite opacity tween clamped to 0..1, so nothing loops unattended.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    public partial class ChaosHubWindow
    {
        // ---- icons --------------------------------------------------------------------------

        /// <summary>WPF ArtIcon: the picture, clipped to the rounded square, under an accent ring.</summary>
        private static Border ArtIcon(Bitmap art, double size, double radius, Color accent, double ring = 2.5) => new()
        {
            Width = size, Height = size,
            Child = new Grid
            {
                Clip = new RectangleGeometry(new Rect(0, 0, size, size)) { RadiusX = radius, RadiusY = radius },
                Children =
                {
                    new Image { Source = art, Stretch = Stretch.UniformToFill },
                    new Border
                    {
                        CornerRadius = new CornerRadius(radius),
                        BorderBrush = new SolidColorBrush(accent),
                        BorderThickness = new Thickness(ring),
                        IsHitTestVisible = false,
                    },
                }
            },
        };

        /// <summary>Real art if the slot has it, else the tinted glyph square; same box either way.</summary>
        private static Border IconOrGlyph(Bitmap? art, string glyph, double size, double radius, Color accent, double opacity)
        {
            if (art == null) return GlyphIcon(glyph, size, radius, accent, opacity);
            var icon = ArtIcon(art, size, radius, accent, ring: 3);
            icon.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.Opacity = opacity;
            return icon;
        }

        private Bitmap? _tileUnknownArt;
        private bool _tileUnknownTried;

        /// <summary>WPF TileUnknownArt: the stitched keyhole a mystery pad wears (hub/tile_unknown.png).</summary>
        private Bitmap? TileUnknownArt
        {
            get
            {
                if (!_tileUnknownTried) { _tileUnknownTried = true; _tileUnknownArt = ChaosArt.Resolve("hub", "tile_unknown"); }
                return _tileUnknownArt;
            }
        }

        // ---- how-to card --------------------------------------------------------------------

        /// <summary>True while the how-to card shows a picture (tests).</summary>
        internal bool HowToImageShown => _howToImageBox.IsVisible;

        private void PaintHowToImage(string? image)
        {
            Bitmap? art = null;
            try { art = string.IsNullOrEmpty(image) ? null : ChaosArt.Resolve("howto", image); }
            catch (Exception ex) { Log.Debug("ChaosHub how-to art: {E}", ex.Message); }
            if (this.FindControl<Border>("HowToImageArt") is { } host)
                host.Background = art == null ? null : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            _howToImageBox.IsVisible = art != null;
        }

        // ---- the menu flipbook --------------------------------------------------------------

        private Bitmap?[]? _menuFrames;                       // 0 idle, 1 blink, 2 invite, 3 kiss, 4 wink, 5 hair-tuck
        private (int Frame, int HoldMs)[] _flipSeq = Array.Empty<(int, int)>();
        private int _seqPos, _shownFrame = -1, _flipGen;
        private bool _flipRunning, _menuArtHooked;
        private DispatcherTimer? _fadeTween;
        private long _clickReadyAtMs;
        private const int MenuFadeMs = 550;

        /// <summary>Frames loaded for the flipbook, or 0 when a still (or nothing) shows (tests).</summary>
        internal int MenuFlipFrames { get; private set; }
        /// <summary>True while the flipbook is stepping (tests).</summary>
        internal bool MenuFlipRunning => _flipRunning;
        /// <summary>The frame index showing, or -1 for a still (tests).</summary>
        internal int MenuFrameShown => _shownFrame;

        private Border? MenuArtBase => this.FindControl<Border>("MenuArtBaseBox");
        private Border? MenuArtTop => this.FindControl<Border>("MenuArtTopBox");

        private static IBrush Fill(Bitmap art, Stretch stretch = Stretch.UniformToFill) => new ImageBrush(art) { Stretch = stretch };

        /// <summary>WPF LoadMenuFrames: the flipbook if menu_1/2/3.png are all present, else a single
        /// still (menu.png, then the banner). Shows the first frame.</summary>
        private void LoadMenuFrames()
        {
            try
            {
                var all = new Bitmap?[6];
                for (int i = 0; i < 6; i++) all[i] = ChaosArt.ResolveMenuFrame(i + 1);
                if (MenuArtBase is not { } baseBox) return;

                if (all[0] != null && all[1] != null && all[2] != null)   // core 3 must exist
                {
                    _menuFrames = all;
                    _flipSeq = BuildFlipSeq();
                    _shownFrame = 0;
                    _seqPos = 0;
                    MenuFlipFrames = 0;
                    foreach (var f in all) if (f != null) MenuFlipFrames++;
                    baseBox.Background = Fill(all[0]!);
                }
                else if (ChaosArt.ResolveMenu() is { } still) baseBox.Background = Fill(still);
                else if (ChaosArt.ResolveBanner() is { } banner) baseBox.Background = Fill(banner, Stretch.Uniform);

                if (_menuArtHooked) return;
                _menuArtHooked = true;
                // The flipbook follows the art panel: on screen it steps, hidden it rests.
                _menuArtPanel.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncFlipbook(); };
                _menuView.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncFlipbook(); };
                Action onMotion = () => Dispatcher.UIThread.Post(SyncFlipbook);
                AmbientFxCanvas.Env.MotionGateChanged += onMotion;
                Opened += (_, _) => SyncFlipbook();
                Closed += (_, _) => { AmbientFxCanvas.Env.MotionGateChanged -= onMotion; StopFlipbook(); };
                SyncFlipbook();
            }
            catch (Exception ex) { Log.Debug("ChaosHub menu art: {E}", ex.Message); }
        }

        /// <summary>WPF BuildFlipSeq: settle on idle between each expression so they read as momentary;
        /// a frame missing from disk is skipped.</summary>
        private (int, int)[] BuildFlipSeq()
        {
            const int Idle = 6000;
            var seq = new List<(int, int)>();
            void Add(int idx, int hold) { if (_menuFrames != null && idx < _menuFrames.Length && _menuFrames[idx] != null) seq.Add((idx, hold)); }
            Add(0, Idle); Add(1, 1900);   // idle, blink
            Add(0, Idle); Add(4, 2300);   // idle, wink
            Add(0, Idle); Add(2, 3000);   // idle, invite
            Add(0, Idle); Add(3, 2700);   // idle, kiss
            Add(0, Idle); Add(5, 2500);   // idle, hair-tuck
            if (seq.Count == 0) seq.Add((0, Idle));
            return seq.ToArray();
        }

        private void SyncFlipbook()
        {
            bool want = _menuFrames != null && _flipSeq.Length > 1 && IsVisible
                && _menuView.IsVisible && _menuArtPanel.IsVisible && AmbientFxCanvas.Env.AllowAmbientLoops;
            if (want == _flipRunning) return;
            if (want) StartFlipbook(); else StopFlipbook();
        }

        private void StartFlipbook()
        {
            _flipRunning = true;
            ArmFlip(++_flipGen, _flipSeq[_seqPos].HoldMs);
        }

        /// <summary>WPF StopFlipbook, plus the rest pose: a fade caught half way lands on its frame.</summary>
        private void StopFlipbook()
        {
            _flipRunning = false;
            _flipGen++;
            _fadeTween?.Stop();
            _fadeTween = null;
            if (MenuArtTop is { } top) top.Opacity = 0;
            if (_menuFrames != null && _shownFrame >= 0 && _menuFrames[_shownFrame] is { } shown && MenuArtBase is { } baseBox)
                baseBox.Background = Fill(shown);
        }

        private void ArmFlip(int gen, int holdMs) => DispatcherTimer.RunOnce(() =>
        {
            if (gen != _flipGen || !_flipRunning) return;
            AdvanceFlip();
            ArmFlip(gen, _flipSeq[_seqPos].HoldMs);
        }, TimeSpan.FromMilliseconds(Math.Max(1, holdMs)));

        private void AdvanceFlip()
        {
            if (_menuFrames == null || _flipSeq.Length == 0) return;
            _seqPos = (_seqPos + 1) % _flipSeq.Length;
            CrossfadeTo(_flipSeq[_seqPos].Frame);
        }

        /// <summary>WPF CrossfadeTo: the new frame fades in on the top layer (sine in-out), then
        /// becomes the base and the top layer goes clear again.</summary>
        private void CrossfadeTo(int idx)
        {
            if (_menuFrames == null || MenuArtTop is not { } top || MenuArtBase is not { } baseBox) return;
            idx = ((idx % _menuFrames.Length) + _menuFrames.Length) % _menuFrames.Length;
            var src = _menuFrames[idx];
            if (src == null || idx == _shownFrame) return;
            _shownFrame = idx;

            _fadeTween?.Stop();
            top.Background = Fill(src);
            var tween = _fadeTween = TransformTween.Run(top, TimeSpan.FromMilliseconds(MenuFadeMs), new (double, AvaloniaProperty, double)[]
            {
                (0, OpacityProperty, 0.0), (1, OpacityProperty, 1.0),
            }, new SineEaseInOut());
            DispatcherTimer.RunOnce(() =>
            {
                if (!ReferenceEquals(tween, _fadeTween) || _shownFrame != idx) return;   // superseded or stopped
                baseBox.Background = Fill(src);
                top.Opacity = 0;
            }, TimeSpan.FromMilliseconds(MenuFadeMs + 20));
        }

        /// <summary>WPF MenuArt_Click: a click asks for the next pose, once the current one has played out.</summary>
        private void MenuArtClicked()
        {
            if (_menuFrames == null || !_flipRunning) return;
            long now = Environment.TickCount64;
            if (now < _clickReadyAtMs) return;   // still cooling down
            AdvanceFlip();
            int hold = _flipSeq.Length > 0 ? _flipSeq[_seqPos].HoldMs : 1000;
            _clickReadyAtMs = now + MenuFadeMs + hold + MenuFadeMs;
            ArmFlip(++_flipGen, hold);           // restart the hold from this pose
        }
    }
}
