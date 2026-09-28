using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Xunit;

namespace ConditioningControlPanel.Tests;

[Collection(CompanionWpfRenderCollection.Name)]
public class EmiPresentationTests
{
    private static void WithPresentation(Action<EmiDeskWindow, Window> test, bool prewarm = false)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var stage = new Window { Left = -10000, Top = -10000, Width = 1, Height = 1,
                ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            var emi = new EmiDeskWindow { Left = -10000, Top = -10000, Opacity = 0 };
            try
            {
                if (prewarm) { emi.Show(); emi.Hide(); }
                stage.Show();
                stage.Closing += (_, _) => emi.EndPresentation();
                emi.BeginPresentation(stage, stage.Close);
                test(emi, stage);
            }
            finally { emi.EndPresentation(); emi.Close(); stage.Close(); }
        });
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Newly_created_Emi_stays_natively_visible_after_queued_startup_work(bool prewarm)
    {
        WithPresentation((emi, stage) =>
        {
            var speech = new Window { Owner = emi, ShowActivated = false, ShowInTaskbar = false,
                Left = -10000, Top = -10000, Width = 1, Height = 1, Opacity = 0 };
            try
            {
                speech.Show();
                var frame = new DispatcherFrame();
                emi.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Assert.True(emi.IsVisible);
                Assert.True(IsWindowVisible(new WindowInteropHelper(emi).Handle),
                    "Emi must remain visible at the native window level after the dispatcher drains.");
                Assert.NotNull(((Image)emi.FindName("BodyImage")).Source);
            }
            finally { speech.Close(); }
        }, prewarm);
    }

    [Fact]
    public void Presentation_keeps_resize_and_close_but_blocks_drag_and_shortcuts()
    {
        WithPresentation((emi, stage) =>
        {
            Assert.True(emi.PresentationActive);
            Assert.Same(stage, emi.Owner);
            foreach (var name in new[] { "BtnClose", "ResizeGrip" })
                Assert.Equal(Visibility.Visible, ((FrameworkElement)emi.FindName(name)).Visibility);
            foreach (var name in new[] { "BtnHelp", "BtnGear" })
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)emi.FindName(name)).Visibility);
            var root = (FrameworkElement)emi.FindName("BodyRoot");
            root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.False(root.IsMouseCaptured);
            emi.ApplyBodyWidth(300);
            Assert.Equal(300, emi.BodyWidth);
            Assert.False(emi.InputLocked);
        });
    }

    [Fact]
    public void Closing_the_show_preserves_the_same_Emi_and_restores_normal_controls()
    {
        WithPresentation((emi, stage) =>
        {
            bool closed = false;
            emi.Closed += (_, _) => closed = true;
            emi.ApplyBodyWidth(280);
            stage.Close();
            Assert.False(closed);
            Assert.False(emi.PresentationActive);
            Assert.Null(emi.Owner);
            Assert.Equal(280, emi.BodyWidth);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)emi.FindName("BtnHelp")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)emi.FindName("BtnGear")).Visibility);
        });
    }

    [Fact]
    public void Emi_close_button_stops_the_owned_show()
    {
        WithPresentation((emi, stage) =>
        {
            bool stopped = false;
            stage.Closed += (_, _) => stopped = true;
            ((Button)emi.FindName("BtnClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(stopped);
            Assert.False(emi.PresentationActive);
        });
    }
    [Fact]
    public void Presentation_entrance_finishes_and_restores_full_size()
    {
        WithPresentation((emi, stage) =>
        {
            emi.RunPresentationEntrance();
            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (!emi.PresentationArriving || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.False(emi.PresentationArriving);
            Assert.False(emi.InputLocked);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)emi.FindName("BodyRoot")).Visibility);
            var scale = (System.Windows.Media.ScaleTransform)emi.FindName("CrtScale");
            Assert.Equal(1,scale.ScaleX); Assert.Equal(1,scale.ScaleY);
            Assert.True(IsWindowVisible(new WindowInteropHelper(emi).Handle));
        });
    }

    [Fact]
    public void Closing_during_entrance_cancels_it_and_restores_Emi()
    {
        WithPresentation((emi, stage) =>
        {
            emi.RunPresentationEntrance();
            stage.Close();
            Assert.False(emi.PresentationArriving);
            Assert.False(emi.InputLocked);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)emi.FindName("BodyRoot")).Visibility);
            Assert.Equal(1,((System.Windows.Media.ScaleTransform)emi.FindName("CrtScale")).ScaleY);
        });
    }
}
