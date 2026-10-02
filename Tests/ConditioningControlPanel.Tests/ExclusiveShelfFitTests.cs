using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ExclusiveShelfFitTests
{
    [Theory]
    [InlineData(1403, 4)]
    [InlineData(1060, 3)]
    [InlineData(700, 2)]
    [InlineData(400, 1)]
    public void Fills_the_row_with_as_many_columns_as_fit(double available, int columns)
    {
        var (cols, width, _) = ExclusiveShelfFit.For(available);
        Assert.Equal(columns, cols);
        Assert.True(cols * (width + ExclusiveShelfFit.Gap) <= available, "the row must fit");
    }

    [Fact]
    public void Keeps_the_card_shape_and_never_shrinks_below_the_old_height()
    {
        var (_, w, h) = ExclusiveShelfFit.For(1403);
        Assert.True(w >= ExclusiveShelfFit.MinWidth);
        Assert.True(h >= ExclusiveShelfFit.BaseHeight);
        Assert.Equal((1, ExclusiveShelfFit.BaseWidth, ExclusiveShelfFit.BaseHeight), ExclusiveShelfFit.For(double.NaN));
    }
}
