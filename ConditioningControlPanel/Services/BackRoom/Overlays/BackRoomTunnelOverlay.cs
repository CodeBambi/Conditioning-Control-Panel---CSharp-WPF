using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.BackRoom.Overlays;

/// <summary>
/// Hypno v3 <c>tunnel</c> (CONTRACT 10.13.B): tunnel vision, one radial vignette per screen, centred on that
/// screen. The edges darken toward <c>rgba(6,3,12)</c> while the centre always stays clear. The wanted level
/// comes from <c>fx-tunnel</c> (already gated, halved under Calm and throttled by the dispatcher);
/// <see cref="TunnelModel"/> eases toward it and lets go by itself 1500 ms after the last update.
/// </summary>
internal sealed class BackRoomTunnelOverlay : BackRoomOverlayWindow
{
    private static BackRoomTunnelOverlay? _instance;
    private static readonly Color Ink = Color.FromRgb(6, 3, 12);

    private sealed class Vignette
    {
        public PxRect Local;
        public readonly Rectangle Rect = new() { IsHitTestVisible = false };
        public readonly GradientStop Clear = new(Color.FromArgb(0, 6, 3, 12), 0.9);
        public readonly GradientStop Dark = new(Color.FromArgb(0, 6, 3, 12), 1.0);
        public readonly RadialGradientBrush Brush;

        public Vignette()
        {
            Brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Pad };
            Brush.GradientStops.Add(Clear);
            Brush.GradientStops.Add(Dark);
            Rect.Fill = Brush;
        }
    }

    private readonly TunnelModel _model = new();
    private readonly List<Vignette> _screens = new();
    private bool _still;

    private BackRoomTunnelOverlay() : base(frameMs: 33, zRank: 3) { }

    public static void Set(double level, bool still) => OnUi(() =>
    {
        // Opening a surface just to draw nothing is not worth a layered window.
        if (level <= 0 && (_instance == null || _instance._model.Idle)) return;
        if (!MayCreate(_instance)) return;
        (_instance ??= new BackRoomTunnelOverlay()).Want(level, still);
    });

    /// <summary>Gone at once (suspend, close, exit, that station's station-close).</summary>
    public static void Cancel() => OnUi(() =>
    {
        if (_instance is not { } w) return;
        w._model.Cancel();
        w.Paint(0);
        w.Sleep();
    });

    private void Want(double level, bool still)
    {
        _still = still;
        _model.Set(level, Environment.TickCount64);
        Wake();   // first, so the vignettes are laid out in this window's own DPI
        if (_screens.Count == 0) Layout();
    }

    protected override void OnRescaled()
    {
        if (_screens.Count == 0) return;
        Layout();
        Paint(_model.Level);
    }

    /// <summary>One vignette per monitor, laid out when the tunnel first opens after a quiet spell (the
    /// monitors cannot change under a running tunnel without a display change, which re-lays next time).</summary>
    private void Layout()
    {
        Stage.Children.Clear();
        _screens.Clear();
        foreach (var s in BackRoomOverlayScreens.All())
        {
            var v = new Vignette { Local = Local(s.BoundsPx) };
            Canvas.SetLeft(v.Rect, v.Local.X);
            Canvas.SetTop(v.Rect, v.Local.Y);
            v.Rect.Width = v.Local.W;
            v.Rect.Height = v.Local.H;
            v.Brush.Center = v.Brush.GradientOrigin = new System.Windows.Point(v.Local.W / 2, v.Local.H / 2);
            _screens.Add(v);
            Stage.Children.Add(v.Rect);
        }
    }

    protected override bool Frame(long nowMs, long dtMs)
    {
        double level = _model.Step(nowMs, dtMs, _still);
        Paint(level);
        return !_model.Idle;
    }

    private void Paint(double level)
    {
        foreach (var v in _screens)
        {
            if (level < TunnelModel.Epsilon) { v.Rect.Opacity = 0; continue; }
            var shape = BackRoomOverlayMath.Tunnel(v.Local.W, v.Local.H, level);
            v.Brush.RadiusX = v.Brush.RadiusY = shape.Outer;
            v.Clear.Offset = shape.Outer > 0 ? Math.Clamp(shape.Inner / shape.Outer, 0, 1) : 0;
            v.Dark.Color = Color.FromArgb((byte)Math.Round(255 * shape.Alpha), Ink.R, Ink.G, Ink.B);
            v.Rect.Opacity = 1;
        }
    }

    protected override void OnIdleHidden()
    {
        // Re-read the monitors next time: a display change while hidden must not leave stale vignettes.
        Stage.Children.Clear();
        _screens.Clear();
    }
}
