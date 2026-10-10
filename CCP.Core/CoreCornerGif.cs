using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The corner-GIF SURFACE seam: "the standalone corner-overlay settings changed - put the
    /// picture where they now say".
    ///
    /// <para><see cref="Services.CornerGifPlanner"/> decides everything about a corner GIF except
    /// the one thing a head must do: create a transparent, click-through, always-on-top window and
    /// play an animation in it. This is that handback. The WPF head seeds it to
    /// <c>App.CornerGif</c>; the Avalonia head to <c>CornerGifOverlay</c>.</para>
    ///
    /// <para>One entry point rather than three, matching what the config UI actually asks for: a
    /// negative index means "rebuild every slot" (a corner pick or an enable toggle shifts the
    /// same-corner nudge, so both slots move), and a non-negative index means "rebuild only this
    /// slot" - the live size/opacity slider edit, which must leave the other slot's window and its
    /// running animation untouched.</para>
    ///
    /// <para>Unseeded this is a NO-OP, and that is the honest answer rather than merely a safe one:
    /// a head with no overlay surface has no window to rebuild. Settings are saved by the caller
    /// either way, so the slot is correct on disk and appears the moment a surface exists. It never
    /// throws - it rides on every slider tick and on the panic path's teardown.</para>
    /// </summary>
    public static class CoreCornerGif
    {
        /// <summary>Rebuild one slot's overlay (index >= 0) or all of them (index &lt; 0).</summary>
        public static volatile Action<int>? RefreshHandler;

        /// <summary>True when a head has actually seeded a surface, so a caller can say "saved,
        /// not shown" instead of implying a picture appeared.</summary>
        public static bool HasSurface => RefreshHandler != null;

        /// <summary>
        /// Re-realize the corner overlays from the current settings. <paramref name="index"/> below
        /// zero rebuilds every slot. Safe to call from any thread and at any time; the seeded
        /// handler owns its own thread marshalling.
        /// </summary>
        public static void Refresh(int index = -1)
        {
            try { RefreshHandler?.Invoke(index); } catch { /* a live-apply nudge must never throw */ }
        }

        // ---- the session-scoped corner GIF (WPF SessionEngine.ShowCornerGif / CloseCornerGif) ----

        /// <summary>Raise the session's corner GIF: path ("" = the built-in corner art), corner, size, opacity.</summary>
        public static volatile Action<string, Models.CornerPosition, int, int>? ShowSessionHandler;

        /// <summary>Take it down. True = the corner goes back to the user's own slots (a terminal close);
        /// false = hide only (a pause, a panic press, a recreate).</summary>
        public static volatile Action<bool>? HideSessionHandler;

        /// <summary>A standalone corner overlay is up or queued (WPF CornerGifService.HasActiveOverlays).</summary>
        public static volatile Func<bool>? StandaloneActiveProvider;

        /// <summary>A fullscreen spiral is on screen right now, whoever raised it (WPF OverlayService.IsSpiralVisible).</summary>
        public static volatile Func<bool>? SpiralVisibleProvider;

        /// <summary>A session corner GIF is on screen or on its way (WPF SessionEngine.IsSessionCornerGifActive).</summary>
        public static volatile bool SessionActive;

        /// <summary>Raised when the standalone side changed, so a running session re-asks its admission
        /// (WPF CornerGifService.RefreshOverlays -> SessionEngine.RefreshCornerGifPolicy).</summary>
        public static event Action? StandaloneChanged;

        public static void RaiseStandaloneChanged() { try { StandaloneChanged?.Invoke(); } catch { } }

        public static bool StandaloneActive { get { try { return StandaloneActiveProvider?.Invoke() == true; } catch { return false; } } }
        public static bool SpiralVisible { get { try { return SpiralVisibleProvider?.Invoke() == true; } catch { return false; } } }

        public static void ShowSession(string path, Models.CornerPosition position, int size, int opacity)
        {
            try { ShowSessionHandler?.Invoke(path, position, size, opacity); } catch { }
        }

        public static void HideSession(bool handBack)
        {
            try { HideSessionHandler?.Invoke(handBack); } catch { }
        }
    }
}
