using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The ladder page half: every refusal the server words has a line on the page.</summary>
public class ChasterLadderViewTests
{
    [Theory]
    [InlineData("test_lock", "chaster_ladder_why_test_lock")]
    [InlineData("chaster_taken", "chaster_ladder_why_chaster_taken")]
    [InlineData("later", "chaster_ladder_why_other")]
    public void Every_refusal_has_words(string reason, string key) =>
        Assert.Equal(key, ChasterTabView.WhyKey(reason));
}
