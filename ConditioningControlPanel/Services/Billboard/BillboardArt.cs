using System;
using System.Collections.Generic;
using System.Windows;

namespace ConditioningControlPanel.Services.Billboard
{
    // The WPF half of the Tonight Board contract (BillboardContract.cs lives in CCP.Core):
    // art views are FrameworkElements, so their registry stays in this head.
    /// <summary>
    /// The art views a card can wear, by key. Each lane registers its own views once at
    /// startup; the deck asks for a fresh view per card. A view that implements
    /// <see cref="IBillboardArtView"/> gets play/pause and is told when it leaves.
    /// </summary>
    public static class BillboardArt
    {
        private static readonly Dictionary<string, Func<object?, FrameworkElement>> Factories =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, Func<object?, FrameworkElement> factory) => Factories[key] = factory;

        public static bool IsRegistered(string key) => Factories.ContainsKey(key);

        /// <summary>A new view for the key, or null when no lane registered it.</summary>
        public static FrameworkElement? Create(string key, object? data) =>
            Factories.TryGetValue(key, out var make) ? make(data) : null;

        /// <summary>Test seam.</summary>
        internal static void ResetForTests() => Factories.Clear();
    }

    /// <summary>Optional lifecycle for an art view. All calls come on the UI thread.</summary>
    public interface IBillboardArtView
    {
        /// <summary>The card is on screen and motion is allowed: start clocks, players, loops.</summary>
        void Play();

        /// <summary>Another tab, a fold, or Motion Off: hold still on the current frame. Hover never pauses art.</summary>
        void Pause();

        /// <summary>The card left the screen for good. Release clocks, players, bitmaps.</summary>
        void Release();

        /// <summary>A pointer press on the art, in 0..1 coordinates of the view. Most views ignore it.</summary>
        void Touch(Point normalized);
    }
}
