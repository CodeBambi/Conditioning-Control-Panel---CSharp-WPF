using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--video-check &lt;file&gt; [out.png]`: opens the real MiniPlayerWindow on the desktop with a
    /// video, as the Library's preview click will, and proves decode rather than drawing: frames
    /// change over time, the seek slider moves the player, pause freezes the frames, close disposes
    /// the player, frees the frame buffer and ends LibVLC's decoder threads. Non-zero on any
    /// mismatch. <see cref="Check"/> is shared with the headless test.
    /// </summary>
    internal static class VideoCheck
    {
        public static int Run(string file, string? shot)
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
            new LibVlcAudio().Seed(); // as App startup: video gets the same, single LibVLC
            var fails = -1;
            Dispatcher.UIThread.Post(async () =>
            {
                try { fails = await Check(file, Console.WriteLine, shot); }
                catch (Exception e) { Console.Error.WriteLine("video-check threw: " + e); }
                finally { lifetime.Shutdown(); }
            });
            lifetime.Start(Array.Empty<string>());
            Console.WriteLine(fails == 0 ? "PASS" : $"FAIL ({fails} mismatch(es))");
            return fails == 0 ? 0 : 1;
        }

        internal static async Task<int> Check(string file, Action<string> log, string? shot = null)
        {
            var fails = 0;
            void Expect(bool ok, string what) { log($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }

            var threadsBefore = Threads();
            var w = new MiniPlayerWindow();
            w.LoadFile(file);
            w.Show();

            for (var i = 0; i < 100 && w.VideoFrame == null; i++) await Task.Delay(50);
            Expect(w.VideoFrame != null, $"first frame decoded ({w.VideoFrame?.PixelSize})");
            if (w.VideoFrame == null) { w.Close(); return fails; }

            var a = Hash(w.VideoFrame);
            await Task.Delay(500);
            var b = Hash(w.VideoFrame!);
            Expect(a != b, $"frames change while playing ({a:x} -> {b:x})");
            if (shot != null) Screenshot(w, shot, log);

            var slider = w.FindControl<Slider>("SeekSlider")!;
            var before = w.Player!.Position;
            slider.Value = 70;
            w.SeekToSliderPosition(); // the slider's pointer-release path
            await Task.Delay(400);
            var after = w.Player!.Position;
            Expect(after >= 0.65f && after < 0.9f, $"seek to 70% moves the player ({before:0.00} -> {after:0.00})");

            w.TogglePlayPause();
            await Task.Delay(600);
            var glyph = w.FindControl<Button>("BtnPlayPause")!.Content as string;
            var p1 = Hash(w.VideoFrame!);
            await Task.Delay(500);
            var p2 = Hash(w.VideoFrame!);
            Expect(!w.Player!.IsPlaying && glyph == "▶", $"pause stops the player (IsPlaying={w.Player.IsPlaying}, glyph {glyph})");
            Expect(p1 == p2, $"frames frozen while paused ({p1:x} / {p2:x})");

            var threadsOpen = Threads();
            w.Close();
            await Task.Delay(800);
            var threadsAfter = Threads();
            Expect(w.Player == null && w.VideoFrame == null && w.FrameBufferFreed, "close disposes the player, bitmap and frame buffer");
            Expect(threadsAfter < threadsOpen, $"close ends LibVLC's threads (threads {threadsBefore} before, {threadsOpen} open, {threadsAfter} after)");
            return fails;
        }

        private static int Threads()
        {
            using var p = Process.GetCurrentProcess();
            return p.Threads.Count;
        }

        private static long Hash(WriteableBitmap bmp)
        {
            using var fb = bmp.Lock();
            var bytes = new byte[fb.RowBytes * fb.Size.Height];
            Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
            var h = new HashCode();
            h.AddBytes(bytes);
            return (uint)h.ToHashCode();
        }

        private static void Screenshot(Window w, string path, Action<string> log)
        {
            var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
            using var rtb = new RenderTargetBitmap(size);
            rtb.Render(w);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            rtb.Save(path);
            log($"  screenshot: {path}");
        }
    }
}
