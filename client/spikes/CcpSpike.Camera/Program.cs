using System.Diagnostics;
using CcpClient.Desktop;
using CcpClient.Desktop.Camera;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpSpike.Camera;

/// <summary>
/// <b>The camera-capture hardware harness.</b> It opens a real camera on this machine through the
/// real product code, reads real frames, releases the device, and then opens it a second time to
/// prove the release was real.
///
/// <para><b>Every step is deliberately observable by a human standing at the machine</b>, because
/// two of the claims this slice makes cannot be checked by any assertion: that the camera INDICATOR
/// lights while the device is open and goes out when it closes, and that the LED on the hardware
/// does the same. The run pauses at the moments those change so somebody can look.</para>
///
/// <para><b>It reads the operating system's own record of camera use</b> —
/// <c>CapabilityAccessManager\ConsentStore\webcam\NonPackaged</c>'s <c>LastUsedTimeStart</c> and
/// <c>LastUsedTimeStop</c> for this executable — before and after. That is the same bookkeeping the
/// taskbar camera indicator is driven from, so a <c>Start</c> that moves and a <c>Stop</c> that
/// lands after it is Windows agreeing that this process held the camera and then let it go. It is a
/// proxy for the indicator, not the indicator itself, and the run says so.</para>
///
/// <para><b>Nothing about a frame is printed.</b> Counts, states, dimensions and rung names only —
/// <c>Services/Webcam/WebcamTrackingService.cs:28-29</c>'s rule, which this harness is bound by
/// exactly as the product is.</para>
/// </summary>
internal static class Program
{
    private const string ConsentStoreKey =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged";

    private static async Task<int> Main(string[] args)
    {
        var frames = ArgumentValue(args, "--frames") is { } value && int.TryParse(value, out var parsed)
            ? parsed
            : 60;
        var preferred = ArgumentValue(args, "--device");
        var pause = !args.Contains("--no-pause", StringComparer.Ordinal);

        Console.WriteLine("=== camera capture hardware harness ===");
        Console.WriteLine($"platform: {CameraDeviceSourceFactory.CurrentPlatform()}");

        var directory = Path.Combine(Path.GetTempPath(), "ccp-camera-spike-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = new ConsoleLog();
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), log);

        // engineAdmitted: true is the ONLY thing this harness fakes, and it is the reason a product
        // run cannot reach any of this. It does not manufacture a gaze engine — it lets the rungs
        // BELOW the engine gate execute so the consent gate, the roster and the open can be
        // exercised at all. Every product path has this false.
        var participant = new CameraParticipant(infra, directory, engineAdmitted: true);

        try
        {
            await participant.StartAsync(CancellationToken.None);
            Console.WriteLine($"capture backend: {participant.CaptureBackend}");

            // ── 1. Before consent: the gate must refuse without touching anything ───────────────
            Console.WriteLine();
            Console.WriteLine("[1] start WITHOUT consent");
            Report(await participant.StartCaptureAsync(preferred, CancellationToken.None));
            Console.WriteLine($"    enumerations={participant.Enumerations} opens={participant.CameraOpenAttempts} "
                + $"running={participant.CaptureRunning}");
            if (participant.CameraOpenAttempts != 0)
            {
                Console.WriteLine("    FAIL: a camera was opened without consent");
                return 1;
            }

            // ── 2. Consent, and then STILL nothing until an explicit start ──────────────────────
            Console.WriteLine();
            Console.WriteLine("[2] grant consent (throwaway data directory)");
            var granted = await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UtcNow);
            Console.WriteLine($"    granted={granted} enumerations={participant.Enumerations} "
                + $"opens={participant.CameraOpenAttempts}");
            if (participant.CameraOpenAttempts != 0 || participant.Enumerations != 0)
            {
                Console.WriteLine("    FAIL: granting consent touched a camera");
                return 1;
            }

            var before = ReadUsageRecord();
            Console.WriteLine($"    OS camera-use record before: {before}");

            // ── 3. The explicit start: a real camera opens ──────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("[3] EXPLICIT START — a real camera should open now");
            Prompt(pause, "    LOOK AT THE CAMERA INDICATOR AND THE LED, then press Enter to start.");
            var opened = Stopwatch.StartNew();
            var state = await participant.StartCaptureAsync(preferred, CancellationToken.None);
            opened.Stop();
            Report(state);
            if (participant.LastInventory is { } roster)
            {
                Console.WriteLine($"    roster ({roster.Route}): {roster.Devices.Count} device(s)");
                for (var index = 0; index < roster.Devices.Count; index++)
                {
                    // Printed to this console for a human choosing --device. It is never persisted,
                    // never logged by the product, and never leaves this machine.
                    Console.WriteLine($"      [{index}] {roster.Devices[index].DisplayName}");
                    Console.WriteLine($"          {roster.Devices[index].StableId}");
                }
            }

