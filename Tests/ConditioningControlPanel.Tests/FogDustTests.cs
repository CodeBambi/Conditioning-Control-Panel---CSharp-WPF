using System;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// The dust in the section edge's fog (owner, 2026-10-07: "more particles, granular, like
    /// little dust"): tiny twinkling specks that stay inside the strip and never pop.
    /// </summary>
    public class FogDustTests
    {
        [Fact]
        public void Dust_is_tiny_and_plentiful()
        {
            Assert.True(EdgeFogMath.DustSizeMaxPx <= 3.0);
            Assert.True(EdgeFogMath.DustLong > EdgeFogMath.BigLong + EdgeFogMath.SmallLong);
            Assert.True(EdgeFogMath.DustShort > EdgeFogMath.BigShort + EdgeFogMath.SmallShort);
        }

        [Theory]
        [InlineData(0.0, 2.0)]
        [InlineData(0.5, 7.0)]
        [InlineData(1.0, 7.0)]
        [InlineData(1.0, 2.0)]
        public void A_speck_wanders_inside_the_strip(double u, double wander)
        {
            double depth = EdgeFogMath.DustDepth(u, wander);
            Assert.True(depth - wander >= 0.9, $"outer reach {depth - wander}");
            Assert.True(depth + wander <= EdgeFogMath.StripPx - 1.9, $"inner reach {depth + wander}");
        }

        [Fact]
        public void A_speck_fades_in_and_out_and_stays_under_its_cap()
        {
            Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 0, 4, 0, 1));
            Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 4, 4, 0, 1));
            for (double tw = 0; tw < Math.PI * 2; tw += 0.3)
            {
                double a = EdgeFogMath.DustAlpha(EdgeFogMath.DustAlphaMax, 2, 4, tw, 1.5);
                Assert.InRange(a, 0, 0.95);
            }
        }

        [Fact]
        public void Twinkle_dims_but_never_blacks_out_a_speck()
        {
            double lo = EdgeFogMath.DustAlpha(0.6, 2, 4, -Math.PI / 2, 1);
            double hi = EdgeFogMath.DustAlpha(0.6, 2, 4, Math.PI / 2, 1);
            Assert.True(lo > 0.3 && lo < hi, $"lo {lo} hi {hi}");
        }
    }
}
