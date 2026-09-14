using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Dev;

/// <summary>
/// Desk-run trigger for THE BACK ROOM effects (launched via <c>--backroom-fx-rig [outDir]</c>). Fires
/// each fx id at each intensity through the real dispatcher and the real services, without the room,
/// writes every ack to <c>acks.jsonl</c> and grabs the screen a few times per effect.
///
/// <para>Options: <c>--fx fx.jackpot,fx.melt</c> (default every contract id), <c>--intensity
/// Calm,Normal</c> (default all three), <c>--motion Full|Reduced|Off</c> (default Full, so Normal and
/// Full can be seen whatever the desk's own Motion setting is), <c>--all-toggles</c> (ignore the
/// feature toggles for this run only; the room itself never can), <c>--exit</c> (shut down when done).
/// Settings are never written. Dead code in every normal launch.</para>
///
/// <para>Screen grabs need a composited, unlocked desktop (see DoorShooter for why), and the brain
/// drain surface is excluded from capture unless AllowOverlayCapture is on, so a melt grab can come
/// back without the melt.</para>
/// </summary>
internal static class BackRoomFxRig
{
    private static readonly string[] PresetWords = { "Drop", "Relax", "Let Go", "Sink" };

    public static void Run(string outDir, string[] args)
    {
        try
        {
            Directory.CreateDirectory(outDir);
            var ids = ArgList(args, "--fx") ?? BackRoomFxPlan.KnownIds.ToArray();
            var intensities = (ArgList(args, "--intensity") ?? new[] { "Calm", "Normal", "Full" })
                .Select(x => Enum.TryParse<BackRoomFxIntensity>(x, true, out var v) ? v : (BackRoomFxIntensity?)null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            var motion = Enum.TryParse<MotionLevel>(ArgList(args, "--motion")?.FirstOrDefault(), true, out var m) ? m : MotionLevel.Full;
            bool allToggles = args.Contains("--all-toggles");
            bool exit = args.Contains("--exit");
            _ = RunAsync(outDir, ids, intensities, motion, allToggles, exit);
        }
        catch (Exception ex) { App.Logger?.Error(ex, "[BackRoomFxRig] failed to start"); }
    }

    private static async Task RunAsync(string outDir, string[] ids, BackRoomFxIntensity[] intensities,
        MotionLevel motion, bool allToggles, bool exit)
    {
        try
        {
            await Task.Delay(8000);   // let startup settle (services, main window, first-run modals)
            var fx = BackRoomFxServices.Shared;
            var deal = BuildDeal();
            var log = Path.Combine(outDir, "acks.jsonl");
            App.Logger?.Information("[BackRoomFxRig] {Ids} ids x {N} intensities, {Gifs} dealt gifs, motion {Motion}",
                ids.Length, intensities.Length, deal.Gifs.Count, motion);

            foreach (var id in ids)
            {
                foreach (var intensity in intensities)
                {
                    if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
                    fx.CancelAll();
                    await Task.Delay(1500);

                    var real = BackRoomFxServices.ReadEnvironment();
                    var gates = allToggles ? FxGates.AllOn with { SpiralStill = real.Gates.SpiralStill } : real.Gates;
                    var ack = fx.Fire(id, SymbolsFor(id), deal, new FxEnvironment(motion, intensity, gates));
                    File.AppendAllText(log, JsonConvert.SerializeObject(new
                    {
                        fxId = id, intensity = intensity.ToString(), motion = motion.ToString(),
                        fired = ack.Fired, skipped = ack.Skipped.Select(s => new { prim = s.Prim, why = s.Why.ToString().ToLowerInvariant() }),
                    }) + Environment.NewLine);

                    var recipe = BackRoomFxPlan.Recipe(id, intensity);
                    int lengthMs = recipe == null ? 0 : recipe.Steps.Max(s => s.AtMs + Math.Max(s.DurationMs, s.Count * BackRoomFxPlan.WordGapMs));
                    var shots = new List<int> { 800, 1800 };
                    if (recipe?.IsHero == true) shots.Add(recipe.HeroMs + 1500);
                    else if (lengthMs > 2600) shots.Add(Math.Min(lengthMs - 400, 4000));

                    int elapsed = 0;
                    for (int i = 0; i < shots.Count; i++)
                    {
                        await Task.Delay(Math.Max(0, shots[i] - elapsed));
                        elapsed = shots[i];
                        Grab(Path.Combine(outDir, $"{id.Replace('.', '_')}_{intensity.ToString().ToLowerInvariant()}_{(char)('a' + i)}.png"));
                    }
                    await Task.Delay(Math.Max(0, lengthMs + 1200 - elapsed));
                }
            }
            fx.CancelAll();
            File.WriteAllText(Path.Combine(outDir, "done.txt"), DateTime.Now.ToString("O"));
            App.Logger?.Information("[BackRoomFxRig] done");
            if (exit) Application.Current?.Shutdown();
        }
        catch (Exception ex) { App.Logger?.Error(ex, "[BackRoomFxRig] run failed"); }
    }

    /// <summary>Symbols a real tape would carry for each line (CONTRACT section 3.2 / 4).</summary>
    private static string[] SymbolsFor(string id) => id switch
    {
        "fx.jackpot" => new[] { "emi3", "emi3", "emi3" },
        "fx.gif_storm" => new[] { "gif1", "gif1", "gif1" },
        "fx.gif_burst" => new[] { "gif0", "gif2", "gif3" },
        "fx.sub_cascade" => new[] { "sub0", "sub1", "sub2" },
        "fx.sub_pair" => new[] { "sub0", "sub3" },
        "fx.sub_single" => new[] { "sub1" },
        "fx.melt" => new[] { "melt" },
        _ => Array.Empty<string>(),
    };

    /// <summary>A stand-in deal from the flash pool (local files under the assets folder only). The room
    /// uses the C4 media feed instead; this keeps the rig independent of it.</summary>
    private static BackRoomMediaDeal BuildDeal()
    {
        var gifs = new List<BackRoomGif>();
        try
        {
            var root = Path.GetFullPath(App.EffectiveAssetsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var picks = App.Flash?.GetChaosImagePaths(24) ?? new List<string>();
            foreach (var p in picks.Where(p => !FlashService.IsRemotePath(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (gifs.Count >= 4) break;
                var full = Path.GetFullPath(p);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) continue;
                var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                var url = "https://ccp.assets/" + string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
                gifs.Add(new BackRoomGif("g" + gifs.Count, url, 0, 0, "pool"));
            }
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoomFxRig] could not deal gifs"); }
        var words = PresetWords.Select((w, i) => new BackRoomWord("s" + i, w, "preset")).ToList();
        return new BackRoomMediaDeal(1, gifs, words);
    }

    private static void Grab(string file)
    {
        try
        {
            var b = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
            bmp.Save(file, ImageFormat.Png);
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "[BackRoomFxRig] screen grab failed"); }
    }

    private static string[]? ArgList(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        if (i < 0 || i + 1 >= args.Length || args[i + 1].StartsWith("--")) return null;
        return args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
