using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// Chess spectating, host side. Pure, so the wire checks are testable without a window.
/// <list type="bullet">
/// <item>host -&gt; page <c>pbp:settings</c> carries <c>letPeopleWatch</c> (bool, every boot) and,
/// on a board opened straight into a spectate, <c>spectateMatchId</c> (once).</item>
/// <item>page -&gt; host <c>{ type: 'pbp:setting', key: 'letPeopleWatch', value: bool }</c> when
/// the page's "Let people watch my games" toggle changes. Any other key or a non-bool value is
/// ignored: the page cannot write arbitrary settings through this frame.</item>
/// </list>
/// </summary>
internal static class PbpWatchRules
{
    public const string SettingFrame = "pbp:setting";
    public const string LetPeopleWatchKey = "letPeopleWatch";

    /// <summary>The server's match id: <c>m_</c> then hex (16 today; 8 to 32 accepted).</summary>
    public static bool IsMatchId(string? id) =>
        id != null && id.Length >= 10 && id.Length <= 34 && id.StartsWith("m_", StringComparison.Ordinal)
        && id.Skip(2).All(Uri.IsHexDigit);

    /// <summary>Read a <c>pbp:setting</c> frame. True only for a key this host knows with a
    /// value of the right type.</summary>
    public static bool TryRead(JObject? o, out string key, out bool value)
    {
        key = "";
        value = false;
        if (o == null || (string?)o["type"] != SettingFrame) return false;
        if (o["key"]?.Type != JTokenType.String || (string?)o["key"] != LetPeopleWatchKey) return false;
        if (o["value"]?.Type != JTokenType.Boolean) return false;
        key = LetPeopleWatchKey;
        value = o.Value<bool>("value");
        return true;
    }

    /// <summary>Store a read setting. Returns true when it changed something (worth a save).</summary>
    public static bool Apply(AppSettings? s, string key, bool value)
    {
        if (s == null) return false;
        if (key != LetPeopleWatchKey) return false;
        if (s.PbpLetPeopleWatch == value) return false;
        s.PbpLetPeopleWatch = value;
        return true;
    }

    /// <summary>What the settings frame says about watching. Missing settings = the default (on).</summary>
    public static bool LetPeopleWatch(AppSettings? s) => s?.PbpLetPeopleWatch ?? true;
}
