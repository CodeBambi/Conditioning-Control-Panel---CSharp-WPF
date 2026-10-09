using System;
using System.Collections.Generic;
using System.Windows.Media;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// Art lookup for the Training Programs run view - the day mood plate and the program sigil.
///
/// Modelled on <c>Services/Quiz/IntakeNiche.PassCardImage()</c> and carrying the same null
/// contract: every method returns null when nothing resolves, and callers MUST collapse the host
/// element rather than render an empty box. The run view is authored to read correctly with ZERO
/// images present - the plate column shrinks to nothing and the sigil falls back to the 4px accent
/// bar - so a missing PNG is a plainer screen, never a broken one.
///
/// Everything goes through <see cref="ModResourceResolver.ResolveImage"/>, so a mod can drop the
/// same relative paths under its own <c>resources/programs/</c> and re-skin the whole run view.
/// That resolver caches on <c>{ActiveModId}:{path}</c> and returns frozen images, so calling this
/// on every refresh is cheap and thread-safe.
///
/// The art contract is white RGB with the luminance in the ALPHA channel. The view renders it as a
/// Rectangle filled with the program accent and masked by the image, so bright source becomes full
/// accent and dark source becomes fully transparent. Do NOT ship opaque greyscale here: an opaque
/// plate under a translucent accent lifts the blacks to half-accent and washes out.
/// </summary>
internal static class ProgramArt
{
    /// <summary>
    /// Mood plate for a day, keyed on that day's SESSION TEMPLATE - not on the day number. Four
    /// authored archetypes therefore cover a 7-day program and a 28-day program alike, which is
    /// the same decision that keeps a 28-day program at four templates instead of 28 session files.
    ///
    /// Order (Core <see cref="ProgramArtPaths.DayPlate"/>): per-program override, then the shared
    /// archetype plate, then a generic fallback.
    /// </summary>
    /// <returns>A frozen ImageSource, or null - the caller collapses the tile.</returns>
    internal static ImageSource? DayPlate(ProgramDefinition? program, ProgramDay? day) =>
        First(ProgramArtPaths.DayPlate(program, day));

    /// <summary>
    /// Wide (16:9) hero band art for the run view's Today panel, keyed on the day's session
    /// template exactly like <see cref="DayPlate"/>.
    ///
    /// Order: per-program hero override, shared archetype hero, generic hero, and finally the 1:1
    /// plate chain. The plate fallback is deliberate: the view renders this with
    /// Stretch=UniformToFill, and the plates are abstract luminance glows that survive the crop,
    /// so the hero band works before any hero_*.png has ever been generated.
    /// </summary>
    /// <returns>A frozen ImageSource, or null - the caller collapses the band's art column.</returns>
    internal static ImageSource? DayHero(ProgramDefinition? program, ProgramDay? day) =>
        First(ProgramArtPaths.DayHero(program, day)) ?? DayPlate(program, day);

    /// <summary>
    /// Full-colour banner strip (24.6:1, left ~45% pre-faded to alpha 0 in the file) - the art the
    /// Dashboard Today card wears. Unlike the plate/sigil art this is NOT a luminance mask; it is
    /// rendered as-is.
    ///
    /// Order: the program's own file, then a file shared by every program on the same mod, then
    /// (optionally) the generic fallback. The browse catalogue passes includeDefault:false - five
    /// cards side by side wearing the same fallback banner read as a copy-paste bug, and the emoji
    /// icon is the better tiebreaker there - while the Today card shows one program at a time and
    /// keeps the default.
    /// </summary>
    /// <returns>A frozen ImageSource, or null - the caller collapses the art layer.</returns>
    internal static ImageSource? Banner(ProgramDefinition? program, bool includeDefault = true) =>
        First(ProgramArtPaths.Banner(program, includeDefault));

    /// <summary>Program sigil for the run header. Null collapses it and restores the 4px accent bar.</summary>
    internal static ImageSource? Sigil(ProgramDefinition? program)
    {
        var path = ProgramArtPaths.Sigil(program);
        return path == null ? null : First(new[] { path });
    }

    /// <summary>The first candidate that resolves, or null. Art failing must never break the tab.</summary>
    private static ImageSource? First(IReadOnlyList<string> paths)
    {
        try
        {
            foreach (var path in paths)
            {
                var image = ModResourceResolver.ResolveImage(path);
                if (image != null) return image;
            }
            return null;
        }
        catch { return null; }
    }
}
