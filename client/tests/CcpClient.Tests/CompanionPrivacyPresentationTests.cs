using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Floor guards over the two headed pairs this packet adds to
/// <c>client/tools/verify/checks.json</c>: <c>companion-privacy</c> (audit row A3 over row A4) and
/// <c>companion-transcript</c> (row D11). Same shape as the rack's, the session rack's and the
/// toast's guards: a pair is evidence only while it stays a PAIR and while neither half can accept
/// the other's colour.
///
/// <para><b>What these are and are not.</b> They are LEXICAL guards over a JSON file, a PowerShell
/// script and an AXAML file on disk. They prove the four checks still exist, still claim the class
/// a headless frame cannot discharge, still separate the two colours of each pair, and still name
/// colours the product really paints. They prove NOTHING about pixels — a real capture does that,
/// and it is a Windows-headed step this suite cannot take.</para>
/// </summary>
public class CompanionPrivacyPresentationTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    private static string CompanionMarkup() =>
        File.ReadAllText(Path.Combine(
            FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Features", "Companion", "CompanionWindow.axaml"));

    private static IReadOnlyList<ManifestCheck> ChecksFor(string surface) =>
        [.. CheckManifest.Load(ManifestPath())
            .Where(c => string.Equals(c.Surface, surface, StringComparison.Ordinal))];

    /// <summary>
    /// Both surfaces checked in BOTH states, every check claiming <c>presentation-verified</c>. A
    /// surface checked in one state cannot distinguish anything — the whole bite is that the other
    /// state's real capture fails it — and a check quietly demoted to <c>draw-verified</c> would
    /// let a headless run claim a composited pixel it never read.
    /// </summary>
    [Fact]
    public void BothPairsAreCheckedInBothStates_AndEveryCheckClaimsPresentationVerified()
    {
        Assert.Equal(
            ["companion-privacy-broad-seat", "companion-privacy-titles-seat"],
            ChecksFor("companion-privacy").Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["companion-transcript-closed-ground", "companion-transcript-open-ground"],
            ChecksFor("companion-transcript").Select(c => c.Name).Order(StringComparer.Ordinal));

        var all = ChecksFor("companion-privacy").Concat(ChecksFor("companion-transcript")).ToList();
        Assert.Equal(4, all.Count);
        foreach (var check in all)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);
            Assert.Equal("region-color", check.Kind);
        }

        Assert.Equal(
            ["companion-privacy/broad", "companion-privacy/titles", "companion-transcript/closed", "companion-transcript/open"],
            all.Select(c => $"{c.Surface}/{c.State}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Neither half of either pair accepts a colour the surface it samples can really show. The
    /// arithmetic that matters: the transcript pair is 13/13/26 apart (the companion ground against
    /// the panel fill), which is exactly the separation the permissions pair already runs at
    /// tolerance 4 — widen either past it and the pair stops proving anything.
    /// </summary>
    [Fact]
    public void NoCheckAcceptsAnotherColourItsOwnSurfaceCanShow()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the companion window ground (CompanionWindow.axaml Background)", 0x12, 0x12, 0x20),
            ("the unselected dial seat / panel fill (Border.dial-seat)", 0x1F, 0x1F, 0x3A),
            ("the SELECTED dial seat (Border.dial-seat.selected)", 0x4A, 0x2C, 0x55),
            ("the companion header plate (#FF241A2B)", 0x24, 0x1A, 0x2B),
            ("the companion button face (Button.companion, #FF2E2E4A)", 0x2E, 0x2E, 0x4A),
        ];

        var checks = ChecksFor("companion-privacy").Concat(ChecksFor("companion-transcript")).ToList();
        Assert.Equal(4, checks.Count);

        var compared = 0;
        foreach (var check in checks)
        {
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var neighbour in neighbours)
            {
                if (neighbour.R == r && neighbour.G == g && neighbour.B == b)
                {
                    continue; // this IS the colour the check is for
                }

                compared++;
                var distance = Math.Max(
                    Math.Abs(neighbour.R - r), Math.Max(Math.Abs(neighbour.G - g), Math.Abs(neighbour.B - b)));
                Assert.True(check.Tolerance < distance,
                    $"check '{check.Name}' expects {check.ExpectedColor} with tolerance {check.Tolerance}, which "
                    + $"also ACCEPTS {neighbour.Name} — they are only {distance} apart on the widest channel. A "
                    + "check that cannot tell the two states apart is not evidence about either");
            }
        }

        // Every comparison skips exactly one neighbour — the check's own colour — so this
        // arithmetic holds only if all four checks expect a colour this list really names.
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);
    }

    /// <summary>
    /// The token layer (2026-08-25) put one indirection between this guard and the value it reads.
    /// The companion surface names a key now — <c>{DynamicResource SeatBgBrush}</c> — and the byte
    /// lives in <c>Themes/Ccp.axaml</c>. Resolving it here keeps the guard pointed at the colour
    /// the product actually paints instead of at the spelling it happens to use; a guard that only
    /// grepped for a literal would have gone quiet the moment the literal moved, which is the
    /// failure mode this whole class exists to prevent.
    ///
    /// <para>Deliberately a two-step text resolution and not a XAML load: this is a pure-logic
    /// fact in the non-Avalonia project, and the point is to read what the SOURCE declares.</para>
    /// </summary>
    private static string ResolveDeclaredColour(string range, string role)
    {
        var brushRef = System.Text.RegularExpressions.Regex.Match(range, @"\{DynamicResource (\w+)\}");
        var literal = System.Text.RegularExpressions.Regex.Match(range, @"#[0-9A-Fa-f]{8}");
        Assert.True(brushRef.Success || literal.Success,
            $"{role} declares neither a token nor a colour literal");

        // WHICHEVER COMES FIRST, and the ordering is the whole point rather than a tidiness
        // preference: these ranges are style blocks that set several brush properties, so
        // "the first token anywhere in the range" and "the first colour anywhere in the range"
        // are different sites. Preferring tokens unconditionally read the badge ring's
        // ShellAccentBright for the selected dial seat's fill and passed a confident wrong answer.
        if (!brushRef.Success || (literal.Success && literal.Index < brushRef.Index))
        {
            // Still a literal at this site: a surface that has not been tokenised is checked
            // exactly as it was.
            return literal.Value.ToUpperInvariant();
        }

        var brushKey = brushRef.Groups[1].Value;
        var tokens = ThemeTokens();

        // Brush -> the Color key it binds -> that key's value. Both hops are asserted rather than
        // defaulted: a brush that resolves to nothing would otherwise read as "no colour declared"
        // and pass the caller a silent empty string.
        var brush = System.Text.RegularExpressions.Regex.Match(
            tokens, $@"<SolidColorBrush x:Key=""{brushKey}"" Color=""\{{DynamicResource (\w+)\}}""");
        Assert.True(brush.Success, $"{role}: Themes/Ccp.axaml declares no brush '{brushKey}'");

        var colourKey = brush.Groups[1].Value;
        var colour = System.Text.RegularExpressions.Regex.Match(
            tokens, $@"<Color x:Key=""{colourKey}"">(#[0-9A-Fa-f]{{8}})</Color>");
        Assert.True(colour.Success, $"{role}: Themes/Ccp.axaml declares no colour '{colourKey}'");
        return colour.Groups[1].Value.ToUpperInvariant();
    }

    private static string ThemeTokens() =>
        File.ReadAllText(Path.Combine(
            FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Themes", "Ccp.axaml"));

    /// <summary>
    /// The colours in the manifest are the colours the PRODUCT paints. A manifest that agreed only
    /// with itself would keep passing after a restyle, over captures of the new colour it no longer
    /// describes.
    /// </summary>
    [Fact]
    public void EveryExpectedColourIsOneTheCompanionSurfaceReallyDeclares()
    {
        var markup = CompanionMarkup();

        // The seat pair: unselected fill on Border.dial-seat, selected fill on its .selected arm.
        Assert.Contains("<Style Selector=\"Border.dial-seat\">", markup, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Border.dial-seat.selected\">", markup, StringComparison.Ordinal);
        var seat = markup.IndexOf("<Style Selector=\"Border.dial-seat\">", StringComparison.Ordinal);
        var selected = markup.IndexOf("<Style Selector=\"Border.dial-seat.selected\">", StringComparison.Ordinal);
        Assert.True(seat < selected, "the selected arm must follow the base seat style, or it never wins");

        // Bounded at each style's own </Style>, so a later style's brush can never answer for
        // this one.
        var selectedEnd = markup.IndexOf("</Style>", selected, StringComparison.Ordinal);
        Assert.True(selectedEnd > selected, "the selected dial-seat style is never closed");
        Assert.Equal("#FF1F1F3A", ResolveDeclaredColour(markup[seat..selected], "the unselected dial seat"));
        Assert.Equal("#FF4A2C55", ResolveDeclaredColour(markup[selected..selectedEnd], "the selected dial seat"));

        // The transcript pair's `closed` half is the companion window's own ground.
        var groundAt = markup.IndexOf("Background=", StringComparison.Ordinal);
        Assert.True(groundAt >= 0, "the companion window declares no Background at all");
        Assert.Equal(
            "#FF121220",
            ResolveDeclaredColour(markup[groundAt..(groundAt + 80)], "the companion window ground"));

        // Every colour the surface can declare, resolved through the token layer where it uses one.
        var declaredColours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(markup, @"#[0-9A-Fa-f]{8}"))
        {
            declaredColours.Add(m.Value);
        }

        var tokens = ThemeTokens();
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(markup, @"\{DynamicResource (\w+)\}"))
        {
            var brush = System.Text.RegularExpressions.Regex.Match(
                tokens, $@"<SolidColorBrush x:Key=""{m.Groups[1].Value}"" Color=""\{{DynamicResource (\w+)\}}""");
            if (!brush.Success)
            {
                continue; // a geometry or font token, which carries no colour to check
            }

            var colour = System.Text.RegularExpressions.Regex.Match(
                tokens, $@"<Color x:Key=""{brush.Groups[1].Value}"">(#[0-9A-Fa-f]{{8}})</Color>");
            Assert.True(colour.Success,
                $"Themes/Ccp.axaml declares brush '{m.Groups[1].Value}' over a colour key that does not exist");
            declaredColours.Add(colour.Groups[1].Value);
        }

        foreach (var check in ChecksFor("companion-privacy").Concat(ChecksFor("companion-transcript")))
        {
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            var declared = $"#FF{r:X2}{g:X2}{b:X2}";
            // The transcript's own ground is set in code, not markup — it is the same #1F1F3A.
            var inTranscript = declared.Equals("#FF1F1F3A", StringComparison.OrdinalIgnoreCase);
            Assert.True(declaredColours.Contains(declared) || inTranscript,
                $"check '{check.Name}' expects {check.ExpectedColor}, which the companion surface does not declare");
        }
    }

    /// <summary>
    /// <b>Both surfaces are drivable, and the script gates on UIA before it reads a pixel.</b> A
    /// manifest naming a surface/state pair <c>capture.ps1</c> cannot bind would fail at parameter
    /// binding — before any refusal the script writes — and a script that captured without reading
    /// the tree first would hand a colour check the job of deciding whether the state was ever
    /// reached.
    /// </summary>
    [Fact]
    public void TheScriptCanDriveBothPairs_AndRefusesBeforeItReadsAnyPixel()
    {
        var script = CaptureScript();

        foreach (var surface in new[] { "companion-privacy", "companion-transcript" })
        {
            Assert.Contains($"'{surface}'", script, StringComparison.Ordinal);
            Assert.Contains($"elseif ($Surface -eq '{surface}')", script, StringComparison.Ordinal);
        }

        Assert.Contains("'companion-privacy' = @('broad', 'titles')", script, StringComparison.Ordinal);
        Assert.Contains("'companion-transcript' = @('closed', 'open')", script, StringComparison.Ordinal);

        // The UIA gates that must run before the capture — each one a refusal by name.
        string[] gates =
        [
            // A3: the dial's own copy, read from the tree rather than assumed from the markup.
            "the privacy card is not headed by upstream's line",
            "the dial is not at Off in a fresh process; the default is not closed",
            "the Off stop does not carry upstream's sentence",
            // A4: the inversion — asking must not widen, and the dial must not claim it did.
            "the per-app editor is on screen before anyone asked for it",
            "asking for page titles did not open the per-app editor",
            "the dial moved to \"+ Page titles\" with NO app named",
            "naming an app did not move the dial to \"+ Page titles\"",
            // D11: absence and presence of a whole window, plus its three lines of copy.
            "the transcript window is already open before anything was pressed",
            "the transcript is not headed by upstream's line",
            "the transcript's empty state is not upstream's line",
            "the transcript's storage note is not upstream's line",
        ];
        Assert.NotEmpty(gates);
        foreach (var gate in gates)
        {
            Assert.Contains(gate, script, StringComparison.Ordinal);
        }

        // And the geometry refusals: a band that has drifted onto glyphs, out of its seat, or out
        // of the window it is supposed to be sampling is a failure rather than a capture.
        //
        // THE SEAT PAIR HAS THREE OF THEM NOW, and the middle one is why: the capture deliberately
        // starts ABOVE the seat's 5 DIP of padding so that the seat's own 1 DIP border is in the
        // picture. Cropped to the padding alone both captures were a single flat colour, which the
        // capture step's non-vacuity gate refuses — so "not too far up" is no longer the only way
        // this band can be wrong, and "not far enough up" is now a refusal in its own right.
        //
        // THE TRANSCRIPT PAIR NOW HAS THE SAME SHAPE, plus one refusal no other surface here needs.
        // Its band is the only one that deliberately extends OUTSIDE the window whose state it is
        // reading: the transcript is a large flat window with no boundary anywhere near the sampled
        // rows, so the boundary the `open` capture carries is the transcript's OWN LEFT EDGE, which
        // the band straddles. That makes "the band is inside the transcript" the wrong assertion —
        // the correct one is that the transcript's real edge lands where the companion's layout says
        // it will, and inside the band rather than beside it.
        string[] geometry =
        [
            "it is sampling the segment's own glyphs",
            "it would carry no boundary and be a flat fill again",
            "off the dial strip entirely",
            "it is sampling the heading's glyphs",
            "'the dial seat band' 'the companion window'",
            "'the transcript sample band' 'the companion window'",
            "it would carry no boundary in `closed` and be a flat fill again",
            "the band would not straddle it and the open capture would be a flat fill again",
            "'the band right of the transcript edge' 'the transcript window'",
            "the whole band is covered and the capture carries no boundary",
        ];
        Assert.NotEmpty(geometry);
        foreach (var refusal in geometry)
        {
            Assert.Contains(refusal, script, StringComparison.Ordinal);
        }
    }
}
