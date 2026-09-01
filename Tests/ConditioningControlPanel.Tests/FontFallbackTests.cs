using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BROKEN-FONT GUARD, pinned.
///
/// <para><b>The incident.</b> A v6.8.6 user's Cascadia install was corrupt, and every panel he
/// opened rendered blank: WPF threw <c>UnauthorizedAccessException</c> out of
/// <c>FontFamily.GetFirstMatchingFont</c> during <c>TextBlock.MeasureOverride</c>. Because that is
/// the LAYOUT pass it re-threw on every measure, so the UI never completed a frame and crash.log
/// reached roughly half a gigabyte in one session.</para>
///
/// <para><b>The trap this suite exists to catch.</b> Every Cascadia site in the app ALREADY named
/// a comma fallback chain when that happened. A chain only rescues a family that is ABSENT - WPF
/// walks to the next link when a name does not resolve. It does nothing for a family that is
/// PRESENT but unreadable, because deciding whether the first link matches means opening the font
/// file, and that open is what throws. So "it has a fallback" is NOT the guarantee, and the next
/// person to add a hardcoded Cascadia with a comma after it would reintroduce the bug while
/// looking like they fixed it. The real guarantee is that risky faces are named in ONE swappable
/// place that <see cref="FontGuard"/> can strike out at startup.</para>
/// </summary>
public class FontFallbackTests
{
    /// <summary>Every shipped source file of the given extension, across every product root — see
    /// <see cref="SourceRoots"/> for the roots and the excluded directories.</summary>
    private static IEnumerable<string> SourceFiles(string extension) =>
        SourceRoots.EnumerateProductSources("*" + extension);

    // ------------------------------------------------------------------ the chain filter

    [Fact]
    public void A_healthy_machine_gets_its_chain_back_untouched()
    {
        Assert.Equal(FontGuard.MonoChain, FontGuard.FilterChain(FontGuard.MonoChain, _ => false));
    }

    [Fact]
    public void A_broken_first_link_is_struck_out_and_the_rest_survive_in_order()
    {
        var filtered = FontGuard.FilterChain(FontGuard.MonoChain,
            f => f.Equals("Cascadia Mono", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Consolas, Courier New", filtered);
    }

    [Fact]
    public void A_broken_middle_link_is_struck_out_without_disturbing_its_neighbours()
    {
        var filtered = FontGuard.FilterChain("Cascadia Mono, Consolas, Courier New",
            f => f.Equals("Consolas", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Cascadia Mono, Courier New", filtered);
    }

    /// <summary>
    /// The filter must never hand WPF an empty family name - that throws, which would turn the
    /// guard itself into the crash it exists to prevent.
    /// </summary>
    [Fact]
    public void Striking_out_every_link_still_leaves_something_nameable()
    {
        Assert.Equal("Segoe UI", FontGuard.FilterChain(FontGuard.MonoChain, _ => true));
        Assert.Equal("Segoe UI", FontGuard.FilterChain("", _ => true));
        Assert.Equal("Segoe UI", FontGuard.Sanitize(null));
    }

    /// <summary>
    /// Bundled faces are pack-relative (<c>./#Press Start 2P</c>) and ship inside the app, so they
    /// can never be the machine's broken system font and must pass through even when a family of
    /// the same bare name is struck out.
    /// </summary>
    [Fact]
    public void A_bundled_pack_relative_face_is_never_struck_out()
    {
        var filtered = FontGuard.FilterChain("./#Press Start 2P, Consolas, Courier New",
            f => f.Equals("Press Start 2P", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("./#Press Start 2P, Consolas, Courier New", filtered);
    }

    // ------------------------------------------------------------------ the markup pin

    /// <summary>
    /// The scans below are all "find me the offenders, expect none", which is exactly the shape
    /// that passes for free when the file walk returns nothing. <see cref="SourceRoots"/> guards
    /// the empty-walk and lost-root cases for every guard in the suite; this keeps the sharper
    /// "and it found the actual markup" pin on top of it.
    /// </summary>
    [Fact]
    public void The_markup_scan_actually_finds_the_app()
    {
        var xaml = SourceFiles(".xaml").ToList();

        Assert.True(xaml.Count > 50,
            "the XAML walk found only " + xaml.Count + " files across the product roots" +
            " - the scans in this suite would be passing vacuously");
        Assert.Contains(xaml, p => Path.GetFileName(p) == "MainWindow.xaml");
    }

    /// <summary>
    /// Cascadia is the face that took the app down, so no markup may name it directly any more:
    /// it lives in App.xaml's <c>Font.Mono</c> resource, which the guard can replace. This is the
    /// assertion that fails when someone pastes a hardcoded chain back in.
    /// </summary>
    [Fact]
    public void No_markup_names_Cascadia_directly()
    {
        var offenders = SourceFiles(".xaml")
            // Each head's own App.xaml is the one allowed place: it holds the Font.Mono resource
            // that FontGuard swaps. Matched on the filename, so a new head is exempt on arrival
            // rather than failing this test the day it lands.
            .Where(p => !string.Equals(Path.GetFileName(p), "App.xaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => File.ReadAllText(p).Contains("Cascadia", StringComparison.OrdinalIgnoreCase))
            .Select(SourceRoots.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "these XAML files name Cascadia directly instead of binding {DynamicResource Font.Mono}, " +
            "so FontGuard cannot swap the face out when the user's install is corrupt: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// The one place Cascadia IS named must be the app-level resource, and the markup has to bind
    /// it DYNAMICALLY - a StaticResource is snapshotted at parse time and would ignore the swap.
    /// </summary>
    [Fact]
    public void App_xaml_declares_the_mono_resource_and_agrees_with_the_guard()
    {
        var appXaml = SourceRoots.ReadProductFile("App.xaml");

        Assert.Contains("x:Key=\"" + FontGuard.MonoResourceKey + "\"", appXaml);
        Assert.Contains(FontGuard.MonoChain, appXaml);
    }

    [Fact]
    public void Every_consumer_of_the_mono_resource_binds_it_dynamically()
    {
        var staticUses = SourceFiles(".xaml")
            .Where(p => File.ReadAllText(p).Contains("StaticResource " + FontGuard.MonoResourceKey))
            .Select(SourceRoots.Relative)
            .ToList();

        Assert.True(staticUses.Count == 0,
            "StaticResource snapshots the value at parse time, so FontGuard's swap would never " +
            "reach these files - use DynamicResource: " + string.Join(", ", staticUses));
    }

    /// <summary>
    /// The whole point of the mechanical pass: a hardcoded face with NO fallback at all degrades
    /// to whatever WPF picks. Consolas was the big offender (108 bare sites). This keeps new ones
    /// from creeping back in.
    /// </summary>
    [Fact]
    public void No_markup_names_a_risky_face_without_a_fallback_behind_it()
    {
        var risky = new[] { "Consolas", "Impact", "Segoe MDL2 Assets", "Segoe UI Emoji" };
        var offenders = new List<string>();

        foreach (var path in SourceFiles(".xaml"))
        {
            var text = File.ReadAllText(path);
            foreach (var face in risky)
            {
                // A bare value is the closing quote right after the family name - a chain has a
                // comma there instead.
                if (Regex.IsMatch(text, "FontFamily=\"" + Regex.Escape(face) + "\"") ||
                    Regex.IsMatch(text, "Property=\"FontFamily\"\\s+Value=\"" + Regex.Escape(face) + "\""))
                {
                    offenders.Add(SourceRoots.Relative(path) + " -> " + face);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "these sites name a font with no fallback behind it: " + string.Join(", ", offenders));
    }
}
