using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Shared source handling for the two corner-GIF overlays: the session-scoped one
    /// (<see cref="Session.SessionEngine"/>, which the 28-day programs raise) and the standalone
    /// one (<see cref="CornerGifService"/>). Both drew the SAME asset the same uncapped way, so a
    /// fix applied to one never covered the other - which is why the program-day freeze survived
    /// #221 and #227.
    ///
    /// <para>What actually froze: nothing here sets <c>CornerGifPath</c> on a program day, so both
    /// paths fell through to the built-in spiral - 2400x1600, 32 frames - and handed it to
    /// XamlAnimatedGif, which builds the render thread a WriteableBitmap at the GIF's NATIVE size
    /// and lets WPF resample 3.84 MP down to a 300px (or, on Kept/Firmware, a 70px) overlay on
    /// EVERY frame, forever, on a WS_EX_LAYERED window whose composition is a full
    /// UpdateLayeredWindow blit rather than a GPU surface flip. #221/#227 only cheapened the
    /// filter (Fant -> bilinear); the source stayed enormous. The spiral OVERLAY had already been
    /// through this in #572 (OverlayService.DecodeGifFrames: cap the dimension, cap the frames,
    /// budget the bytes, decode off the UI thread) - this is that same discipline, for the corner.</para>
    ///
    /// <para>Frames are decoded ONCE, off the UI thread, downscaled to the size the overlay
    /// actually draws at, and played back as a frozen keyframe animation
    /// (<see cref="AnimatedWebp.AttachAnimation(Image, Uri, int, int)"/>). The render thread then
    /// blits a frame that is already the right size - no per-frame resample at all.</para>
    /// </summary>
    internal static class CornerGifMedia
    {
        /// <summary>
        /// Source GIFs beyond this many pixels are logged as a hazard. Deliberately BELOW the
        /// built-in spiral's 3.84 MP: the old 4 MP guard sat 4% above the one asset that was
        /// actually causing the freezes, so every corner-GIF hang report was silent about it.
        /// A megapixel is already ~10x more source than a 300px corner overlay can show.
        /// </summary>
        internal const long OversizeSourcePixels = 1_000_000;

        /// <summary>
        /// Hard ceiling on the decoded frame set's long edge, whatever the overlay's size setting
        /// says. 512 covers the largest corner overlay the UI offers (and a hand-edited settings
        /// file's larger one) at 2x DPI without ever approaching the source's native size.
        /// </summary>
        internal const int MaxDecodeDimension = 512;

        /// <summary>Frame-count cap for a corner overlay (the built-in spiral has 32).</summary>
        internal const int MaxDecodeFrames = 48;

        /// <summary>
        /// The corner overlay's default art when the user has not picked a file.
        ///
        /// <para>An active mod's (or event skin's) own spiral still wins - the corner GIF has
        /// always drawn the mod's art and that is branding, not an accident. Otherwise this is
        /// deliberately NOT the fullscreen spiral: <c>Resources/spiral_corner.gif</c> is the same
        /// loop pre-scaled to 360x240, so the default corner overlay costs ~1/44th of the pixels
        /// even before the decode cap gets involved, and no program template needs a data edit.</para>
        /// </summary>
        internal static string ResolveDefaultUriString()
        {
            try
            {
                if (ModResourceResolver.HasModOverride("spirals/spiral.gif")
                    || ModResourceResolver.HasModOverride("spiral.gif"))
                    return ModResourceResolver.ResolveSpiralUri();
            }
            catch { /* fall through to the built-in corner asset */ }

            return ModResourceResolver.ResolveUri("spiral_corner.gif");
        }

        /// <summary>
        /// Read a GIF's pixel dimensions WITHOUT decoding it. The old code called
        /// <c>System.Drawing.Image.FromFile</c> purely to read Width/Height and threw the object
        /// away, so the built-in spiral was decoded twice per show (GDI+ here, XamlAnimatedGif
        /// again for playback). <see cref="BitmapCacheOption.None"/> + DelayCreation keeps WIC on
        /// the frame header. Works for both file:// and pack:// sources.
        /// </summary>
        internal static bool TryGetPixelSize(Uri uri, out double width, out double height)
        {
            width = 0;
            height = 0;

            try
            {
                var decoder = BitmapDecoder.Create(uri,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None);
                if (decoder.Frames.Count > 0)
                {
                    width = decoder.Frames[0].PixelWidth;
                    height = decoder.Frames[0].PixelHeight;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CornerGifMedia: header read failed for {Uri} ({Error}) - retrying via stream", uri, ex.Message);
                try
                {
                    using var stream = OpenSource(uri);
                    if (stream == null) return false;
                    var decoder = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                        BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        width = decoder.Frames[0].PixelWidth;
                        height = decoder.Frames[0].PixelHeight;
                    }
                }
                catch (Exception ex2)
                {
                    App.Logger?.Warning("CornerGifMedia: could not read corner GIF dimensions for {Uri}: {Error}", uri, ex2.Message);
                    return false;
                }
            }

            return width > 0 && height > 0;
        }

        /// <summary>Opens a corner-GIF source, handling pack:// resources and plain files alike.</summary>
        internal static Stream? OpenSource(Uri uri)
        {
            if (uri.IsFile)
                return new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Application.GetResourceStream(uri)?.Stream;
        }

        /// <summary>
        /// One warning line when the source is far larger than the overlay draws it. Fires on the
        /// built-in spiral now that the threshold is honest, so the next corner-GIF hang report
        /// carries the dimensions instead of nothing.
        /// </summary>
        internal static void WarnIfOversize(string tag, Uri uri, double srcW, double srcH, double dstW, double dstH)
        {
            long sourcePixels = (long)srcW * (long)srcH;
            if (sourcePixels <= OversizeSourcePixels) return;

            App.Logger?.Warning(
                "{Tag}: source GIF is {W}x{H} ({MP:F1}MP) but the overlay draws it at {TW}x{TH} ({Path}) - frames are decoded down to the overlay size, but a smaller source GIF would still be cheaper to load.",
                tag, (int)srcW, (int)srcH, sourcePixels / 1_000_000.0, (int)dstW, (int)dstH, uri);
        }

        /// <summary>
        /// Start the corner overlay's animation on <paramref name="img"/>: decode off the UI
        /// thread, downscaled to the physical pixels the overlay actually occupies, then play the
        /// frozen frames back as a keyframe animation. Pair with <see cref="Detach"/> at teardown -
        /// a RepeatBehavior.Forever clock pins its target until it is cleared.
        /// </summary>
        internal static void Attach(Image img, Uri uri, double renderWidth, double renderHeight, double dpiScale)
        {
            double longEdge = Math.Max(renderWidth, renderHeight) * (dpiScale > 0 ? dpiScale : 1.0);
            int maxDim = (int)Math.Clamp(Math.Ceiling(longEdge), 64, MaxDecodeDimension);
            AnimatedWebp.AttachAnimation(img, uri, maxDim, MaxDecodeFrames);
        }

        /// <summary>Release the animation clock's pin on <paramref name="img"/>.</summary>
        internal static void Detach(Image img) => AnimatedWebp.Detach(img);

        // ---- who is allowed to raise a corner overlay (ticket 1539282547484139682) ----
        //
        // Two independent services can put a spiral in a screen corner: SessionEngine (session and
        // 28-day program days) and CornerGifService (the Spiral card's standalone slots). Neither
        // knew about the other, so a program day could stack its corner spiral on top of a
        // standalone one - two spirals, and neither answered the user's own switches. The source
        // handling was already shared here; the ADMISSION rule belongs here too, for the same
        // reason: a rule applied in only one of the two places is the bug all over again.
        //
        // Pure and static so both call sites - and the tests - reach the same answer.

        /// <summary>
        /// May a SESSION (or a 28-day program day) raise its corner GIF right now?
        ///
        /// <para><paramref name="templateEnabled"/> is the session/program template's own
        /// <c>CornerGifEnabled</c>, which used to be the ONLY gate.
        /// <paramref name="userAllowed"/> is the user's master (<c>AppSettings.SessionCornerGifAllowed</c>) -
        /// the switch the support workaround assumed existed.
        /// <paramref name="standaloneOverlayActive"/> is "a standalone corner overlay is already on
        /// screen (or queued)": the user's own app-wide choice wins, and the session does not stack
        /// a second spiral behind it.</para>
        /// </summary>
        internal static bool AllowSessionCornerGif(bool templateEnabled, bool userAllowed, bool standaloneOverlayActive)
            => templateEnabled && userAllowed && !standaloneOverlayActive;

        /// <summary>
        /// May a STANDALONE corner-GIF slot realize right now? The mirror of
        /// <see cref="AllowSessionCornerGif"/>: a slot the user enabled still yields while a
        /// session-scoped corner GIF is on screen, so the two can never both be up. The session's
        /// overlay is torn down at session end, and CornerGifService is refreshed there, so the
        /// standalone slots come back on their own.
        ///
        /// <para>Note this does NOT read the session master: <c>SessionCornerGifAllowed</c> is about
        /// what a session may raise, never about the user's own overlays.</para>
        /// </summary>
        internal static bool AllowStandaloneCornerGif(bool slotEnabled, bool sessionCornerGifActive)
            => slotEnabled && !sessionCornerGifActive;
    }
}
