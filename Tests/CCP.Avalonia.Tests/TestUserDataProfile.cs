using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace CCP.Avalonia.Tests;

/// <summary>Points CorePaths.UserData at a throwaway folder before any test touches it, so no
/// test reads or writes the real profile (e.g. SessionLogService's session_logs).</summary>
internal static class TestUserDataProfile
{
    internal static readonly string Root = Path.Combine(
        Path.GetTempPath(), "ccp-avalonia-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(Root);
        // Awareness polls the foreground window title; no test may ever read the real desktop.
        ConditioningControlPanel.Avalonia.Platform.X11ActiveWindow.Disabled = true;
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", Root);
        _ = ConditioningControlPanel.CorePaths.UserData;   // installs the SandboxNet guard before any test runs
        // Every one-shot feature card already spent: a shown shell lands on the Dashboard, whose card
        // would otherwise open (and stay open as a passive surface) in whichever test shows a shell
        // first. FeatureIntroWiringTests un-spends the keys it drives.
        ConditioningControlPanel.CoreSettings.Current.SeenFeatureIntros.AddRange(
            ConditioningControlPanel.Avalonia.Views.Windows.FeatureIntros.All.Keys);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        };
    }
}
