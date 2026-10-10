using System.Linq;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>k4 HA3, ported from Tests/ConditioningControlPanel.Tests/WhatMovedCardTests.cs: upgrades see the
/// card once, fresh installs never do, every row lands on a real destination.</summary>
public class WhatMovedPlanTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(3, true, false)]
    public void Offered_once_and_only_to_upgrades(int shown, bool fresh, bool expected)
        => Assert.Equal(expected, WhatMovedPlan.ShouldOffer(shown, fresh));

    [Fact]
    public void Three_to_five_rows_each_landing_somewhere_real()
    {
        var rows = WhatMovedPlan.Rows;
        Assert.InRange(rows.Count, 3, 5);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
        foreach (var row in rows)
        {
            Assert.StartsWith("whatmoved_row_", row.TextKey);
            Assert.False(string.IsNullOrEmpty(row.Glyph));
            if (row.SettingsSection != null)
            {
                Assert.Equal("appsettings", row.Tab);
                Assert.Equal(row.SettingsSection, row.GlowKey);
            }
            else
            {
                Assert.Equal(row.Tab, row.GlowKey);
            }
        }
    }
}
