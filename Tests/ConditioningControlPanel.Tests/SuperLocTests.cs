using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Super;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Every Super string is a key in all nine languages, in the house voice.</summary>
public class SuperLocTests
{
    [Fact]
    public void Every_super_key_is_in_all_nine_languages_without_dashes()
    {
        var dir = FindLanguages();
        var en = JObject.Parse(File.ReadAllText(Path.Combine(dir, "en.json")));
        var keys = en.Properties().Select(p => p.Name).Where(k => k.StartsWith("super_")).ToList();
        foreach (SuperEffect e in Enum.GetValues<SuperEffect>())
        {
            Assert.Contains("super_name_" + e.ToString().ToLowerInvariant(), keys);
            Assert.Contains("super_twist_" + e.ToString().ToLowerInvariant(), keys);
        }
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);
        foreach (var f in files)
        {
            var o = JObject.Parse(File.ReadAllText(f));
            foreach (var k in keys)
            {
                var v = (string?)o[k];
                Assert.False(string.IsNullOrEmpty(v), $"{Path.GetFileName(f)} is missing {k}");
                Assert.DoesNotContain('—', v!);
                Assert.DoesNotContain('–', v!);
                Assert.DoesNotContain('!', v!);
            }
        }
    }

    private static string FindLanguages()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var p = Path.Combine(d.FullName, "ConditioningControlPanel", "Localization", "Languages");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("Localization/Languages");
    }
}
