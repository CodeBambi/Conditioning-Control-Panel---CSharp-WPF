using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// Downloads video files from URLs for audio extraction.
    /// Supports progress reporting and cancellation.
    /// </summary>
    public class VideoDownloader : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        private const int BUFFER_SIZE = 81920; // 80KB buffer
        private const int MAX_RETRIES = 3;
        private const int RETRY_DELAY_MS = 2000;

        /// <summary>
        /// Progress event with bytes downloaded and total bytes (if known)
        /// </summary>
        public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

        public VideoDownloader()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10) // Long timeout for large videos
            };

            // Set user agent to avoid blocks
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>
        /// Downloads a video URL to a temporary file
        /// </summary>
        /// <param name="videoUrl">URL of the video to download</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Path to the downloaded temp file</returns>
        public async Task<string> DownloadAsync(string videoUrl, CancellationToken ct = default)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"haptic_video_{Guid.NewGuid()}.tmp");

            Exception? lastException = null;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    await DownloadToFileAsync(videoUrl, tempPath, ct);
                    Log.Information("VideoDownloader: Downloaded video to {Path}", tempPath);
                    return tempPath;
                }
                catch (OperationCanceledException)
                {
                    // Clean up on cancellation
                    TryDeleteFile(tempPath);
                    throw;
                }
                catch (Exception ex) when (attempt < MAX_RETRIES)
                {
                    lastException = ex;
                    Log.Warning("VideoDownloader: Download attempt {Attempt} failed: {Error}. Retrying...",
                        attempt, ex.Message);

                    TryDeleteFile(tempPath);
                    await Task.Delay(RETRY_DELAY_MS * attempt, ct);
                }
            }

            throw new AudioSyncException($"Failed to download video after {MAX_RETRIES} attempts", lastException);
        }

        private async Task DownloadToFileAsync(string url, string filePath, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            long downloadedBytes = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, true);

            var buffer = new byte[BUFFER_SIZE];
            int bytesRead;
            var lastProgressReport = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                // Report progress every 100ms
                if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds > 100)
                {
                    lastProgressReport = DateTime.UtcNow;
                    ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(downloadedBytes, totalBytes));
                }
            }

            // Final progress report
            ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(downloadedBytes, totalBytes));
        }

        /// <summary>
        /// Checks if a URL is likely a video that can be downloaded
        /// </summary>
        public static bool IsLikelyVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            var lowerUrl = url.ToLowerInvariant();

            // Check for common video extensions
            if (lowerUrl.Contains(".mp4") || lowerUrl.Contains(".webm") ||
                lowerUrl.Contains(".m4v") || lowerUrl.Contains(".mov") ||
                lowerUrl.Contains(".avi") || lowerUrl.Contains(".mkv"))
            {
                return true;
            }

            // Check for common video CDN patterns
            if (lowerUrl.Contains("/video/") || lowerUrl.Contains("/media/") ||
                lowerUrl.Contains("cdn") || lowerUrl.Contains("stream"))
            {
                return true;
            }

            return false;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Progress event arguments for video download
    /// </summary>
    public class DownloadProgressEventArgs : EventArgs
    {
        public long BytesDownloaded { get; }
        public long? TotalBytes { get; }
        public double? PercentComplete => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : null;

        public DownloadProgressEventArgs(long bytesDownloaded, long? totalBytes)
        {
            BytesDownloaded = bytesDownloaded;
            TotalBytes = totalBytes;
        }
    }

    /// <summary>
    /// Exception for audio sync related errors
    /// </summary>
    public class AudioSyncException : Exception
    {
        public AudioSyncException(string message) : base(message) { }
        public AudioSyncException(string message, Exception? inner) : base(message, inner) { }
    }
}
