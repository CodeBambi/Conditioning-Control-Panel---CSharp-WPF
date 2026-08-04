using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// Turns "template X at intensity 0.42 for 45 minutes" into a real <see cref="Session"/> the engine
/// can run. This is the piece that keeps a 28-day program at four authored templates instead of 28
/// hand-built session files.
///
/// Rules:
/// - Every numeric field lerps between the template's Floor and Ceiling by the day's intensity.
/// - Booleans, enums and phrase pools come from Floor, because *which features a template runs*
///   is the template's identity, not a thing the curve should decide.
/// - The result is always a fresh deep copy. SessionDefinition.ToSession/FromSession are shallow,
///   so lerping in place would quietly corrupt the authored template.
/// </summary>
public static class ProgramSessionBuilder
{
    /// <summary>
    /// Prefix on every generated session name. AchievementService.TrackSessionComplete matches
    /// built-in sessions by lowercase substring ("morning drift", "gamer girl", "distant doll",
    /// "good girls"), so program session names are namespaced and screened below.
    /// </summary>
    public const string NamePrefix = "Program";

    private static readonly string[] ReservedNameSubstrings =
    {
        "morning drift", "gamer girl", "distant doll", "good girls"
    };

    /// <summary>
    /// Build the transient session for a program day. Never registered with SessionManager and never
    /// written to disk - the engine accepts a Session object directly.
    /// </summary>
    public static Session Build(ProgramDefinition program, ProgramDay day)
    {
        if (program == null) throw new ArgumentNullException(nameof(program));
        if (day == null) throw new ArgumentNullException(nameof(day));

        var template = program.GetTemplate(day.SessionTemplateId)
            ?? throw new InvalidOperationException(
                $"Program '{program.Id}' day {day.DayIndex} references unknown template '{day.SessionTemplateId}'.");

        var intensity = Math.Clamp(day.Intensity, 0.0, 1.0);
        var settings = LerpSettings(template.Floor, template.Ceiling, intensity);

        if (day.Overrides is { Count: > 0 })
            ApplyOverrides(settings, day.Overrides);

        return new Session
        {
            Id = $"program_{program.Id}_day{day.DayIndex}",
            Name = BuildSessionName(program, day, template),
            Icon = program.Icon,
            DurationMinutes = Math.Max(1, day.SessionMinutes),
            IsAvailable = true,
            Difficulty = DifficultyFor(intensity),
            BonusXP = BonusXpFor(intensity, day.IsBoss),
            Source = SessionSource.Custom,
            Description = string.IsNullOrWhiteSpace(day.Blurb) ? template.Description : day.Blurb,
            Settings = settings,
            Phases = new List<SessionPhase>(),

            // Deliberately empty. TimelineEvents is the editor's representation (#429); a lerped
            // session that carried the template's un-lerped timeline would display a lie.
            TimelineEvents = new List<TimelineEvent>()
        };
    }

    /// <summary>
    /// "Program · Day 3 — Bubble Induction", screened so a template name can never collide with the
    /// substrings AchievementService matches on and falsely unlock someone's achievements.
    /// </summary>
    public static string BuildSessionName(ProgramDefinition program, ProgramDay day, ProgramSessionTemplate template)
    {
        var label = !string.IsNullOrWhiteSpace(day.Title) ? day.Title : template.Name;
        var name = $"{NamePrefix} · Day {day.DayIndex} — {label}";

        if (!ContainsReserved(name)) return name;

        // The authored title collides. Try the program title, then fall back to the id, which is
        // the only component guaranteed to carry no prose - an escape hatch that can itself collide
        // is not an escape hatch.
        var byTitle = $"{NamePrefix} · {program.Title} · Day {day.DayIndex}";
        if (!ContainsReserved(byTitle)) return byTitle;

        return $"{NamePrefix} · {program.Id} · Day {day.DayIndex}";
    }

