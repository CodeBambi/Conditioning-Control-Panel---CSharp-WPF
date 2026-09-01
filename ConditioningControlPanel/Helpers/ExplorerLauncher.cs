using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Opens Windows Explorer on a file or folder.
    ///
    /// <para>Every "Reveal in Explorer" / "Open folder" button in the app used to hand-roll
    /// <c>Process.Start("explorer.exe", $"/select,\"{path}\"")</c>. Building the command line as a
    /// single pre-quoted string is fragile: the embedded quotes are re-parsed by the CRT before
    /// explorer ever sees them, and on a mismatch explorer silently falls back to opening the
    /// default folder (the desktop) instead of reporting an error - so the button looked like it
    /// did nothing useful (ccp-bugs #998, media on D: while the app runs from C:).</para>
    ///
    /// <para><see cref="ProcessStartInfo.ArgumentList"/> hands the argument over as a single
    /// pre-tokenized element and lets the runtime apply the correct Windows quoting rules, so
    /// spaces and non-ASCII characters survive intact. It requires
    /// <c>UseShellExecute = false</c>, which is fine - explorer.exe is a plain executable.</para>
    /// </summary>
    public static class ExplorerLauncher
    {
        /// <summary>
        /// Opens Explorer with <paramref name="path"/> selected. If the file no longer exists,
        /// falls back to opening its containing directory. Returns true if Explorer was launched.
        /// Never throws.
        /// </summary>
        public static bool RevealInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                if (File.Exists(path))
                {
                    // One argument, not two: explorer expects the switch and the path glued by the
                    // comma. ArgumentList quotes the whole token for us.
                    return Launch($"/select,{Path.GetFullPath(path)}");
                }

                if (Directory.Exists(path))
                    return Launch(Path.GetFullPath(path));

                // File is gone (deleted / drive detached) - settle for its folder.
                return OpenFolder(Path.GetDirectoryName(path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: reveal failed for {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Opens <paramref name="directory"/> in Explorer. Returns true if Explorer was launched.
        /// Never throws.
        /// </summary>
        public static bool OpenFolder(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            try
            {
                if (!Directory.Exists(directory))
                {
                    Log.Debug("ExplorerLauncher: folder no longer exists: {Dir}", directory);
                    return false;
                }
                return Launch(Path.GetFullPath(directory));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: open folder failed for {Dir}", directory);
                return false;
            }
        }

        private static bool Launch(string argument)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    // ArgumentList needs UseShellExecute = false; it is the whole point of this helper.
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(argument);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ExplorerLauncher: explorer.exe failed to start for {Arg}", argument);
                return false;
            }
        }
    }
}
