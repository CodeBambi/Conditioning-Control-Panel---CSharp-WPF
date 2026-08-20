using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Last line of defence before a missing bundled native can take startup down: prove Skia
    /// loads, and if it does not, clear the extraction cache and relaunch.
    ///
    /// <para>The app publishes as <c>PublishSingleFile</c> + <c>SelfContained</c> with
    /// <c>IncludeNativeLibrariesForSelfExtract=true</c>, so every native dependency (libSkiaSharp,
    /// libvosk, onnxruntime, OpenCvSharpExtern, sherpa, the whole libvlc tree) lives inside the exe
    /// and is unpacked into <c>%TEMP%\.net\ConditioningControlPanel\&lt;bundle-id&gt;\</c>.</para>
    ///
    /// <para><b>What the host does and does not cover.</b> Measured on .NET 8, 2026-08-20: at
    /// process start the host verifies that folder against the bundle manifest and re-extracts
    /// anything missing - even natives nothing ever loads. Strip the folder to three files and the
    /// next launch silently restores all 669. So a merely incomplete cache is NOT what this guards.
    /// Two states survive that check:</para>
    /// <list type="bullet">
    ///   <item>files deleted AFTER startup, mid-session - the host has already run its check and
    ///   will not look again. This is what bricked 6.8.3 for the first users to update: our own
    ///   <see cref="UpdateService"/> cache prune fired on a background thread and stripped the live
    ///   folder. Fixed at the source, but installs already poisoned by the shipped build stay
    ///   poisoned - the folder is there and the host is happy with it - so they need this to
    ///   recover on their own;</item>
    ///   <item>a file that is PRESENT but unloadable - truncated by a full disk, mangled or stubbed
    ///   by antivirus. The host checks existence, not integrity, so it will never replace it.</item>
    /// </list>
    ///
    /// <para><b>Why it is worth a startup probe.</b> The failure is invisible from the message the
    /// user gets. Skia backs the compositor, flashes, subliminals and bubbles, but the first thing
    /// to touch it is <see cref="Controls.AmbientFxCanvas"/> in MainWindow.xaml - so the dialog
    /// blames a decorative FX control and buries "Dll was not found" at <c>SKPaint..ctor()</c> two
    /// levels down an inner exception. MainWindow never opens, yet the dispatcher handler swallows
    /// the throw and the process stays alive as a windowless zombie still logging barks and LibVLC
    /// init, so it looks like it is running. Nobody is guessing "delete a hidden temp folder" from
    /// that.</para>
    ///
    /// <para>The delete has to happen from OUTSIDE the process: Windows pins loaded image sections,
    /// and we have wpfgfx_cor3.dll and PresentationNative_cor3.dll mapped out of that very folder,
    /// so an in-process <c>Directory.Delete</c> would strip it further rather than remove it. We
    /// hand off to a batch helper that waits for us to exit first, the same shape (and for the same
    /// reason) as the update helper in <see cref="UpdateService"/>.</para>
    ///
    /// <para>Scope note: the probe is deliberately just Skia. It is the one native whose absence is
    /// unconditionally fatal, and any damage broad enough to matter takes it out. A cache that
    /// somehow kept Skia but lost, say, OpenCvSharpExtern degrades exactly as it always has (webcam
    /// features no-op) rather than taking the whole app down.</para>
    /// </summary>
    internal static class NativeBundleGuard
    {
        /// <summary>
        /// One repair attempt per version per window. A second failure inside it means the cache
        /// was not the problem (or the delete could not land), and looping would be worse than
        /// stopping: relaunch, fail, relaunch is indistinguishable from the app refusing to open.
        /// </summary>
        private static readonly TimeSpan RepairCooldown = TimeSpan.FromMinutes(10);

        private const string StampFileName = "native-repair.stamp";

        /// <summary>
        /// Verifies the bundled natives are loadable. Returns true when startup may continue.
        ///
        /// <para>Returns false when the caller must stop: either a repair relaunch is already on
        /// its way (the user sees the app restart itself once) or we have given up and shown a
        /// dialog that names the folder to delete. Either way the caller should
        /// <see cref="Application.Shutdown()"/> and return - do NOT fall through to building
        /// MainWindow, which is the crash this exists to prevent.</para>
        /// </summary>
        /// <param name="beforeExit">
        /// Runs immediately before the relaunch/dialog, while we are still the foreground app.
        /// Used to tear the splash screen down so it cannot sit on top of the message box or
        /// linger over the restarted instance.
        /// </param>
        public static bool VerifyOrRepair(Action? beforeExit = null)
        {
            // Fail OPEN on anything unexpected. This runs before every launch, so a bug in here
            // would be a worse outage than the one it prevents: returning true just puts us back
            // on the pre-guard path, where a genuinely broken cache still throws where it always
            // did and the crash log still tells the story.
            try
            {
                return VerifyOrRepairCore(beforeExit);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[NATIVES] Guard failed unexpectedly — continuing startup unguarded");
                return true;
            }
        }

        private static bool VerifyOrRepairCore(Action? beforeExit)
        {
            Exception? failure = ProbeSkia();
            if (failure == null)
            {
                // Healthy launch: drop any stamp so a future truncation gets its own full attempt
                // instead of inheriting a months-old cooldown.
                ClearStamp();
                return true;
            }

            App.Logger?.Fatal(failure,
                "[NATIVES] Bundled native libraries are not loadable — the single-file extraction cache is incomplete or damaged");

            var cacheRoot = ResolveExtractionRoot();
            var appExe = Environment.ProcessPath;

            if (cacheRoot == null || string.IsNullOrEmpty(appExe))
            {
                // Not a single-file build (a dev `dotnet run`, where natives come from the output
                // folder) or the cache is already gone. Nothing to clear, so a relaunch would just
                // reproduce this. Whatever is wrong is a real deployment fault.
                App.Logger?.Fatal("[NATIVES] No extraction cache to clear (root={Root}, exe={Exe}) — cannot self-repair",
                    cacheRoot ?? "<none>", appExe ?? "<none>");
                beforeExit?.Invoke();
                ShowGiveUpDialog(cacheRoot, failure);
                return false;
            }

            if (RepairedRecently())
            {
                App.Logger?.Fatal("[NATIVES] Already cleared {Root} for v{Version} within the last {Minutes} minutes — not looping",
                    cacheRoot, UpdateService.AppVersion, RepairCooldown.TotalMinutes);
                beforeExit?.Invoke();
                ShowGiveUpDialog(cacheRoot, failure);
                return false;
            }

            App.Logger?.Warning("[NATIVES] Clearing {Root} and relaunching to force a full re-extract", cacheRoot);
            WriteStamp();

            if (!LaunchRepairHelper(cacheRoot, appExe))
            {
                beforeExit?.Invoke();
                ShowGiveUpDialog(cacheRoot, failure);
                return false;
            }

            beforeExit?.Invoke();
            return false;
        }

        /// <summary>
        /// Constructs an <c>SKPaint</c>, which is what forces libSkiaSharp to load. Returns the
        /// exception on failure, null when Skia is healthy.
        /// </summary>
        private static Exception? ProbeSkia()
        {
            try
            {
                using var paint = new SkiaSharp.SKPaint();
                // Touch the instance so nothing can optimize the construction away.
                paint.IsAntialias = true;
                return null;
            }
            catch (DllNotFoundException ex) { return ex; }
            catch (BadImageFormatException ex) { return ex; }
            // SkiaSharp resolves its native lib from a static initializer, so on some paths the
            // load failure surfaces wrapped the first time any Skia type is touched. Only unwrap
            // to a verdict when the inner cause really is a missing/corrupt native — anything
            // else is a Skia bug, not a cache problem, and clearing the cache would not fix it.
            catch (TypeInitializationException ex)
                when (ex.InnerException is DllNotFoundException or BadImageFormatException)
            {
                return ex;
            }
        }

        /// <summary>
        /// The <c>&lt;base&gt;\&lt;app-name&gt;</c> folder holding every bundle-id the host has
        /// unpacked for this exe, or null when this build does not self-extract at all.
        /// Older versions' folders are swept with ours - they are pure cache and the host
        /// rebuilds whichever it needs.
        /// </summary>
        private static string? ResolveExtractionRoot()
        {
            try
            {
                // A single-file bundle reports an empty Location for its entry assembly; a normal
                // build reports a real path and never extracts anything. IL3000 warns about
                // exactly that empty return — here it IS the signal we want, not a mistake.
#pragma warning disable IL3000
                if (!string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location))
                    return null;
#pragma warning restore IL3000

                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return null;

                // Same precedence the host itself uses.
                var baseDir = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.Combine(Path.GetTempPath(), ".net");

                var root = Path.Combine(baseDir, Path.GetFileNameWithoutExtension(exe));
                return Directory.Exists(root) ? root : null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[NATIVES] Could not resolve the bundle extraction root");
                return null;
            }
        }

        /// <summary>
        /// Writes and starts the helper that waits for us to exit, deletes the cache and relaunches.
        /// Returns false if it could not be started, in which case nothing has been deleted.
        /// </summary>
        private static bool LaunchRepairHelper(string cacheRoot, string appExe)
        {
            try
            {
                var helperDir = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel_Repair");
                Directory.CreateDirectory(helperDir);

                var logPath = Path.Combine(App.UserDataPath, "logs", "native-repair.log");
                try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); } catch { }

                var helperPath = Path.Combine(helperDir, "native-repair.cmd");
                var pid = Environment.ProcessId;

                // Paths are baked in as literals (no positional args) so spaces are safe — the
                // install dir is "…\Programs\Conditioning Control Panel".
                var lines = new[]
                {
                    "@echo off",
                    "setlocal enableextensions",
                    $"set \"LOG={logPath}\"",
                    $"set \"APPEXE={appExe}\"",
                    $"set \"CACHE={cacheRoot}\"",
                    $"echo [native-repair] start pid={pid} > \"%LOG%\"",
                    // Wait for us to exit so the loaded image sections are released; without this
                    // the rd below silently leaves the mapped DLLs behind. Capped at ~1min so a
                    // recycled PID cannot hang the helper forever.
                    "set /a tries=0",
                    ":waitloop",
                    $"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \"{pid}\" >nul",
                    "if errorlevel 1 goto gone",
                    "set /a tries+=1",
                    "if %tries% GEQ 30 (",
                    "  echo [native-repair] wait timed out, proceeding anyway >> \"%LOG%\"",
                    "  goto gone",
                    ")",
                    "ping 127.0.0.1 -n 2 >nul",
                    "goto waitloop",
                    ":gone",
                    "echo [native-repair] app exited (tries=%tries%), clearing cache >> \"%LOG%\"",
                    "rd /s /q \"%CACHE%\" 2>nul",
                    "if exist \"%CACHE%\" (",
                    "  echo [native-repair] WARNING cache survived the delete >> \"%LOG%\"",
                    ") else (",
                    "  echo [native-repair] cache cleared >> \"%LOG%\"",
                    ")",
                    "echo [native-repair] relaunching app >> \"%LOG%\"",
                    "start \"\" \"%APPEXE%\"",
                    "echo [native-repair] done >> \"%LOG%\"",
                    "endlocal",
                };

                // UTF-8 without BOM — a BOM breaks batch parsing of the first line.
                File.WriteAllText(helperPath, string.Join("\r\n", lines) + "\r\n",
                    new System.Text.UTF8Encoding(false));

                // A .cmd is not a PE, so it needs an explicit interpreter. The helper outlives us
                // because WPF does not job-object its children.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{helperPath}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });

                App.Logger?.Information("[NATIVES] Repair helper launched ({Helper}); log: {Log}", helperPath, logPath);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Fatal(ex, "[NATIVES] Could not launch the repair helper");
                return false;
            }
        }

        private static string StampPath => Path.Combine(App.UserDataPath, StampFileName);

        /// <summary>
        /// True when this same version already burned its repair attempt inside the cooldown.
        /// A version bump gets a fresh attempt: the new bundle has its own extraction folder and
        /// deserves one.
        /// </summary>
        private static bool RepairedRecently()
        {
            try
            {
                if (!File.Exists(StampPath))
                    return false;

                var parts = File.ReadAllText(StampPath).Trim().Split('|');
                if (parts.Length != 2)
                    return false;
                if (!string.Equals(parts[0], UpdateService.AppVersion, StringComparison.Ordinal))
                    return false;
                if (!long.TryParse(parts[1], out var ticks))
                    return false;

                return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < RepairCooldown;
            }
            catch
            {
                // An unreadable stamp must not block the repair — that would turn a transient IO
                // hiccup into a permanently unstartable app.
                return false;
            }
        }

        private static void WriteStamp()
        {
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(StampPath, $"{UpdateService.AppVersion}|{DateTime.UtcNow.Ticks}");
            }
            catch { }
        }

        private static void ClearStamp()
        {
            try
            {
                if (File.Exists(StampPath))
                    File.Delete(StampPath);
            }
            catch { }
        }

        /// <summary>
        /// The dead end: tell the user what is actually wrong and the one folder to delete. Far
        /// better than the XamlParseException naming a decorative FX control they have never
        /// heard of.
        /// </summary>
        private static void ShowGiveUpDialog(string? cacheRoot, Exception failure)
        {
            try
            {
                var where = cacheRoot ?? Path.Combine(Path.GetTempPath(), ".net", "ConditioningControlPanel");
                MessageBox.Show(
                    "Conditioning Control Panel could not load its graphics libraries, so it can't start.\n\n" +
                    "This is almost always a damaged temporary cache rather than a damaged install. " +
                    "Close the app, delete this folder, then launch again:\n\n" +
                    where + "\n\n" +
                    "It is rebuilt automatically on the next start, so deleting it is safe and loses nothing. " +
                    "If that doesn't help, your antivirus may be quarantining the app's files.\n\n" +
                    "Details (also in the crash log): " + failure.Message,
                    "Conditioning Control Panel — startup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* MessageBox can fail if the desktop is going away */ }
        }
    }
}
