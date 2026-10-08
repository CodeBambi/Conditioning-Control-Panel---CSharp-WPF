using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The art views a card can wear, by key (WPF 7.1.5 BillboardContract.BillboardArt). Each lane
    /// registers its views once at startup; the deck asks for a fresh view per card. A view that
    /// implements <see cref="IBillboardArtView"/> gets play/pause and is told when it leaves.
    /// The card rules themselves live in Core (ConditioningControlPanel.Services.Billboard).
    /// </summary>
    public static class BillboardArt
    {
        private static readonly Dictionary<string, Func<object?, Control>> Factories =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, Func<object?, Control> factory) => Factories[key] = factory;

        public static bool IsRegistered(string key) => Factories.ContainsKey(key);

        /// <summary>A new view for the key, or null when no lane registered it.</summary>
        public static Control? Create(string key, object? data) =>
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

    /// <summary>A view the host can tint with the card's hue after it is made.</summary>
    internal interface IAccentedArt
    {
        Color Accent { set; }
    }

    /// <summary>
    /// The board's motion gates, read the way every ambient loop in this head reads them
    /// (<see cref="AmbientFxCanvas"/>.Env = WPF MotionFx). Tests pin a level through the overrides.
    /// </summary>
    internal static class BoardMotion
    {
        internal static Func<bool>? TransitionsOverride, AmbientOverride, ParticlesOverride;

        public static bool AllowTransitions => TransitionsOverride?.Invoke() ?? AmbientFxCanvas.Env.AllowTransitions;
        public static bool AllowAmbientLoops => AmbientOverride?.Invoke() ?? AmbientFxCanvas.Env.AllowAmbientLoops;
        public static bool AllowParticles => ParticlesOverride?.Invoke() ?? AmbientFxCanvas.Env.AllowParticles;

        /// <summary>The resolved level (Off = still cards, no auto-advance).</summary>
        public static global::ConditioningControlPanel.Models.MotionLevel Level =>
            TransitionsOverride != null
                ? (TransitionsOverride() ? (AllowAmbientLoops ? global::ConditioningControlPanel.Models.MotionLevel.Full : global::ConditioningControlPanel.Models.MotionLevel.Reduced) : global::ConditioningControlPanel.Models.MotionLevel.Off)
                : AmbientFxCanvas.Env.Level;
    }
}
