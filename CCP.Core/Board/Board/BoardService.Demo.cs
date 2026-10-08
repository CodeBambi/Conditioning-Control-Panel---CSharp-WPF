#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    // DEBUG only: CCP_BOARD_DEMO=<path to a PNG> shows that picture as the board without the server,
    // so the board can be looked at before the routes are deployed. Optional CCP_BOARD_DEMO_FX
    // (comma list, default ola,twinkle,shine,chase), CCP_BOARD_DEMO_FRAMES and CCP_BOARD_DEMO_FPS.
    // The demo post uses a version from the file's write time, so a re-saved picture reads as NEW.
    public sealed partial class BoardService
    {
        private static string? DemoPath
        {
            get
            {
                var p = Environment.GetEnvironmentVariable("CCP_BOARD_DEMO");
                return !string.IsNullOrWhiteSpace(p) && File.Exists(p) ? p : null;
            }
        }

        private static BoardPost? DemoPost()
        {
            var path = DemoPath;
            if (path == null) return null;
            var fx = (Environment.GetEnvironmentVariable("CCP_BOARD_DEMO_FX") ?? "ola,twinkle,shine,chase")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int frames = int.TryParse(Environment.GetEnvironmentVariable("CCP_BOARD_DEMO_FRAMES"), out var f) ? Math.Clamp(f, 1, 8) : 1;
            int fps = int.TryParse(Environment.GetEnvironmentVariable("CCP_BOARD_DEMO_FPS"), out var r) ? Math.Clamp(r, 1, 12) : 6;
            int version = 1_000_000 + (int)(File.GetLastWriteTimeUtc(path).Ticks / TimeSpan.TicksPerSecond % 1_000_000);
            return new BoardPost(version, new List<string>(fx), null, "lobby", BoardAudience.Everyone, frames, fps, 64, 36);
        }

        private static async System.Threading.Tasks.Task<byte[]?> DemoFetchAsync(int version, System.Threading.CancellationToken ct)
        {
            var path = DemoPath;
            return path == null ? null : await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
    }
}
#endif
