using System;
using System.Globalization;
using System.IO;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF's FileSystem.DeleteFile(..., SendToRecycleBin) on this head: the freedesktop.org home
    /// trash ($XDG_DATA_HOME/Trash), so a deleted library file can be restored from the file
    /// manager. The .trashinfo is created first with CreateNew, which reserves the name atomically.
    /// </summary>
    internal static class TrashBin
    {
        /// <summary>Tests point this at a temp folder so they never touch the real trash.</summary>
        internal static string? RootOverride;

        private static string Root
        {
            get
            {
                if (RootOverride != null) return RootOverride;
                var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrWhiteSpace(data) || !Path.IsPathRooted(data))
                    data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(data, "Trash");
            }
        }

        /// <summary>Moves <paramref name="path"/> to the trash; returns the trashed file's path.</summary>
        internal static string Move(string path)
        {
            var full = Path.GetFullPath(path);
            var files = Path.Combine(Root, "files");
            var info = Path.Combine(Root, "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            var stem = Path.GetFileName(full);
            for (var n = 1; ; n++)
            {
                var name = n == 1 ? stem : $"{stem}.{n}";
                if (File.Exists(Path.Combine(files, name))) continue;
                var infoPath = Path.Combine(info, name + ".trashinfo");
                try
                {
                    using var w = new StreamWriter(new FileStream(infoPath, FileMode.CreateNew));
                    w.Write("[Trash Info]\nPath=" + Uri.EscapeDataString(full).Replace("%2F", "/")
                            + "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "\n");
                }
                catch (IOException) when (File.Exists(infoPath)) { continue; }

                var target = Path.Combine(files, name);
                try { File.Move(full, target); }
                catch { File.Delete(infoPath); throw; }
                return target;
            }
        }
    }
}
