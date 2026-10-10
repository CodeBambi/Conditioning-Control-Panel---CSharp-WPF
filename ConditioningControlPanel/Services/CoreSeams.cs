using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Billboard.Board;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.Stakes;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF answers to the seams of rules that live once in CCP.Core (Lobby, PvP stakes, Goon
    /// online media, the Racing Thoughts door, the Tonight Board picture). Each seam gets exactly what the WPF 7.1.5 copy of the class read
    /// directly (BackRoomApi, App.Chaster, the dispatcher, WPF imaging), so moving the class to
    /// Core changes nothing a player can see. Idempotent; called first thing in App startup and
    /// once by the test assembly.
    /// </summary>
    internal static class CoreSeams
    {
        private static bool _wired;

        public static void Wire()
        {
            if (_wired) return;
            _wired = true;

            // The account door and the proxy.
            LobbyWire.DefaultIdentity = () => BackRoomApi.AppIdentity();
            LobbyWire.DefaultBaseUrl = () => BackRoomApi.BaseUrl;
            StakeApi.DefaultIdentity = () => BackRoomApi.AppIdentity();
            StakeApi.DefaultBaseUrl = () => BackRoomApi.BaseUrl;

            // PvP stakes: Circe's Tab and the UI thread.
            StakeBridge.ChasterState = () => App.Chaster is { } c ? (c.IsLinked, c.SafetyHoldRemaining) : null;
            StakeBridge.OnUi = apply =>
            {
                var d = App.Current?.Dispatcher;
                if (d == null || d.CheckAccess()) apply(); else d.Invoke(apply);
            };
            StakeSettlement.BookTime = (row, seconds) => App.Chaster?.NoteSeconds(row, seconds).AppliedSeconds ?? 0;
            StakeSettlement.Account = () => BackRoomApi.AppIdentity()?.UnifiedId;
            StakeSettlement.OnUi = write => App.Current?.Dispatcher?.BeginInvoke(write);

            // Goon online media: the app's downloader into {assets}/.temp.
            GoonGame.GoonOnlineMedia.Materialize = (url, ct) => Fyp.Online.RemoteMediaCache.MaterializeAsync(url, ct, ownerReleases: true);
            GoonGame.GoonOnlineMedia.ReleaseTempFile = Fyp.Online.RemoteMediaCache.ReleaseTempFile;

            // Racing Thoughts: which original tracks this account owns.
            Race.RacingAccess.IsGrantedProvider = Prizes.PrizeGrants.IsGranted;

            // The Tonight Board picture: WPF imaging, any thread.
            BoardPicture.PngDecoder = DecodePng;
        }

        /// <summary>PNG bytes to straight Bgra32 pixels (0xAARRGGBB ints, row-major). The size
        /// gate runs before the format conversion, as it did when this lived in BoardPicture.</summary>
        private static (int[] Pixels, int Width, int Height)? DecodePng(byte[] png)
        {
            using var ms = new MemoryStream(png, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            BitmapSource src = decoder.Frames[0];
            if (src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
            if (src.PixelWidth > BoardWire.MaxFrameWidth * BoardWire.MaxFrames || src.PixelHeight > 512) return null;
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new int[w * h];
            src.CopyPixels(px, w * 4, 0);
            return (px, w, h);
        }
    }
}
