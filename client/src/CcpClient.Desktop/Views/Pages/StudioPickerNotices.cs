using CcpClient.Desktop.Effects;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The two Studio pickers' catalogues and their words: WHICH SPIRAL is drawn, and WHAT COLOUR the
/// tint is. Both backends were already here — <see cref="SpiralLibrary"/> resolves a file and
/// <see cref="PinkFilterColour"/> parses a hex string — and neither had anything that PICKED.
/// </summary>
public static class StudioPickerNotices
{
    /// <summary>
    /// The first entry of the spiral picker: no file chosen, so
    /// <see cref="SpiralLibrary.Resolve"/> takes the library's first.
    ///
    /// <para>Upstream's first card is "Default" and means the spiral compiled into the application
    /// (<c>Features/SpiralFeatureControl.xaml.cs:265</c>). This port bundles no art at all — D86,
    /// recorded on <see cref="SpiralLibrary"/> — so the same slot can only mean "whatever the folder
    /// offers first", and the label says that rather than promising a built-in that does not
    /// exist.</para>
    /// </summary>
    public const string SpiralLibraryDefault = "Library default (the first file in the folder)";

    /// <summary>
    /// The empty-library line, which is upstream's own empty state
    /// (<c>Features/SpiralFeatureControl.xaml:193-199</c>: "No spirals in your folder yet …") minus
    /// its promise of a built-in spiral, for the reason <see cref="SpiralLibraryDefault"/> gives.
    /// </summary>
    public static string SpiralLibraryEmpty(string spiralsFolder) =>
        $"No spirals to choose from yet. Put a .gif, .png or .jpg in {spiralsFolder}, then Refresh.";

    /// <summary>
    /// How the picker labels one library file: the FILE NAME only, never the full path — the same
    /// media-logging rule the panel's own library line already follows, and upstream's own card
    /// caption (<c>Path.GetFileNameWithoutExtension</c>, <c>SpiralFeatureControl.xaml.cs:275</c>).
    /// </summary>
    public static string SpiralLabel(string path) =>
        System.IO.Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name
            ? name
            : System.IO.Path.GetFileName(path);

    /// <summary>
    /// The tint palette.
    ///
    /// <para><b>A bounded palette rather than upstream's full colour dialog, and the reason is a
    /// dependency rather than a design opinion.</b> Upstream opens
    /// <c>System.Windows.Forms.ColorDialog</c> with <c>FullOpen = true</c>
    /// (<c>Features/PinkFilterFeatureControl.xaml.cs:181-188</c>) — a Win32 dialog with no
    /// cross-platform counterpart. Avalonia's own <c>ColorPicker</c> lives in a separate
    /// <c>Avalonia.Controls.ColorPicker</c> package that this client does not reference and that
    /// adding is a project-file change outside this slice. So the user gets a NAMED set they can
    /// pick from and a reset, which is the outcome the card owes: a tint they chose instead of one
    /// they were given.</para>
    ///
    /// <para>The first entry is the RESET, and its value is the empty string upstream writes —
    /// "empty = default (mod / hot pink)" in upstream's own comment (<c>:199</c>). Every other
    /// value is a <c>#RRGGBB</c> literal in exactly the shape upstream persists
    /// (<c>$"#{R:X2}{G:X2}{B:X2}"</c>, <c>:189</c>) and
    /// <see cref="PinkFilterColour.TryParseHex"/> reads.</para>
    /// </summary>
    public static readonly IReadOnlyList<(string Label, string Hex)> TintPalette =
    [
        ("Hot pink (the default)", ""),
        ("Rose", "#FF3B6E"),
        ("Bubblegum", "#FF9ED8"),
        ("Lilac", "#C77DFF"),
        ("Violet", "#7B2FF7"),
        ("Cyan", "#31E1F7"),
        ("Amber", "#FFB13B"),
        ("Crimson", "#B3123C"),
    ];

    /// <summary>
    /// Which palette entry a persisted colour string is, or 0 (the default) when the string is
    /// blank or is a colour no entry carries — a hand-edited file, or a value written by a build
    /// with a different palette. Never -1: a picker showing nothing selected would be a third state
    /// the tint itself does not have, since <see cref="PinkFilterColour.Resolve"/> always answers
    /// with a colour.
    /// </summary>
    public static int TintIndexOf(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour))
        {
            return 0;
        }

        for (var i = 0; i < TintPalette.Count; i++)
        {
            if (string.Equals(TintPalette[i].Hex, colour, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }
}
