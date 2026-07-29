using System;
using System.Text;
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
    /// <summary>Folder inside Resources/ (and inside a mod's resources/) that holds this art.</summary>
    private const string Folder = "programs";

    /// <summary>
    /// Mood plate for a day, keyed on that day's SESSION TEMPLATE - not on the day number. Four
    /// authored archetypes therefore cover a 7-day program and a 28-day program alike, which is
    /// the same decision that keeps a 28-day program at four templates instead of 28 session files.
    ///
    /// Order: per-program override, then the shared archetype plate, then a generic fallback.
    /// </summary>
    /// <returns>A frozen ImageSource, or null - the caller collapses the tile.</returns>
    internal static ImageSource? DayPlate(ProgramDefinition? program, ProgramDay? day)
    {
        try
        {
            if (program == null || day == null) return null;

            var key = TemplateSlug(program, day);
            if (string.IsNullOrEmpty(key)) return PlateDefault();

            var programKey = Slug(program.Id);
            if (!string.IsNullOrEmpty(programKey))
            {
                var owned = ModResourceResolver.ResolveImage($"{Folder}/{programKey}_plate_{key}.png");
                if (owned != null) return owned;
            }

            return ModResourceResolver.ResolveImage($"{Folder}/plate_{key}.png") ?? PlateDefault();
        }
        catch { return null; }
    }

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
    internal static ImageSource? DayHero(ProgramDefinition? program, ProgramDay? day)
    {
        try
        {
            if (program == null || day == null) return null;

            var key = TemplateSlug(program, day);
            if (!string.IsNullOrEmpty(key))
            {
                var programKey = Slug(program.Id);
                if (!string.IsNullOrEmpty(programKey))
                {
                    var owned = ModResourceResolver.ResolveImage($"{Folder}/{programKey}_hero_{key}.png");
                    if (owned != null) return owned;
                }

                var shared = ModResourceResolver.ResolveImage($"{Folder}/hero_{key}.png");
                if (shared != null) return shared;
            }

            return ModResourceResolver.ResolveImage($"{Folder}/hero_default.png") ?? DayPlate(program, day);
        }
        catch { return null; }
    }

    /// <summary>Program sigil for the run header. Null collapses it and restores the 4px accent bar.</summary>
    internal static ImageSource? Sigil(ProgramDefinition? program)
    {
        try
        {
            var programKey = Slug(program?.Id);
            if (string.IsNullOrEmpty(programKey)) return null;

            return ModResourceResolver.ResolveImage($"{Folder}/sigil_{programKey}.png");
        }
        catch { return null; }
    }

    private static ImageSource? PlateDefault()
    {
        try { return ModResourceResolver.ResolveImage($"{Folder}/plate_default.png"); }
        catch { return null; }
    }

    /// <summary>
    /// Archetype key for a day: the slug of its session template's display Name.
    ///
    /// Built-in templates are ids like <c>BW-Drift</c> with Name <c>Drift</c>, so the key is
    /// <c>drift</c>. When a definition carries a day whose template is missing from Templates (a
    /// hand-authored or third-party program), the id is used instead with any <c>XX-</c> vendor
    /// prefix stripped, so <c>BW-Drift</c> still lands on <c>drift</c>.
    /// </summary>
    internal static string TemplateSlug(ProgramDefinition? program, ProgramDay? day)
    {
        var templateId = day?.SessionTemplateId;
        if (string.IsNullOrWhiteSpace(templateId)) return "";

        if (program != null)
        {
            foreach (var template in program.Templates)
            {
                if (!string.Equals(template.Id, templateId, StringComparison.OrdinalIgnoreCase)) continue;
                var named = Slug(template.Name);
                if (!string.IsNullOrEmpty(named)) return named;
                break;
            }
        }

        return Slug(StripVendorPrefix(templateId));
    }

    /// <summary>"BW-Drift" -> "Drift". Only a short leading token before the first dash is dropped.</summary>
    private static string StripVendorPrefix(string id)
    {
        var dash = id.IndexOf('-');
        if (dash <= 0 || dash > 4 || dash == id.Length - 1) return id;
        return id.Substring(dash + 1);
    }

    /// <summary>
    /// Lowercase, ASCII-alphanumeric, runs of anything else collapsed to a single underscore.
    /// "Drift" -> "drift", "First Week" -> "first_week". Used for both halves of the filename so a
    /// program id and a template name can never produce a path the resolver would reject.
    /// </summary>
    internal static string Slug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var sb = new StringBuilder(raw.Length);
        var pendingSeparator = false;

        foreach (var ch in raw)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                if (pendingSeparator && sb.Length > 0) sb.Append('_');
                pendingSeparator = false;
                sb.Append(ch);
            }
            else if (ch >= 'A' && ch <= 'Z')
            {
                if (pendingSeparator && sb.Length > 0) sb.Append('_');
                pendingSeparator = false;
                sb.Append((char)(ch + 32));
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }
}
