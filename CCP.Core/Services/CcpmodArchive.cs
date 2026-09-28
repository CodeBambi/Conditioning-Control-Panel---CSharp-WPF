using System;
using System.IO;
using System.IO.Compression;

namespace ConditioningControlPanel.Services
{
    /// <summary>The one extraction path for every .ccpmod / content zip.</summary>
    public static class CcpmodArchive
    {
        /// <summary>
        /// ZipFile.ExtractToDirectory on Windows, so the WPF head keeps .NET's exact behaviour
        /// (illegal-char sanitising, IOException for a directory entry with data, case-insensitive
        /// containment). Off Windows only, a Windows-zipped "resources\x.png" entry is sent to a
        /// folder (Linux keeps the backslash as a file-name character), and entries that resolve
        /// outside <paramref name="dir"/> still throw.
        /// </summary>
        public static void Extract(string zipPath, string dir, bool overwriteFiles = false)
        {
            if (OperatingSystem.IsWindows()) { ZipFile.ExtractToDirectory(zipPath, dir, overwriteFiles); return; }
            Directory.CreateDirectory(dir);   // as the framework does: an empty archive still yields the folder
            var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('\\', '/')));
                if (!dest.StartsWith(root, StringComparison.Ordinal))
                    throw new IOException($"Zip entry '{entry.FullName}' would extract outside {dir}.");
                if (dest.EndsWith(Path.DirectorySeparatorChar)) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwriteFiles);
            }
        }
    }
}
