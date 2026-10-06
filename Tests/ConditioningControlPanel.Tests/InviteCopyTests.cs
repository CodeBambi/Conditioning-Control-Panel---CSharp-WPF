using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Controls.Invites;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The invites panel's words: every key it reads exists in all nine languages with the same
/// placeholders as English, and every refusal the server can send has a line of its own.
/// </summary>
public class InviteCopyTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static JObject Lang(string lang) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppDir(), "Localization", "Languages", lang + ".json")));

    private static string[] KeysThePanelReads()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Controls", "Invites", "InvitePanel.cs"))
                  + File.ReadAllText(Path.Combine(AppDir(), "Controls", "Invites", "InviteRedeemBox.cs"));
        var keys = Regex.Matches(src, "\"(invites_[a-z_]+)\"").Select(m => m.Groups[1].Value)
            .Where(k => !k.EndsWith("_", StringComparison.Ordinal))
            .Distinct().ToList();
        foreach (var reason in new[] { "unknown_code", "used", "own_code", "already_had_week",
                     "already_subscribed", "too_fast", "offline", "bad_code", "signin", "anything_else" })
            keys.Add(InvitePanel.ReasonKey(reason));
        return keys.Distinct().ToArray();
    }

    private static string Placeholders(string s)
        => string.Join(",", Regex.Matches(s, @"\{\d\}").Select(m => m.Value).OrderBy(v => v));

    [Fact]
    public void EveryKeyThePanelReadsIsInEveryLanguageWithTheSamePlaceholders()
    {
        var keys = KeysThePanelReads();
        Assert.Contains("invites_redeem_go", keys);
        Assert.Contains("invites_err_generic", keys);
        Assert.Contains("invites_redeem_ok_pending", keys);

        var en = Lang("en");
        foreach (var lang in Languages)
        {
            var json = Lang(lang);
            foreach (var key in keys)
            {
                var value = json.Value<string>(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}.json is missing {key}");
                Assert.Equal(Placeholders(en.Value<string>(key)!), Placeholders(value!));
            }
        }
    }

    [Theory]
    [InlineData("used", "invites_err_used")]
    [InlineData("already_had_week", "invites_err_already_had_week")]
    [InlineData("offline", "invites_err_offline")]
    [InlineData("signin", "invites_err_signin")]
    [InlineData("account_too_old", "invites_err_generic")]
    [InlineData(null, "invites_err_generic")]
    [InlineData("not_a_reason_we_know", "invites_err_generic")]
    public void ReasonKey(string? reason, string expected) => Assert.Equal(expected, InvitePanel.ReasonKey(reason));
}
