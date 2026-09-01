using System;
using System.IO;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Renders the real MainWindow offscreen and writes a PNG.
    ///
    /// This exists because "it compiles" and "it boots" are both weaker claims than "it draws".
    /// A desktop screenshot cannot run on a CI runner with no display server; headless Skia
    /// rendering can, so this is the only form of visual proof that survives in the pipeline.
    /// It draws the same window the user sees - not a stripped-down stand-in.
    /// </summary>
    internal static class RenderProof
    {
        public static int Run(string outPath) => Run(outPath, null);

        /// <param name="view">Optional view to host instead of the default window content, so a
        /// ported view can be rendered on its own for a side-by-side fidelity comparison against
        /// its WPF original.</param>
        public static int Run(string outPath, global::Avalonia.Controls.Control? view)
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

                var window = new MainWindow { Width = 880, Height = 620 };
                if (view is not null)
                {
                    window.Content = view;
                    window.Width = 720;
                    window.Height = 760;
                }
                window.Show();

                // Two passes: layout settles on the first, content is painted on the second.
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                using var frame = window.CaptureRenderedFrame();
                if (frame is null)
                {
                    Console.Error.WriteLine("render produced no frame");
                    return 1;
                }

                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                frame.Save(outPath);

                var len = new FileInfo(outPath).Length;
                Console.WriteLine($"rendered MainWindow -> {outPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height}, {len} bytes)");
                return len > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("render failed: " + ex);
                return 1;
            }
        }
    }
}
