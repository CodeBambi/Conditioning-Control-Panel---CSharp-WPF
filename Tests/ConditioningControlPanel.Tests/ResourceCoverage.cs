using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConditioningControlPanel.Tests;

/// <summary>Checks the resource path produced by a WPF <c>Resource</c> item.</summary>
internal static class ResourceCoverage
{
    internal static bool IsCovered(string projectFile, string resourcePath)
    {
        var logicalPath = @"Resources\" + resourcePath.Replace('/', '\\');
        foreach (var item in XDocument.Load(projectFile).Descendants()
                     .Where(e => e.Name.LocalName == "Resource"))
        {
            var link = ((string?)item.Attribute("Link"))?.Trim();
            foreach (var rawInclude in ((string?)item.Attribute("Include") ?? string.Empty).Split(';'))
            {
                var include = rawInclude.Trim().Replace('/', '\\');
                if (include.Length == 0 || include.Contains("$(")) continue;

                // The bytes live under the shared /Assets root. Match the logical path created by
                // its Resources\ Link, the same normalization used by EmiRingCatalogueTests.
                var shared = include.StartsWith(@"..\Assets\", StringComparison.OrdinalIgnoreCase);
                if (shared) include = @"Resources\" + include.Substring(@"..\Assets\".Length);
                if (!Matches(include, logicalPath)) continue;

                // A shared-root Include is only a pack Resource when its Link points back under
                // Resources. Project-local Resource items may use their Include as-is.
                if (shared && string.IsNullOrWhiteSpace(link)) continue;
                if (string.IsNullOrWhiteSpace(link) || Matches(link, logicalPath)) return true;
            }
        }

        return false;
    }

    private static bool Matches(string pattern, string path)
    {
        var escaped = Regex.Escape(pattern.Replace('/', '\\'))
            .Replace(Regex.Escape("%(RecursiveDir)"), @"(?:[^\\]+\\)*")
            .Replace(Regex.Escape("%(Filename)"), @"[^\\]+")
            .Replace(Regex.Escape("%(Extension)"), @"\.[^\\]+")
            .Replace(@"\*\*\\", @"(.*\\)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^\\]*");
        return Regex.IsMatch(path, "^" + escaped + "$", RegexOptions.IgnoreCase);
    }
}
