using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Glyph;

/// <summary>
/// The honest refusal: a glyph surface for a platform this build has no per-pixel-alpha mechanism
/// for.
///
/// <para><b>Every member refuses, including the ones a caller might think are harmless.</b>
/// <see cref="Withdraw"/> and <see cref="MoveTo"/> refuse as loudly as <see cref="Present"/> does,
/// and <see cref="IsPresenting"/> is always false — a partial refusal is a path a caller can mistake
/// for a surface on screen, which is the first attempt's whole failure mode in miniature
/// (<c>CCP.Avalonia/Platform/AvaloniaOverlaySurface.cs:31-35</c>, a click-through implementation
/// whose method body is a comment).</para>
/// </summary>
/// <param name="code">The reason code every operation carries.</param>
/// <param name="detail">What is absent, what the route would be, and the manual gate.</param>
public sealed class UnsupportedGlyphSurface(string code, string detail) : IGlyphSurface
{
    private readonly CapabilityReason _reason = new(
        code ?? throw new ArgumentNullException(nameof(code)),
        detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <inheritdoc/>
    public bool IsPresenting => false;

    /// <inheritdoc/>
    public CapabilityState Present(GlyphSurfaceRequest request, GlyphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(frame);
        return new CapabilityState.Unavailable(_reason);
    }

    /// <inheritdoc/>
    public CapabilityState Paint(GlyphFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new CapabilityState.Unavailable(_reason);
    }

    /// <inheritdoc/>
    public CapabilityState MoveTo(GlyphBounds bounds) => new CapabilityState.Unavailable(_reason);

    /// <inheritdoc/>
    public void Reassert()
    {
        // Nothing exists to raise. The overlay's Reassert returns void for the same reason: it
        // confirms nothing even where it works, so there is no claim here to get wrong.
    }

    /// <inheritdoc/>
    public CapabilityState Withdraw() => new CapabilityState.Unavailable(_reason);

    /// <inheritdoc/>
    public void Dispose()
    {
        // No window, no GDI object, no thread affinity. Declared so a caller's using-block is the
        // same shape on every platform.
    }
}
