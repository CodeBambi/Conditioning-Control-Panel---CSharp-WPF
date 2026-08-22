using System.Globalization;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Video;

// SP-140 deliverable 2 — the D124 measurement. NOT A TEST. See Measure.csproj for why.
//
// PRIVACY, which is a hard constraint of this packet and of the directory it reads:
//   * every file is OPENED, one frame is DECODED, and the clip is DISPOSED. Nothing else.
//   * no byte of any file is copied anywhere, no frame is written out, no still is saved.
//   * the decoded frame is reduced to two integers (its length, and how many distinct pixels
//     appear among at most 256 sampled points) before it leaves scope. Those are numbers about
//     a buffer, not content.
//   * FILENAMES ARE PRINTED ONLY FOR A FILE THAT REFUSES, because a parity defect nobody can
//     identify cannot be fixed. Every file that opens is reported by index and by technical
//     facts only. The product's own refusal strings already carry Path.GetFileName.

string[] Extensions = [".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm"];

var roots = args.Length > 0 ? args : [];
if (roots.Length == 0)
{
    Console.Error.WriteLine("usage: Measure <file-or-directory>...");
    return 2;
}

var files = new List<string>();
foreach (var root in roots)
{
    if (Directory.Exists(root))
    {
        files.AddRange(Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }
    else if (File.Exists(root))
    {
        files.Add(root);
    }
    else
    {
        Console.Error.WriteLine($"not found: {root}");
        return 2;
    }
}

var source = new MediaFoundationClipSource();
Console.WriteLine($"MediaStackUsable={source.MediaStackUsable}  files={files.Count}");
Console.WriteLine("idx | ext  | MiB    | open | frame | WxH        | fps    | duration   | btmup | colours | detail");

var opened = 0;
var decoded = 0;
var index = 0;
foreach (var file in files)
{
    index++;
    var ext = Path.GetExtension(file).ToLowerInvariant();
    var mib = (new FileInfo(file).Length / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);

    var state = source.Open(file, out var clip);
    if (state is not CapabilityState.Available || clip is null)
    {
        var reason = state is CapabilityState.Unavailable u
            ? $"{u.Reason.Code}: {u.Reason.Detail}"
            : state.ToString();
        // REFUSED — this one is named, because it is the parity defect.
        Console.WriteLine($"{index,3} | {ext,-4} | {mib,6} | NO   | -     | -          | -      | -          | -     | -       | {reason}");
        Console.WriteLine($"      REFUSED FILE: {file}");
        continue;
    }

    opened++;
    using (clip)
    {
        VideoFrame? frame = null;
        for (var attempt = 0; attempt < 30 && frame is null && !clip.Ended; attempt++)
        {
            frame = clip.ReadFrame();
        }

        var info = clip.Info;
        var fps = info.FrameInterval > TimeSpan.Zero
            ? (1.0 / info.FrameInterval.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture)
            : "none";
        var duration = info.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        var colours = frame is null ? "-" : DistinctSampledPixels(frame).ToString(CultureInfo.InvariantCulture);
        var got = frame is not null;
        if (got)
        {
            decoded++;
        }

        Console.WriteLine(
            $"{index,3} | {ext,-4} | {mib,6} | yes  | {(got ? "yes" : "NO"),-5} | " +
            $"{info.Width + "x" + info.Height,-10} | {fps,-6} | {duration} | {info.BottomUp,-5} | {colours,-7} | " +
            (got ? $"{frame!.Pixels.Length} bytes" : "no frame in 30 ReadSample calls"));
        if (!got)
        {
            Console.WriteLine($"      OPENED BUT NO FRAME: {file}");
        }
    }
}

Console.WriteLine();
Console.WriteLine($"TOTAL {files.Count} | OPENED {opened} | DECODED-A-FRAME {decoded} | REFUSED {files.Count - opened}");
return 0;

// How many distinct BGRX pixels appear among at most 256 evenly spaced sample points. One
// integer. A solid or zeroed buffer answers 1; a real picture answers many. Nothing about the
// picture itself survives this function.
static int DistinctSampledPixels(VideoFrame frame)
{
    var pixels = frame.Pixels;
    var count = pixels.Length / VideoFrame.BytesPerPixel;
    if (count == 0)
    {
        return 0;
    }

    var step = Math.Max(1, count / 256);
    var seen = new HashSet<uint>();
    for (var i = 0; i < count; i += step)
    {
        var o = i * VideoFrame.BytesPerPixel;
        seen.Add((uint)(pixels[o] | (pixels[o + 1] << 8) | (pixels[o + 2] << 16)));
    }

    return seen.Count;
}
