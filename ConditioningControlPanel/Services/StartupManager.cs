using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages Windows startup registration using the Startup folder (AV-friendly approach).
    /// Uses COM Interop to create standard Windows shortcuts rather than registry modifications.
    /// </summary>
    public static class StartupManager
    {
        private const string ShortcutName = "ConditioningControlPanel.lnk";

        private static string StartupFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        private static string ShortcutPath => Path.Combine(StartupFolderPath, ShortcutName);
        private static string ApplicationPath => System.Reflection.Assembly.GetExecutingAssembly().Location
            .Replace(".dll", ".exe"); // Handle both .exe and .dll scenarios

        /// <summary>
        /// Checks if the application is registered to start with Windows.
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                return File.Exists(ShortcutPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Could not check startup registration: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Registers the application to start with Windows by creating a shortcut in the Startup folder.
        /// </summary>
        public static bool Register()
        {
            try
            {
                // Get the actual executable path
                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    App.Logger?.Warning("Could not find executable path for startup registration");
                    return false;
                }

                // Create the shortcut
                CreateShortcut(ShortcutPath, exePath, Path.GetDirectoryName(exePath) ?? "",
                    "Conditioning Control Panel", "--startup");

                App.Logger?.Information("Registered for Windows startup: {Path}", ShortcutPath);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to register for Windows startup");
                return false;
            }
        }

        /// <summary>
        /// Unregisters the application from Windows startup by removing the shortcut.
        /// </summary>
        public static bool Unregister()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                    App.Logger?.Information("Unregistered from Windows startup");
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to unregister from Windows startup");
                return false;
            }
        }

        /// <summary>
        /// Sets the startup registration state based on the provided value.
        /// </summary>
        public static bool SetStartupState(bool enabled)
        {
            return enabled ? Register() : Unregister();
        }

        /// <summary>
        /// Synchronizes the startup state with the current settings.
        /// Call this on app startup to ensure consistency.
        /// </summary>
        public static void SyncWithSettings(bool shouldBeEnabled)
        {
            var isCurrentlyEnabled = IsRegistered();
            if (shouldBeEnabled != isCurrentlyEnabled)
            {
                SetStartupState(shouldBeEnabled);
            }
        }

        private static string GetExecutablePath()
        {
            // Try to get the executable path
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                return processPath;
            }

            // Fallback: construct from assembly location
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(assemblyLocation))
            {
                // For .NET Core/.NET 5+ single-file apps, the assembly location might be empty or point to .dll
                var exePath = assemblyLocation.Replace(".dll", ".exe");
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }

            // Try AppContext.BaseDirectory
            var baseDir = AppContext.BaseDirectory;
            var appName = AppDomain.CurrentDomain.FriendlyName;
            if (!appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                appName += ".exe";
            }
            var candidatePath = Path.Combine(baseDir, appName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            return processPath ?? "";
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory,
            string description, string arguments = "")
        {
            // Use COM interop to create a proper Windows shortcut
            var shellLink = (IShellLink)new ShellLink();

            shellLink.SetPath(targetPath);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetDescription(description);

            if (!string.IsNullOrEmpty(arguments))
            {
                shellLink.SetArguments(arguments);
            }

            // Save the shortcut
            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, false);

            // Release COM objects
            Marshal.ReleaseComObject(persistFile);
            Marshal.ReleaseComObject(shellLink);
        }

        #region COM Interop for Shell Links

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath,
                out IntPtr pfd, int fFlags);
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
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        #endregion
    }
}
