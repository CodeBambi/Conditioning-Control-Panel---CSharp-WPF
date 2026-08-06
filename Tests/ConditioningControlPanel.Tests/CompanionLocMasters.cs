using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The nine shipping language files, read straight off disk, strictly.
///
/// <para><b>What this replaces.</b> While the Companion tab redesign lived on its own branch it
/// could not touch <c>Localization/Languages/*.json</c>, so it carried its EN copy in a staging
/// table (<c>CompanionLocStaging</c>) plus a JSON hand-off. The loc pass merged all of it into the
/// nine real files and deleted the vehicle; the suites that used to assert against the staged
/// masters now assert against <c>en.json</c> itself, which is strictly better — a key that never
/// reached the language files now fails instead of quietly resolving from a private table.</para>
///
/// <para><b>Why the repo copy and not the one beside the binary.</b> The build copies the language
/// files into the test output directory, so a stale bin could pass a suite that the committed file
/// would fail. Walking up to the source tree asserts against what actually ships. The walk throws
/// with the searched path rather than skipping, so a moved file cannot make a suite pass
/// vacuously.</para>
/// </summary>
internal static class CompanionLocMasters
{
    /// <summary>Every language code with a file in <c>Localization/Languages</c>.</summary>
    public static readonly string[] Languages =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string>? _companion;
    private static string? _languagesDirectory;

    /// <summary>The full <c>en.json</c>.</summary>
    public static IReadOnlyDictionary<string, string> English => For("en");

    /// <summary>
    /// The <c>companion_*</c> family of <c>en.json</c> — the page's own copy. Scoped to the prefix
    /// so a mock string matching some unrelated button label elsewhere in the app cannot pass a
    /// "this line came from the catalogue" assertion.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Companion => _companion ??= BuildCompanion();

    /// <summary>Directory holding the nine language files, found by walking up from the test binary.</summary>
    public static string LanguagesDirectory => _languagesDirectory ??= FindLanguagesDirectory();

    /// <summary>The EN master for a key, or the key itself — the same shape <c>Loc.Get</c> has.</summary>
    public static string Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return English.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>One language file, parsed with <see cref="JsonSerializer"/> — strict on purpose.</summary>
    public static IReadOnlyDictionary<string, string> For(string language)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(language, out var cached)) return cached;

            var path = PathFor(language);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                         ?? throw new InvalidOperationException($"{language}.json deserialized to null");
            var wrapped = new ReadOnlyDictionary<string, string>(parsed);
            Cache[language] = wrapped;
            return wrapped;
        }
    }

    /// <summary>Absolute path of one language file in the source tree.</summary>
    public static string PathFor(string language) =>
        Path.Combine(LanguagesDirectory, language + ".json");

    private static IReadOnlyDictionary<string, string> BuildCompanion() =>
        new ReadOnlyDictionary<string, string>(
            English.Where(kv => kv.Key.StartsWith("companion_", StringComparison.Ordinal))
                   .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));

    private static string FindLanguagesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel",
                                         "Localization", "Languages");
            if (File.Exists(Path.Combine(candidate, "en.json"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate ConditioningControlPanel/Localization/Languages walking up from " +
            AppContext.BaseDirectory);
    }
}

/// <summary>
/// Loads English into <see cref="LocalizationManager"/> before the first test runs.
///
/// <para>The Companion page's viewmodels resolve their copy through <c>Loc.Get</c> like the rest of
/// the app. Left uninitialized the manager echoes every key back, so a suite comparing a viewmodel
/// string to its EN master would compare "Companion" to "companion_header_title" and fail for a
/// reason that has nothing to do with the code under test. A module initializer runs before any
/// test in the assembly, which is the only hook early enough: a fixture or a lazy static could be
/// touched after the viewmodel already captured its strings.</para>
///
/// <para>Failure is swallowed on purpose. If the language files are not deployed the manager keeps
/// echoing keys and the loc suites fail with their own message, which is far more useful than a
/// type-initialization exception on an unrelated test.</para>
/// </summary>
internal static class TestLocalizationBootstrap
{
    [ModuleInitializer]
    internal static void UseEnglish()
    {
        try
        {
            LocalizationManager.Instance.Initialize("en");
        }
        catch (Exception)
        {
            // see remarks
        }
    }
}
