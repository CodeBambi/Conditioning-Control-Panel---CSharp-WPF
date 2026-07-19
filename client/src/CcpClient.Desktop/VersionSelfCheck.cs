using System.Reflection;

namespace CcpClient.Desktop;

/// <summary>
/// Runtime version surface (client/docs/release-publish-gates.md §2). Reads the
/// <see cref="AssemblyInformationalVersionAttribute"/> from the entry assembly — the
/// canonical display version. NEVER reads a version from a file path
/// (<c>FileVersionInfo</c>) or <c>Assembly.Location</c>: both break under single-file
/// publish (Location is empty; the exe is the apphost, not the assembly). The attribute
/// travels in-memory in every artifact mode (Debug/Release/published).
/// </summary>
public static class VersionSelfCheck
{
    public const string Flag = "--version";

    /// <summary>The canonical display version, or null when the attribute is absent.</summary>
    public static string? GetInformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// Bounded diagnostic path at the top of <c>Main</c> (same shape as
    /// <c>--verify-assets</c>): prints the informational version and exits. No window,
    /// no lifetime, no participants (SP-003 phase discipline).
    /// </summary>
    public static int Run(Assembly assembly, TextWriter output)
    {
        var version = GetInformationalVersion(assembly);
        if (string.IsNullOrEmpty(version))
        {
            output.WriteLine("version: FAIL (AssemblyInformationalVersionAttribute missing)");
            return 1;
        }

        output.WriteLine($"version: {version}");
        return 0;
    }
}
