using System;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionCapsule.cs.
    ///
    /// <para>Makes a <see cref="Border"/> a true capsule (stadium) — the shape every chip, pill,
    /// tag, badge and segment strip in the mockup has. The rule is the whole type:
    /// radius = height / 2, rewritten on every real size change.</para>
    ///
    /// <para>The WPF original exists because <c>CornerRadius="999"</c> renders as a full ellipse
    /// there rather than clamping to a stadium. Whether Avalonia's renderer clamps the same way is
    /// not something this port bets on: keeping the attached property makes the shape deterministic
    /// on both heads and keeps the theme's setters a line-for-line diff against the WPF file.</para>
    /// </summary>
    public static class CompanionCapsule
    {
        /// <summary>
        /// True keeps the Border's <see cref="Border.CornerRadius"/> pinned at half its rendered
        /// height.
        /// </summary>
        public static readonly AttachedProperty<bool> IsCapsuleProperty =
            AvaloniaProperty.RegisterAttached<Border, bool>(
                "IsCapsule", typeof(CompanionCapsule));

        static CompanionCapsule()
        {
            // ponytail: subscription is never disposed, so setting IsCapsule back to false does not
            // detach (the WPF original does). Nothing in the theme ever unsets it, and the
            // observable is rooted by the Border itself, so it dies with the control. Add a
            // per-border IDisposable if a caller ever needs to turn a capsule back into a box.
            IsCapsuleProperty.Changed.AddClassHandler<Border>((border, e) =>
            {
                if (e.GetNewValue<bool>()) border.GetObservable(Visual.BoundsProperty)
                                                 .Subscribe(new BoundsObserver(border));
            });
        }

        /// <summary>Gets <see cref="IsCapsuleProperty"/>.</summary>
        public static bool GetIsCapsule(Border element) => element.GetValue(IsCapsuleProperty);

        /// <summary>Sets <see cref="IsCapsuleProperty"/>.</summary>
        public static void SetIsCapsule(Border element, bool value)
            => element.SetValue(IsCapsuleProperty, value);

        /// <summary>
        /// The whole rule: radius = height / 2. Internal so a unit test can drive it without a
        /// layout pass, exactly as the WPF version is.
        /// </summary>
        internal static void Apply(Border border)
        {
            double half = border.Bounds.Height / 2.0;
            if (half <= 0) return;

            // Skip an identical write so a pathological host cannot turn this into a churn loop.
            if (Math.Abs(border.CornerRadius.TopLeft - half) < 0.01) return;

            border.CornerRadius = new CornerRadius(half);
        }

        private sealed class BoundsObserver : IObserver<Rect>
        {
            private readonly Border _border;
            public BoundsObserver(Border border) => _border = border;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(Rect value) => Apply(_border);
        }
    }
}
