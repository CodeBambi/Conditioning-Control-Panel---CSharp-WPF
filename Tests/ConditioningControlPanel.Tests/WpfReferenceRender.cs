using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The WPF half of the reference-render workflow (.github/workflows/reference-render.yml): renders
/// every <see cref="Window"/> and <see cref="UserControl"/> in the product assembly to
/// <c>$CCP_REFERENCE_RENDER_DIR/&lt;TypeName&gt;.png</c>, so pair-renders.py can put it beside the
/// Avalonia render of the same parity row.
///
/// <para>Opt-in: without <c>CCP_REFERENCE_RENDER_DIR</c> the test returns at once, so the normal
/// suite is unchanged. With it, nothing ever fails the run - a type that throws or times out is a
/// line in <c>_failures.txt</c>, because the point is the pictures that did draw.</para>
///
/// <para>Isolation is the suite's own: <see cref="TestLocalizationBootstrap"/> already points
/// CCP_USERDATA_DIR at a fresh temp profile before anything reads it. No App.OnStartup runs, so no
/// services, audio, network or devices; windows are never shown - their content is detached
/// and rendered offscreen, the same trick LeashExplainRenderTests uses.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class WpfReferenceRender
{
    private const int PerTypeTimeoutSeconds = 30;

    [Fact]
    [Trait("Category", "ReferenceRender")]
    public void RenderEveryView()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_REFERENCE_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        var failures = new List<string>();
        var index = new SortedDictionary<string, object>(StringComparer.Ordinal);
        if (PackUriBootstrap.Failure != null) failures.Add("(harness): " + PackUriBootstrap.Failure);

        // Views read App.Settings in their constructors; a default settings object in the sandboxed
        // profile is what a fresh install shows. The setter is private, so reflection.
        try
        {
            if (App.Settings == null)
                typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null, new ConditioningControlPanel.Services.SettingsService());
        }
        catch (Exception ex) { failures.Add("(settings): " + FirstLine(ex)); }

        // A view's timer or async continuation that throws later must not kill the harness thread.
        WpfRenderHarness.OnStaThread(() =>
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => e.Handled = true);

        var product = typeof(App).Assembly;
        var types = Loadable(product)
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                        && (typeof(Window).IsAssignableFrom(t) || typeof(UserControl).IsAssignableFrom(t)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wedged = false;

        foreach (var type in types)
        {
            var name = taken.Add(type.Name) ? type.Name : type.FullName!;
            var file = name + ".png";
            if (wedged)
            {
                failures.Add($"{name}: skipped, render thread wedged by an earlier type");
                index[name] = new { file = (string?)null, ok = false, error = "skipped" };
                continue;
            }

            // Fewest parameters first; required ones get "" / default / null (see RenderOne).
            var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Count(p => !p.IsOptional)).FirstOrDefault();
            if (ctor == null)
            {
                failures.Add($"{name}: no public constructor");
                index[name] = new { file = (string?)null, ok = false, error = "no public constructor" };
                continue;
            }

            Exception? error = null;
            int w = 0, h = 0;
            try
            {
                WpfRenderHarness.OnStaThread(() =>
                {
                    try { (w, h) = RenderOne(ctor, Path.Combine(dir, file)); }
                    catch (Exception ex) { error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex; }
                }, PerTypeTimeoutSeconds);
            }
            catch (Exception ex)
            {
                error = ex;
                wedged = ex.Message.Contains("did not finish in time", StringComparison.Ordinal);
            }

            if (error == null)
                index[name] = new { file, width = w, height = h, ok = true, error = (string?)null };
            else
            {
                failures.Add($"{name}: {FirstLine(error)}");
                index[name] = new { file = (string?)null, ok = false, error = FirstLine(error) };
            }
        }

        File.WriteAllLines(Path.Combine(dir, "_failures.txt"), failures);
        File.WriteAllText(Path.Combine(dir, "_index.json"),
            JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static (int, int) RenderOne(ConstructorInfo ctor, string path)
    {
        var args = ctor.GetParameters().Select(p =>
            p.HasDefaultValue ? p.DefaultValue
            : p.ParameterType == typeof(string) ? ""
            : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
            : null).ToArray();
        var view = (FrameworkElement)ctor.Invoke(args);

        FrameworkElement root;
        int w, h;
        var background = (Brush)new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E));
        if (view is Window window)
        {
            // Never shown: the content is lifted out and drawn on its own, with the window's own
            // background and resources so DynamicResource lookups still find them.
            w = Size(window.Width, 1280);
            h = Size(window.Height, 800);
            if (window.Background != null) background = window.Background;
            var content = window.Content as FrameworkElement;
            window.Content = null;
            var host = new Border { Background = background, Child = content, Resources = window.Resources };
            try { window.Close(); } catch { /* never shown; nothing to close */ }
            root = host;
        }
        else
        {
            w = Size(view.Width, 900);
            h = Size(view.Height, 600);
            root = new Border { Background = background, Child = view };
        }

        root.Measure(new System.Windows.Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        return (w, h);
    }

    private static int Size(double design, int fallback) =>
        double.IsNaN(design) || design < 1 || double.IsInfinity(design) ? fallback : (int)Math.Ceiling(design);

    /// <summary>Type, message and the first product frame - enough to tell a harness gap from a bug.</summary>
    private static string FirstLine(Exception ex)
    {
        var frame = (ex.StackTrace ?? "").Split('\n')
            .FirstOrDefault(l => l.Contains("ConditioningControlPanel.", StringComparison.Ordinal)
                                 && !l.Contains(".Tests.", StringComparison.Ordinal))?.Trim();
        return $"{ex.GetType().Name}: {ex.Message}".Split('\n')[0].Trim() + (frame == null ? "" : " | " + frame);
    }

    private static IEnumerable<Type> Loadable(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
    }
}
