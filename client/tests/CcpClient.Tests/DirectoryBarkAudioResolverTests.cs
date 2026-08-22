using CcpClient.Desktop.Companion;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The ONLY production <see cref="IBarkAudioResolver"/>, driven for the first time.
///
/// <para><b>Why this file exists.</b> The zero-execution census reported
/// <see cref="DirectoryBarkAudioResolver"/> as a shipped type with zero executed lines. It has
/// exactly two references tree-wide — its own declaration and one wiring in the DTRH host — and
/// every bark fact in the suite substitutes a recording fake (<c>BarkPipelineTests.cs:593</c>), so
/// the implementation that ships had never run. Its summary promised <i>missing file → null, never
/// throws</i> over a <see cref="Path.Combine(string, string)"/> that throws on a null segment.</para>
///
/// <para><b>The ruling these facts encode: the CONTRACT was true and the CODE was wrong.</b> WPF's
/// resolver opens with <c>if (string.IsNullOrWhiteSpace(file)) return null;</c>
/// (<c>ConditioningControlPanel/Services/Companion/BarkService.cs:1411-1413</c>) and every later
/// step is <c>File.Exists(p) ? p : null</c> (<c>:1420</c>, <c>:1428</c>, <c>:1432</c>) — so in the
/// product being ported, a null, blank or missing voiceline is silently no-audio and never an
/// exception. The greenfield promise stated that outcome correctly; only the greenfield code
/// diverged. The port's own consumer agrees from the inside: <c>BarkPipeline.SafeResolve</c>
/// (<c>BarkPipeline.cs:540-551</c>) catches a resolver throw and logs "audio resolver faulted",
/// i.e. it already treats a throw here as a defect — and reports it in the vocabulary of bad
/// CONTENT, which is why the defect had to be pinned on the resolver directly and could never have
/// been seen through the pipeline.</para>
/// </summary>
public sealed class DirectoryBarkAudioResolverTests
{
    [Fact]
    public void AnExistingFileUnderTheRoot_ResolvesToItsFullPath()
    {
        using var dir = new TempSoundsRoot();
        var expected = dir.WriteFile("attention_check_fail_1.mp3");

        var resolved = new DirectoryBarkAudioResolver(dir.Root).Resolve("attention_check_fail_1.mp3");

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void AFileThatIsNotThere_IsANullMiss()
    {
        using var dir = new TempSoundsRoot();

        var resolved = new DirectoryBarkAudioResolver(dir.Root).Resolve("never_recorded.mp3");

        // Null is the ONLY miss channel — BarkPipeline turns it into the typed
        // BarkSuppression.AudioResolveFailed and the bark still surfaces as text.
        Assert.Null(resolved);
    }

    [Fact]
    public void ANullFileName_IsAMiss_NotAnArgumentNullException()
    {
        // THE bite. Before the fix this reached Path.Combine(root, null) and threw
        // ArgumentNullException out of a method documented "never throws". WPF answers null:
        // BarkService.cs:1412-1413 takes `string? file` and guards IsNullOrWhiteSpace first.
        using var dir = new TempSoundsRoot();

        var resolved = new DirectoryBarkAudioResolver(dir.Root).Resolve(null!);

        Assert.Null(resolved);
    }

    [Fact]
    public void BlankAndWhitespaceNames_AreMisses_RatherThanALookupOfTheRootItself()
    {
        // Contract statements, NOT the bite, and the distinction matters: Path.Combine(root, "")
        // returns root and File.Exists on a directory is false, so both of these answered null even
        // before the guard existed. They are here because WPF's IsNullOrWhiteSpace guard covers
        // them explicitly and a future rewrite of Resolve must keep the outcome, not because
        // removing the guard would turn them red.
        using var dir = new TempSoundsRoot();
        var resolver = new DirectoryBarkAudioResolver(dir.Root);

        Assert.Null(resolver.Resolve(string.Empty));
        Assert.Null(resolver.Resolve("   "));
        Assert.Null(resolver.Resolve("\t"));
    }

    [Fact]
    public void ADirectoryWithTheRequestedName_IsAMiss_BecauseADirectoryIsNotPlayable()
    {
        using var dir = new TempSoundsRoot();
        Directory.CreateDirectory(Path.Combine(dir.Root, "looks_like_a_file.mp3"));

        var resolved = new DirectoryBarkAudioResolver(dir.Root).Resolve("looks_like_a_file.mp3");

        Assert.Null(resolved);
    }

    [Fact]
    public void ARootDirectoryThatDoesNotExist_IsAMiss_NotAnIOException()
    {
        // The shipped wiring points at <DtrhDataDirectory>/companion_audio, which does not exist on
        // a fresh install because the port ships no voice assets yet (named limit). Every bark
        // resolution on a first run therefore takes exactly this path.
        using var dir = new TempSoundsRoot();
        var absent = Path.Combine(dir.Root, "companion_audio");

        var resolved = new DirectoryBarkAudioResolver(absent).Resolve("attention_check_fail_1.mp3");

        Assert.Null(resolved);
    }

    [Fact]
    public void ANullRoot_IsRefusedAtCONSTRUCTION_SoResolveIsUnconditionallySafe()
    {
        // The fail-fast/contain split, the same one the clock seams use: a wiring bug fails on the
        // thread that CAUSED it, so "Resolve never throws" needs no caveat about how it was built.
        var refused = Assert.Throws<ArgumentNullException>(() => new DirectoryBarkAudioResolver(null!));

        Assert.Equal("root", refused.ParamName);
    }

    [Fact]
    public void AnABSOLUTEFileName_DISCARDSTheRoot_WhichIsTheWPFPropertyAndIsDELIBERATE()
    {
        // A NAMED divergence, pinned so that changing it later is a decision rather than an
        // accident. Path.Combine(root, absolute) returns the absolute path, so a manifest naming an
        // absolute file escapes the sounds root entirely. WPF has the identical property at
        // BarkService.cs:1419 (same Path.Combine, same manifest-supplied filename), so adding
        // containment here would invent behaviour the ported product does not have. END CONDITION:
        // the first user- or mod-supplied bark manifest makes this a live decision again.
        //
        // Stated OS-NEUTRALLY on purpose. A Windows literal like "C:\\x.mp3" is not rooted on Unix
        // and would pass here while failing on the Linux leg, and floor.json's allowedSkips is not
        // a rescue for that. So the escaping name is a REAL absolute path produced by the platform
        // itself, in the shape AiCommandEnvelope.cs:485 already uses in product code.
        //
        // And it is an ABSOLUTE path, not merely a Path.IsPathRooted one: CompositionRoot.cs:189
        // already records that IsPathRooted "wrongly accepts" drive-relative values like "C:foo",
        // which are rooted by that predicate yet do NOT discard the root the way "C:\foo" does.
        // The property pinned here is the absolute one.
        using var sounds = new TempSoundsRoot();
        using var elsewhere = new TempSoundsRoot();
        var escaping = Path.GetFullPath(elsewhere.WriteFile("outside_the_root.mp3"));
        Assert.True(Path.IsPathFullyQualified(escaping), $"the escaping name must be absolute on this OS: {escaping}");

        var resolved = new DirectoryBarkAudioResolver(sounds.Root).Resolve(escaping);

        Assert.Equal(escaping, resolved);
        Assert.NotEqual(sounds.Root, Path.GetDirectoryName(resolved));
    }

    // ---------- harness ----------

    private sealed class TempSoundsRoot : IDisposable
    {
        public TempSoundsRoot() => Directory.CreateDirectory(Root);

        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "ccp-bark-audio-" + Guid.NewGuid().ToString("N"));

        /// <summary>Writes a zero-length file (the resolver asks File.Exists, never for content).</summary>
        public string WriteFile(string fileName)
        {
            var path = Path.Combine(Root, fileName);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }
}
