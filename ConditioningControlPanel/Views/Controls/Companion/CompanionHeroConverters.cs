using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  Converters owned by the hero package (Z1 + its constellation band).
    //
    //  They live here rather than in CompanionConverters.cs so the shared file stays the
    //  scaffold's, and they are instanced inside RelationshipConstellation's own resources —
    //  a DataTemplate resolves StaticResource against the dictionary it was declared in
    //  (Known Issues #4), so a converter a template needs must be in that same scope.
    // =====================================================================================

    /// <summary>
    /// How far the earned (pink) part of the constellation connector runs, as a star
    /// <see cref="GridLength"/> — the Trainer Card bar recipe again, so the line needs no
    /// ActualWidth arithmetic and survives any resize.
    ///
    /// <para>The connector is laid out across the span between the <i>centres</i> of the first and
    /// last node, so the fraction here is measured in that span: node <c>n</c> sits at
    /// <c>n / (StageCount - 1)</c>. A dormant strip earns nothing, which is what makes the
    /// pre-Train-4 band read as five equal outlines with a hairline through them.</para>
    ///
    /// <para>Values are <c>[0] = CurrentStage (int)</c>, <c>[1] = IsLive (bool)</c>.</para>
    /// </summary>
    public sealed class ConstellationFillConverter : IMultiValueConverter
    {
        /// <summary>When true the converter returns the remainder column instead.</summary>
        public bool Invert { get; set; }

        /// <summary>
        /// 0..1 along the node-centre span. Pure, clamped, and shared with the unit tests so the
        /// geometry cannot drift from <see cref="ConstellationMath"/>'s idea of the stage ladder.
        /// </summary>
        public static double FillFraction(int currentStage, bool isLive)
        {
            if (!isLive || ConstellationMath.StageCount < 2) return 0.0;
            return ConstellationMath.ClampStage(currentStage) / (double)(ConstellationMath.StageCount - 1);
        }

        public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            int stage = 0;
            bool live = false;

            if (values != null)
            {
                if (values.Length > 0 && values[0] is int i) stage = i;
                if (values.Length > 1 && values[1] is bool b) live = b;
            }

            double f = FillFraction(stage, live);
            if (Invert) f = 1.0 - f;
            return new GridLength(f, GridUnitType.Star);
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object>();
    }
}
