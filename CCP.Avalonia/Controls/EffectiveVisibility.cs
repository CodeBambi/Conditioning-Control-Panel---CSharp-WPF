using System;
using System.Linq;
using Avalonia;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF's IsVisibleChanged fires when any ANCESTOR hides; Avalonia's IsVisible is the element's
    /// own and <c>Visual.IsEffectivelyVisibleChanged</c> is internal. The shell hides a tab by
    /// setting the tab's IsVisible, so an FX loop that only watched itself kept ticking - and an
    /// adorner kept drawing - under the next page (the Play spiral bleeding through Home).
    /// </summary>
    internal static class EffectiveVisibility
    {
        /// <summary>Calls <paramref name="changed"/> whenever <paramref name="v"/> or any current
        /// visual ancestor flips IsVisible. Hook on attach, dispose on detach.</summary>
        public static IDisposable Watch(Visual v, Action changed)
        {
            var chain = v.GetSelfAndVisualAncestors().ToArray();
            void On(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.IsVisibleProperty) changed();
            }
            foreach (var a in chain) a.PropertyChanged += On;
            return new Unhook(() => { foreach (var a in chain) a.PropertyChanged -= On; });
        }

        private sealed class Unhook(Action undo) : IDisposable
        {
            private Action? _undo = undo;
            public void Dispose() { _undo?.Invoke(); _undo = null; }
        }
    }
}
