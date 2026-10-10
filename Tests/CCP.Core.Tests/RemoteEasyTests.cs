using System;
using ConditioningControlPanel.Services.Remote;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Remote Control v2: the subject's Easy button and the More / Easy / Stop signal.</summary>
public class RemoteEasyTests
{
    [Fact]
    public void Easy_halves_down_to_a_quarter_and_stays_there()
    {
        Assert.Equal(0.5, RemoteEasy.Next(1.0));
        Assert.Equal(0.25, RemoteEasy.Next(0.5));
        Assert.Equal(0.25, RemoteEasy.Next(0.25));
        Assert.Equal(0.25, RemoteEasy.Next(0.0));
    }

    [Fact]
    public void Scale_never_rounds_a_real_ask_to_nothing()
    {
        Assert.Equal(20, RemoteEasy.Scale(40, 0.5));
        Assert.Equal(1, RemoteEasy.Scale(2, 0.25));
        Assert.Equal(0, RemoteEasy.Scale(0, 0.5));
    }

    [Fact]
    public void A_controller_ask_is_eased_and_remembered_unscaled()
    {
        var o = new EasedOpacity();
        Assert.Equal(20, o.Ask(40, 50, 0.5));
        Assert.Equal(40, o.Requested);
        Assert.Equal(10, o.Rescale(current: 20, showing: true, factor: 0.25));
        Assert.Null(o.SubjectOriginal);     // the controller set it: nothing of the subject's to restore
        Assert.Equal(50, o.Ask(90, 50, 1.0));
    }

    [Fact]
    public void Easy_on_the_subjects_own_showing_overlay_is_handed_back_later()
    {
        var o = new EasedOpacity();
        Assert.Null(o.Rescale(current: 30, showing: false, factor: 0.5));
        Assert.Equal(15, o.Rescale(current: 30, showing: true, factor: 0.5));
        Assert.Equal(30, o.SubjectOriginal);
        Assert.Equal(8, o.Rescale(current: 15, showing: true, factor: 0.25));
        Assert.Equal(30, o.SubjectOriginal);
        o.Reset();
        Assert.Null(o.Requested);
        Assert.Null(o.SubjectOriginal);
    }
}
