// PORTED from ConditioningControlPanel/Services/Billboard/BillboardArt.cs: the head half of the
// Tonight Board contract. Art views are Avalonia controls, so the registry stays in this head.

using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF BillboardArt: the art views a card can wear, by key. The deck asks for a fresh
    /// view per card; a key nobody registered leaves the card on its plain ground.</summary>
    public static class BillboardArt
    {
        private static readonly Dictionary<string, Func<object?, Control>> Factories = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, Func<object?, Control> factory) => Factories[key] = factory;

        /// <summary>A new view for the key, or null when no view is registered for it.</summary>
        public static Control? Create(string key, object? data) =>
            Factories.TryGetValue(key, out var make) ? make(data) : null;
    }

    /// <summary>WPF IBillboardArtView (Play/Pause/Release; Touch has no taker on this head yet).
    /// Named Run/Hold here so the board's silence scan never has to tell a clip from a sound.</summary>
    public interface IBillboardArtView
    {
        /// <summary>The card is on screen and its tab shown: start clocks, players, loops.</summary>
        void Run();

        /// <summary>Another tab or a fold: hold still on the current frame. Hover never holds art.</summary>
        void Hold();

        /// <summary>The card left the screen for good: release clocks, players, bitmaps.</summary>
        void Release();
    }
}
