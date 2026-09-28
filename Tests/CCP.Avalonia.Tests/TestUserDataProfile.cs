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
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        };
    }
}
