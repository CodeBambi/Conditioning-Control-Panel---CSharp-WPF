using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// The ambient layers' cost, measured: each preset composed on an <see cref="AmbientFxCanvas"/>
    /// inside a window of the given size, stepped and repainted <c>frames</c> times, timing sim plus
    /// Skia paint (the UI-thread share of a frame; the upload and composite are the render thread's).
    /// Backs the <c>--fx-bench</c> self-check and the bench test. Needs a running Avalonia platform.
    /// </summary>
    public static class FxBench
    {
        public readonly record struct Row(string Name, double MsPerFrame, int BackingW, int BackingH);

        /// <summary>The surfaces 7.1.5 runs, by their layer sets.</summary>
        public static readonly (string Name, AmbientFxConfig Config, double W, double H)[] Presets =
        {
            ("dashboard fog+aurora+dust", new AmbientFxConfig { Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.AuroraWash | AmbientFxLayers.DustField }, 1400, 900),
            ("settings embers+dust", new AmbientFxConfig { Layers = AmbientFxLayers.Embers | AmbientFxLayers.DustField }, 1400, 900),
            ("launcher fog+glow+embers", new AmbientFxConfig { Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.GlowBreath | AmbientFxLayers.Embers | AmbientFxLayers.SheenSweep }, 1200, 760),
            ("section edge top fog+drift", new AmbientFxConfig { Layers = AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, EdgeSide = EdgeSide.Top }, 1400, 56),
            ("premium vault motes", new AmbientFxConfig { Layers = AmbientFxLayers.VaultMotes | AmbientFxLayers.FogDrift | AmbientFxLayers.DustField }, 1400, 900),
        };

        public static List<Row> Run(int frames = 120)
        {
            Dispatcher.UIThread.VerifyAccess();
            var rows = new List<Row>();
            foreach (var (name, config, w, h) in Presets)
            {
                var fx = new AmbientFxCanvas();
                var win = new Window { Width = w, Height = h, Content = fx, ShowActivated = false };
                win.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    fx.StartLayers(config);
                    fx.StepForTests(30);                          // warm the pools and the sprites
                    var sw = Stopwatch.StartNew();
                    fx.StepForTests(Math.Max(1, frames));
                    sw.Stop();
                    var (bw, bh) = fx.Surface.BackingSize;
                    rows.Add(new Row(name, sw.Elapsed.TotalMilliseconds / Math.Max(1, frames), bw, bh));
                }
                finally
                {
                    fx.Stop();
                    win.Content = null;
                    win.Close();
                }
            }
            return rows;
        }

        /// <summary>Plain-text table, one row per preset.</summary>
        public static string Format(IEnumerable<Row> rows)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in rows)
                sb.AppendLine(FormattableString.Invariant($"{r.Name,-30} {r.MsPerFrame,7:0.000} ms/frame  backing {r.BackingW}x{r.BackingH}"));
            return sb.ToString();
        }
    }
}
