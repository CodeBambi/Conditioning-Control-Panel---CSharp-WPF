using System;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The BYO (OpenAI-compatible) API key at rest (ai#8). WPF SecureStringHelper on Windows, byte for byte:
/// <c>CompanionPrompt.OpenAiCompatibleApiKey</c> holds base64(DPAPI, CurrentUser, no entropy), so a key WPF
/// saved reads back here and the reverse. Where DPAPI does not exist (Linux) the key goes to the secret
/// store (CoreSecrets, the Secret Service) and settings.json holds only <see cref="Marker"/>.
/// NEVER the key in the clear in settings: with no store at all nothing is saved.
/// </summary>
internal static class ApiKeyProtector
{
    internal const string SecretName = "openai_compat_key";
    internal const string Marker = "secret:" + SecretName;

    /// <summary>Tests only: false forces the secret-store path on Windows.</summary>
    internal static bool UseDpapi = OperatingSystem.IsWindows();

    /// <summary>The value to store in settings for <paramref name="plain"/>; empty revokes the key.</summary>
    internal static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            CoreSecrets.Store(SecretName, null);   // a revoke clears both places
            return string.Empty;
        }
        if (UseDpapi && OperatingSystem.IsWindows())
        {
            try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser)); }
            catch (Exception ex) { Log.Warning(ex, "ApiKeyProtector: DPAPI protect failed, trying the secret store"); }
        }
        if (!CoreSecrets.HasStore)
        {
            Log.Warning("ApiKeyProtector: no secret store on this system, the API key is not saved");
            return string.Empty;
        }
        CoreSecrets.Store(SecretName, plain);
        return Marker;
    }

    /// <summary>OpenAiCompatibleService.ApiKeyUnprotect. A WPF legacy plain value (not base64) reads as itself,
    /// as SecureStringHelper.Unprotect does; anything unreadable is null, never the blob.</summary>
    internal static string? Unprotect(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        if (raw == Marker) return CoreSecrets.Retrieve(SecretName);
        byte[] blob;
        try { blob = Convert.FromBase64String(raw); }
        catch (FormatException) { return raw; }   // never protected (WPF backward compatibility)
        if (!OperatingSystem.IsWindows()) return null;   // a DPAPI blob from Windows: unreadable here
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) { Log.Warning(ex, "ApiKeyProtector: unprotect failed"); return null; }
    }

    /// <summary>WPF MainWindow.SetCustomApiKey: one way, protected on the way in, saved.</summary>
    internal static void SaveCustomKey(string? key)
    {
        var p = CoreSettings.Current?.CompanionPrompt;
        if (p == null) return;
        p.OpenAiCompatibleApiKey = Protect(key);
        CoreSettings.Save();
    }
}
