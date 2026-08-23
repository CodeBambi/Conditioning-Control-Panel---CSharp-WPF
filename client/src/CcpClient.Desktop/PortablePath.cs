namespace CcpClient.Desktop;

/// <summary>
/// Path handling that gives the SAME answer on Windows and on Linux.
///
/// <para>.NET's <see cref="Path"/> members are deliberately platform-specific: on Unix the
/// only directory separator is <c>'/'</c> and the only invalid filename characters are NUL
/// and <c>'/'</c>. That is correct for paths this process just built, and wrong for every
/// path that arrived from somewhere else — a session document authored on Windows, a preset
/// synced between machines, a name a page typed. On Linux
/// <c>Path.GetFileName(@"C:\spirals\classic.gif")</c> returns the WHOLE string, and
/// <c>Path.GetInvalidFileNameChars()</c> lets a backslash through into a filename. The first
/// defect printed a full absolute path into a refusal a user reads; the second wrote a file
/// whose name contains a separator.</para>
///
/// <para>Outcome parity, not mechanism: WPF ran on Windows only, so its answers were always
/// the Windows ones (<c>SessionFileService.cs:311-315</c> sanitises with the Windows invalid
/// set; <c>FlashService.cs:589-597</c> is the "name the file, never the path" display rule
/// the spiral/video refusals inherit). These helpers keep the WPF-observed answer on both
/// platforms instead of inheriting whatever the host OS happens to think.</para>
/// </summary>
public static class PortablePath
{
    /// <summary>Both separators, on every platform — the point of the type.</summary>
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// The Windows invalid-filename set, applied on every platform: NUL, control chars
    /// 1-31, and <c>" &lt; &gt; | : * ? \ /</c>. A superset of the Unix set, so a name
    /// sanitised here is legal on both — and, crucially, a name sanitised on Linux is the
    /// same name it would have been given on Windows, which is what makes a synced or
    /// copied data directory keep working.
    /// </summary>
    public static readonly char[] InvalidFileNameChars =
        [.. Enumerable.Range(0, 32).Select(i => (char)i), '"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    /// <summary>
    /// The last segment of <paramref name="path"/>, splitting on EITHER separator.
    /// Use this for anything a person reads: it is the "file name only, never the full
    /// path" display rule, and unlike <see cref="Path.GetFileName(string?)"/> it does not
    /// stop working when the path came from the other operating system.
    /// </summary>
    public static string FileName(string? path) =>
        path is null or "" ? string.Empty : path[(path.LastIndexOfAny(Separators) + 1)..];
}
