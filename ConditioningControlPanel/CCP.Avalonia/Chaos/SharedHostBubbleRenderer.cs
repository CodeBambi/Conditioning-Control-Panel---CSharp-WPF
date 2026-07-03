using Avalonia.Media.Imaging;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Renders chaos bubbles as children of the single shared <see cref="ChaosBubbleHostOverlay"/>.
/// Used when <see cref="AppSettings.ChaosBubbleSharedHost"/> is enabled.
/// </summary>
public sealed class SharedHostBubbleRenderer : IBubbleRenderer
{
    private readonly Bitmap? _bubbleBitmap;
    private readonly Dictionary<Guid, AvaloniaBubble> _bubbles = new();

    public SharedHostBubbleRenderer(Bitmap? bubbleBitmap)
    {
        _bubbleBitmap = bubbleBitmap;
        ChaosBubbleHostOverlay.EnsureCreated();
    }

    public void Create(BubbleState state)
    {
        if (_bubbles.ContainsKey(state.Id)) return;

        var bubble = new AvaloniaBubble(_bubbleBitmap, state.Size);
        ApplyVisualState(bubble, state);
        _bubbles[state.Id] = bubble;
        ChaosBubbleHostOverlay.Add(bubble);
    }

    public void Move(BubbleState state)
    {
        if (!_bubbles.TryGetValue(state.Id, out var bubble)) return;

        ApplyVisualState(bubble, state);
        // BubbleState.X/Y are per-screen LOGICAL DIPs; the host places in PHYSICAL px (the
        // cross-screen-safe currency), so convert with the bubble's own screen scaling first
        // (same seam the compositor BubbleLayer uses: physical = logical * scaling).
        double s = state.Scaling > 0 ? state.Scaling : 1.0;
        ChaosBubbleHostOverlay.Place(bubble, state.X * s, state.Y * s);
    }

    public void SetLabel(Guid id, string label)
    {
        if (!_bubbles.TryGetValue(id, out var bubble)) return;
        bubble.SetLabel(label);
    }

    public void SetFuse(Guid id, double fraction)
    {
        if (!_bubbles.TryGetValue(id, out var bubble)) return;
        bubble.SetFuse(fraction);
    }

    public void Pop(BubbleState state, Action onComplete)
    {
        if (!_bubbles.Remove(state.Id, out var bubble))
        {
            onComplete();
            return;
        }

        bubble.Pop(() =>
        {
            ChaosBubbleHostOverlay.Remove(bubble);
            onComplete();
        });
    }

    public void Destroy(Guid id)
    {
        if (!_bubbles.Remove(id, out var bubble)) return;
        ChaosBubbleHostOverlay.Remove(bubble);
    }

    private static void ApplyVisualState(AvaloniaBubble bubble, BubbleState state)
    {
        string? label = null;
        (byte r, byte g, byte b)? tint = null;
        var fuseFraction = 1.0;
        var isBrittle = false;

        if (state.Spec is { } spec)
        {
            label = spec.Label;
            tint = (spec.TintR, spec.TintG, spec.TintB);
            fuseFraction = spec.IsLive && spec.FuseMs > 0
                ? Math.Clamp(state.FuseRemainingMs / spec.FuseMs, 0.0, 1.0)
                : 1.0;
            isBrittle = spec.IsBrittle;

            if (tint.HasValue)
                bubble.SetTint(tint.Value.r, tint.Value.g, tint.Value.b);
        }

        bubble.SetVisual(state.Scale, state.Opacity);
        bubble.SetLabel(label ?? "");
        bubble.SetFuse(fuseFraction);
        bubble.SetShielded(state.IsShielded);
        bubble.SetBrittle(isBrittle);
    }
}
