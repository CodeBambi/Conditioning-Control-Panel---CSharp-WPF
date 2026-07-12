using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Compositor;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Protocol-level test of the per-region click-through SWALLOW decision. It reproduces the
/// <c>CompositorEngine.BuildAndPublishCaptureMask</c> loop (skip inactive, skip ambient,
/// collect non-ambient regions) and the <c>AvaloniaMouseHook</c> decision
/// (<c>mask.Contains(pt)</c> ? swallow : pass) on a representative layer set, proving the
/// polarity the 2026-07-09 team review requires, deterministically and without a native hook:
///   - a click inside a VIDEO region is SWALLOWED (captured);
///   - a click in a region covered ONLY by the ambient filter/spiral is PASSED;
///   - a click on a HOLD-TO-DEFUSE bubble is PASSED (GetAsyncKeyState carve-out).
/// </summary>
public class CaptureMaskProtocolTests
{
    // Minimal fake layers implementing the Core ILayer contract via default interface members.
    private sealed class FakeAmbientLayer : ILayer
    {
        public int ZIndex => 60;
        public bool IsActive => true;
        public void OnActivated() { }
        public void OnDeactivated() { }
        // Inherits IsAmbientClickThrough => false? No — we want ambient. Override explicitly:
        public bool IsAmbientClickThrough => true;
    }

    private sealed class FakeVideoLayer : ILayer
    {
        private readonly PixelRect _bounds;
        public FakeVideoLayer(PixelRect bounds) => _bounds = bounds;
        public int ZIndex => 10;
        public bool IsActive => true;
        public void OnActivated() { }
        public void OnDeactivated() { }
        public void CollectCaptureRegions(CaptureMaskBuilder builder, IReadOnlyList<ScreenInfo> screens)
            => builder.Add(_bounds); // full-bounds video
    }

    private sealed class FakeBubbleLayer : ILayer
    {
        private readonly PixelRect _ambientBubble;
        private readonly PixelRect _holdToDefuseBubble;
        public FakeBubbleLayer(PixelRect ambient, PixelRect holdToDefuse)
        { _ambientBubble = ambient; _holdToDefuseBubble = holdToDefuse; }
        public int ZIndex => 45;
        public bool IsActive => true;
        public void OnActivated() { }
        public void OnDeactivated() { }
        public void CollectCaptureRegions(CaptureMaskBuilder builder, IReadOnlyList<ScreenInfo> screens)
        {
            builder.Add(_ambientBubble);
            // Hold-to-defuse bubble is deliberately NOT added (BubbleLayer excludes HoldToDefuse
            // items so the click passes through to GetAsyncKeyState).
        }
    }

    // Reproduce the engine's per-tick mask build.
    private static CaptureMask BuildMask(IReadOnlyList<ILayer> layers, IReadOnlyList<ScreenInfo> screens)
    {
        var b = new CaptureMaskBuilder();
        foreach (var layer in layers)
        {
            if (!layer.IsActive) continue;
            if (layer.IsAmbientClickThrough) continue;
            layer.CollectCaptureRegions(b, screens);
        }
        return b.Build();
    }

    private static readonly ScreenInfo Screen = new("primary", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1.0);

    [Fact]
    public void Click_Inside_Video_Region_Is_Swallowed()
    {
        var layers = new ILayer[]
        {
            new FakeVideoLayer(Screen.Bounds),                 // capturing, full-bounds
            new FakeAmbientLayer(),                            // ambient — excluded
        };
        var mask = BuildMask(layers, new[] { Screen });

        // The hook returns 1 (swallow) when mask.Contains(pt) is true.
        Assert.True(mask.Contains(960, 540));      // center of video
        Assert.True(mask.Contains(0, 0));          // corner of video
    }

    [Fact]
    public void Click_In_Ambient_Only_Region_Is_Passed()
    {
        // Ambient filter + spiral ONLY, no capturing layer: mask is empty -> hook passes.
        var layers = new ILayer[]
        {
            new FakeAmbientLayer(),
        };
        var mask = BuildMask(layers, new[] { Screen });

        Assert.Same(CaptureMask.Empty, mask);
        Assert.False(mask.Contains(960, 540));     // passed (not swallowed)
        Assert.False(mask.Contains(100, 100));     // passed
    }

    [Fact]
    public void Click_On_HoldToDefuse_Bubble_Is_Passed_Ambient_Bubble_Is_Swallowed()
    {
        var ambient = new PixelRect(100, 100, 60, 60);
        var holdToDefuse = new PixelRect(500, 500, 60, 60);
        var layers = new ILayer[]
        {
            new FakeBubbleLayer(ambient, holdToDefuse),
            new FakeAmbientLayer(),
        };
        var mask = BuildMask(layers, new[] { Screen });

        // Ambient (click-to-pop) bubble: captured -> swallow.
        Assert.True(mask.Contains(130, 130));
        // Hold-to-defuse bubble: NOT in the mask -> the click passes through to GetAsyncKeyState.
        Assert.False(mask.Contains(530, 530));
    }

    [Fact]
    public void Inactive_Layers_Never_Contribute_Regions()
    {
        var layers = new ILayer[] { new InactiveVideoLayer(), new FakeAmbientLayer() };
        var mask = BuildMask(layers, new[] { Screen });
        // The inactive video layer is skipped (IsActive == false) and the ambient layer is
        // skipped (ambient), so the mask stays empty -> all clicks pass.
        Assert.Same(CaptureMask.Empty, mask);
        Assert.False(mask.Contains(960, 540));
    }
}

file sealed class InactiveVideoLayer : ILayer
{
    public int ZIndex => 10;
    public bool IsActive => false; // inactive: must be skipped by the engine loop
    public void OnActivated() { }
    public void OnDeactivated() { }
    public void CollectCaptureRegions(CaptureMaskBuilder builder, IReadOnlyList<ScreenInfo> screens)
        => builder.Add(new PixelRect(0, 0, 1920, 1080));
}
