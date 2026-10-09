// Profile pictures for the friends chip, the friends drawer and the Trainer Card (WPF
// FriendsLook.PaintPhotoAsync + MainWindow.Browser.cs's ProfileViewerAvatar loads). One cache by
// url for the process: a picture is fetched and decoded once, off the UI thread, and the decoded
// Bitmap is never written again, so handing it to any number of brushes is safe.
//
// Consent: a friend's url only exists when that account's presence rung allows it (the server
// returns null otherwise, Services/Friends/CONTRACT.md); your own picture is drawn only when
// ShareProfilePicture is on and Discord is signed in (WPF Browser.cs:1909).
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    internal static class AvatarPhotos
    {
        /// <summary>The proxy a first-party avatar path (<c>/v2/friends/avatar/&lt;id&gt;</c>) resolves against.</summary>
        internal const string ProxyBase = "https://codebambi-proxy.vercel.app";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();

        /// <summary>The fetch; tests swap it for canned bytes (no network from tests).</summary>
        internal static Func<string, Task<byte[]>> Fetch { get; set; } = url => Http.GetByteArrayAsync(url);

        /// <summary>Your own picture's url, or null when you do not share one. Tests swap it.</summary>
        internal static Func<int, string?> OwnUrl { get; set; } = size =>
        {
            try
            {
                return CoreSettings.Current.ShareProfilePicture && Platform.AccountSeed.Discord?.IsAuthenticated == true
                    ? Platform.AccountSeed.Discord.GetAvatarUrl(size)
                    : null;
            }
            catch { return null; }
        };

        /// <summary>An absolute https url, or null (relative paths are the proxy's; anything else is refused).</summary>
        internal static string? Resolve(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var full = url.StartsWith("/", StringComparison.Ordinal) ? ProxyBase + url : url;
            return full.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>The decoded picture, or null when it cannot be had. A failed load is forgotten so a later call retries.</summary>
        internal static Task<Bitmap?> LoadAsync(string? url, int decodeWidth = 256)
        {
            if (Resolve(url) is not { } full) return Task.FromResult<Bitmap?>(null);
            var task = Cache.GetOrAdd(full + "#" + decodeWidth, _ => Task.Run(async () =>
            {
                try
                {
                    var bytes = await Fetch(full).ConfigureAwait(false);
                    using var ms = new MemoryStream(bytes);
                    return (Bitmap?)Bitmap.DecodeToWidth(ms, decodeWidth);
                }
                catch (Exception ex)
                {
                    Log.Debug("Avatar photo load failed: {E}", ex.Message);
                    return null;
                }
            }));
            _ = task.ContinueWith(t => { if (t.Result == null) Cache.TryRemove(full + "#" + decodeWidth, out _); },
                TaskScheduler.Default);
            return task;
        }

        /// <summary>Paints the picture onto the disc and hides the initials once it lands; any failure keeps the initials.</summary>
        internal static void Paint(Border disc, Control initials, string? url, int decodeWidth = 96)
        {
            if (Resolve(url) == null) return;
            _ = PaintAsync(disc, initials, url!, decodeWidth);
        }

        private static async Task PaintAsync(Border disc, Control initials, string url, int decodeWidth)
        {
            var bmp = await LoadAsync(url, decodeWidth).ConfigureAwait(false);
            if (bmp == null) return;
            void Apply()
            {
                disc.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                initials.IsVisible = false;
                disc.Tag = "avatar-photo";
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }

        /// <summary>Tests: drop every cached picture.</summary>
        internal static void ClearForTests() => Cache.Clear();
    }
}
