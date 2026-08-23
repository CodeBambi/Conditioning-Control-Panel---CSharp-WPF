using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The two Studio pickers' catalogues. The spiral library and the tint parser were already here and
/// already covered; what is new — and what these facts are about — is that a picker has to offer
/// EVERY entry, where <see cref="SpiralLibrary.Resolve"/> only ever answers with the first.
/// </summary>
public class StudioPickerTests
{
    /// <summary>
    /// The picker's list is every decodable file, in the same ordinal order
    /// <see cref="SpiralLibrary.Resolve"/> falls through. <c>Resolve</c> can never show this: it
    /// returns one path, so a library enumeration that dropped everything after the first would
    /// have passed every fact that existed before the picker did.
    /// </summary>
    [Fact]
    public void TheLibraryOffersEveryDecodableFile_InTheSameOrderResolveFallsThrough()
    {
        using var lab = new Library();
        lab.Write("zebra.gif");
        lab.Write("Apple.png");
        lab.Write("banana.jpg");
        lab.Write("notes.txt");
        lab.Write("clip.webp");
        Directory.CreateDirectory(Path.Combine(lab.Folder, "a-subfolder"));

        var listed = SpiralLibrary.ListFolder(lab.Folder);

        Assert.Equal(
            [
                Path.Combine(lab.Folder, "Apple.png"),
                Path.Combine(lab.Folder, "banana.jpg"),
                Path.Combine(lab.Folder, "zebra.gif"),
            ],
            listed);

        // The head of the list IS what an unconfigured install draws, so the picker and the module
        // cannot disagree about which spiral "library default" means.
        Assert.Equal(listed[0], SpiralLibrary.Resolve(lab.AssetsRoot, null));
    }

    /// <summary>An absent folder is an ordinary first-run state — no exception, and a picker with
    /// nothing but its default entry.</summary>
    [Fact]
    public void AMissingLibraryFolderIsAnEmptyListRatherThanAThrow()
    {
        using var lab = new Library();
        Assert.Empty(SpiralLibrary.ListFolder(lab.Folder));
    }

    /// <summary>The label is the file name without its extension — upstream's own card caption
    /// (<c>Features/SpiralFeatureControl.xaml.cs:275</c>) — and never the full path, which is the
    /// port's standing rule for what a panel prints.</summary>
    [Theory]
    [InlineData("spiral-one.gif", "spiral-one")]
    [InlineData("no-extension", "no-extension")]
    public void ASpiralIsLabelledByItsFileNameAndNeverItsPath(string fileName, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-picker-label", fileName);

        Assert.Equal(expected, StudioPickerNotices.SpiralLabel(path));
        Assert.DoesNotContain(Path.GetTempPath(), StudioPickerNotices.SpiralLabel(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every palette entry is a value the module can actually read back. Entry 0 is upstream's
    /// reset — the empty string it stores for "use the default tint"
    /// (<c>Features/PinkFilterFeatureControl.xaml.cs:199</c>) — and every other entry parses
    /// through the same lenient parser the effect uses, so a typo in the palette cannot ship a
    /// swatch that silently falls back to hot pink.
    /// </summary>
    [Fact]
    public void EveryPaletteEntryIsAColourTheModuleCanReadBack_AndTheFirstIsUpstreamsReset()
    {
        Assert.Equal(string.Empty, StudioPickerNotices.TintPalette[0].Hex);
        Assert.False(PinkFilterColour.TryParseHex(StudioPickerNotices.TintPalette[0].Hex, out _));

        foreach (var (label, hex) in StudioPickerNotices.TintPalette.Skip(1))
        {
            Assert.True(PinkFilterColour.TryParseHex(hex, out var rgb), $"'{label}' ({hex}) does not parse");

            // And it is a colour the user would SEE as a choice: hot pink is what a blank falls back
            // to, so a palette entry equal to it would be a second reset wearing another name.
            Assert.NotEqual((PinkFilterColour.HotPink.Red, PinkFilterColour.HotPink.Green, PinkFilterColour.HotPink.Blue), rgb);
        }

        Assert.Equal(
            StudioPickerNotices.TintPalette.Count,
            StudioPickerNotices.TintPalette.Select(entry => entry.Label).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A persisted colour maps back onto the entry that wrote it, case-insensitively, and anything
    /// the palette does not carry lands on the default rather than on "nothing selected" — a state
    /// the tint itself cannot be in, because <see cref="PinkFilterColour.Resolve"/> always answers
    /// with a colour.
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData(null, 0)]
    [InlineData("#31E1F7", 5)]
    [InlineData("#31e1f7", 5)]
    [InlineData("#010203", 0)]
    [InlineData("not a colour", 0)]
    public void APersistedColourMapsBackOntoItsPaletteEntry_AndAnUnknownOneOntoTheDefault(
        string? colour, int expected)
    {
        Assert.Equal(expected, StudioPickerNotices.TintIndexOf(colour));
    }

    private sealed class Library : IDisposable
    {
        public Library() =>
            AssetsRoot = Path.Combine(Path.GetTempPath(), "ccp-picker-" + Guid.NewGuid().ToString("N"));

        public string AssetsRoot { get; }

        public string Folder => SpiralLibrary.Folder(AssetsRoot);

        public void Write(string name)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name), [0x00]);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(AssetsRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
