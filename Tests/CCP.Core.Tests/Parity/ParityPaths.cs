using System;
using System.IO;

namespace CCP.Core.Tests.Parity;

/// <summary>Repo paths for the parity tests (the repo root is the folder holding CCP.Core and Assets).</summary>
internal static class ParityPaths
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "CCP.Core"))
                                && Directory.Exists(Path.Combine(dir.FullName, "Assets"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root from " + AppContext.BaseDirectory);
    }
}
