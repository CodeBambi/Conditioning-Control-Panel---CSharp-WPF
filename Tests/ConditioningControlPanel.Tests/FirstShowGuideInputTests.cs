using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

[Collection(CompanionWpfRenderCollection.Name)]
public class FirstShowGuideInputTests
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle,int index);

    [Fact]
    public void Painted_guide_passes_native_input_through_but_owned_controls_stay_interactive()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var stage = new Window { WindowStyle=WindowStyle.None, AllowsTransparency=true, Background=Brushes.Gold,
                Left=-10000, Top=-10000, Width=20, Height=20, Opacity=0, ShowActivated=false, ShowInTaskbar=false };
            var speech = new Window { Left=-10000, Top=-10000, Width=20, Height=20,
                Opacity=0, ShowActivated=false, ShowInTaskbar=false, Content=new Button {Content="Next"} };
            try
            {
                stage.Show(); speech.Owner=stage; speech.Show();
                int speechStyle = GetWindowLong(new WindowInteropHelper(speech).Handle,-20);
                FirstShowInput.PassThrough(stage);
                FirstShowInput.PassThrough(stage);
                int style = GetWindowLong(new WindowInteropHelper(stage).Handle,-20);
                Assert.NotEqual(0,style & 0x20);
                Assert.NotEqual(0,style & 0x80000);
                Assert.False(stage.IsHitTestVisible);
                Assert.True(speech.IsHitTestVisible);
                Assert.Equal(speechStyle,GetWindowLong(new WindowInteropHelper(speech).Handle,-20));
            }
            finally { speech.Close(); stage.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preview_lights_the_card_without_toggling_its_real_state(bool active)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new FeatureCard { IsActive=active, DimWhenInactive=true };
            var ring=(FrameworkElement)card.FindName("ActiveBorder");
            var art=(FrameworkElement)card.FindName("ContentRoot");
            var mute=(FrameworkElement)card.FindName("ImgIconMute");
            card.SetTutorialPreview(true);
            Assert.Equal(active,card.IsActive);
            Assert.Equal(Visibility.Visible,ring.Visibility);
            Assert.Equal(1,art.Opacity); Assert.Equal(0,mute.Opacity);
            card.SetTutorialPreview(false);
            Assert.Equal(active,card.IsActive);
            Assert.Equal(active ? Visibility.Visible : Visibility.Collapsed,ring.Visibility);
            Assert.Equal(active ? 0 : 1,mute.Opacity);
        });
    }

    [Fact]
    public void Leaving_preview_preserves_a_real_state_change_made_during_the_tour()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card=new FeatureCard { DimWhenInactive=true };
            card.SetTutorialPreview(true);
            card.IsActive=true;
            card.SetTutorialPreview(false);
            Assert.True(card.IsActive);
            Assert.Equal(Visibility.Visible,((FrameworkElement)card.FindName("ActiveBorder")).Visibility);
        });
    }
}
