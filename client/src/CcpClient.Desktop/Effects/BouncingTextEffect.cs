using System.Globalization;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Bouncing Text</b> — <b>the module this port refused to fake for nine waves</b>.
///
/// <para>SP-106 read its window and said no with evidence rather than an excuse (D83/D84): its
/// content is transparency-backed glyphs
/// (<c>Services/Subliminal/BouncingTextService.cs:827-828</c>, <c>AllowsTransparency = true</c> and
/// <c>Background = Brushes.Transparent</c>), and this port's overlay composites one uniform
/// <c>LWA_ALPHA</c> over an opaque frame by design. At the shipped default opacity of 100
/// (<c>CCP.Core/Models/AppSettings.cs:3619</c>) porting it on that surface would have put a BLACK
/// SCREEN with a word on it in front of the user — worse than the absent module. It also MOVES
/// every frame, and moving an overlay means re-<c>Present</c>ing it, which momentarily clears
/// click-through and walks the OS's whole top-level z-order.</para>
///
/// <para><b>Both blockers were capability-shaped and both are now closed by one capability.</b>
/// <c>Glyph/**</c> composites per-pixel alpha whose transparent pixels are measured — on the
/// composited desktop, over a known background — to show what is behind them, and its
/// <see cref="Glyph.IGlyphSurface.MoveTo"/> is one call with no style write, no z-order walk and no
/// hit test. D83 and D84 close together because they were the same missing thing seen twice.</para>
///
/// <para><b>The module takes no clock and owns no timer</b>, exactly as
/// <see cref="SpiralOverlayEffect"/> does and for SP-106's reason: an interval that decides when a
/// MODULE is due belongs to <see cref="PacedSessionEffect{TFiring}"/>, and a cadence that keeps a
/// SURFACE correct belongs to the surface. A bouncing logo is never "due"; it is on, and what is on
/// screen moves.</para>
///
/// <para><b>What is NOT ported</b>, and is recorded rather than stubbed: the six per-frame transform
/// effects and their corner burst (<c>:558-600</c>, two of which ship ON), the XP award with its
/// two-second cooldown and 150-per-minute ceiling (<c>:499-513</c>), the achievement and haptic
/// hooks, the second independent logo (<c>:143</c>), the dual-monitor spread (<c>:332-336</c>), the
/// pause-during-video behaviour and its "show over videos" dial (<c>:373-383</c>), and the OCR
/// self-exclusion rectangles the avatar's awareness reads (<c>:88-119</c>). Every one of them is a
/// subsystem this port does not have or a cost class this surface is not built for; a silent no-op
/// would make the module look complete.</para>
/// </summary>
public sealed class BouncingTextEffect : OwnedSessionEffect
{
    /// <summary>WPF's rack key for this module.</summary>
    public const string EffectId = "bouncing-text";

    /// <summary>
    /// The row's label.
    ///
    /// <para><b>It names the ported half</b>, on the precedent SP-109 set and SP-111 and SP-113
    /// followed: the user reads the scope of the row before enabling anything, and the same string
    /// is pinned beside the dot it justifies.</para>
    /// </summary>
    public const string DisplayTitle = "Bouncing Text (motion half)";

    /// <summary>What the panel leads with, verbatim, and what the arm result's Degraded reason
    /// carries — one string so the two cannot drift into two accounts of one absence.</summary>
    public const string TransformsAbsentNotice =
        "The six per-frame transform effects are not ported: breathing, wobble, spin, velocity tilt, "
        + "squash-and-stretch and the corner burst. Two of them ship ON upstream. In this port a surface's "
        + "content is a rasterised bitmap, so a rotation or a scale means re-rastering and re-compositing every "
        + "frame - a different cost class from the move this module is built on. The XP award, the achievement "
        + "and haptic hooks, the second logo and the dual-monitor spread are not ported either.";

    private readonly PersistenceStore<BouncingTextPresetDocument> _preset;
    private readonly IBouncingTextSurface? _surface;

