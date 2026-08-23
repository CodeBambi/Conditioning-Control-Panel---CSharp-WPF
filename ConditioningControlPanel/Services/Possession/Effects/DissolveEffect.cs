using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "dissolve" - the toggle crumbles to ash under your cursor. A snapshot of the control breaks into
/// 6x3 tiles that scatter with gravity, ember-tinted so the debris is obviously Possession's, while the
/// REAL control sits underneath at Opacity 0 and still takes your click. A beat and a half later the
/// ash flies back together and the control is simply there again. Twice per haunt, no more.
/// </summary>
public sealed class DissolveEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Toggle, PossessionRole.Button };

    private const int Cols = 6;
    private const int Rows = 3;
    private const double ScatterMs = 900;
    private const double GapMs = 1600;
    private const double ReformMs = 600;
    private const int MaxCrumbles = 2;

    private FrameworkElement? _el;
    private MouseEventHandler? _enter;
    private Ghost? _ghost;
    private int _crumbles;
    private bool _busy;

    public override string Id => "dissolve";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(20);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>Charged on the first hover, so the ember fires as the control starts to come apart.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
        => ctx.Host.GhostLayer != null && target?.Element != null;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _el = target?.Element;
        if (_el == null) return Task.CompletedTask;

        _enter = (_, __) => { _ = CrumbleAsync(); };
        _el.MouseEnter += _enter;
        try { if (_el.IsMouseOver) _ = CrumbleAsync(); } catch { }
        return Task.CompletedTask;
    }

    private async Task CrumbleAsync()
    {
        if (_busy || _crumbles >= MaxCrumbles) return;
        _busy = true;
        try
        {
            var ctx = Ctx;
            var el = _el;
            if (ctx == null || el == null) return;
            var ct = Cts?.Token ?? CancellationToken.None;

            await ChargeAndPossessAsync(el, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            var ghost = Ghost.Capture(el, ctx.Host);
            if (ghost == null) return;
            _ghost = ghost;
            _crumbles++;

            ghost.Hide();   // opacity only: the toggle you are watching turn to ash still toggles
            var tiles = ghost.ExplodeIntoTiles(Cols, Rows);
            if (tiles.Count == 0) { ghost.Dispose(); _ghost = null; return; }

            var moves = new List<(TranslateTransform Tr, RotateTransform Rot, Image Tile, Rectangle Plate)>(tiles.Count);
            foreach (var tile in tiles)
            {
                var tr = new TranslateTransform();
                var rot = new RotateTransform();
                var group = new TransformGroup();
                group.Children.Add(rot);
                group.Children.Add(tr);
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
                tile.RenderTransform = group;

                // Ember plate: same size, same seat, SAME transform object, so the tint rides the tile.
                var plate = new Rectangle
                {
                    Width = tile.Width,
                    Height = tile.Height,
                    Fill = EmberBrush,
                    Opacity = 0,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = group
                };
                ghost.AddExtra(plate, Canvas.GetLeft(tile), Canvas.GetTop(tile));
                moves.Add((tr, rot, tile, plate));
            }

            foreach (var m in moves)
            {
                double dx = Amp(Rand(20, 60)) * Sign();
                double dy = Amp(Rand(20, 60));
                PossAnim.To(m.Tr, TranslateTransform.XProperty, dx, ScatterMs, PossAnim.EaseOut);
                PossAnim.To(m.Tr, TranslateTransform.YProperty, dy, ScatterMs, PossAnim.Gravity);
                PossAnim.To(m.Rot, RotateTransform.AngleProperty, Amp(25) * Sign(), ScatterMs, PossAnim.EaseOut);
                PossAnim.To(m.Tile, UIElement.OpacityProperty, 0.08, ScatterMs, PossAnim.EaseIn);
                PossAnim.Pulse(m.Plate, UIElement.OpacityProperty, 0.45, ScatterMs * 0.25, ScatterMs * 0.75);
            }

            if (!await PossAnim.DelayAsync(ScatterMs + GapMs, ct).ConfigureAwait(true)) return;
            if (!ReferenceEquals(_ghost, ghost)) return;

            foreach (var m in moves)
            {
                PossAnim.To(m.Tr, TranslateTransform.XProperty, 0, ReformMs, PossAnim.EaseOut);
                PossAnim.To(m.Tr, TranslateTransform.YProperty, 0, ReformMs, PossAnim.EaseOut);
                PossAnim.To(m.Rot, RotateTransform.AngleProperty, 0, ReformMs, PossAnim.EaseOut);
                PossAnim.To(m.Tile, UIElement.OpacityProperty, 1, ReformMs, PossAnim.EaseOut);
            }

            if (!await PossAnim.DelayAsync(ReformMs + 40, ct).ConfigureAwait(true)) return;

            ghost.Dispose();
            if (ReferenceEquals(_ghost, ghost)) _ghost = null;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession dissolve failed: {Error}", ex.Message); }
        finally
        {
            _busy = false;
            // Whatever happened, never leave the real control invisible behind stale ash.
            if (Cts?.IsCancellationRequested == true) { try { _ghost?.Dispose(); } catch { } _ghost = null; }
        }
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_el != null && _enter != null) _el.MouseEnter -= _enter;
        }
        catch { }
        _enter = null;

        try { _ghost?.Dispose(); } catch { }
        _ghost = null;
        _el = null;
        _crumbles = 0;
        _busy = false;
        return Task.CompletedTask;
    }
}
