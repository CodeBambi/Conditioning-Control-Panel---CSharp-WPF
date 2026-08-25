using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "stealcard" - the tube takes an option away. The warden glides beside a card that actually DOES
/// something (a card with at least one enabled control inside it), holds a beat so the theft has an
/// author, and then the card shrinks, flies into the tube and is gone. A snapshot flies; the real card
/// sits at Opacity 0 with hit-testing off, so the option is really unavailable for the whole two
/// minutes. On the way back the tube spits it out: the ghost flies from the tube, grows into its seat
/// and the real card comes back exactly as it was.
///
/// <para><b>Which card.</b> An inert card is not an option, so stealing one is just a hole in the
/// layout. This picks a card that contains a visible, enabled, interactive control, and it refuses the
/// Lockdown card itself, anything carrying (or inside anything carrying) <c>Possession.Exclude</c> -
/// which is where the Emergency Exit button and the secret exit box live - and any card it has already
/// taken this lockdown.</para>
///
/// <para><b>Why the tube may not come.</b> The warden's gates are mirrored rather than shared: the tube
/// is a user-visible actor the bark system and the bubble egg also move, and a fullscreen video makes
/// gliding it both invisible and a render-thread hazard. When it cannot come, the card is still taken -
/// it just implodes where it stands instead of flying to a face.</para>
/// </summary>
public sealed class StealCardEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Card };

    private const double BeatMs = 500;
    private const double SuckMs = 700;
    private const double SpitMs = 650;
    private const double TinyScale = 0.06;

    /// <summary>Cards already taken this lockdown. Cleared on the next activation, so a long session
    /// cannot lose the same option twice while a short one still has cards left to take.</summary>
    private readonly HashSet<string> _stolen = new(StringComparer.Ordinal);
    private bool _hookedActivation;

    private Ghost? _ghost;
    private TranslateTransform? _tr;
    private ScaleTransform? _scale;
    private Point _flightTarget;     // layer space, where the tube was standing
    private bool _flew;

    /// <summary>True while this effect holds the warden's tube lease (see <see cref="TakeTube"/>).</summary>
    private bool _holdsTube;

    public override string Id => "stealcard";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(120);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        HookActivationReset();

        var el = target?.Element;
        if (el == null || ctx.Host.GhostLayer == null) return false;
        if (PossessionVisual.IsWindowChrome(el)) return false;

        // The warden's own rule, and it applies to the theft whether or not the tube shows up: a
        // mandatory video owns the screen, and an option vanishing behind it is a bug report.
        try { if (App.Video?.IsPlaying == true) return false; } catch { }

        if (target != null && _stolen.Contains(target.Key)) return false;
        if (IsOffLimits(el)) return false;
        if (!HasLiveOption(el, 0)) return false;

        var bounds = PossessionVisual.BoundsOf(ctx.Host, el);
        if (bounds.IsEmpty || bounds.Width < 40 || bounds.Height < 24) return false;

        // A thing that spans the room is a RAIL, not a card. The window's bottom action bar tags as a
        // card (it is a Border full of buttons) and taking it removes START, Save and the app's own
        // Exit in one grab: five options at once, and a hole big enough to read as the app breaking.
        // The owner asked for one option gone, so anything this wide is left alone.
        var layer = ctx.Host.GhostLayer;
        double layerW = layer?.ActualWidth ?? 0;
        double layerH = layer?.ActualHeight ?? 0;
        if (layerW > 0 && bounds.Width > layerW * 0.8) return false;
        if (layerH > 0 && bounds.Height > layerH * 0.7) return false;

        return true;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        if (el == null) return;

        var ghost = Ghost.Capture(el, ctx.Host);
        if (ghost == null) return;
        _ghost = ghost;
        if (target != null) _stolen.Add(target.Key);

        // Hit-testing off as well as invisible: the whole point is that the option is GONE, not that
        // it is invisible and still clickable by muscle memory. Ghost.Restore puts both back.
        ghost.Hide(alsoDisableHitTesting: true);

        var visual = ghost.Visual;
        if (visual == null) return;

        _tr = new TranslateTransform();
        _scale = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_tr);
        visual.RenderTransformOrigin = new Point(0.5, 0.5);
        visual.RenderTransform = group;

        // Where it flies to: the tube if it comes, else its own centre (a plain implosion).
        var centre = new Point(ghost.Origin.X + ghost.SizeDip.Width / 2.0,
                               ghost.Origin.Y + ghost.SizeDip.Height / 2.0);
        _flightTarget = centre;

        var tube = TakeTube();
        try
        {
            if (tube != null)
            {
                _flew = await GlideBesideAsync(tube, el, ct).ConfigureAwait(true);
                if (_flew)
                {
                    if (!await PossAnim.DelayAsync(BeatMs, ct).ConfigureAwait(true)) return;
                    var mouth = TubeMouthInLayer(ctx);
                    if (mouth.HasValue) _flightTarget = mouth.Value;
                }
            }

            double dx = _flightTarget.X - centre.X;
            double dy = _flightTarget.Y - centre.Y;

            PossAnim.To(_scale, ScaleTransform.ScaleXProperty, TinyScale, SuckMs, PossAnim.EaseIn);
            PossAnim.To(_scale, ScaleTransform.ScaleYProperty, TinyScale, SuckMs, PossAnim.EaseIn);
            PossAnim.To(_tr, TranslateTransform.XProperty, dx, SuckMs, PossAnim.EaseIn);
            PossAnim.To(_tr, TranslateTransform.YProperty, dy, SuckMs, PossAnim.EaseIn);
            PossAnim.To(visual, UIElement.OpacityProperty, 0, SuckMs, PossAnim.EaseIn);
            await PossAnim.DelayAsync(SuckMs + 30, ct).ConfigureAwait(true);

            // The tube got what it came for; send it home rather than leaving it parked for two minutes.
            if (_flew) SendTubeHome();
        }
        finally
        {
            // Every early return above (cancelled beat, cancelled suck) must give the tube back, or the
            // warden's busy flag stays raised for the rest of the lockdown and it never knocks again.
            ReleaseTube();
        }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 260, SpitMs);
        var ghost = _ghost;
        var visual = ghost?.Visual;

        try
        {
            if (visual != null && ms > 0 && _tr != null && _scale != null)
            {
                // It comes back out of wherever it went in, at the size it went in at, and grows.
                PossAnim.To(visual, UIElement.OpacityProperty, 1, ms * 0.4, PossAnim.EaseOut);
                PossAnim.To(_scale, ScaleTransform.ScaleXProperty, 1, ms, PossAnim.EaseOut);
                PossAnim.To(_scale, ScaleTransform.ScaleYProperty, 1, ms, PossAnim.EaseOut);
                PossAnim.To(_tr, TranslateTransform.XProperty, 0, ms, PossAnim.EaseOut);
                PossAnim.To(_tr, TranslateTransform.YProperty, 0, ms, PossAnim.EaseOut);
                await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession stealcard return failed: {Error}", ex.Message); }

        // Dispose restores the real card's ORIGINAL opacity and hit-testing and sweeps the snapshot.
        try { ghost?.Dispose(); }
        catch (Exception ex) { App.Logger?.Warning("Possession stealcard restore failed: {Error}", ex.Message); }

        // Belt and braces - the apply already sent it home. Only re-send when the tube is actually
        // free: SendTubeHome CLEARS the tube's captured home, so doing it during a warden knock or
        // leave strands the tube wherever that verb left it.
        if (_flew && TakeTube() != null) SendTubeHome();

        _ghost = null;
        _tr = null;
        _scale = null;
        _flew = false;
    }

    // ---- picking a victim that is actually an option -------------------------------------------

    /// <summary>True when the subtree holds a visible, enabled control the user could actually use.
    /// A card of labels is decoration; taking it removes nothing.</summary>
    private static bool HasLiveOption(DependencyObject node, int depth)
    {
        if (node == null || depth > 16) return false;
        try
        {
            if (node is FrameworkElement fe)
            {
                if (!fe.IsVisible) return false;
                if (Possession.GetExclude(fe)) return false;

                bool interactive = fe is ButtonBase || fe is Slider || fe is ComboBox
                                   || fe is TextBoxBase || fe is ListBoxItem;
                if (interactive && fe.IsEnabled && fe.ActualWidth > 0 && fe.ActualHeight > 0
                    && !PossessionVisual.IsWindowChrome(fe))
                    return true;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                if (HasLiveOption(VisualTreeHelper.GetChild(node, i), depth + 1)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>The cards nothing may take: the Lockdown card (it is the surface that explains what is
    /// happening), anything excluded by hand (Emergency Exit, the secret exit box, the badge) and
    /// anything sitting inside something excluded.</summary>
    private static bool IsOffLimits(FrameworkElement el)
    {
        try
        {
            if (Possession.GetExclude(el)) return true;
            if (ContainsExcluded(el, 0)) return true;

            for (DependencyObject? node = el; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement fe) continue;
                if (Possession.GetExclude(fe)) return true;
                var n = fe.Name;
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("lockdown", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("emergency", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        catch { return true; }   // a card we cannot reason about is a card we do not take
        return false;
    }

    private static bool ContainsExcluded(DependencyObject node, int depth)
    {
        if (node == null || depth > 16) return false;
        try
        {
            if (depth > 0 && node is FrameworkElement fe && Possession.GetExclude(fe)) return true;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                if (ContainsExcluded(VisualTreeHelper.GetChild(node, i), depth + 1)) return true;
        }
        catch { }
        return false;
    }

    private void HookActivationReset()
    {
        if (_hookedActivation) return;
        try
        {
            var lockdown = App.Lockdown;
            if (lockdown == null) return;
            _hookedActivation = true;
            lockdown.LockdownActivated += () => { try { _stolen.Clear(); } catch { } };
        }
        catch { }
    }

    // ---- the tube ------------------------------------------------------------------------------

    /// <summary>The warden's gates, mirrored (Warden.IsAvailable is the same list) PLUS the warden's
    /// own busy flag, taken as a lease. Null means the theft happens without a thief on screen.
    ///
    /// <para><b>The lease is the whole coordination mechanism.</b> A theft and a warden verb both move
    /// the tube and both rely on <c>AvatarTubeWindow</c>'s single captured "home"; whichever one calls
    /// ReturnHomeAsync first clears it and strands the other. Taking the same <c>_busy</c> flag the
    /// verbs take means only one of them is ever moving the tube, so the capture bookkeeping has
    /// exactly one owner. Also honours the warden's cooldown-free availability gates.</para></summary>
    private AvatarTubeWindow? TakeTube()
    {
        try
        {
            if (_holdsTube) return App.AvatarWindow;
            if (App.Settings?.Current?.LockdownWardenEnabled != true) return null;
            var tube = App.AvatarWindow;
            if (tube == null || !tube.CanPerformPossessionMove) return null;
            if (App.Video?.IsPlaying == true) return null;

            var warden = App.Possession?.Warden as Warden;
            if (warden != null && !warden.TryTakeTube()) return null;   // a knock / stare / leave owns it
            _holdsTube = true;
            return tube;
        }
        catch { return null; }
    }

    /// <summary>Give the tube back to the warden. Safe when the lease was never taken.</summary>
    private void ReleaseTube()
    {
        if (!_holdsTube) return;
        _holdsTube = false;
        try { (App.Possession?.Warden as Warden)?.ReleaseTube(); } catch { }
    }

    private static async Task<bool> GlideBesideAsync(AvatarTubeWindow tube, FrameworkElement el,
                                                     CancellationToken ct)
    {
        try
        {
            // PointToScreen hands back device pixels, which is the space the tube moves in.
            if (!el.IsVisible || PresentationSource.FromVisual(el) == null) return false;
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            var rect = new Rect(tl, br);
            if (rect.Width <= 0 || rect.Height <= 0) return false;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            await tube.GlideToScreenRectAsync(rect, cts.Token).ConfigureAwait(true);
            return !cts.IsCancellationRequested;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession stealcard glide failed: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>The tube's centre in GhostLayer coordinates. PointFromScreen takes device pixels, so
    /// the tube's physical rect goes straight in.</summary>
    private static Point? TubeMouthInLayer(PossessionContext ctx)
    {
        try
        {
            var tube = App.AvatarWindow;
            var layer = ctx.Host.GhostLayer;
            if (tube == null || layer == null) return null;
            if (!tube.TryGetTubeScreenRect(out var r) || r.Width <= 0) return null;
            return layer.PointFromScreen(new Point(r.X + r.Width / 2.0, r.Y + r.Height / 2.0));
        }
        catch { return null; }
    }

    /// <summary>Send the tube home, and give the warden its lease back only once it has ARRIVED.
    /// ReturnHomeAsync clears the tube's captured home in its finally, so releasing any earlier would
    /// let a warden verb capture a home that is about to be wiped out from under it.</summary>
    private void SendTubeHome()
    {
        if (!_holdsTube) return;
        _holdsTube = false;   // the homeward leg owns the lease from here; ReleaseTube() now no-ops

        static void Done() { try { (App.Possession?.Warden as Warden)?.ReleaseTube(); } catch { } }

        try
        {
            var t = App.AvatarWindow?.ReturnHomeAsync(CancellationToken.None);
            if (t == null) { Done(); return; }
            _ = t.ContinueWith(_ => Done(), CancellationToken.None,
                               TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch { Done(); }
    }
}
