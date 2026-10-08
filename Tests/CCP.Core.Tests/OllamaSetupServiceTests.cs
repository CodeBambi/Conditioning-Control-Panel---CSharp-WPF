using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Runs alone: one test prepends to the process-wide PATH.</summary>
[CollectionDefinition("ProcessPath", DisableParallelization = true)]
public sealed class ProcessPathCollection { }

/// <summary>OllamaSetupService off Windows: `ollama` comes from PATH (no installer, decision in
/// docs/avalonia-decisions.md), and exit stops only the `ollama serve` this app spawned. The binary
/// is a shell-script fake and the API a loopback fake; no real ollama, pull or network.</summary>
[Collection("ProcessPath")]
public sealed class OllamaSetupServiceTests
{
    [Fact]
    public void FindOnPathReturnsTheOllamaBinaryFromPath()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("ccp-ollama-path-").FullName;
        try
        {
            var bin = Path.Combine(dir, "ollama");
            File.WriteAllText(bin, "#!/bin/sh\n");
            Assert.Equal(bin, OllamaSetupService.FindOnPath("/nonexistent-ccp" + Path.PathSeparator + dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StopSpawnedServerStopsOnlyTheServerThisAppStarted()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("ccp-ollama-serve-").FullName;
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var host = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/";
        probe.Stop();
        using var api = new HttpListener();
        api.Prefixes.Add(host);
        api.Start();
        _ = Task.Run(async () =>
        {
            while (api.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await api.GetContextAsync(); } catch { return; }
                var bytes = Encoding.UTF8.GetBytes("{\"models\":[]}");
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        // The user's own server (systemd / a terminal `ollama serve`): never ours to stop.
        using var users = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
        try
        {
            var bin = Path.Combine(dir, "ollama");
            File.WriteAllText(bin, "#!/bin/sh\nexec sleep 60\n");
            File.SetUnixFileMode(bin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + oldPath);

            Assert.True(await OllamaSetupService.StartServiceAsync(host));
            var spawned = (Process)typeof(OllamaSetupService)
                .GetField("_spawnedServer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            var spawnedId = spawned.Id;

            OllamaSetupService.StopSpawnedServer();

            Assert.False(users.HasExited);
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(spawnedId));
            OllamaSetupService.StopSpawnedServer(); // idempotent
            Assert.False(users.HasExited);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            OllamaSetupService.StopSpawnedServer();
            try { users.Kill(); } catch { }
            api.Stop();
            Directory.Delete(dir, true);
        }
    }
}
