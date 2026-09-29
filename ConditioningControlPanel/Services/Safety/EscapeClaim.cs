using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// The surfaces that drop or close on Escape and so may take an Escape from the panic key
    /// (bug hunt 2026-09-29, TAB-8 / DESK-3): Circe's Tab price box, the friends drawer and the
    /// dashboard's click-choice popup. Each marks its root once with <see cref="Mark"/> and calls
    /// <see cref="Taken"/> from its own Escape handler. The global hook asks
    /// <see cref="KeyboardInASurface"/> and <see cref="OnItsWay"/> and lets
    /// <see cref="PanicPolicy.SurfaceTakesEscape"/> decide. The Ctrl+K palette keeps its own
    /// hand-off (<c>SettingsPaletteWindow.TryConsumeEscape</c>).
    /// </summary>
    internal static class EscapeClaim
    {
        private static readonly DependencyProperty DropsOnEscapeProperty = DependencyProperty.RegisterAttached(
            "DropsOnEscape", typeof(bool), typeof(EscapeClaim), new PropertyMetadata(false));

        /// <summary>How long a taken press may take to reach its surface. Past it the next Escape
        /// may be taken again, so a press that went elsewhere cannot turn the surfaces off.</summary>
        internal static readonly TimeSpan OnItsWayFor = TimeSpan.FromSeconds(2);

        private static DateTime? _takenAtUtc;

        /// <summary>Marks an element whose own Escape handler drops or closes it.</summary>
        internal static void Mark(DependencyObject surface) => surface.SetValue(DropsOnEscapeProperty, true);

        /// <summary>True when the element with the keyboard sits in a marked surface, so its own
        /// Escape handler is the one this press reaches.</summary>
        internal static bool KeyboardInASurface() => InASurface(Keyboard.FocusedElement as DependencyObject);

        internal static bool InASurface(DependencyObject? node)
        {
            for (var hops = 0; node != null && hops < 256; hops++)
            {
                if (node.GetValue(DropsOnEscapeProperty) is true) return true;
                var up = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : null;
                node = up ?? LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        /// <summary>The hook left this press to a surface.</summary>
        internal static void Claimed(DateTime nowUtc) => _takenAtUtc = nowUtc;

        /// <summary>A surface's own Escape handler ran: the press it was left arrived.</summary>
        internal static void Taken() => _takenAtUtc = null;

        /// <summary>A press left to a surface a moment ago has not reached it yet.</summary>
        internal static bool OnItsWay(DateTime nowUtc) =>
            _takenAtUtc is { } t && nowUtc >= t && nowUtc - t < OnItsWayFor;
    }
}
