using System;

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

        /// <summary>Collective override, or null. Faults are swallowed - see <see cref="ActiveModToken"/>.</summary>
        public static string? CollectiveOverride
        {
            get { try { return CollectiveOverrideProvider?.Invoke(); } catch { return null; } }
        }
    }
}
