using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/StartupManager.cs (WPF 7.1.5): a shortcut named
    /// <c>ConditioningControlPanel.lnk</c> in the user's Startup folder, made through the shell's
    /// IShellLink COM object (AV-friendly, no registry Run key), argument <c>--startup</c>. Same file
    /// name as WPF, so a shortcut the WPF app made reads as registered here.
    /// </summary>
    internal static class WindowsStartupShortcut
    {
        private const string ShortcutName = "ConditioningControlPanel.lnk";

        /// <summary>Test seam: the Startup folder. Null = <c>CCP_STARTUP_FOLDER</c> if set, else the
        /// real Startup folder (the test assemblies point the variable at a sandbox).</summary>
        internal static string? FolderOverride { get; set; }

        internal static string ShortcutPath
        {
            get
            {
                var dir = FolderOverride;
                if (dir is null)
                {
                    dir = Environment.GetEnvironmentVariable("CCP_STARTUP_FOLDER");
                    if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                }
                return Path.Combine(dir, ShortcutName);
            }
        }

        internal static bool IsRegistered()
        {
            try { return File.Exists(ShortcutPath); }
            catch (Exception ex) { Log.Warning("Could not check startup registration: {Error}", ex.Message); return false; }
        }

        internal static bool SetStartupState(bool enabled) => enabled ? Register() : Unregister();

        private static bool Register()
        {
            try
            {
                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Log.Warning("Could not find executable path for startup registration");
                    return false;
                }
                var args = "--startup";
                // A framework-dependent run (dotnet CCP.Avalonia.dll) names the entry assembly too.
                if (Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    && System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
                    args = "\"" + dll + "\" " + args;

                var path = ShortcutPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                CreateShortcut(path, exePath, Path.GetDirectoryName(exePath) ?? "", "Conditioning Control Panel", args);
                Log.Information("Registered for Windows startup: {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register for Windows startup");
                return false;
            }
        }

        private static bool Unregister()
        {
            try
            {
                var path = ShortcutPath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Information("Unregistered from Windows startup");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to unregister from Windows startup");
                return false;
            }
        }

        internal static string GetExecutablePath()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath)) return processPath;
            var appName = AppDomain.CurrentDomain.FriendlyName;
            if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) appName += ".exe";
            var candidate = Path.Combine(AppContext.BaseDirectory, appName);
            return File.Exists(candidate) ? candidate : processPath ?? "";
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory,
            string description, string arguments)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var shellLink = (IShellLink)new ShellLink();
            try
            {
                shellLink.SetPath(targetPath);
                shellLink.SetWorkingDirectory(workingDirectory);
                shellLink.SetDescription(description);
                if (!string.IsNullOrEmpty(arguments)) shellLink.SetArguments(arguments);
                ((IPersistFile)shellLink).Save(shortcutPath, false);
            }
            finally { Marshal.ReleaseComObject(shellLink); }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxArgs);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }

    /// <summary>The OS autostart entry: the Startup-folder shortcut on Windows (WPF parity), the XDG
    /// <c>.desktop</c> entry elsewhere.</summary>
    internal static class OsAutostart
    {
        /// <summary>Test seam: one folder for whichever backend runs.</summary>
        internal static string? DirectoryOverride
        {
            get => OperatingSystem.IsWindows() ? WindowsStartupShortcut.FolderOverride : XdgAutostart.DirectoryOverride;
            set { WindowsStartupShortcut.FolderOverride = value; XdgAutostart.DirectoryOverride = value; }
        }

        internal static string EntryPath => OperatingSystem.IsWindows() ? WindowsStartupShortcut.ShortcutPath : XdgAutostart.EntryPath;

        internal static bool IsRegistered() =>
            OperatingSystem.IsWindows() ? WindowsStartupShortcut.IsRegistered() : XdgAutostart.IsRegistered();

        internal static bool SetStartupState(bool enabled) =>
            OperatingSystem.IsWindows() ? WindowsStartupShortcut.SetStartupState(enabled) : XdgAutostart.SetStartupState(enabled);
    }
}
