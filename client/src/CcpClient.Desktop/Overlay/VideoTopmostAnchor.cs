namespace CcpClient.Desktop.Overlay;

/// <summary>
/// The window a self-raising surface must stay BELOW while a mandatory video is on screen — and
/// only the modules upstream scopes that rule to.
///
/// <para><b>The failure.</b> Every native surface this client owns pins itself with
/// <c>SetWindowPos(HWND_TOPMOST)</c>, which means "the top of the topmost band", and each does it
/// on its own cadence: the tint and the spiral every 5 s
/// (<c>Effects/PinkFilterSurfacePresenter.cs:80</c>, <c>Effects/SpiralSurfacePresenter.cs:89</c>),
/// flash every 1 s (<c>Effects/FlashSurfacePresenter.cs:109</c>), bouncing text every 500 ms
/// (<c>Effects/BouncingTextSurfacePresenter.cs:78</c>). The video window is placed topmost ONCE
/// (<c>Video/Win32VideoPresence.cs:138</c>) and re-raises only inside its own bounded placement
/// read-back (<c>Video/Win32VideoPresence.cs:559</c>), so the last module to tick wins the top of
/// the band and the video stays under it. The video surface is OPAQUE
/// (<c>Video/Win32VideoPresence.cs:130</c>, <c>LWA_ALPHA 0xFF</c>) and the tint covers the whole
/// display, so the user watches a mandatory clip through a full-screen tint.</para>
///
/// <para><b>Upstream's rule, and upstream's SCOPE.</b> WPF resolves an action before every pin
/// instead of always pinning to the top: <c>Services/Notifications/OverlayService.cs:2851-2860</c>,
/// <c>ResolveZOrderAction</c> — <c>if (hasVideo &amp;&amp; !isVideoWindow &amp;&amp; !aboveVideo)
/// return ZOrderAction.PinBelowVideo;</c> — and <c>ReassertOne</c> (<c>:2870-2874</c>) issues
/// <c>SetWindowPos(hwnd, videoHwnd, …)</c>, which keeps <c>WS_EX_TOPMOST</c> and the whole topmost
/// band while parking the window under the clip. It reaches exactly three window lists —
/// pink filter, spiral, brain-drain blur (<c>:2793-2801</c>) — plus the compositor hosts that carry
/// the same fullscreen effects (<c>:2807-2818</c>). <b>Flash and bouncing text are deliberately
/// outside it</b>: <c>Services/Flash/FlashService.cs:203-224</c> calls flashes "the top attention
/// layer by design" and force-raises every one of them with no video test at all, skipping only the
/// compositor host and saying why at <c>:230-235</c>; and
/// <c>Services/Subliminal/BouncingTextService.cs:390-398</c> re-asserts every ~500 ms precisely
/// because bouncing text loses topmost "when competing with flash/video/overlay windows", through a
/// bare <c>SetWindowPos(HWND_TOPMOST)</c> at <c>:1048-1052</c>. Nothing in
/// <c>Session/SessionParticipant.cs</c> suppresses flash or subliminals during a video, so pinning
/// them below an opaque full-display clip would make them invisible for its whole length.</para>
///
/// <para><b>Why a published handle and not a service lookup.</b> Upstream asks one overlay service
/// that owns every window (<c>App.Video?.IsPlaying</c>, <c>OverlayService.cs:2783-2786</c>). This
/// port has no such owner: it has one <see cref="Effects.OverlaySurfaceSet"/> per module, each on
/// its own clock. So the video publishes its handle here and the yielding modules read it. One
/// <c>nint</c>, written by the surface that owns it and read on every re-assertion.</para>
///
/// <para><b>Why a stale handle cannot strand anything.</b> <see cref="InsertAfter"/> asks the OS
/// whether the anchor is still a visible window. A presence destroyed without releasing — a crash
/// path, a torn-down thread — reads back as not-a-window and every caller falls through to
/// <see cref="TopOfBand"/>. Nothing here outlives the process and no missed release can leave a
/// surface permanently demoted.</para>
///
/// <para><b>What this does NOT do.</b> It does not order the modules against EACH OTHER. Tint,
/// spiral, flash and bouncing text still take the top of the band from one another on their own
/// cadences, which is upstream's behaviour rather than a gap — WPF gives that pair no rule either
/// (<c>BouncingTextService.cs:1048-1052</c> against <c>OverlayService.cs:711-717</c>).</para>
///
/// <para><b>Deliberately process-global.</b> There is one desktop, one video surface at a time, and
/// the surfaces that must yield are constructed independently of the one they yield to; a plumbed
/// instance would have to thread through five module compositions to say the same thing. The
/// DECISION is pure (<see cref="Resolve"/>) so it is drivable without touching this state at all.
/// </para>
/// </summary>
public static class VideoTopmostAnchor
{
    private static nint _anchor;

    /// <summary><c>HWND_TOPMOST</c> — the top of the topmost band, and what every self-raiser
    /// passes when no video is up. Half of what <see cref="Resolve"/> returns.</summary>
    public static nint TopOfBand => Win32OverlayInterop.HwndTopmost;

    /// <summary>The window currently anchored, or 0. Diagnostics and facts; never a claim.</summary>
    public static nint Current => Volatile.Read(ref _anchor);

    /// <summary>Whether a video surface is on screen. The cheap half of the question — no OS call,
    /// so the reconcile loop can ask it every tick on any platform.</summary>
    public static bool IsClaimed => Volatile.Read(ref _anchor) != 0;

    /// <summary>Publish the window the yielding modules must stay below. The last claim wins: there
    /// is one video surface in this process and both video modules share it
    /// (<c>Session/SessionParticipant.cs:474-492</c>).</summary>
    public static void Claim(nint window) => Volatile.Write(ref _anchor, window);

    /// <summary>Withdraw a claim. Only the window holding it can clear it, so a late release from a
    /// presence that was already replaced cannot unpin the live one.</summary>
    public static void Release(nint window)
    {
        if (window != 0)
        {
            Interlocked.CompareExchange(ref _anchor, 0, window);
        }
    }

    /// <summary>
    /// The pure decision: what <paramref name="self"/> should pass as <c>hWndInsertAfter</c>.
    /// The anchor when one is claimed, is still on screen, and is not the caller itself;
    /// <see cref="TopOfBand"/> otherwise.
    ///
    /// <para>Separated from <see cref="InsertAfter"/> so the rule is drivable without a window, a
    /// desktop or the global claim — and so "no video is playing" is a case with an ANSWER rather
    /// than a branch nothing reaches.</para>
    /// </summary>
    public static nint Resolve(nint anchor, nint self, bool anchorIsOnScreen) =>
        anchor != 0 && anchor != self && anchorIsOnScreen ? anchor : TopOfBand;

    /// <summary><see cref="Resolve"/> over the live claim, with the OS asked whether the anchor is
    /// still a visible window.</summary>
    public static nint InsertAfter(nint self)
    {
        var anchor = Volatile.Read(ref _anchor);
        return Resolve(
            anchor,
            self,
            anchor != 0
                && OperatingSystem.IsWindows()
                && Win32OverlayInterop.IsWindow(anchor)
                && Win32OverlayInterop.IsWindowVisible(anchor));
    }
}
