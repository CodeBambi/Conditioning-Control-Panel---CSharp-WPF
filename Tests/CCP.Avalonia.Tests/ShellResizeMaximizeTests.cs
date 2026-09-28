using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    [Fact]
    public async Task EdgePressResizesAndMaximizedLayoutStaysUniform()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var w = new MainShellWindow();
            w.Show();
            w.MinWidth = w.MinHeight = 0;
            w.Width = 1113; w.Height = 1979;   // the portrait work area the bug was seen on
            Dispatcher.UIThread.RunJobs();

            // A left-press on the right edge goes to the window manager as East.
            w.MouseDown(new Point(1111, 900), MouseButton.Left);
            w.MouseUp(new Point(1111, 900), MouseButton.Left);
            Assert.Equal(WindowEdge.East, w.LastResizeEdge);

            // The canvas scales uniformly and is centred: no stretched art, margins split evenly.
            var canvas = w.Named<Grid>("DesignCanvas")!;
            var tl = canvas.TranslatePoint(new Point(0, 0), w)!.Value;
            var br = canvas.TranslatePoint(new Point(canvas.Bounds.Width, canvas.Bounds.Height), w)!.Value;
            double sx = (br.X - tl.X) / canvas.Bounds.Width, sy = (br.Y - tl.Y) / canvas.Bounds.Height;
            Assert.Equal(sx, sy, 3);
            Assert.Equal(1113, br.X - tl.X, 0);
            Assert.Equal(tl.Y, w.Bounds.Height - br.Y, 0);
            w.Close();
            return Task.CompletedTask;
        });
    }
}
