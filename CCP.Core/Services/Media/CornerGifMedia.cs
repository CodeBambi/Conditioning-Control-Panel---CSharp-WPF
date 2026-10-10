// PORTED from ConditioningControlPanel/Services/Media/CornerGifMedia.cs (7.1.5): the admission rules.
// The decode half lives in each head (CornerGifOverlay on Avalonia).
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Who may hold the screen corner: the session-scoped corner GIF (a session or a 28-day program
    /// day raises it) or the user's own standalone slots. One rule read by both sides, so the two can
    /// never both be up and neither can drift from the other.
    /// </summary>
    public static class CornerGifMedia
    {
        /// <summary>A session that carries no corner art of its own draws the SPIRAL.</summary>
        public static bool SessionCornerArtIsSpiral(string? cornerGifPath)
        {
            if (string.IsNullOrWhiteSpace(cornerGifPath)) return true;
            try { return !File.Exists(cornerGifPath); }
            catch { return true; }
        }

        /// <summary>
        /// May a SESSION (or a program day) raise its corner GIF right now? The template asked for it,
        /// the user's master (AppSettings.SessionCornerGifAllowed) allows it, and no standalone corner
        /// overlay is up or queued (the user's own choice wins, the session does not stack a second one).
        /// When the art is the spiral, the Spiral card's own master (the user's SpiralEnabled as it stood
        /// before the session) vetoes it and a fullscreen spiral already on screen makes it a duplicate.
        /// </summary>
        public static bool AllowSessionCornerGif(
            bool templateEnabled,
            bool userAllowed,
            bool standaloneOverlayActive,
            bool artIsSpiral,
            bool userSpiralAllowed,
            bool spiralOverlayActive)
            => templateEnabled
               && userAllowed
               && !standaloneOverlayActive
               && (!artIsSpiral || (userSpiralAllowed && !spiralOverlayActive));

        /// <summary>May a STANDALONE slot realize right now? It yields while a session corner GIF is on
        /// screen. It does NOT read the session master: that is about what a session may raise.</summary>
        public static bool AllowStandaloneCornerGif(bool slotEnabled, bool sessionCornerGifActive)
            => slotEnabled && !sessionCornerGifActive;
    }
}
