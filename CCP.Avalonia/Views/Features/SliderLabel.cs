using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// The one view-side thing every feature panel's slider handler does: repaint the value
    /// TextBlock beside it. On WPF each handler also writes App.Settings; that half is not
    /// portable yet, so this is what remains of ~40 near-identical handlers across the panels.
    /// </summary>
    internal static class SliderLabel
    {
        public static Slider Wire(Control host, string slider, string label, Func<double, string> format)
        {
            var s = host.FindControl<Slider>(slider)!;
            var t = host.FindControl<TextBlock>(label)!;
            t.Text = format(s.Value);
            s.ValueChanged += (_, e) => t.Text = format(e.NewValue);
            return s;
        }
    }
}
