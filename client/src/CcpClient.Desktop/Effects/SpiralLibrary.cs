namespace CcpClient.Desktop.Effects;

/// <summary>
/// Which spiral this module draws — the port's <c>GetSpiralPath()</c>
/// (<c>Services/Notifications/OverlayService.cs:299-319</c>).
///
/// <para><b>WPF's chain is three links and the port ports two of them.</b> Upstream: the configured
/// <c>SpiralPath</c> if that file still exists; else the randomiser's pick from the library folder;
/// else <c>ModResourceResolver.ResolveSpiralUri()</c>, a spiral compiled into the application.
/// Here: the configured path if it still exists, else the FIRST supported file in the library
/// folder, in ordinal order.</para>
///
/// <para><b>The randomiser is not ported (D87)</b> — it is a dial the port's panel has no control
/// for, and a hidden random choice a user cannot see or turn off is worse than a deterministic one.
/// <b>The built-in spiral is not ported (D86)</b> — the port bundles no art at all, and the legacy
/// tree's bytes are owned by the legacy tree (<c>client/CLAUDE.md</c>: payloads are LINKED, never
/// forked). A library with nothing in it is therefore an ordinary first-run state, exactly as an
/// empty images folder is for Flash Images, and the module says so in type rather than drawing
/// something nobody chose.</para>
///
/// <para><b>Re-resolved on every engage, never cached</b>, because WPF re-resolves on every
/// reconcile too (<c>OverlayService.cs:437</c> calls <c>GetSpiralPath()</c> inside
/// <c>RefreshOverlays</c>). A user who drops a spiral into the folder mid-session gets it at the
/// next gesture instead of at the next launch.</para>
/// </summary>
public static class SpiralLibrary
{
    /// <summary>
    /// WPF's own extension list for the spiral pool (<c>OverlayService.cs:205</c>), minus the two
    /// this port cannot decode. Upstream's is
    /// <c>{ ".gif", ".png", ".jpg", ".jpeg", ".webp" }</c>; <c>.webp</c> is dropped because GDI+ has
    /// no WebP codec — the same hole <see cref="GdiPlusFlashFrameSource"/> already records (D58) —
    /// so listing it would offer a file that silently draws nothing.
    /// </summary>
    public static readonly string[] Extensions = [".gif", ".png", ".jpg", ".jpeg"];

    /// <summary>The library folder: <c>&lt;assetsRoot&gt;/spirals</c>. WPF's is
    /// <c>%LOCALAPPDATA%/ConditioningControlPanel/Spirals</c> (<c>OverlayService.cs:331</c>), which
    /// is the same place relative to the app's own data root; the port keeps its user media under
    /// one <c>assets/</c> root (<c>SessionParticipant.AssetsRootFor</c>) and this is a folder in
    /// it.</summary>
    public static string Folder(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);
        return Path.Combine(assetsRoot, "spirals");
    }

    /// <summary>
    /// The spiral to draw, or null when there is none. Never throws: a folder that cannot be read
    /// is the same outcome as a folder with nothing in it, which is what WPF's own
    /// <c>try { … } catch { return null; }</c> around the pool build says (<c>:326-346</c>).
    /// </summary>
    /// <param name="assetsRoot">The user-media root.</param>
    /// <param name="configuredPath">The persisted <c>SpiralPath</c>; blank means "use the library".</param>
    public static string? Resolve(string assetsRoot, string? configuredPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);

        // WPF re-tests File.Exists rather than trusting the setting (OverlayService.cs:302-304):
        // the file the user picked last month may be gone, and a missing path must fall through to
        // the library instead of showing nothing.
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var candidates = ListFolder(Folder(assetsRoot));
        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Everything the library holds, in the order the picker offers it and the order
    /// <see cref="Resolve"/> falls back through — upstream's own <c>RefreshLibrary</c> enumeration
    /// (<c>Features/SpiralFeatureControl.xaml.cs:253-278</c>), which is where its SPIRAL LIBRARY
    /// card's cards come from.
    ///
    /// <para><b>Ordinal, so the choice is the same on every machine and in every run.</b> WPF's
    /// enumeration order feeds a randomiser and therefore does not matter upstream; here it is both
    /// the picker's order AND, for a user who has picked nothing, the choice itself — so it is
    /// pinned rather than left to the file system. (Upstream sorts by file name
    /// case-insensitively at <c>:262</c>; ordinal on the full path is this port's existing rule and
    /// is kept, because changing it would change which spiral an unconfigured install draws.)</para>
    ///
    /// <para><b>Never throws</b>, for the same reason <see cref="Resolve"/> does not: a folder that
    /// cannot be read is the same outcome as a folder with nothing in it, which is what WPF's own
    /// <c>try { … } catch { }</c> around its enumeration says (<c>:280-283</c>). An empty list is an
    /// ordinary first-run state, not an error.</para>
    /// </summary>
    /// <param name="spiralsFolder">The library folder — <see cref="Folder"/>'s answer, which is
    /// also what <c>SessionParticipant.SpiralsFolder</c> already hands the panel, so the picker and
    /// the panel's own library line cannot end up looking at two different places.</param>
    public static IReadOnlyList<string> ListFolder(string spiralsFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(spiralsFolder);

        try
        {
            if (!Directory.Exists(spiralsFolder))
            {
                return [];
            }

            return
            [
                .. Directory.GetFiles(spiralsFolder)
                    .Where(static file => Extensions.Contains(
                        Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
