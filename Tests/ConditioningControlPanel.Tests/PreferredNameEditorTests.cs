using System.Globalization;
using ConditioningControlPanel.Views.Controls.Companion.V2;
using Xunit;
namespace ConditioningControlPanel.Tests;
public sealed class PreferredNameEditorTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(" \r\n ", "")]
    [InlineData("  Alex\t  Rose \n", "Alex Rose")]
    public void NamesAreSingleLineAndEmptyClears(string? input, string expected)
        => Assert.Equal(expected, PreferredNameEditor.Normalize(input));

    [Fact]
    public void LengthLimitPreservesCombinedCharacters()
    {
        var name = string.Concat(System.Linq.Enumerable.Repeat("e\u0301", 70));
        var normalized = PreferredNameEditor.Normalize(name);
        Assert.Equal(64, new StringInfo(normalized).LengthInTextElements);
        Assert.EndsWith("e\u0301", normalized);
    }
}
