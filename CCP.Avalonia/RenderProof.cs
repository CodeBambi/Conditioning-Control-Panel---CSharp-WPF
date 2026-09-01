using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// Renders the real MainWindow, or any ported view, offscreen and writes a PNG.
    ///
    /// This exists because "it compiles" and "it boots" are both weaker claims than "it draws".
    /// A desktop screenshot cannot run on a CI runner with no display server; headless Skia
    /// rendering can, so this is the only form of visual proof that survives in the pipeline.
    /// It draws the same window the user sees - not a stripped-down stand-in.
    /// </summary>
    internal static class RenderProof
    {
        private static bool _setUp;

        /// <summary>
        /// One headless platform per process. SetupWithoutStarting() throws if called twice,
        /// which is what --render-all would otherwise do for every view.
        /// </summary>
        private static void EnsureSetUp()
        {
            if (_setUp) return;
            // Start from the app's own builder so the render uses the same Inter font the user
            // sees; a bare Configure<App>() drew every PNG in the platform fallback face, which
            // hides exactly the text-metric overruns these renders exist to show. UseHeadless
            // replaces the windowing subsystem UsePlatformDetect chose, so no display is needed.
            Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            _setUp = true;
        }

        public static int Run(string outPath) => Run(outPath, null);

        /// <param name="viewFactory">Optional factory for a view to host instead of the default
        /// window content, so a ported view can be rendered on its own for a side-by-side fidelity
        /// comparison against its WPF original.
        ///
        /// A FACTORY, not an instance, and that distinction is load-bearing: an instance passed as
        /// an argument is constructed before this method runs, so its XAML loads before
        /// SetupWithoutStarting() has called App.Initialize(). The view then binds against an
        /// uninitialised app - which showed up as every localized string rendering as its raw key
        /// while the same lookup succeeded in --smoke. Construct after setup, not before.</param>
        public static int Run(string outPath, Func<Control>? viewFactory)
        {
            Window? window = null;
            try
            {
                EnsureSetUp();

                if (viewFactory is not null)
                {
                    var view = viewFactory();
                    if (view is Window w)
                    {
                        // A ported dialog is a Window: show IT, not its content re-parented into a
                        // host. Re-parenting breaks every `$parent[Window]` binding in the dialog
                        // (they resolve to the host, whose DataContext is null) and rendered blank
                        // labels with exit code 0 - the exact class of bug this flag exists to catch.
                        window = w;
                        if (double.IsNaN(w.Width) || w.Width < 200) w.Width = 1100;
                        if (double.IsNaN(w.Height) || w.Height < 200) w.Height = 780;
                    }
                    else
                    {
                        // Wide enough that the shell's 196px rail plus content is not clipped.
                        window = new MainWindow { Width = 1100, Height = 780, Content = view };
                    }
                }
                else
                {
                    window = new MainWindow { Width = 880, Height = 620 };
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
                Console.WriteLine($"rendered -> {outPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height}, {len} bytes)");
                return len > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("render failed: " + ex);
                return 1;
            }
            finally
            {
                // --render-all runs many views in one process; a window left open by a failed
                // view would otherwise stay in the headless platform's window list.
                try { window?.Close(); } catch { /* already closed or never shown */ }
            }
        }

        /// <summary>Render one view, found by simple or full type name.</summary>
        public static int RunView(string typeName, string outPath)
        {
            var type = Views().FirstOrDefault(t =>
                string.Equals(t.Name, typeName, StringComparison.Ordinal) ||
                string.Equals(t.FullName, typeName, StringComparison.Ordinal));
            if (type is null)
            {
                Console.Error.WriteLine($"no view named '{typeName}'. Known views:");
                foreach (var t in Views()) Console.Error.WriteLine("  " + t.Name);
                return 2;
            }
            return Run(outPath, () => (Control)Activator.CreateInstance(type)!);
        }

        /// <summary>Render every view to &lt;dir&gt;/&lt;TypeName&gt;.png. Non-zero if any fails.</summary>
        public static int RunAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var failed = new List<string>();
            var views = Views().ToList();
            foreach (var t in views)
            {
                var rc = Run(Path.Combine(dir, t.Name + ".png"), () => (Control)Activator.CreateInstance(t)!);
                if (rc != 0) failed.Add(t.Name);
            }
            Console.WriteLine($"{views.Count - failed.Count}/{views.Count} views rendered to {dir}");
            foreach (var f in failed) Console.Error.WriteLine("  FAILED " + f);
            return failed.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Every concrete Control with a parameterless constructor under the Views namespace.
        /// Sorted so the --render-all output and the "known views" listing are stable.
        /// </summary>
        private static IEnumerable<Type> Views() =>
            typeof(RenderProof).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract
                            && typeof(Control).IsAssignableFrom(t)
                            && (t.Namespace ?? "").StartsWith("ConditioningControlPanel.Avalonia.Views", StringComparison.Ordinal)
                            && t.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(t => t.Name, StringComparer.Ordinal);
    }
}
