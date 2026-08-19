using CcpClient.Desktop.Audio;
using Xunit;

namespace CcpClient.Tests;

// TEMPORARY SP-109 measurement harness. Deleted before the packet lands.
public class ZzMeasureAudio
{
    [Fact]
    public async Task Measure()
    {
        var lines = new List<string>();
        void L(string s) { lines.Add(s); }

        L($"windows={OperatingSystem.IsWindows()} pid={Environment.ProcessId}");
        L($"activeRenderEndpoints={WasapiRenderProbe.ActiveRenderEndpointCount()}");
        var before = WasapiRenderProbe.SessionForThisProcess();
        L($"session BEFORE any audio: {before}");

        var backend = new SoundFlowAudioBackend(m => L($"  [backend] {m}"));
        var devices = backend.EnumerateDevices();
        L($"soundflow devices ({devices.Count}): {string.Join(" | ", devices)}");

        var init = backend.TryInit(null, out var err);
        L($"TryInit -> {init} err={err}");
        var afterInit = WasapiRenderProbe.SessionForThisProcess();
        L($"session AFTER device init/start: {afterInit}");

        var wav = TestWav.Write(Path.Combine(Path.GetTempPath(), "ccp-sp109-measure", "tone.wav"), seconds: 3.0);
        L($"wav={wav} bytes={new FileInfo(wav).Length}");

        IAudioPlayer? player = null;
        try
        {
            player = backend.CreatePlayer(wav, 0.9f);
            L($"player created state={player.State}");
            player.Play();
            L($"after Play() state={player.State} pos={player.PositionSec}");

            float bestPeak = 0;
            var seenActive = false;
            try
            {
                await TestWait.Until(
                    () =>
                    {
                        var f = WasapiRenderProbe.SessionForThisProcess();
                        if (f.Active) { seenActive = true; }
                        if (f.Peak > bestPeak) { bestPeak = f.Peak; }
                        return bestPeak > 0f;
                    },
                    "the OS session meter to read a non-zero peak",
                    window: TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { L($"peak wait failed: {ex.Message}"); }

            var during = WasapiRenderProbe.SessionForThisProcess();
            L($"session DURING playback: {during} | seenActive={seenActive} bestPeak={bestPeak}");
            L($"player state during={player.State} pos={player.PositionSec}");

            player.Stop();
            L($"after Stop() state={player.State}");
        }
        catch (Exception ex)
        {
            L($"PLAY FAILED {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            player?.Dispose();
        }

        var afterStop = WasapiRenderProbe.SessionForThisProcess();
        L($"session AFTER stop: {afterStop}");
        backend.Dispose();
        var afterTeardown = WasapiRenderProbe.SessionForThisProcess();
        L($"session AFTER backend dispose: {afterTeardown}");

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "ccp-sp109-measure.txt"), lines);
        Assert.Fail(string.Join("\n", lines));
    }
}
