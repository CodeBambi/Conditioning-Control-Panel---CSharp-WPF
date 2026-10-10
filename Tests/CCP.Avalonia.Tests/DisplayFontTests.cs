using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fredoka, the display face. WPF 7.1.5 packs it (/Fonts/#Fredoka, about 75 call sites); the port
/// named it ("Fredoka, Segoe UI") but packed it on neither OS, so Windows drew Segoe UI and Linux
/// Inter. It is packed now and the bare name is mapped to the packed family.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DisplayFontTests
{
    [Fact]
    public void TheFontShipsWithItsLicence()
    {
        var fonts = Path.Combine(RepoRoot(), "Assets", "fonts");
        Assert.True(File.Exists(Path.Combine(fonts, "Fredoka.ttf")));
        var licence = File.ReadAllText(Path.Combine(fonts, "OFL-Fredoka.txt"));
        Assert.Contains("SIL Open Font License, Version 1.1", licence);
        Assert.Contains("Fredoka Project Authors", licence);
    }

    [Fact]
    public async Task ThePackedFamilyResolvesByItsNameAtEveryWeight()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Assert.True(AssetLoader.Exists(new Uri("avares://CCP.Avalonia/Resources/fonts/Fredoka.ttf")));
            Assert.True(AssetLoader.Exists(new Uri("avares://CCP.Avalonia/Resources/fonts/OFL-Fredoka.txt")));
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold })
            {
                var face = new Typeface(new FontFamily(AppFonts.BundledDisplay), FontStyle.Normal, weight);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(face, out var glyphs), "weight " + weight);
                Assert.StartsWith(AppFonts.DisplayName, glyphs!.FamilyName);   // Avalonia reads name id 1: "Fredoka Light"
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheBareNameTheViewsUseLandsOnThePackedFace()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            // A font manager built the way Program.cs builds the app's: with AppFonts.Options().
            var views = new Typeface(new FontFamily("Fredoka, Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);
            // Without the mapping the name falls through to the next family (what the port drew before).
            if (FontManager.Current.TryGetGlyphTypeface(views, out var unmapped)) Assert.DoesNotContain(AppFonts.DisplayName, unmapped!.FamilyName);
            using (BindFontOptions(AppFonts.Options()))
            {
                const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var impl = typeof(FontManager).GetProperty("PlatformImpl", any)!.GetValue(FontManager.Current)!;
                var manager = (FontManager)typeof(FontManager).GetConstructors(any).Single(c => c.GetParameters().Length == 1).Invoke(new[] { impl });
                var face = new Typeface(new FontFamily("Fredoka, Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);
                Assert.True(manager.TryGetGlyphTypeface(face, out var glyphs));
                Assert.StartsWith(AppFonts.DisplayName, glyphs!.FamilyName);
                // Controls: it is the mapping that did it, and only for that name.
                var other = new Typeface(new FontFamily("Nope Sans, Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);
                if (manager.TryGetGlyphTypeface(other, out var plain)) Assert.DoesNotContain(AppFonts.DisplayName, plain!.FamilyName);
            }
            return Task.CompletedTask;
        });
    }

    // Avalonia 12's facade omits the locator: the same scoped binding FirstRunFolderPickerTests uses.
    private static IDisposable BindFontOptions(FontManagerOptions options)
    {
        var locatorType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator")!;
        var mutable = locatorType.GetProperty("CurrentMutable")!;
        var scope = (IDisposable)locatorType.GetMethod("EnterScope")!.Invoke(mutable.GetValue(null), null)!;
        var locator = mutable.GetValue(null)!;   // re-read AFTER EnterScope
        var registration = locator.GetType().GetMethods()
            .Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(FontManagerOptions)).Invoke(locator, null)!;
        registration.GetType().GetMethods()
            .Single(m => m.Name == "ToConstant" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(FontManagerOptions)).Invoke(registration, new object[] { options });
        return scope;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
