// SP-025 V3 dev-loop experiment: product-shaped media replace + release.
// One app-lifetime LibVLC + MediaPlayer (vmem vout, sw decode — V1/V2 disciplines, the
// spike-proven 96x96 RV32 shape); per cycle: new Media → Play → wait EndReached →
// Stop (background thread) → Media.Dispose (the V3 suspect). 5 cycles. Every step
// line-flushed BEFORE the risky call so a crash is attributable.
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

static void Line(string s) { Console.WriteLine(s); Console.Out.Flush(); }

var clip = args.Length > 0 ? args[0] : throw new ArgumentException("usage: V3MediaRelease <video-path>");
Line($"clip={clip}");

Core.Initialize();
using var vlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
Line("libvlc up");

const int W = 96, H = 96;
var frameBuffer = new byte[W * H * 4];
var pin = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
var frames = 0;

var player = new MediaPlayer(vlc);
player.SetVideoFormatCallbacks((ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines) =>
{
    var rv32 = System.Text.Encoding.ASCII.GetBytes("RV32");
    Marshal.Copy(rv32, 0, chroma, 4);
    width = W; height = H; pitches = W * 4; lines = H;
    return 1;
}, null);
player.SetVideoCallbacks(
    (IntPtr opaque, IntPtr planes) => { Marshal.WriteIntPtr(planes, pin.AddrOfPinnedObject()); return pin.AddrOfPinnedObject(); },
    null!,
    (IntPtr opaque, IntPtr picture) => {
        frames++;
        if (frames <= 3)
        {
            long sum = 0; var n = 0;
            for (var i = 0; i + 2 < frameBuffer.Length; i += 401 * 4) { sum += (frameBuffer[i] + frameBuffer[i + 1] + frameBuffer[i + 2]) / 3; n++; }
            Line($"frame {frames} luma={(n > 0 ? sum / n : -1)}");
        }
    });
Line("vmem wired (spike shape)");

for (var i = 1; i <= 5; i++)
{
    var media = new Media(vlc, clip, FromType.FromPath);
    player.Media = media;
    var endTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    void OnEnd(object? s, EventArgs e) => endTcs.TrySetResult();
    player.EndReached += OnEnd;
    Line($"cycle {i}: play");
    if (!player.Play()) { Line($"cycle {i}: PLAY REFUSED"); return 2; }
    var done = await Task.WhenAny(endTcs.Task, Task.Delay(20000)) == endTcs.Task;
    player.EndReached -= OnEnd;
    Line($"cycle {i}: endReached={done} frames={frames}");
    Line($"cycle {i}: stop (background thread — vmem deadlock class)");
    await Task.Run(() => player.Stop());
    Line($"cycle {i}: MEDIA.DISPOSE — the V3 suspect");
    media.Dispose();
    Line($"cycle {i}: media disposed CLEAN");
}

Line("all 5 cycles clean — Media.Dispose after EndReached is SAFE in the product shape");
return 0;
