using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CcpVerify;

// CcpVerify — tier-3 deterministic named-check evaluation against one capture.
// Usage: CcpVerify --capture <png|bmp> --surface <name> --state <name> [--manifest <path>]
//        CcpVerify --vacuity <png|bmp>
// Exit 0: all checks passed (or the capture is non-vacuous, in --vacuity mode).
// Exit 1: usage/config error. Exit 2: first failed check named on stdout.
// Exit 3: the capture is VACUOUS — fewer than two distinct colours, count named on stdout.
// Decode uses Avalonia.Media.Imaging.Bitmap (Skia codec: PNG and BMP) — never System.Drawing.
//
// --vacuity is the CAPTURE STEP'S gate, and it lives here so there is ONE implementation of the
// rule rather than one per capture script (capture.ps1 on Windows, capture-wslg.sh on Linux, and
// self-test.ps1 through both). capture.ps1's standing rule — "this script never reads a pixel,
// all pixel logic lives in CcpVerify" — is why the scripts call in rather than counting for
// themselves. It is deliberately NOT the named-check evaluation: a capture script must stay able
// to photograph a deliberately regressed build (self-test.ps1 phase 1 seeds one), so the capture
// step asks only "did anything get drawn at all", never "is it the right colour".

string? capture = null, surface = null, state = null, manifestPath = null, vacuity = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--capture" when i + 1 < args.Length: capture = args[++i]; break;
        case "--surface" when i + 1 < args.Length: surface = args[++i]; break;
        case "--state" when i + 1 < args.Length: state = args[++i]; break;
        case "--manifest" when i + 1 < args.Length: manifestPath = args[++i]; break;
        case "--vacuity" when i + 1 < args.Length: vacuity = args[++i]; break;
        default:
            Console.Error.WriteLine($"unknown or incomplete argument: {args[i]}");
            return 1;
    }
}

if (vacuity is not null)
{
    try
    {
        var image = DecodeCapture(vacuity);
        var census = CaptureCensus.Of(image);
        Console.WriteLine($"capture: {vacuity} ({image.Width}x{image.Height})");
        if (census.IsVacuous)
        {
            Console.WriteLine($"VACUOUS CAPTURE: {census}");
            return 3;
        }

        Console.WriteLine($"NON-VACUOUS CAPTURE: {census}");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
}

if (capture is null || surface is null || state is null)
{
    Console.Error.WriteLine("usage: CcpVerify --capture <png|bmp> --surface <name> --state <name> [--manifest <path>]");
    Console.Error.WriteLine("       CcpVerify --vacuity <png|bmp>");
    return 1;
}

manifestPath ??= Path.Combine(AppContext.BaseDirectory, "checks.json");
if (!File.Exists(manifestPath))
{
    // Development-tree fallback: tools/verify/checks.json relative to the capture or cwd.
    manifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "checks.json");
}

try
{
    var checks = CheckManifest.Load(manifestPath);
    var image = DecodeCapture(capture);
    Console.WriteLine($"capture: {capture} ({image.Width}x{image.Height})");

    // BEFORE any named check, and it reports the root cause instead of a symptom. A vacuous
    // capture already failed here (0/525 pixels matched), which is honest but names the wrong
    // thing: the defect is that nothing was captured, not that a border is the wrong colour.
    var census = CaptureCensus.Of(image);
    if (census.IsVacuous)
    {
        Console.WriteLine($"VACUOUS CAPTURE: {census}");
        return 3;
    }

    Console.WriteLine($"census: {census}");
    Console.WriteLine($"manifest: {manifestPath} ({checks.Count} checks; surface={surface} state={state})");

    var results = CheckEvaluator.EvaluateCapture(checks, surface, state, image);
    foreach (var result in results)
    {
        Console.WriteLine(result.ToString());
    }

    var firstFailure = results.FirstOrDefault(r => !r.Passed);
    if (firstFailure is not null)
    {
        Console.WriteLine($"FIRST FAILED CHECK: {firstFailure.Name}");
        return 2;
    }

    Console.WriteLine("ALL CHECKS PASSED");
    return 0;
}
catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static DecodedImage DecodeCapture(string path)
{
    if (!File.Exists(path))
    {
        throw new IOException($"capture not found: {path}");
    }

    // Skia decode needs a render platform: headless with real drawing (official v12 pattern —
    // docs.avaloniaui.net/docs/concepts/headless, fetched 2026-07-19). No app window is ever shown.
    AppBuilder.Configure<Application>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    using var bitmap = new Bitmap(path);
    var width = bitmap.PixelSize.Width;
    var height = bitmap.PixelSize.Height;
    if (bitmap.Format is null || bitmap.Format.Value != Avalonia.Platform.PixelFormat.Bgra8888)
    {
        throw new InvalidDataException($"capture '{path}' decoded as {bitmap.Format?.ToString() ?? "unknown"}; expected Bgra8888.");
    }

    var bgra = new byte[width * height * 4];
    unsafe
    {
        fixed (byte* ptr = bgra)
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), (IntPtr)ptr, bgra.Length, width * 4);
        }
    }

    return new DecodedImage(width, height, bgra);
}
