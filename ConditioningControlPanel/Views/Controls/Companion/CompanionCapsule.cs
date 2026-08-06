using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Makes a <see cref="Border"/> a true capsule (stadium) — the shape every chip, pill, tag,
    /// badge and segment strip in the mockup has.
    ///
    /// <para><b>Why this type exists.</b> The mockup writes <c>border-radius:999px</c>, and the
    /// obvious translation is a <c>CornerRadius</c> of 999. In CSS that clamps to a stadium. In
    /// WPF it does not: <see cref="Border"/> clamps the X and Y radii to half the width and half
    /// the height <i>independently</i>, so an over-large radius produces a full ellipse. Measured
    /// on a 110×26 Border, the filled area lands on 78% of the box — which is π/4, the area of the
    /// inscribed ellipse — against 94% for a correct stadium. Every chip on the page was rendering
    /// as a lens. <c>CompanionCapsuleTests</c> pins both halves of that finding so nobody
    /// "simplifies" this back to a constant.</para>
    ///
    /// <para><b>Why an attached property and not a binding.</b> A stadium's radius is half the
    /// element's height, and the height is padding- and font-driven, so no constant works for
    /// controls that run from 18px switches to 41px inputs. Binding <c>CornerRadius</c> to
    /// <c>ActualHeight</c> through a converter would be a layout-thrash binding, which this page
    /// forbids. This is neither: <see cref="Border.CornerRadiusProperty"/> is registered
    /// <c>AffectsRender</c> only, never <c>AffectsMeasure</c>, so writing it from
    /// <see cref="FrameworkElement.SizeChanged"/> re-renders without re-measuring and therefore
    /// cannot feed back into the size that triggered it. One assignment per real size change.</para>
    ///
    /// <para>Usage — in a style, or directly on a Border inside a ControlTemplate:</para>
    /// <code>
    /// &lt;Setter Property="cmp:CompanionCapsule.IsCapsule" Value="True"/&gt;
    /// &lt;Border cmp:CompanionCapsule.IsCapsule="True" .../&gt;
    /// </code>
    /// </summary>
    public static class CompanionCapsule
    {
        /// <summary>
        /// True keeps the Border's <see cref="Border.CornerRadius"/> pinned at half its rendered
        /// height. Setting it to false detaches the handler and leaves the radius where it is.
        /// </summary>
        public static readonly DependencyProperty IsCapsuleProperty =
            DependencyProperty.RegisterAttached(
                "IsCapsule",
                typeof(bool),
                typeof(CompanionCapsule),
                new PropertyMetadata(false, OnIsCapsuleChanged));

        /// <summary>Gets <see cref="IsCapsuleProperty"/>.</summary>
        public static bool GetIsCapsule(DependencyObject element)
            => (bool)element.GetValue(IsCapsuleProperty);

        /// <summary>Sets <see cref="IsCapsuleProperty"/>.</summary>
        public static void SetIsCapsule(DependencyObject element, bool value)
            => element.SetValue(IsCapsuleProperty, value);

        private static void OnIsCapsuleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Border border) return;

            border.SizeChanged -= OnBorderSizeChanged;
            if (!Equals(e.NewValue, true)) return;

            border.SizeChanged += OnBorderSizeChanged;

            // A style setter is applied before the first layout pass, so ActualHeight is still 0
            // here and SizeChanged will do the real work. When the property is set on an element
            // that has already been arranged, this is what catches it up.
            Apply(border);
        }

        private static void OnBorderSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged && sender is Border border) Apply(border);
        }

        /// <summary>
        /// The whole rule: radius = height / 2. Exposed for the unit test, which has no message
        /// loop to raise SizeChanged with.
        /// </summary>
        internal static void Apply(Border border)
        {
            double half = border.ActualHeight / 2.0;
            if (half <= 0) return;

            // Skip an identical write so a pathological host cannot turn this into a churn loop.
            if (border.CornerRadius.TopLeft == half) return;

            border.CornerRadius = new CornerRadius(half);
        }
    }
}
