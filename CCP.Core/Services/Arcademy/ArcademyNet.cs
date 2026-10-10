using System;
using System.Net.Http;
using Serilog;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// The one seam the Arcademy's three sync clients share in Core. WPF calls
/// <c>MergedAccountRecovery.TryHandle(status, body)</c> (contract D: a 409 "merged" reply re-runs
/// sign-in); in Core the recovery is the head's, seeded on <see cref="V2AuthService.MergedRecovery"/>.
/// Fire and forget, exactly as the WPF call is: the caller's own failure path still runs.
/// </summary>
internal static class ArcademyNet
{
    public static void Merged(HttpResponseMessage response, string? body)
    {
        try
        {
            if ((int)response.StatusCode != 409) return;
            _ = V2AuthService.MergedRecovery?.Invoke(response, body);
        }
        catch (Exception ex) { Log.Debug("ArcademyNet.Merged: {E}", ex.Message); }
    }
}
