using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Provides security utilities for the application
    /// </summary>
    public static class SecurityHelper
    {
        /// <summary>
        /// Validates that a file path is within allowed directories (prevents path traversal)
        /// </summary>
        public static bool IsPathSafe(string path, string allowedBasePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                var fullPath = Path.GetFullPath(path);
                var basePath = Path.GetFullPath(allowedBasePath);

                // Ensure the resolved path starts with the allowed base path
                return fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                App.Logger.Warning(ex, "Path validation failed for: {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// True when <paramref name="directory"/> is one of Windows' personal or system roots
        /// rather than a folder someone filled on purpose - the Desktop, Documents, Pictures,
        /// Videos, Music, Downloads, the OneDrive root, the user profile root, or a bare drive
        /// root. Bug #1053: a feature that takes ONE file the user browsed to and then treats
        /// that file's whole parent folder as a content pool turns "I picked a spiral that was
        /// sitting on my Desktop" into "the app is showing me my family photos". Picking a file
        /// out of one of these folders is never a statement about the folder, so callers that
        /// widen a single pick into a folder scan must refuse these roots.
        ///
        /// Deliberately exact-match, not prefix-match: <c>Desktop\spirals</c> IS a folder the
        /// user made and filled, and staying usable matters. Only the bare roots are refused.
        /// </summary>
        public static bool IsPersonalFolderRoot(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return true;

            try
            {
                var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

                // A bare drive root ("D:\") trims to "D:" - and Path.GetPathRoot of any path
                // under it returns "D:\", so compare the trimmed forms.
                var root = Path.GetPathRoot(dir);
                if (!string.IsNullOrEmpty(root) &&
                    string.Equals(dir, Path.TrimEndingDirectorySeparator(root!), StringComparison.OrdinalIgnoreCase))
                    return true;

                foreach (var folder in new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.CommonDesktopDirectory,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyVideos,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.UserProfile,
                    Environment.SpecialFolder.CommonDocuments,
                    Environment.SpecialFolder.CommonPictures,
                    Environment.SpecialFolder.CommonVideos,
                })
                {
                    var known = Environment.GetFolderPath(folder);
                    if (string.IsNullOrEmpty(known)) continue;
                    if (string.Equals(dir, Path.TrimEndingDirectorySeparator(Path.GetFullPath(known)),
                                      StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Downloads and OneDrive have no SpecialFolder id. Downloads is a known folder
                // the user can relocate, but the profile-relative name catches the default, and
                // a relocated Downloads that is not one of the roots above is rare enough to
                // leave to the exact-match rule.
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                {
                    foreach (var name in new[] { "Downloads", "Desktop", "Documents", "Pictures", "Videos", "Music" })
                    {
                        if (string.Equals(dir, Path.Combine(profile, name), StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                {
                    var oneDrive = Environment.GetEnvironmentVariable(variable);
                    if (string.IsNullOrWhiteSpace(oneDrive)) continue;
                    var oneDriveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oneDrive));
                    if (string.Equals(dir, oneDriveRoot, StringComparison.OrdinalIgnoreCase)) return true;
                    // OneDrive's Known Folder Move relocates Desktop/Documents/Pictures under it.
                    foreach (var name in new[] { "Desktop", "Documents", "Pictures", "Videos", "Music" })
                    {
                        if (string.Equals(dir, Path.Combine(oneDriveRoot, name), StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                // A path we cannot even resolve is not a path we should scan.
                App.Logger?.Warning(ex, "IsPersonalFolderRoot check failed for: {Path}", directory);
                return true;
            }
        }

        /// <summary>
        /// Sanitizes a filename to prevent directory traversal and invalid characters
        /// </summary>
        public static string SanitizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return string.Empty;

            // Remove path components
            filename = Path.GetFileName(filename);

            // Remove invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder();

            foreach (var c in filename)
            {
                if (Array.IndexOf(invalidChars, c) < 0)
                    sanitized.Append(c);
            }

            // Prevent hidden files and special names
            var result = sanitized.ToString().TrimStart('.');
            
            // Block reserved Windows names
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", 
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

            var nameWithoutExt = Path.GetFileNameWithoutExtension(result).ToUpperInvariant();
            if (Array.Exists(reserved, r => r == nameWithoutExt))
                return string.Empty;

            return result;
        }

        /// <summary>
        /// Generates a cryptographically secure random string
        /// </summary>
        public static string GenerateSecureToken(int length = 32)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Securely compares two strings in constant time (prevents timing attacks)
        /// </summary>
        public static bool SecureCompare(string a, string b)
        {
            if (a == null || b == null)
                return a == b;

            if (a.Length != b.Length)
                return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        /// <summary>
        /// Clears sensitive data from memory
        /// </summary>
        public static void SecureClear(byte[] data)
        {
            if (data == null) return;
            
            // Overwrite with random data then zeros
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(data);
            Array.Clear(data, 0, data.Length);
        }

        /// <summary>
        /// Validates file integrity using SHA256
        /// </summary>
        public static string ComputeFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Failed to compute hash for: {Path}", filePath);
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks if the application is running with elevated privileges
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if running in a debugger or analysis tool
        /// </summary>
        public static bool IsDebuggerAttached()
        {
            return System.Diagnostics.Debugger.IsAttached || IsDebuggerPresentNative();
        }

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        private static bool IsDebuggerPresentNative()
        {
            try
            {
                return IsDebuggerPresent();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rate limiter to prevent abuse
        /// </summary>
        public class RateLimiter
        {
            private readonly int _maxAttempts;
            private readonly TimeSpan _window;
            private readonly Queue<DateTime> _attempts = new();
            private readonly object _lock = new();

            public RateLimiter(int maxAttempts, TimeSpan window)
            {
                _maxAttempts = maxAttempts;
                _window = window;
            }

            public bool TryAcquire()
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    
                    // Remove old attempts outside the window
                    while (_attempts.Count > 0 && now - _attempts.Peek() > _window)
                        _attempts.Dequeue();

                    if (_attempts.Count >= _maxAttempts)
                        return false;

                    _attempts.Enqueue(now);
                    return true;
                }
            }

            public void Reset()
            {
                lock (_lock)
                {
                    _attempts.Clear();
                }
            }
        }
    }
}
