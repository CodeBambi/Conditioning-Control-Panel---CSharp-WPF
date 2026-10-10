using System.Threading.Tasks;
using Avalonia;
using CCP.Avalonia.Testing;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Xunit;

namespace CCP.Avalonia.Tests.Home;

/// <summary>ArtFill maps a picture exactly as the ImageBrush it replaced (perf pass 2026-10-09:
/// Home tiles were re-rendered through a tile shader every frame).</summary>
public class ArtFillTests
{
    [Fact]
    public void UniformToFill_CropsTheLongSide_Centred()
    {
        var m = ArtFill.Map(new Size(200, 100), null, new Size(100, 100), Stretch.UniformToFill, AlignmentX.Center, AlignmentY.Center)!.Value;
        Assert.Equal(new Rect(50, 0, 100, 100), m.Src);
        Assert.Equal(new Rect(0, 0, 100, 100), m.Dest);
    }

    [Fact]
    public void UniformToFill_Alignment_PicksTheEdge()
    {
        var right = ArtFill.Map(new Size(200, 100), null, new Size(100, 100), Stretch.UniformToFill, AlignmentX.Right, AlignmentY.Center)!.Value;
        Assert.Equal(new Rect(100, 0, 100, 100), right.Src);
        var top = ArtFill.Map(new Size(100, 300), null, new Size(100, 100), Stretch.UniformToFill, AlignmentX.Center, AlignmentY.Top)!.Value;
        Assert.Equal(new Rect(0, 0, 100, 100), top.Src);
    }

    [Fact]
    public void Uniform_Letterboxes_AndViewboxCrops()
    {
        var m = ArtFill.Map(new Size(200, 100), null, new Size(100, 100), Stretch.Uniform, AlignmentX.Center, AlignmentY.Center)!.Value;
        Assert.Equal(new Rect(0, 25, 100, 50), m.Dest);
        var vb = ArtFill.Map(new Size(200, 200), new Rect(.5, 0, .5, .5), new Size(50, 50), Stretch.UniformToFill, AlignmentX.Center, AlignmentY.Center)!.Value;
        Assert.Equal(new Rect(100, 0, 100, 100), vb.Src);
    }

    [Fact]
    public Task Paint_ClearsTheOldBrush_AndAMissingPictureDrawsNothing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var host = new Border { CornerRadius = new CornerRadius(11), Background = Brushes.Red };
        ArtFill.Paint(host, null, Stretch.UniformToFill);
        Assert.Null(host.Background);
        Assert.Null(host.Child);
        var fill = new ArtFill { Source = null };
        host.Child = fill;
        ArtFill.Paint(host, null, Stretch.UniformToFill);
        Assert.Same(fill, host.Child);
        Assert.Null(fill.Source);
        return Task.CompletedTask;
    });
}
