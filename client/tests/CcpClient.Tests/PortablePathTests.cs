using CcpClient.Desktop;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The two answers that must NOT change with the operating system underneath them.
///
/// <para>The first Linux run of this port found both of these as product defects, not as test
/// gaps. <c>Path.GetFileName(@"C:\spirals\classic.gif")</c> on Linux returns the whole string,
/// because a backslash is an ordinary filename character there — so the spiral panel printed a
/// full absolute path, complete with a drive letter for a drive the machine does not have, into
/// a refusal a person reads. <c>Path.GetInvalidFileNameChars()</c> on Linux is only NUL and
/// '/', so a drafted session named <c>a/b\c</c> was written to disk as <c>a_b\c</c> — a
/// separator standing inside a filename, and a different file from the one the same run makes
/// on Windows.</para>
///
/// <para>These facts pin <see cref="PortablePath"/>, which is where both answers now come from.
/// They pass on Windows before and after the fix, because Windows already treats both
/// separators as separators and already forbids the whole set — that is exactly the shape of
/// the class, and why it survived until a Linux machine ran the suite. On Linux they FAIL
/// against the framework calls they replaced. The Windows-side inversion is carried by
/// <see cref="PathPortabilityGuardTests"/>, which fails on the un-fixed source itself.</para>
/// </summary>
public class PortablePathTests
{
    [Fact]
    public void TheDisplayNameIsTheLastSegmentUnderEITHERSeparator_AndNeverCarriesADriveLetter()
    {
        // The measured Linux failure, in one line: this is the exact string the spiral panel had
        // in hand when it printed a C: drive at a machine that has no drives.
        Assert.Equal("classic.gif", PortablePath.FileName(@"C:\spirals\classic.gif"));
        Assert.Equal("classic.gif", PortablePath.FileName("/home/u/spirals/classic.gif"));

        // A path can be mixed — a Windows-authored preset joined against a Linux root gets both.
        // The LAST separator of either kind wins, whichever it is.
        Assert.Equal("c.gif", PortablePath.FileName(@"C:\a/b\c.gif"));
        Assert.Equal("c.gif", PortablePath.FileName(@"/mnt/a\b/c.gif"));

        // A bare name is already the display name; nothing is stripped from it.
        Assert.Equal("classic.gif", PortablePath.FileName("classic.gif"));

        // Degenerate inputs answer with the empty string rather than throwing: this runs inside
        // refusal text, and a refusal that throws while explaining itself is a crash the user
        // gets instead of the sentence they were owed.
        Assert.Equal(string.Empty, PortablePath.FileName(null));
        Assert.Equal(string.Empty, PortablePath.FileName(""));
        Assert.Equal(string.Empty, PortablePath.FileName(@"C:\spirals\"));

        // And the user-visible surfaces that had the defect now read the same on both platforms.
        // StudioPage's own doc rule is "the FILE NAME only, never the full path"
        // (WPF FlashService.cs:589-597 is where that rule comes from); on Linux it was printing
        // the whole path instead.
        var notice = StudioPage.DescribeSpiralLibrary(@"C:\Users\u\spirals\classic.gif", "/tmp/spirals");
        Assert.Equal("Drawing classic.gif.", notice);
        Assert.DoesNotContain("C:", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInvalidFilenameSetIsTheWindowsSetOnBOTHPlatforms()
    {
        var invalid = PortablePath.InvalidFileNameChars;

        // Both separators, always. This is the pair the framework set drops on Linux, and the
        // reason a drafted session came out named "a_b\c" there.
        Assert.Contains('\\', invalid);
        Assert.Contains('/', invalid);

        // The rest of the Windows-forbidden punctuation, which Unix filesystems happily accept
        // and which therefore silently survives into a filename on Linux.
        foreach (var c in new[] { '"', '<', '>', '|', ':', '*', '?' })
        {
            Assert.Contains(c, invalid);
        }

        // NUL and the C0 control range.
        Assert.Contains('\0', invalid);
        Assert.Contains((char)31, invalid);
        Assert.DoesNotContain((char)32, invalid);

        // A superset of whatever the host framework forbids: sanitising here can never produce a
        // name the running OS would reject.
        Assert.All(Path.GetInvalidFileNameChars(), c => Assert.Contains(c, invalid));

        // And the sink that had the defect gives the WPF answer (SessionFileService.cs:311-315)
        // on both platforms — every one of these characters is a separator or forbidden on
        // Windows, and all but '/' are legal in a Linux filename.
        Assert.Equal("a_b_c", IntakeDraftSink.SanitizeFileName(@"a/b\c"));
        Assert.Equal("a_b_c_d", IntakeDraftSink.SanitizeFileName("a:b*c?d"));
    }
}
