using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Where a flash goes and how big it is, as pure functions — WPF <c>CalculateGeometry</c>
/// (<c>Services/Flash/FlashService.cs:2290-2315</c>), <c>SpawnBounds</c> (<c>:2374-2383</c>) and
/// <c>IsOverlapping</c> (<c>:2526-2557</c>).
///
/// <para>Separated from the presenter for the same reason <see cref="FlashSchedule"/> is separated
/// from the effect: this is the whole behaviour-visible part of the placement, and a formula in a
/// pure function can be pinned at its boundaries without a window, a monitor or a compositor.</para>
/// </summary>
public static class FlashGeometry
{
    /// <summary>WPF's base box: 40 % of the monitor in each axis (<c>FlashService.cs:2292-2293</c>,
    /// its comment: "matching Python").</summary>
    public const double BaseFraction = 0.4;

    /// <summary>WPF's smallest flash in either axis (<c>:2300-2301</c>).</summary>
    public const int MinimumEdge = 50;

    /// <summary>WPF's <c>SpawnEdgePadding</c> (<c>:2320-2321</c>): "keep targets away from screen
    /// edges so they're fully visible and clickable".</summary>
    public const int EdgePadding = 50;

    /// <summary>WPF calls two rectangles overlapping when the shared area exceeds 30 % of the NEW
    /// image's area (<c>:2544-2547</c>) — the new one's, not the older one's.</summary>
    public const double OverlapFraction = 0.3;

    /// <summary>WPF re-picks up to ten times before accepting an overlap (<c>:1198-1207</c>).</summary>
    public const int OverlapAttempts = 10;

    /// <summary>
    /// The size a source image is drawn at. WPF's arithmetic in WPF's order: a 40 %-of-monitor
    /// box, the aspect-preserving ratio that fits the image inside it, the user's scale
    /// percentage, then a 50 px floor per axis applied AFTER the multiply
    /// (<c>FlashService.cs:2292-2301</c>).
    /// </summary>
    /// <param name="sourceWidth">Decoded pixel width.</param>
    /// <param name="sourceHeight">Decoded pixel height.</param>
    /// <param name="monitorWidth">Display width in physical pixels.</param>
    /// <param name="monitorHeight">Display height in physical pixels.</param>
    /// <param name="scalePercent">WPF's <c>ImageScale</c>, 50-250, where 100 is "normal"
    /// (<c>AppSettings.cs:838-850</c>).</param>
    public static (int Width, int Height) Size(
        int sourceWidth, int sourceHeight, int monitorWidth, int monitorHeight, int scalePercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(monitorWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(monitorHeight, 0);

        var scale = scalePercent / 100.0;
        var baseWidth = monitorWidth * BaseFraction;
        var baseHeight = monitorHeight * BaseFraction;
        var ratio = Math.Min(baseWidth / sourceWidth, baseHeight / sourceHeight) * scale;

        return (
            Math.Max(MinimumEdge, (int)(sourceWidth * ratio)),
            Math.Max(MinimumEdge, (int)(sourceHeight * ratio)));
    }

    /// <summary>
    /// The legal range for a top-left spawn point, monitor-local. Half-open on the max end,
    /// matching <see cref="Random.Next(int, int)"/> — WPF's <c>SpawnBounds</c> verbatim
    /// (<c>FlashService.cs:2374-2383</c>), including the <c>max(min + 1, …)</c> that keeps the
    /// range non-empty for an image wider than the padded area.
    /// </summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) SpawnBounds(
        int monitorWidth, int monitorHeight, int width, int height) =>
        (EdgePadding,
         EdgePadding,
         Math.Max(EdgePadding + 1, monitorWidth - width - EdgePadding),
         Math.Max(EdgePadding + 1, monitorHeight - height - EdgePadding));

    /// <summary>One uniform spawn point inside <paramref name="display"/>, in the display's own
    /// coordinate space (<c>FlashService.cs:2360</c>).</summary>
    public static OverlayBounds Spawn(OverlayBounds display, int width, int height, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var (minX, minY, maxX, maxY) = SpawnBounds(display.Width, display.Height, width, height);
        return new OverlayBounds(
            display.X + random.Next(minX, maxX),
            display.Y + random.Next(minY, maxY),
            width,
            height);
    }

    /// <summary>
    /// WPF's overlap test (<c>FlashService.cs:2539-2547</c>): the intersection's area against 30 %
    /// of the CANDIDATE's area. Reproduced with WPF's own <c>&gt;= 0</c> comparison on each axis,
    /// which is deliberately not a strict intersection test — two rectangles that merely touch
    /// contribute a zero-area overlap and are therefore not overlapping, so the difference never
    /// shows.
    /// </summary>
    public static bool Overlaps(OverlayBounds candidate, OverlayBounds existing)
    {
        var dx = Math.Min(candidate.X + candidate.Width, existing.X + existing.Width)
            - Math.Max(candidate.X, existing.X);
        var dy = Math.Min(candidate.Y + candidate.Height, existing.Y + existing.Height)
            - Math.Max(candidate.Y, existing.Y);

        if (dx < 0 || dy < 0)
        {
            return false;
        }

        return (long)dx * dy > candidate.Width * (long)candidate.Height * OverlapFraction;
    }

    /// <summary>
    /// A placement that tries not to sit on top of what is already up: up to
    /// <see cref="OverlapAttempts"/> fresh picks, then whatever the last pick was — WPF's loop and
    /// WPF's give-up (<c>FlashService.cs:1198-1207</c>). Giving up rather than never spawning is
    /// deliberate upstream, and it is kept: a flash that refused to appear because the screen was
    /// busy would be a worse outcome than one that overlaps.
    /// </summary>
    public static OverlayBounds Place(
        OverlayBounds display, int width, int height, IReadOnlyList<OverlayBounds> occupied, Random random)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        ArgumentNullException.ThrowIfNull(random);

        var candidate = Spawn(display, width, height, random);
        for (var attempt = 0; attempt < OverlapAttempts; attempt++)
        {
            var clash = false;
            for (var i = 0; i < occupied.Count; i++)
            {
                if (Overlaps(candidate, occupied[i]))
                {
                    clash = true;
                    break;
                }
            }

            if (!clash)
            {
                return candidate;
            }

            candidate = Spawn(display, width, height, random);
        }

        return candidate;
    }
}
