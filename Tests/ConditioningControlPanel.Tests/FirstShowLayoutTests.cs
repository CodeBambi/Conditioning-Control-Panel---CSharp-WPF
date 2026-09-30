using System.Windows;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FirstShowLayoutTests
{
    [Theory]
    [InlineData(1920,1080,220,460)]
    [InlineData(1366,768,220,460)]
    [InlineData(1280,720,420,344)]
    public void Logo_remains_clear_with_a_resized_Emi_and_long_speech(int width,int height,int emi,int logoWidth)
    {
        var bounds = new Rect(16,16,width-32,height-64);
        var logo = new Rect(width/2-logoWidth/2,height*.43-150,logoWidth,300);
        var speech = new Size(390,260);
        var body = FirstShowLayout.GuideBody(bounds,new Size(emi,emi*1.012),speech,logo);
        var card = FirstShowLayout.Speech(bounds,speech,body,logo,new Point(body.Left,body.Bottom+24));
        Assert.True(bounds.Contains(body)); Assert.True(bounds.Contains(card));
        Assert.False(logo.IntersectsWith(body)); Assert.False(logo.IntersectsWith(card));
        Assert.False(card.IntersectsWith(body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1920)]
    [InlineData(1920)]
    public void Asset_tree_and_speech_stay_clear_on_the_app_monitor(int monitorX)
    {
        var bounds = new Rect(monitorX+16,16,1334,704);
        var target = new Rect(monitorX+160,200,280,490);
        var speech = new Size(390,300);
        var body = FirstShowLayout.GuideBody(bounds,new Size(300,304),speech,target);
        var card = FirstShowLayout.Speech(bounds,speech,body,target,new Point(body.Left,body.Bottom+24));
        Assert.True(bounds.Contains(body)); Assert.True(bounds.Contains(card));
        Assert.False(target.IntersectsWith(body)); Assert.False(target.IntersectsWith(card));
        Assert.False(card.IntersectsWith(body));
    }

    [Fact]
    public void Right_edge_target_does_not_push_the_speech_off_screen()
    {
        var bounds = new Rect(16,16,990,700);
        var target = new Rect(880,220,100,36);
        var body = new Rect(720,320,220,224);
        var card = FirstShowLayout.Speech(bounds,new Size(390,300),body,target,new Point(900,700));
        Assert.True(bounds.Contains(card));
        Assert.False(card.IntersectsWith(target)); Assert.False(card.IntersectsWith(body));
    }
    [Fact]
    public void Outro_speech_stays_next_to_Emi_instead_of_at_screen_center()
    {
        var bounds = new Rect(16,16,1888,1016);
        var body = new Rect(16,812,220,220);
        var logo = new Rect(730,314,460,300);
        var card = FirstShowLayout.Speech(bounds,new Size(390,180),body,logo,new Point(765,852));
        Assert.InRange(card.Left-body.Right,10,24);
        Assert.False(card.IntersectsWith(body)); Assert.False(card.IntersectsWith(logo));
        Assert.True(bounds.Contains(card));
    }
    [Fact]
    public void Emi_stays_beside_the_logo_instead_of_in_a_screen_corner()
    {
        var logo = new Rect(730,314,460,300);
        var body = FirstShowLayout.GuideBody(new Rect(16,16,1888,1016),new Size(220,224),new Size(390,180),logo);
        Assert.False(body.IntersectsWith(logo));
        double dx = System.Math.Max(0,System.Math.Max(body.Left-logo.Right,logo.Left-body.Right));
        double dy = System.Math.Max(0,System.Math.Max(body.Top-logo.Bottom,logo.Top-body.Bottom));
        Assert.InRange(System.Math.Sqrt(dx*dx+dy*dy),18,40);
    }
}
