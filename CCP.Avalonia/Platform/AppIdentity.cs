using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// What this head is called on disk and which version it wears, in ONE place. The number comes
    /// from <c>Version.props</c> through the assembly (App.axaml.cs seeds
    /// <c>CoreReleaseContent.AppVersion</c> from it); the label of an unreleased build
    /// (<c>CcpVersionLabel</c>, e.g. "parity") rides the informational version only, so every
    /// version check keeps parsing plain digits.
    /// </summary>
    internal static class AppIdentity
    {
        /// <summary>The installed exe on Windows. The build output is <c>CCP.Avalonia.exe</c>; the
        /// installer lays it down under WPF's name (installer.iss DestName) because shortcuts, the
        /// updater's relaunch, Windows startup entries and firewall rules made by 7.1.5 all name it.</summary>
        public const string InstalledExeName = "ConditioningControlPanel.exe";

        /// <summary>The build output's own name (dev runs, the Linux tarball, tests, scripts).</summary>
        public const string BuildExeName = "CCP.Avalonia.exe";

        /// <summary>WPF App.xaml.cs:52-61. The installer's AppMutex and a running 7.1.5 look for these.</summary>
        public const string MutexName = "ConditioningControlPanel_SingleInstance_Mutex";
        public const string ShowSignalName = "ConditioningControlPanel_ShowWindow_Signal";
        public const string ShowAckSignalName = "ConditioningControlPanel_ShowAck_Signal";

        private static readonly Regex VersionDigits = new(@"\d+\.\d+\.\d+", RegexOptions.Compiled);

        /// <summary>"7.2.0-parity" on an unreleased build, the plain number on a release.</summary>
        public static string VersionLabel { get; } = ReadLabel(typeof(AppIdentity).Assembly);

        internal static string ReadLabel(Assembly assembly)
        {
            var v = assembly.GetName().Version;
            var number = v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            try
            {
                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return LabelFrom(number, info);
            }
            catch { return number; }
        }

        /// <summary>The informational version when it is this number plus a label; else the number.
        /// A "+sha" build tail is dropped.</summary>
        internal static string LabelFrom(string number, string? informational)
        {
            var info = (informational ?? "").Trim();
            var plus = info.IndexOf('+');
            if (plus >= 0) info = info.Substring(0, plus);
            return info.StartsWith(number + "-", StringComparison.Ordinal) && info.Length > number.Length + 1 ? info : number;
        }

        /// <summary>A release-named text ("v7.1.5 IS OUT") with this build's number in place of the
        /// one frozen in its loc key, so the idle pill can never name another release.</summary>
        internal static string StampVersion(string? text, string version) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(version) ? text ?? "" : VersionDigits.Replace(text, version, 1);
    }
}
