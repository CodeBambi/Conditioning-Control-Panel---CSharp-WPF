using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// "Is this account on a leash right now?" for code that must not reach <c>App.Leash</c> directly
/// (the remote-control door, the tray). The CORE wiring sets <see cref="IsLeashed"/> to read
/// <c>App.Leash</c>; until then it answers false and nothing changes.
/// </summary>
public static class LeashGuard
{
    /// <summary>Set once by the app wiring. Default: never leashed.</summary>
    public static Func<bool> IsLeashed { get; set; } = () => false;

    /// <summary>
    /// <see cref="IsLeashed"/> that never throws. A check that blows up answers TRUE: every caller
    /// uses this to refuse something that could trap the user (Strict Lock, panic off), and
    /// refusing that by mistake costs far less than allowing it by mistake.
    /// </summary>
    public static bool Check()
    {
        try { return (IsLeashed ?? (() => false))(); }
        catch (Exception ex)
        {
            App.Logger?.Warning("[Leash] IsLeashed check threw, treating as leashed: {Error}", ex.Message);
            return true;
        }
    }
}

/// <summary>What the remote-control door does with one command while the account is leashed.</summary>
public enum LeashRemoteVerdict
{
    /// <summary>Run it as sent.</summary>
    Allow,
    /// <summary>Do not run it at all (enable_strict_lock, disable_panic).</summary>
    Refuse,
    /// <summary>Run it, but drop the strict_lock flag (start_session {strict_lock:true}).</summary>
    StripStrict,
}

/// <summary>
/// Owner rule (2026-09-26): while an account is leashed its client refuses, from ANY remote
/// session (not only a leash one), every command that could take its way out away: turning Strict
/// Lock on, turning the panic key off, and a session start that asks for strict lock. Pure, so the
/// door in <c>RemoteControlService.ExecuteCommand</c> is one line and this is the tested part.
/// </summary>
public static class LeashRemoteRule
{
    public static LeashRemoteVerdict Screen(string? action, bool asksStrictLock, bool leashed)
    {
        if (!leashed) return LeashRemoteVerdict.Allow;
        switch (action)
        {
            case "enable_strict_lock":
            case "disable_panic":
                return LeashRemoteVerdict.Refuse;
            case "start_session":
                return asksStrictLock ? LeashRemoteVerdict.StripStrict : LeashRemoteVerdict.Allow;
            default:
                return LeashRemoteVerdict.Allow;
        }
    }

    /// <summary>
    /// Reads a start_session's strict_lock flag the tolerant way: a real true, the string "true"
    /// or a non-zero number all count. A malformed value never throws.
    /// </summary>
    public static bool AsksStrictLock(Newtonsoft.Json.Linq.JObject? parameters)
    {
        try
        {
            var token = parameters?["strict_lock"];
            if (token == null) return false;
            switch (token.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.Boolean: return token.Value<bool>();
                case Newtonsoft.Json.Linq.JTokenType.Integer: return token.Value<long>() != 0;
                case Newtonsoft.Json.Linq.JTokenType.String:
                    return bool.TryParse(token.Value<string>(), out var b) && b;
                default: return false;
            }
        }
        catch { return false; }
    }
}
