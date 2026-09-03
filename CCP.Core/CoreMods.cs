using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The mod seam. Engine code that needs to know "which mod is active" reads it here rather
    /// than through <c>App.Mods</c>, which lives on a <c>System.Windows.Application</c> subclass
    /// and therefore cannot exist in Core.
    ///
    /// Deliberately three delegates and no interface. The only Core consumer today is
    /// <see cref="Localization.VocabTokens"/>, and an interface with one implementation is a
    /// speculative abstraction - the WPF head, a future Avalonia head and a VR head all seed
    /// this the same way. Promote it to an interface when a second consumer needs something
    /// these three cannot express, not before.
    ///
    /// <para><b>Why the active mod is <see cref="object"/>.</b> VocabTokens uses the manifest
    /// purely as a cache-invalidation token - it does <c>ReferenceEquals</c> against the previous
    /// value and never reads a property. Typing it as <c>object?</c> keeps <c>ModManifest</c>
    /// (which is blocked on head-side types) out of Core entirely. If a Core consumer ever needs
    /// real manifest data, that is the moment to move the model, not now.</para>
    ///
    /// <para>Unseeded is a supported state, not a bug: localization initialises before the mod
    /// system, so the earliest reads legitimately happen with no provider attached. Every
    /// accessor returns null and callers fall back to vanilla values.</para>
    ///
    /// <para>Volatile because the head seeds these on the startup thread while engine code may
    /// read them from background threads that never trigger the head's type initializer, and so
    /// get no acquire barrier - the same hazard a code review caught in <see cref="CorePaths"/>.</para>
    /// </summary>
    public static class CoreMods
    {
        /// <summary>
        /// Identity of the active mod, or null when none is active or the mod layer is not up
        /// yet. Used only for reference comparison; never dereferenced.
        /// </summary>
        public static volatile Func<object?>? ActiveModTokenProvider;

        /// <summary>Mod's override for the pet name, or null to use the vanilla term.</summary>
        public static volatile Func<string?>? PetNameOverrideProvider;

        /// <summary>Mod's override for the collective noun, or null to use the vanilla term.</summary>
        public static volatile Func<string?>? CollectiveOverrideProvider;

        /// <summary>
        /// Identity token for the active mod. Swallows provider faults: a throwing mod layer must
        /// never take a UI string with it, which is the contract the WPF call site already had.
        /// </summary>
        public static object? ActiveModToken
        {
            get { try { return ActiveModTokenProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>Pet-name override, or null. Faults are swallowed - see <see cref="ActiveModToken"/>.</summary>
        public static string? PetNameOverride
        {
            get { try { return PetNameOverrideProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>The active mod's display name for its content mode, or null when no mod layer
        /// is up. The settings model shows it and falls back to the vanilla label.</summary>
        public static volatile Func<string?>? ModeDisplayNameProvider;

        /// <summary>Mode display name, or null. Faults are swallowed - see <see cref="ActiveModToken"/>.</summary>
        public static string? ModeDisplayName
        {
            get { try { return ModeDisplayNameProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>Rewrites mod-specific vocabulary in a user-facing string for the active mod, or
        /// null to leave it alone. Session names and descriptions go through this.</summary>
        public static volatile Func<string, string?>? MakeModAwareProvider;

        /// <summary>The string adjusted for the active mod, or the input unchanged when no mod
        /// layer is up. Faults are swallowed - see <see cref="ActiveModToken"/>.</summary>
        public static string MakeModAware(string text)
        {
            try { return MakeModAwareProvider?.Invoke(text) ?? text; } catch { return text; }
        }

        // ---- What the ported views ask of the mod service ------------------------------------
        // The service itself stays in the WPF head: on a switch it clears caches on a dozen head
        // services, and moving that is a project. So the head seeds these, and a head with no mod
        // layer (Linux today) gets exactly what the service answers with no mod active: the
        // built-in CCP default manifest, which is in Core.

        public static volatile Func<string?>? ActiveModIdProvider;
        public static volatile Func<IReadOnlyDictionary<string, ModPackage>?>? InstalledModsProvider;
        public static volatile Func<string?>? AccentColorHexProvider;
        public static volatile Func<string?>? SecondaryColorHexProvider;
        public static volatile Func<string?>? AffirmationProvider;
        public static volatile Func<string, string[]?>? PhrasesProvider;
        public static volatile Func<string?>? PinkRushNameProvider;

        private static readonly Lazy<IReadOnlyDictionary<string, ModPackage>> VanillaMods = new(() =>
            new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase)
            {
                [BuiltInMods.CCPDefaultId] = new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true),
            });

        public static string ActiveModId
        {
            get { try { return ActiveModIdProvider?.Invoke() ?? BuiltInMods.CCPDefaultId; } catch { return BuiltInMods.CCPDefaultId; } }
        }

        public static bool IsCCPDefault => string.Equals(ActiveModId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);

        public static IReadOnlyDictionary<string, ModPackage> InstalledMods
        {
            get { try { return InstalledModsProvider?.Invoke() ?? VanillaMods.Value; } catch { return VanillaMods.Value; } }
        }

        public static string AccentColorHex
        {
            get { try { return AccentColorHexProvider?.Invoke() ?? BuiltInMods.CCPDefault.Theme?.AccentColor ?? "#E84393"; } catch { return "#E84393"; } }
        }

        /// <summary>Vanilla is the service's own CCP-default branch.</summary>
        public static string SecondaryColorHex
        {
            get { try { return SecondaryColorHexProvider?.Invoke() ?? "#8B5CF6"; } catch { return "#8B5CF6"; } }
        }

        public static string Affirmation
        {
            get { try { return AffirmationProvider?.Invoke() ?? BuiltInMods.CCPDefault.Identity?.Affirmation ?? "Subject"; } catch { return "Subject"; } }
        }

        /// <summary>The mod's phrase list for a category, or null with no mod layer, which is what
        /// <c>App.Mods?.GetPhrases(...)</c> returned; callers already carry their own fallback.</summary>
        public static string[]? GetPhrases(string category)
        {
            try { return PhrasesProvider?.Invoke(category); } catch { return null; }
        }

        public static string PinkRushName
        {
            get { try { return PinkRushNameProvider?.Invoke() ?? "PINK RUSH!"; } catch { return "PINK RUSH!"; } }
        }

        /// <summary>The active mod changed. The head forwards its service's event here so a view
        /// subscribes once, to Core, on every head.</summary>
        public static event EventHandler<ModPackage>? ModChanged;

        public static void RaiseModChanged(object? sender, ModPackage package)
        {
            try { ModChanged?.Invoke(sender, package); } catch { /* a subscriber's fault is not the switch's */ }
        }

        /// <summary>Collective override, or null. Faults are swallowed - see <see cref="ActiveModToken"/>.</summary>
        public static string? CollectiveOverride
        {
            get { try { return CollectiveOverrideProvider?.Invoke(); } catch { return null; } }
        }
    }
}
