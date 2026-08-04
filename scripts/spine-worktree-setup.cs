// spine-worktree-setup.cs — T-14 (SP-039) Windows shim for pi-spine's worktreeSetupHook.
//
// Why this exists: the engine spawnSync()s the configured hook path directly with
// NO shell (worktree.mjs runWorktreeSetupHook). On Windows, .sh/.cmd/.mjs all fail
// to spawn (EFTYPE/EINVAL, proven empirically on node v24.5.0) — only a real .exe
// works. This shim runs the actual logic (node .spine/patches/worktree-setup-hook.mjs,
// cwd = lane worktree per the engine contract) with inherited stdio and propagates
// the exit code. Rebuild:
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /out:scripts/spine-worktree-setup.exe scripts/spine-worktree-setup.cs
//
// Fail-safe: if node cannot be launched at all, emit the engine's contract JSON
// with prestaged:false and exit 0 — a broken pre-stage is today's recoverable
// disease; a thrown hook blocks ALL lane provisioning (worse).
using System;
using System.Diagnostics;
using System.IO;

internal static class WorktreeSetupShim
{
    private static int Main()
    {
        var script = Path.Combine(Environment.CurrentDirectory, ".spine", "patches", "worktree-setup-hook.mjs");
        if (!File.Exists(script))
        {
            Console.WriteLine("{\"ok\":true,\"prestaged\":false,\"reason\":\"hook script absent in lane: " + script.Replace("\\", "/") + "\"}");
            return 0;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "\"" + script + "\"",
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false, // inherit stdio: child's stdout IS our stdout
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            // Contract: last stdout line must be JSON {"ok":true}. Emit it ourselves.
            var reason = "node-launch-failed: " + ex.Message.Replace("\\", "/").Replace("\"", "'");
            Console.WriteLine("{\"ok\":true,\"prestaged\":false,\"reason\":\"" + reason + "\"}");
            return 0;
        }
    }
}
