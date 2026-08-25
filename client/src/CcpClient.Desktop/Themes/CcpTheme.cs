using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CcpClient.Desktop.Themes;

/// <summary>
/// A SKIN, and the mechanism by which this product is themed at all.
///
/// <para><b>The finding this exists to close, re-measured on 2026-08-25 before a line of it was
/// written.</b> The shipping product was built from this repository, run headed against a throwaway
/// <c>CCP_USERDATA_DIR</c> (so no mod but the shipping default was installed, and its own selector
/// read "CCP Default"), and every exact hex counted across the whole 2735x1650 window:</para>
/// <code>
///   #08080C 27.37%   #0C0C13  9.21%   #11111A 13.93%   #E84393 3.07%   #B83078 0.10%
///   #34343C  1.28%   #222240  5.13%   #1C1C35  2.86%
///   #121220 32 px    #181830  7 px    #2E2E4A  9 px    #3A3A5C 0    #7A7A94 0    #FF1493 0
/// </code>
/// <para>Six of those seven leading colours are NOT in <c>Resources/Theme/Colors.xaml</c> at all,
/// and four of that file's own values are down at single-digit PIXEL counts. "CCP Default" is
/// itself a mod (<c>Models/BuiltInMods.cs:918-926</c>) and <c>RefreshThemeAwareElements</c> rewrites
/// the matching resource keys from it during the shell's construction
/// (<c>MainWindow/MainWindow.xaml.cs:317</c> calls it; <c>:1619-1623</c> and <c>:1655-1657</c> are
/// the writes). So <c>Colors.xaml</c> is the DESIGN-TIME SEED and this is the skin over it — and
/// the port's <c>Themes/Ccp.axaml</c>, which takes its values from that seed line by line, is the
/// same design-time seed for the same reason. Both products carry the seed on disk and paint the
/// skin at runtime.</para>
///
/// <para><b>Only the six a mod really supplies live here.</b> <c>ModTheme</c> carries
/// <c>AccentColor</c>, <c>AccentLightColor</c>, <c>AccentDarkColor</c>, <c>BackgroundColor</c>,
/// <c>PanelColor</c> and <c>SurfaceColor</c> and nothing else, so <c>ElevatedSurface</c>,
/// <c>SeatBg</c>, the three inks, <c>NeonPurple</c> and both semantics keep their seed value. That
/// asymmetry is upstream's own and it is measurable: <c>#222240</c> — <c>ElevatedSurface</c>,
/// supplied by no mod — is 5.13% of the shipping product's window while <c>#2E2E4A</c>, the seed
/// value of the mod-derived <c>PanelAccent</c> beside it, is nine pixels.</para>
///
/// <para><b>What this deliberately is not.</b> No mod discovery, no chooser, no download, no
/// per-mod assets. This is the theme half of a mod and the one built-in that ships. The shape does
/// accommodate the rest — a manifest reader would produce a <see cref="CcpTheme"/> and call
/// <see cref="ApplyTo"/> on a mod switch exactly as upstream re-runs its own method
/// (<c>MainWindow.xaml.cs:2332</c>) — but nothing here is built for that today.</para>
/// </summary>
/// <param name="Accent">The brand accent. Upstream's <c>PinkColor</c> slot.</param>
/// <param name="AccentLight">The accent's light step. Upstream's <c>PinkButtonHovered</c> slot.</param>
/// <param name="AccentDark">The accent's dark step. Upstream's <c>DarkPink</c> slot.</param>
/// <param name="Background">The application ground. Upstream's <c>DarkerBg</c> slot.</param>
/// <param name="Panel">A card that carries content. Upstream's <c>PanelBg</c> slot.</param>
/// <param name="Surface">Structural chrome. Upstream's <c>SurfaceBg</c> slot.</param>
public sealed record CcpTheme(
    Color Accent,
    Color AccentLight,
    Color AccentDark,
    Color Background,
    Color Panel,
    Color Surface)
{
    /// <summary>
    /// The built-in that ships, and the skin a fresh install of the shipping product really wears.
    /// Every byte is <c>ConditioningControlPanel/Models/BuiltInMods.cs:920-925</c>, in that file's
    /// own order.
    ///
    /// <para><b>THIS IS THE HEADED HARNESS'S SEEDED-REGRESSION ANCHOR.</b>
    /// <c>client/tools/verify/self-test.ps1</c> replaces the hex on the
    /// <c>AccentLight</c> line below with a wrong colour, rebuilds, and requires BOTH
    /// <c>rail-door-selected-border</c> and <c>rack-row-selected-marker</c> to trip on real pixels.
    /// The anchor lives here rather than in <c>Themes/Ccp.axaml</c> because this is where the
    /// value a user SEES comes from: seeding the seed dictionary would be overwritten by
    /// <see cref="ApplyTo"/> three lines into startup and would reach no pixel at all. The script
    /// requires that literal to appear EXACTLY ONCE in this file, so do not restate it in prose.</para>
    /// </summary>
    public static CcpTheme CcpDefault { get; } = new(
        Accent: Color.Parse("#E84393"),
        AccentLight: Color.Parse("#FF6FB5"),
        AccentDark: Color.Parse("#B83078"),
        Background: Color.Parse("#08080C"),
        Panel: Color.Parse("#11111A"),
        Surface: Color.Parse("#0C0C13"));

    /// <summary>
    /// Upstream's <c>LightenColor</c>, byte for byte
    /// (<c>MainWindow/MainWindow.xaml.cs:1750-1756</c>): lift each channel by
    /// <paramref name="amount"/> of its remaining distance to 255, then TRUNCATE to a byte.
    ///
    /// <para>The truncation is not incidental and it is why this is copied rather than
    /// approximated. Rounding instead would move <c>PanelAccent</c> by one unit on two channels,
    /// which is under every tolerance in the check manifest and therefore invisible until a
    /// pixel-exact comparison against the shipping product disagrees for a reason nobody can
    /// find.</para>
    ///
    /// <para>PROVED AGAINST PIXELS, not against the source: <c>LightenColor(#11111A, 0.15)</c>
    /// predicts <c>#34343C</c>, a colour declared in NO dictionary in either product, and that hex
    /// is 57,780 pixels — 1.28% — of the headed capture of the shipping product. Its seed
    /// neighbour <c>#2E2E4A</c> is nine.</para>
    /// </summary>
    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)Math.Min(255, c.R + (255 - c.R) * amount),
        (byte)Math.Min(255, c.G + (255 - c.G) * amount),
        (byte)Math.Min(255, c.B + (255 - c.B) * amount));

    /// <summary>
    /// Upstream's <c>DarkenColor</c>, byte for byte
    /// (<c>MainWindow/MainWindow.xaml.cs:1758-1764</c>): scale each channel toward black and
    /// truncate. Same truncation, same reason.
    /// </summary>
    public static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)Math.Max(0, c.R * (1 - amount)),
        (byte)Math.Max(0, c.G * (1 - amount)),
        (byte)Math.Max(0, c.B * (1 - amount)));

    /// <summary>
    /// The token keys this theme owns, and the colour each one takes. Split out from
    /// <see cref="ApplyTo"/> so the mapping and the arithmetic can be stated without an Avalonia
    /// application in the room.
    ///
    /// <para><b>Two are DERIVED and not carried</b>, because upstream derives them at the moment it
    /// writes them: <c>PanelAccent</c> and <c>PanelAccentHover</c> are
    /// <c>LightenColor(panel, .15)</c> and <c>LightenColor(panel, .25)</c>
    /// (<c>MainWindow.xaml.cs:1611-1612</c>). A mod supplies neither. Pinning them to the values a
    /// particular panel colour happens to produce would look identical for exactly as long as
    /// nobody changed the panel colour.</para>
    ///
    /// <para><b>Fluent's accent family is derived here too</b>, and it has to be: the port shadows
    /// all seven of Fluent's keys because the platform ones resolve from the user's Windows
    /// personalisation setting, so a theme that moved only the base would leave every hover,
    /// pressed and disabled state on the previous accent. The ladder is upstream's own two shade
    /// functions at 0.15 / 0.30 / 0.45 in each direction — no third arithmetic enters the product
    /// for six keys nobody photographs individually.</para>
    /// </summary>
    public Dictionary<string, Color> Tokens()
    {
        var tokens = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            // MainWindow.xaml.cs:1619-1623. The port's key names are upstream's for these five.
            ["DarkerBg"] = Background,
            ["PanelBg"] = Panel,
            ["SurfaceBg"] = Surface,
            ["PanelAccent"] = Lighten(Panel, 0.15),
            ["PanelAccentHover"] = Lighten(Panel, 0.25),

            // MainWindow.xaml.cs:1655-1657 writes PinkColor / DarkPink / PinkButtonHovered from the
            // same three. The port keeps upstream's name for the base and has its own two for the
            // ladder's steps, because two token keys may hold one value but two CHECKS may not —
            // see Themes/Ccp.axaml on why the shell's liveries are separate keys.
            ["PinkColor"] = Accent,
            ["ShellAccent"] = AccentDark,
            ["ShellAccentBright"] = AccentLight,

            ["SystemAccentColor"] = Accent,
            ["SystemAccentColorLight1"] = Lighten(Accent, 0.15),
            ["SystemAccentColorLight2"] = Lighten(Accent, 0.30),
            ["SystemAccentColorLight3"] = Lighten(Accent, 0.45),
            ["SystemAccentColorDark1"] = Darken(Accent, 0.15),
            ["SystemAccentColorDark2"] = Darken(Accent, 0.30),
            ["SystemAccentColorDark3"] = Darken(Accent, 0.45),
        };

        return tokens;
    }

    /// <summary>
    /// Paint the skin over the seed. Upstream assigns into <c>Application.Current.Resources</c> by
    /// KEY and everything downstream repaints because it referenced the key rather than a value
    /// (<c>MainWindow.xaml.cs:1618-1682</c>); this is the same assignment against the same shape.
    ///
    /// <para><b>Colours only, and no brushes</b>, which is where the port stops copying. Upstream
    /// writes a second, brush-shaped copy of every entry "in case any are frozen from initial
    /// load" (<c>:1667</c>) — a WPF Freezable problem, and it is upstream's own bug that the
    /// bottom action bar it misses stays on the seed colour while every other panel follows the
    /// mod. Every brush in <c>Themes/Ccp.axaml</c> is <c>Color="{DynamicResource ...}"</c>, and
    /// <c>ThemeTokenHeadlessTests</c> already measured that replacing a colour key repaints the
    /// brush INSTANCE a tree is already holding. Writing brushes as well would add fourteen
    /// assignments that change nothing.</para>
    /// </summary>
    public void ApplyTo(IResourceDictionary resources)
    {
        foreach (var (key, colour) in Tokens())
        {
            resources[key] = colour;
        }
    }

    /// <summary>
    /// The token layer's own brush for <paramref name="key"/>, for the handful of surfaces this
    /// product builds in C# rather than in markup.
    ///
    /// <para><b>Why it hands back the DICTIONARY'S instance instead of a fresh brush.</b> Every
    /// brush in <c>Themes/Ccp.axaml</c> is <c>Color="{DynamicResource ...}"</c>, so the instance
    /// this returns repaints when the key moves — which is the entire point of the layer, and what
    /// a <c>new SolidColorBrush(...)</c> built from a looked-up colour would throw away. A window
    /// already on screen when a theme is applied follows it.</para>
    ///
    /// <para><b>And why it throws.</b> A missing key resolves to null, a null brush paints nothing,
    /// and the first person to notice is a user looking at invisible text — the exact silent defect
    /// <c>ThemeTokenTests</c> exists to refuse in markup. Code-built surfaces get the same
    /// treatment, loudly and at the point of the mistake.</para>
    /// </summary>
    public static IBrush Brush(string key)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException(
                $"CcpTheme.Brush(\"{key}\") ran with no Application — the token layer is not loaded yet");

        return app.TryFindResource(key, out var found) && found is IBrush brush
            ? brush
            : throw new InvalidOperationException(
                $"the token layer declares no brush '{key}' — a DynamicResource typo paints nothing and warns nobody");
    }
}
