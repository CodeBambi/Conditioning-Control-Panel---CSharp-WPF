using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mouse hook's swallow flags. A swallowed DOWN owes a swallow to its own UP only. A flag left
/// over from a DOWN whose UP never came through (another hook ate it, or Windows skipped the hook
/// on a timeout) used to eat the NEXT click's UP, so every hook after it never heard that click
/// let go and a quick click on a red bubble read as a hold (player report, 2026-10-02).
/// </summary>
public class UpSwallowGateTests
{
    [Fact]
    public void A_swallowed_down_owes_its_own_up_one_swallow()
    {
        var gate = new UpSwallowGate();
        gate.Down(right: false, swallowed: true);
        Assert.True(gate.Pending);
        Assert.True(gate.Up(right: false));
        Assert.False(gate.Pending);
        Assert.False(gate.Up(right: false));
    }

    [Fact]
    public void A_stale_flag_never_eats_the_next_clicks_up()
    {
        var gate = new UpSwallowGate();
        gate.Down(right: false, swallowed: true);
        // Its UP never came through this hook. The next click is not swallowed here...
        gate.Down(right: false, swallowed: false);
        // ...so its UP must pass on to the hooks after this one.
        Assert.False(gate.Up(right: false));
        Assert.False(gate.Pending);
    }

    [Fact]
    public void The_two_buttons_keep_their_own_flags()
    {
        var gate = new UpSwallowGate();
        gate.Down(right: true, swallowed: true);
        gate.Down(right: false, swallowed: false);
        Assert.False(gate.Up(right: false));
        Assert.True(gate.Pending);
        Assert.True(gate.Up(right: true));
    }
}
