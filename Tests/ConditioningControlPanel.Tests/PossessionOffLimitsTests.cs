using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rule that keeps the exits reachable while the room misbehaves (Services/Possession/
/// POSSESSION.md, "Hard rules"): no effect may drop, hide or hit-test-kill a control that IS
/// excluded, that CONTAINS something excluded, or that lives in a room the user has to be able to
/// leave.
///
/// <para>The containment half is the one that had teeth. <c>poss:Possession.Exclude</c> inherits
/// DOWN, so tagging BtnEmergencyExit protects the button and nothing above it - the Lockdown card it
/// sits in is a perfectly ordinary Card target, and FallEffect used to be allowed to tilt that card
/// off the bottom of the window with hit-testing off for 45 s, taking the Emergency Exit and the
/// secret exit box with it.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PossessionOffLimitsTests
{
    // ---- the name half (pure) --------------------------------------------------------------------

    [Theory]
    [InlineData("LockdownCardBorder")]
    [InlineData("BtnEmergencyExit")]
    [InlineData("TxtSecretExitPhrase")]
    [InlineData("lockdowngate")]            // case does not matter
    [InlineData("PanelEMERGENCY")]
    public void ReservedNames_are_off_limits(string name)
        => Assert.True(PossessionOffLimits.IsReservedName(name));

    [Theory]
    [InlineData("BtnStart")]
    [InlineData("CardFlashSettings")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_names_are_not(string? name)
        => Assert.False(PossessionOffLimits.IsReservedName(name));

    [Fact]
    public void A_null_element_is_off_limits()
        => Assert.True(PossessionOffLimits.IsOffLimits(null));

    // ---- the tree half ---------------------------------------------------------------------------

    private static Border Realize(Border card)
    {
        var host = new Grid { Width = 400, Height = 300 };
        host.Children.Add(card);
        host.Measure(new Size(400, 300));
        host.Arrange(new Rect(0, 0, 400, 300));
        host.UpdateLayout();
        return card;
    }

    /// <summary>An ordinary card full of ordinary controls is fair game - the deck would be empty
    /// otherwise, and "everything is off limits" is the failure mode nobody would notice.</summary>
    [Fact]
    public void A_plain_card_is_takeable()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new Border { Width = 200, Height = 120 };
            var inner = new Grid();
            inner.Children.Add(new Button { Content = "Save" });
            card.Child = inner;

            Assert.False(PossessionOffLimits.IsOffLimits(Realize(card)));
        });
    }

    /// <summary>The ship-blocker: the card is not excluded, something INSIDE it is.</summary>
    [Fact]
    public void A_card_containing_an_excluded_control_is_off_limits()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new Border { Width = 200, Height = 120 };
            var inner = new Grid();
            var exit = new Button { Content = "let me out" };
            Possession.SetExclude(exit, true);
            inner.Children.Add(exit);
            card.Child = inner;

            Assert.False(Possession.GetExclude(card));   // the tag really does not reach upward
            Assert.True(PossessionOffLimits.IsOffLimits(Realize(card)));
        });
    }

    [Fact]
    public void An_excluded_card_is_off_limits()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new Border { Width = 200, Height = 120 };
            Possession.SetExclude(card, true);
            Assert.True(PossessionOffLimits.IsOffLimits(Realize(card)));
        });
    }

    [Fact]
    public void A_card_named_for_the_lockdown_is_off_limits()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new Border { Name = "LockdownCardBorder", Width = 200, Height = 120 };
            Assert.True(PossessionOffLimits.IsOffLimits(Realize(card)));
        });
    }

    /// <summary>Reading UPWARD as well: a control inside the lockdown surface is part of it.</summary>
    [Fact]
    public void A_control_inside_a_lockdown_ancestor_is_off_limits()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var inner = new Border { Width = 100, Height = 40 };
            var outer = new Border { Name = "LockdownActivePanel", Width = 200, Height = 120, Child = inner };

            Realize(outer);
            Assert.True(PossessionOffLimits.IsOffLimits(inner));
        });
    }
}
