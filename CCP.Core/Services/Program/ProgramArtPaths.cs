using System;
using System.Collections.Generic;
using System.Text;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// Which Resources/programs/ file each piece of program art comes from, in fallback order, for
/// both heads. Each head resolves the candidates through its own mod-aware image loader (WPF
/// ProgramArt -> ModResourceResolver, Avalonia ModArt.TryLoad) and takes the first that loads; this
/// class only names paths, so it never touches an image type.
/// </summary>
public static class ProgramArtPaths
{
    /// <summary>Folder inside Resources/ (and inside a mod's resources/) that holds this art.</summary>
    public const string Folder = "programs";

    /// <summary>
    /// Day mood plate, keyed on that day's SESSION TEMPLATE: per-program override, shared archetype
    /// plate, generic fallback. Empty when there is no program or day.
    /// </summary>
    public static IReadOnlyList<string> DayPlate(ProgramDefinition? program, ProgramDay? day)
    {
        if (program == null || day == null) return Array.Empty<string>();

        var key = TemplateSlug(program, day);
        if (string.IsNullOrEmpty(key)) return new[] { PlateDefault };

        var list = new List<string>(3);
        var programKey = Slug(program.Id);
        if (!string.IsNullOrEmpty(programKey)) list.Add($"{Folder}/{programKey}_plate_{key}.png");
        list.Add($"{Folder}/plate_{key}.png");
        list.Add(PlateDefault);
        return list;
    }

    /// <summary>Generic plate, the end of the <see cref="DayPlate"/> chain.</summary>
    public const string PlateDefault = Folder + "/plate_default.png";

    /// <summary>
    /// Hero band art keyed like <see cref="DayPlate"/>: per-program hero, shared archetype hero,
    /// generic hero. The caller falls back to the <see cref="DayPlate"/> chain after these.
    /// </summary>
    public static IReadOnlyList<string> DayHero(ProgramDefinition? program, ProgramDay? day)
    {
        if (program == null || day == null) return Array.Empty<string>();

        var list = new List<string>(3);
        var key = TemplateSlug(program, day);
        if (!string.IsNullOrEmpty(key))
        {
            var programKey = Slug(program.Id);
            if (!string.IsNullOrEmpty(programKey)) list.Add($"{Folder}/{programKey}_hero_{key}.png");
            list.Add($"{Folder}/hero_{key}.png");
        }
        list.Add($"{Folder}/hero_default.png");
        return list;
    }

    /// <summary>
    /// Full-colour banner strip: the program's own file, then one shared by every program on the
    /// same mod, then (optionally) the generic fallback. The browse catalogue passes
    /// includeDefault:false - five cards wearing the same fallback banner read as a copy-paste bug.
    /// </summary>
    public static IReadOnlyList<string> Banner(ProgramDefinition? program, bool includeDefault = true)
    {
        if (program == null) return Array.Empty<string>();

        var list = new List<string>(3);
        var key = Slug(program.Id);
        if (!string.IsNullOrEmpty(key)) list.Add($"{Folder}/banner_{key}.png");
        var modKey = Slug(program.ModId);
        if (!string.IsNullOrEmpty(modKey)) list.Add($"{Folder}/banner_{modKey}.png");
        if (includeDefault) list.Add($"{Folder}/banner_default.png");
        return list;
    }

    /// <summary>Program sigil. No fallback: null means the program ships none.</summary>
    public static string? Sigil(ProgramDefinition? program)
    {
        var programKey = Slug(program?.Id);
        return string.IsNullOrEmpty(programKey) ? null : $"{Folder}/sigil_{programKey}.png";
    }

    /// <summary>
    /// Archetype key for a day: the slug of its session template's display Name. When the template
    /// is missing from Templates the id is used instead with any <c>XX-</c> vendor prefix stripped,
    /// so <c>BW-Drift</c> still lands on <c>drift</c>.
    /// </summary>
    public static string TemplateSlug(ProgramDefinition? program, ProgramDay? day)
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
    /// "Drift" -> "drift", "First Week" -> "first_week".
    /// </summary>
    public static string Slug(string? raw)
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
