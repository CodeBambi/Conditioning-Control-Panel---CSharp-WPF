using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The bouncing-text control seam: the four things a settings card asks of the running
    /// bouncing-text feature.
    ///
    /// <para>This is a CONTROL handle, not a draw surface. The motion, colours and XP gating now
    /// live in <see cref="Services.BouncingTextEngine"/> and are the same on every head; what
    /// differs is who owns the surface those logos are painted on. On Windows that is
    /// <c>BouncingTextService</c> with its Win32 layered click-through windows, and these four
    /// members forward to it. A head with no surface leaves them null.</para>
    ///
    /// <para>Unseeded every member is a silent no-op, which is the same answer the WPF call sites
    /// already got from <c>App.BouncingText?.Refresh()</c> before the service was constructed. It
    /// cannot lie about state, because nothing here reports state: the card's checkbox is driven by
    /// <c>AppSettings.BouncingTextEnabled</c>, which is saved whether or not a surface picks it up,
    /// so an unseeded head shows "on, and nothing is drawn" rather than "off".</para>
    ///
    /// <para>Faults are swallowed for the reason the WPF card wrapped both calls in try/catch: a
    /// throwing overlay must not take down the settings panel that nudged it.</para>
    /// </summary>
    public static class CoreBouncingText
    {
        /// <summary>Show the bouncing text. The card calls this only while a session engine is up.</summary>
        public static volatile Action? StartAction;

        /// <summary>Hide it and release the surfaces.</summary>
        public static volatile Action? StopAction;

        /// <summary>Re-read the live settings (speed, size, opacity, font, fixed colour,
        /// show-over-videos). A no-op when nothing is running - the head's own guard, not this one's.</summary>
        public static volatile Action? RefreshAction;

        /// <summary>Rebuild, for the settings that change the rendered element itself (the outlined
        /// style, the second logo). Also a no-op when nothing is running.</summary>
        public static volatile Action? RestartAction;

        public static void Start()   { try { StartAction?.Invoke(); }   catch { } }
        public static void Stop()    { try { StopAction?.Invoke(); }    catch { } }
        public static void Refresh() { try { RefreshAction?.Invoke(); } catch { } }
        public static void Restart() { try { RestartAction?.Invoke(); } catch { } }
    }
}
