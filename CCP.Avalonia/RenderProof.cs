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
            AppBuilder.Configure<App>()
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
            try
            {
                EnsureSetUp();

                var window = new MainWindow { Width = 880, Height = 620 };
                if (viewFactory is not null)
                {
                    var view = viewFactory();
                    // A ported Window cannot be nested; host its content instead. The chrome is
                    // Avalonia's, the content is the port - which is the part under test.
                    window.Content = view is Window w ? DetachContent(w) : view;
                    // Wide enough that the shell's 196px rail plus content is not clipped.
                    window.Width = 1100;
                    window.Height = 780;
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
                window.Close();

                var len = new FileInfo(outPath).Length;
                Console.WriteLine($"rendered -> {outPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height}, {len} bytes)");
                return len > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("render failed: " + ex);
                return 1;
            }
        }

        /// <summary>
        /// Lift a ported Window's content out so it can be hosted. The DataContext lives on the
        /// Window and is inherited by the content, so it must travel too - without this every
        /// bound string in a dialog rendered blank (found by the first --render-all).
        /// </summary>
        private static object? DetachContent(Window w)
        {
            var c = w.Content;
            w.Content = null;
            if (c is StyledElement se && se.DataContext is null)
                se.DataContext = w.DataContext;
            return c;
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
