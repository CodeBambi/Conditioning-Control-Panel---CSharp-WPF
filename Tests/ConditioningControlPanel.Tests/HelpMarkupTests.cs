using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The help cards' key-word markup: what the builder turns into accent runs.</summary>
public class HelpMarkupTests
{
    [Fact]
    public void Plain_text_is_one_segment()
    {
        var parts = HelpMarkup.Split("Flash images on a schedule.");

        Assert.Single(parts);
        Assert.Equal(("Flash images on a schedule.", false), parts[0]);
    }

    [Fact]
    public void Marked_words_come_out_emphasised_in_order()
    {
        var parts = HelpMarkup.Split("Displays **random images** at **set intervals**.");

        Assert.Equal(new[]
        {
            ("Displays ", false), ("random images", true), (" at ", false), ("set intervals", true), (".", false)
        }, parts);
    }

    [Fact]
    public void An_unpaired_marker_stays_literal()
    {
        var parts = HelpMarkup.Split("Hydra mode **spawns more");

        Assert.Single(parts);
        Assert.Equal(("Hydra mode **spawns more", false), parts[0]);
    }

    [Fact]
    public void Empty_and_null_give_nothing()
    {
        Assert.Empty(HelpMarkup.Split(null));
        Assert.Empty(HelpMarkup.Split(""));
    }
}