    /// <summary>
    /// True when a candidate session name would trip one of AchievementService's substring matches
    /// and falsely unlock an achievement the user never earned.
    /// </summary>
    public static bool ContainsReserved(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        var lower = name.ToLowerInvariant();
        foreach (var reserved in ReservedNameSubstrings)
        {
            if (lower.Contains(reserved, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static SessionDifficulty DifficultyFor(double intensity) => intensity switch
    {
        < 0.30 => SessionDifficulty.Easy,
        < 0.60 => SessionDifficulty.Medium,
        < 0.85 => SessionDifficulty.Hard,
        _ => SessionDifficulty.Extreme
    };

    private static int BonusXpFor(double intensity, bool isBoss)
    {
        var baseXp = (int)Math.Round(300 + (700 * Math.Clamp(intensity, 0, 1)));
        return isBoss ? (int)Math.Round(baseXp * 1.5) : baseXp;
    }

    /// <summary>
    /// Lerp every numeric property; take everything else from <paramref name="floor"/>.
    /// Reflection rather than a hand-written field list so a new SessionSettings field is picked up
    /// automatically instead of being silently frozen at its default.
    /// </summary>
    public static SessionSettings LerpSettings(SessionSettings floor, SessionSettings ceiling, double t)
    {
        if (floor == null) throw new ArgumentNullException(nameof(floor));
        ceiling ??= floor;

        t = Math.Clamp(t, 0.0, 1.0);
        var result = new SessionSettings();

        foreach (var prop in SettingsProperties)
        {
            var floorValue = prop.GetValue(floor);
            var ceilingValue = prop.GetValue(ceiling);

            object? value;

            if (prop.PropertyType == typeof(int))
            {
                value = LerpInt((int)(floorValue ?? 0), (int)(ceilingValue ?? 0), t);
            }
            else if (prop.PropertyType == typeof(int?))
            {
                // A nullable that the floor leaves null stays null - those fields fall through to the
                // user's own dashboard values, and writing a number would silently override them.
                var f = (int?)floorValue;
                var c = (int?)ceilingValue;
                value = f.HasValue && c.HasValue ? LerpInt(f.Value, c.Value, t) : f;
            }
            else if (prop.PropertyType == typeof(double))
            {
                value = Lerp((double)(floorValue ?? 0d), (double)(ceilingValue ?? 0d), t);
            }
            else if (prop.PropertyType == typeof(List<string>))
            {
                value = floorValue is List<string> list ? new List<string>(list) : new List<string>();
            }
            else
            {
                value = floorValue;
            }

            prop.SetValue(result, value);
        }

        return result;
    }

    /// <summary>Internal so <see cref="Models.Program.ProgramDefinition.Validate"/> can predict the
    /// same start minutes the builder will actually produce (#747) instead of duplicating the
    /// rounding and drifting from it.</summary>
    internal static int LerpInt(int a, int b, double t) => (int)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero);

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>Deep copy of a settings object, used when a caller needs to mutate without side effects.</summary>
    public static SessionSettings Clone(SessionSettings source) => LerpSettings(source, source, 0);

    /// <summary>
    /// Apply the day's sparse overrides onto the lerped settings. Values that survived a JSON
    /// round-trip arrive as JsonElement rather than int/bool, so they are materialised explicitly -
    /// the same hazard TimelineEvent.GetSetting has to handle (#429).
    /// </summary>
    public static void ApplyOverrides(SessionSettings settings, IReadOnlyDictionary<string, object> overrides)
    {
        if (settings == null || overrides == null) return;

        foreach (var (key, raw) in overrides)
        {
            if (!SettingsByName.TryGetValue(key, out var prop) || !prop.CanWrite)
            {
                App.Logger?.Warning("Program day override targets unknown SessionSettings field '{Field}'", key);
                continue;
            }

            try
            {
                var converted = Coerce(raw, prop.PropertyType);

                // A null only ever lands on a genuinely nullable value type (the int? frequency
                // fields, where null means "leave the user's dashboard value alone"). Writing null
                // into a string property would hand the rest of the app a null it assumes is "".
                if (converted == null && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    App.Logger?.Warning("Program day override for '{Field}' resolved to null and was skipped", key);
                    continue;
                }

                prop.SetValue(settings, converted);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program day override for '{Field}' could not be applied", key);
            }
        }
    }

    private static object? Coerce(object? raw, Type targetType)
    {
        if (raw == null) return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (raw is JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.True or JsonValueKind.False:
                    return underlying == typeof(bool) ? element.GetBoolean() : Convert.ChangeType(element.GetBoolean(), underlying);
                case JsonValueKind.Number:
                    if (underlying == typeof(int)) return element.GetInt32();
                    if (underlying == typeof(double)) return element.GetDouble();
                    return Convert.ChangeType(element.GetDouble(), underlying, CultureInfo.InvariantCulture);
                case JsonValueKind.String:
                    var s = element.GetString() ?? "";
                    return underlying.IsEnum ? Enum.Parse(underlying, s, ignoreCase: true) : Convert.ChangeType(s, underlying, CultureInfo.InvariantCulture);
                case JsonValueKind.Array:
                    if (underlying == typeof(List<string>))
                        return element.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                    break;
            }

            return null;
        }

        if (underlying.IsInstanceOfType(raw)) return raw;
        if (underlying.IsEnum) return Enum.Parse(underlying, raw.ToString() ?? "", ignoreCase: true);
        if (underlying == typeof(List<string>) && raw is IEnumerable<object> seq)
            return seq.Select(o => o?.ToString() ?? "").ToList();

        return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
    }

    private static readonly PropertyInfo[] SettingsProperties = typeof(SessionSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    private static readonly Dictionary<string, PropertyInfo> SettingsByName =
        SettingsProperties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
}