            foreach (var rung in participant.CaptureAttempts)
            {
                Console.WriteLine($"    rung: {rung}");
            }

            Console.WriteLine($"    open took {opened.ElapsedMilliseconds}ms, running={participant.CaptureRunning}, "
                + $"opens={participant.CameraOpenAttempts}");
            if (!participant.CaptureRunning)
            {
                Console.WriteLine("    no camera opened — the rungs above say why");
                return 2;
            }

            // ── 4. Frames ──────────────────────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine($"[4] PUMP {frames} frame(s)");
            var pumped = Stopwatch.StartNew();
            var delivered = await participant.PumpAsync(frames, CancellationToken.None);
            pumped.Stop();
            Console.WriteLine($"    delivered={delivered} in {pumped.ElapsedMilliseconds}ms "
                + $"({(pumped.ElapsedMilliseconds > 0 ? delivered * 1000.0 / pumped.ElapsedMilliseconds : 0):F1} fps), "
                + $"FramesRead={participant.FramesRead}");
            Prompt(pause, "    THE INDICATOR SHOULD BE LIT. Press Enter to close the camera.");

            // ── 5. Release ─────────────────────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("[5] STOP — the device must be released");
            await participant.StopCaptureAsync();
            Console.WriteLine($"    running={participant.CaptureRunning}");
            var after = ReadUsageRecord();
            Console.WriteLine($"    OS camera-use record after:  {after}");
            Prompt(pause, "    THE INDICATOR AND LED SHOULD BE OUT. Press Enter to re-open.");

            // ── 6. Re-open: only possible if the release really happened ───────────────────────
            Console.WriteLine();
            Console.WriteLine("[6] RE-OPEN — this can only succeed if step 5 really let the device go");
            var reopen = await participant.StartCaptureAsync(preferred, CancellationToken.None);
            Report(reopen);
            Console.WriteLine($"    running={participant.CaptureRunning} opens={participant.CameraOpenAttempts}");
            var reDelivered = await participant.PumpAsync(10, CancellationToken.None);
            Console.WriteLine($"    delivered={reDelivered} on the second open");
            await participant.StopCaptureAsync();
            Console.WriteLine($"    running={participant.CaptureRunning}");

            Console.WriteLine();
            Console.WriteLine($"final OS camera-use record: {ReadUsageRecord()}");
            return participant.CaptureRunning ? 3 : 0;
        }
        finally
        {
            await participant.StopAsync();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                Console.WriteLine($"(left the throwaway consent directory at {directory})");
            }
        }
    }

    /// <summary>Windows's own record of when this executable last held the camera. Read-only, and
    /// about THIS process rather than about anything a camera saw.</summary>
    private static string ReadUsageRecord()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "not a Windows machine";
        }

        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            return "unknown executable";
        }

        var valueName = executable.Replace('\\', '#');
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ConsentStoreKey + "\\" + valueName);
            if (key is null)
            {
                return "no record for this executable yet";
            }

            return $"LastUsedTimeStart={Stamp(key.GetValue("LastUsedTimeStart"))} "
                + $"LastUsedTimeStop={Stamp(key.GetValue("LastUsedTimeStop"))}";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "unreadable (" + ex.GetType().Name + ")";
        }
    }

    private static string Stamp(object? raw) => raw is long ticks && ticks > 0
        ? DateTime.FromFileTimeUtc(ticks).ToString("O")
        : "0";

    private static void Report(CapabilityState state)
    {
        var line = state switch
        {
            CapabilityState.Available available => $"Available: {available.Detail}",
            CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code}): {degraded.SurvivingSemantics}",
            CapabilityState.Unavailable unavailable => $"Unavailable({unavailable.Reason.Code})",
            CapabilityState.PermissionRequired permission => $"PermissionRequired({permission.Reason.Code})",
            CapabilityState.DependencyMissing dependency =>
                $"DependencyMissing({dependency.Reason.Code}): needs {dependency.Dependency}",
            CapabilityState.Faulted faulted => $"Faulted({faulted.Reason.Code})",
            _ => state.GetType().Name,
        };

        Console.WriteLine("    state: " + line);
    }

    private static void Prompt(bool pause, string message)
    {
        Console.WriteLine(message);
        if (pause)
        {
            Console.ReadLine();
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed class ConsoleLog : ILogSink
    {
        public void Log(string message) => Console.WriteLine("    log| " + message);
    }
}
