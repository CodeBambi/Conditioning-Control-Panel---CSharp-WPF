using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "reorderdoors" - the rail rearranges itself. Doors trade places in pairs and stay swapped for ten
/// seconds, then glide back. Nothing is actually reordered: each door rides a TransformLease to the
/// other one's row, which means the rail's own layout, its accordion, its active indicator and every
/// other file that walks NavDoorMap are untouched, and a door is clickable exactly where it LANDS
/// (a RenderTransform carries hit-testing with it) - so the user who reaches for Play and gets Studio
/// pressed the door they were looking at, which is the joke.
/// </summary>
public sealed class ReorderDoorsEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.TabHeader };

    private const double SwapMs = 520;
    private const int StaggerMs = 90;
    private const int MaxPairs = 3;

    private sealed class Mover
    {
        public TransformLease Lease = null!;
        public double Dy;          // design units, the door's own space
    }

    private readonly List<Mover> _movers = new();

    public override string Id => "reorderdoors";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(10);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        NameOverrideText = "the doors";
        return CollectDoors(ctx).Count >= 2;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var doors = CollectDoors(ctx);
        if (doors.Count < 2) return;

        // Top to bottom, so a "pair" is always two doors that sit next to each other in the rail.
        doors.Sort((a, b) => a.Top.CompareTo(b.Top));

        int pairs = Math.Min(MaxPairs, doors.Count / 2);
        for (int p = 0; p < pairs; p++)
        {
            var a = doors[p * 2];
            var b = doors[p * 2 + 1];

            // Bounds are layer pixels; the lease moves the door in DESIGN units, so the gap has to be
            // divided by the door's own design-to-layer scale before it is handed over.
            double sy = PossessionVisual.ScaleOf(ctx.Host, a.Element).Y;
            if (sy <= 0.0001) sy = 1;
            double gap = (b.Top - a.Top) / sy;
            if (Math.Abs(gap) < 2) continue;

            var la = TransformLease.Take(a.Element);
            var lb = TransformLease.Take(b.Element);
            if (la == null || lb == null) continue;

            _movers.Add(new Mover { Lease = la, Dy = gap });
            _movers.Add(new Mover { Lease = lb, Dy = -gap });

            if (!ReferenceEquals(a.Element, Element)) PossessAlso(a.Element);
            if (!ReferenceEquals(b.Element, Element)) PossessAlso(b.Element);
        }

        if (_movers.Count == 0) return;

        for (int i = 0; i < _movers.Count; i++)
        {
            var m = _movers[i];
            PossAnim.To(m.Lease.Translate, TranslateTransform.YProperty, m.Dy, SwapMs, PossAnim.EaseInOut);
            if (StaggerMs > 0 && i % 2 == 1)
            {
                if (!await PossAnim.DelayAsync(StaggerMs, ct).ConfigureAwait(true)) return;
            }
        }

        await PossAnim.DelayAsync(SwapMs + 30, ct).ConfigureAwait(true);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 260, SwapMs);

        try
        {
            foreach (var m in _movers)
            {
                if (ms > 0) PossAnim.To(m.Lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
            }
            if (ms > 0 && _movers.Count > 0)
                await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession reorderdoors undo failed: {Error}", ex.Message); }

        foreach (var m in _movers)
        {
            // Zero first, then hand the element its PRIOR transform back (the lease's own job).
            try { m.Lease.ReleaseImmediate(); } catch { }
        }
        _movers.Clear();
    }

    // ---- the rail ------------------------------------------------------------------------------

    private readonly struct Door
    {
        public Door(FrameworkElement element, double top) { Element = element; Top = top; }
        public FrameworkElement Element { get; }
        public double Top { get; }
    }

    private static List<Door> CollectDoors(PossessionContext ctx)
    {
        var doors = new List<Door>();
        try
        {
            foreach (var t in ctx.Host.Targets)
            {
                if (t.Role != PossessionRole.TabHeader) continue;
                var el = t.Element;
                if (el == null || !el.IsVisible) continue;
                if (TransformLease.IsLeased(el)) continue;   // some other ghost is already driving it
                var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
                if (bounds.IsEmpty || bounds.Height <= 0) continue;
                doors.Add(new Door(el, bounds.Y));
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession reorderdoors walk failed: {Error}", ex.Message); }
        return doors;
    }
}
