using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>User bug: "can't scale it by dragging the sides; no resize arrow; full screen looks very
/// bad". WPF: WindowChrome ResizeBorderThickness=5 (MainWindow.xaml:33) and a Viewbox over the
/// 1585x901 DesignCanvas (MainWindow.xaml:282).</summary>
public sealed class ShellResizeMaximizeTests
{
    [Theory]
    [InlineData(2, 400, WindowEdge.West, StandardCursorType.LeftSide)]
    [InlineData(998, 400, WindowEdge.East, StandardCursorType.RightSide)]
    [InlineData(500, 2, WindowEdge.North, StandardCursorType.TopSide)]
    [InlineData(500, 798, WindowEdge.South, StandardCursorType.BottomSide)]
    [InlineData(2, 2, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner)]
    [InlineData(998, 8, WindowEdge.NorthEast, StandardCursorType.TopRightCorner)]
    [InlineData(8, 798, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner)]
    [InlineData(998, 798, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner)]
    public void EdgeHitZones(double x, double y, WindowEdge edge, StandardCursorType cursor)
    {
        Assert.Equal(edge, MainShellWindow.EdgeAt(new Point(x, y), new Size(1000, 800)));
        Assert.Equal(cursor, MainShellWindow.CursorFor(edge));
    }

    [Fact]
    public void InteriorIsNotAnEdge() => Assert.Null(MainShellWindow.EdgeAt(new Point(500, 400), new Size(1000, 800)));

    [Theory]
    [InlineData(1920, 1080, true)]    // 16:9
    [InlineData(1680, 1050, true)]    // 16:10
    [InlineData(1024, 768, false)]    // 4:3
    [InlineData(1113, 1979, false)]   // the reported portrait maximize
    public void StretchPolicy(double w, double h, bool fill)
        => Assert.Equal(fill ? Stretch.Fill : Stretch.Uniform, MainShellWindow.StretchFor(new Size(w, h)));

    private static MainShellWindow Open()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var w = new MainShellWindow();
        w.Show();
        w.MinWidth = w.MinHeight = 0;
        return w;
    }

    private static (Point Tl, Point Br) CanvasRect(MainShellWindow w)
    {
        var c = w.Named<Grid>("DesignCanvas")!;
        return (c.TranslatePoint(new Point(0, 0), w)!.Value,
                c.TranslatePoint(new Point(c.Bounds.Width, c.Bounds.Height), w)!.Value);
    }

    [Fact]
    public async Task LayoutFollowsThePolicyAndEdgesStayAtTheWindowEdge()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var w = Open();
            var box = w.Named<Viewbox>("ShellViewbox")!;

            // 16:9-ish: WPF's Fill, canvas covers the window.
            w.Width = 1600; w.Height = 910;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Stretch.Fill, box.Stretch);
            var (tl, br) = CanvasRect(w);
            Assert.Equal(0, tl.X, 0); Assert.Equal(0, tl.Y, 0);
            Assert.Equal(1600, br.X, 0); Assert.Equal(910, br.Y, 0);

            // Portrait: Uniform, true aspect, full width, top-aligned, band below.
            w.Width = 1113; w.Height = 1979;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Stretch.Uniform, box.Stretch);
            (tl, br) = CanvasRect(w);
            Assert.Equal((br.X - tl.X) / 1585, (br.Y - tl.Y) / 901, 3);
            Assert.Equal(0, tl.X, 0); Assert.Equal(1113, br.X, 0);
            Assert.Equal(0, tl.Y, 0);
            Assert.True(br.Y < 1000, "no band under a Uniform portrait canvas");

            // The edge bands sit on the WINDOW edge, not the canvas edge: the bottom edge is in the band.
            w.MouseDown(new Point(500, 1977), MouseButton.Left);
            w.MouseUp(new Point(500, 1977), MouseButton.Left);
            Assert.Equal(WindowEdge.South, w.LastResizeEdge);
            w.MouseDown(new Point(500, br.Y - 1), MouseButton.Left);   // the canvas's bottom is NOT an edge
            w.MouseUp(new Point(500, br.Y - 1), MouseButton.Left);
            w.MouseDown(new Point(1111, 1500), MouseButton.Left);
            w.MouseUp(new Point(1111, 1500), MouseButton.Left);
            Assert.Equal(WindowEdge.East, w.LastResizeEdge);

            // Maximized: no resize edges at all.
            w.WindowState = WindowState.Maximized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, w.WindowState);
            var edge = w.Bounds.Size;
            w.MouseDown(new Point(2, edge.Height / 2), MouseButton.Left);
            w.MouseUp(new Point(2, edge.Height / 2), MouseButton.Left);
            Assert.Equal(WindowEdge.East, w.LastResizeEdge);   // unchanged: the West press did not resize
            w.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SizeFloorsClampToTheWorkArea()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var w = Open();                  // design floors captured on open: 1131 x 620
            w.RelaxSizeFloorsTo(1113, 560);
            Assert.Equal(1113, w.MinWidth);
            Assert.Equal(560, w.MinHeight);
            w.RelaxSizeFloorsTo(3000, 3000);  // a bigger screen: back to the design floors, never above
            Assert.Equal(1131, w.MinWidth);
            Assert.Equal(620, w.MinHeight);
            w.Close();
            return Task.CompletedTask;
        });
    }
}
