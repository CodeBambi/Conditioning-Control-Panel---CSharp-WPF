using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Keep pictures clear" for Brain Drain (owner + Perly, 2026-09-28): the blur covers the
    /// desktop and every other app, but CCP's own pictures (flashes, videos, lock cards,
    /// subliminals, bubbles) stay sharp on top of it.
    ///
    /// <para>The blur lives in its own topmost window (the compositor's capture-excluded host, or
    /// the legacy per-screen windows), so this is a z-order rule, not a paint rule: the blur has to
    /// sit directly under the LOWEST visible topmost window this process owns. Everything of ours
    /// is above it, every other app's window is below it. The capture already leaves layered
    /// windows out, so nothing of ours is ever inside the blurred copy either.</para>
    ///
    /// <para>Pure so the ordering is testable without windows. The caller walks the topmost band
    /// top to bottom and asks where the blur must go.</para>
    /// </summary>
    internal static class BrainDrainKeepClear
    {
        internal readonly record struct BandEntry(IntPtr Hwnd, bool Ours, bool Drain);

        /// <summary>
        /// The window every blur window must be inserted under, or <see cref="IntPtr.Zero"/> when
        /// nothing needs to move: no blur in the band, nothing of ours to keep clear, or every blur
        /// window already sits below the lowest of our windows.
        /// </summary>
        internal static IntPtr AnchorIfNeeded(IReadOnlyList<BandEntry> band)
        {
            int lowestOurs = -1, highestDrain = -1;
            for (int i = 0; i < band.Count; i++)
            {
                var e = band[i];
                if (e.Drain) { if (highestDrain < 0) highestDrain = i; }
                else if (e.Ours) lowestOurs = i;
            }
            if (lowestOurs < 0 || highestDrain < 0) return IntPtr.Zero;
            return highestDrain < lowestOurs ? band[lowestOurs].Hwnd : IntPtr.Zero;
        }
    }
}