    /// <param name="owner">This module's operation owner: one generation per armed module.</param>
    /// <param name="signal">The one boundary a state change or a draw is allowed to arrive on.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="surface">Where the logo goes. Null composes the module with nowhere to draw,
    /// which is what a test that wants no window does.</param>
    public BouncingTextEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        PersistenceStore<BouncingTextPresetDocument> preset,
        IBouncingTextSurface? surface = null)
        : base(owner, signal, "bouncing-text-layer")
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
        _surface = surface;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>The presentation this module's dials currently describe, so the panel shows the user
    /// the values that would really be used without starting a session.</summary>
    public BouncingTextPresentation Presentation => new(
        _preset.Current.Enabled,
        _preset.Current.Speed,
        _preset.Current.SizePercent,
        _preset.Current.OpacityPercent,
        (BouncingTextColourMode)_preset.Current.ColourMode,
        ParseColour(_preset.Current.FixedColour),
        _preset.Current.Outline,
        _preset.Current.Phrases);

    /// <summary>What the OS last said about this module's surface, verbatim, or null when nothing
    /// has been attempted. The module panel reports it word for word.</summary>
    public CapabilityState? LastPlacement => _surface?.LastPlacement;

    /// <summary>True while the surface is up at all, whether or not it is still holding its
    /// composite. The panel uses this to tell the two states apart; the DOT does not, on purpose.</summary>
    public bool Showing => _surface is { Showing: true };

    /// <summary>How many wall bounces this run has taken. Upstream's own event with the XP
    /// removed.</summary>
    public int Bounces => _surface?.Bounces ?? 0;

    /// <summary>
    /// <b>The eighth meaning of the dot: COMPOSITE.</b> The six before it were the clock (paced),
    /// the screen (continuous), change (moving), custody (non-drawing), reach (audio), demand
    /// (input) and motion (video read-back).
    ///
    /// <para>It is not MOTION (SP-111's seventh): a bouncing logo that stopped moving is still a
    /// picture on the screen, whereas a video that stopped advancing is a dead one. It is not the
    /// SCREEN (SP-105's second): a layered window can be present, visible, on top and composite
    /// NOTHING, and this is the first module in the port whose surface can be in that state — it is
    /// the state the whole capability was built to make impossible to claim. So <c>Live</c> means
    /// the operating system's own copy of the surface still carries the frame's opaque ink.</para>
    /// </summary>
    protected override bool WorkIsRunning => _surface is { Running: true };

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.Enabled = enabled);
        RaiseChanged();
    }

    /// <summary>Move the speed dial and re-apply it. WPF's slider writes the setting and the next
    /// start reads it; here a live run picks it up through <see cref="Refresh"/>.</summary>
    public void SetSpeed(int speed)
    {
        if (_preset.Current.Speed == Math.Clamp(
                speed, BouncingTextPresetDocument.MinSpeed, BouncingTextPresetDocument.MaxSpeed))
        {
            return;
        }

        _preset.Mutate(p => p.Speed = speed);
        Refresh();
    }

    /// <summary>Move the size dial and re-apply it.</summary>
    public void SetSizePercent(int percent)
    {
        if (_preset.Current.SizePercent == Math.Clamp(
                percent, BouncingTextPresetDocument.MinSizePercent, BouncingTextPresetDocument.MaxSizePercent))
        {
            return;
        }

        _preset.Mutate(p => p.SizePercent = percent);
        Refresh();
    }

    /// <summary>Move the opacity dial and re-apply it to whatever is already on screen.</summary>
    public void SetOpacityPercent(int percent)
    {
        if (_preset.Current.OpacityPercent == Math.Clamp(
                percent, BouncingTextPresetDocument.MinOpacityPercent,
                BouncingTextPresetDocument.MaxOpacityPercent))
        {
            return;
        }

        _preset.Mutate(p => p.OpacityPercent = percent);
        Refresh();
    }

    /// <summary>
    /// Put the logo up, or re-apply the dials to the one already up.
    ///
    /// <para>The dial and the generation have been checked by <see cref="OwnedSessionEffect"/>;
    /// what is left is this module's own answers, in the order that keeps each honest.</para>
    /// </summary>
    protected override CapabilityState Engage(int generation)
    {
        var presentation = Presentation;

        // WPF's clamp lets the opacity reach ZERO and WPF at zero still creates an always-on-top
        // window holding fully transparent text - the ghost this port refuses to construct. Nothing
        // is placed and the module says so in type: it really did take the session (Degraded) and
        // really will show nothing (not Available).
        if (presentation.IsInvisible)
        {
            _surface?.Withdraw();
            return new CapabilityState.Degraded(
                "the module is engaged and its logo is fully transparent, so nothing is drawn",
                new CapabilityReason(
                    EffectReasonCodes.BouncingTextTransparent,
                    "the bouncing text's opacity dial is at 0 %, so there is nothing to put on screen"));
        }

        if (_surface is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoSurface,
                $"the '{Id}' module has no surface in this composition, so nothing was placed on screen"));
        }

        // A continuous module's work IS a native window, and a native window belongs to a UI thread.
        // SP-105's finding, unchanged for a sixth module: it is a property of DRAWING at arm time.
        if (!Signal.IsBound)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoUiThread,
                $"no UI thread is bound yet, so the '{Id}' module's surface could not be placed and nothing "
                + "was drawn"));
        }

        return _surface.Engage(presentation);
    }

    /// <summary>
    /// <b>Ready narrows every healthy run</b>, and that is the condition on shipping this row at
    /// all.
    ///
    /// <para>Two causes can be true at once and BOTH travel — SP-111's rule, after SP-109 shipped
    /// the defect once. The run-level cause goes first because it is the one the user can act on;
    /// the build-level one is always present, on healthy runs as well as broken ones, because the
    /// missing transforms are a property of the BUILD rather than of the run.</para>
    /// </summary>
    protected override CapabilityState Ready(CapabilityState engaged)
    {
        if (engaged is CapabilityState.Available available && _surface is { Running: false })
        {
            return new CapabilityState.Degraded(
                available.Detail,
                new CapabilityReason(
                    EffectReasonCodes.BouncingTextNoRaster,
                    "the logo's surface is on screen and the operating system no longer returns its content, so "
                    + "the composite is gone. " + TransformsAbsentNotice));
        }

        if (engaged is CapabilityState.Degraded degraded)
        {
            // The run-level cause FIRST, then the build-level one in the same detail.
            return new CapabilityState.Degraded(
                degraded.SurvivingSemantics,
                new CapabilityReason(
                    degraded.Reason.Code,
                    degraded.Reason.Detail + " " + TransformsAbsentNotice));
        }

        if (engaged is CapabilityState.Available healthy)
        {
            return new CapabilityState.Degraded(
                healthy.Detail,
                new CapabilityReason(EffectReasonCodes.BouncingTextTransformsAbsent, TransformsAbsentNotice));
        }

        return engaged;
    }

    /// <summary>
    /// Take the logo off the screen and stop the cadence.
    ///
    /// <para>For a MOVING module releasing the work is two things at once, and the timer must die
    /// with the surface or a stopped session would keep re-compositing a window nobody can see. The
    /// presenter does both inside <see cref="IBouncingTextSurface.Withdraw"/> so the pair cannot be
    /// separated by a caller. It tests <c>Engaged</c> rather than <c>Showing</c> because a surface
    /// whose composite failed is no longer showing and still owns a window and a timer.</para>
    /// </summary>
    protected override void ReleaseWork()
    {
        if (_surface is not { Engaged: true })
        {
            return;
        }

        Signal.Post(_surface.Withdraw);
    }

    /// <summary>
    /// WPF's <c>GetFixedColor</c> (<c>BouncingTextService.cs:649-666</c>): <c>#RRGGBB</c> or
    /// <c>RRGGBB</c>, anything else falling back to hot pink — which is why this returns null rather
    /// than a colour, so the fallback lives in one place.
    /// </summary>
    public static uint? ParseColour(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length != 6
            || !byte.TryParse(trimmed.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(trimmed.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(trimmed.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return null;
        }

        return (uint)((b << 16) | (g << 8) | r);
    }
}
