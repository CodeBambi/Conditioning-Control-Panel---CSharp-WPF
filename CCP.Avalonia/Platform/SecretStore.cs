using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// This head's <see cref="CoreSecrets"/> provider, one item per name (authtoken, apikey,
/// patreon_auth/_cache, discord_auth/_cache, substar_*). Linux: the Secret Service through
/// libsecret. Windows: DPAPI files that WPF's own stores read and write
/// (<see cref="Dpapi"/>). No usable OS store: values live in memory for this run only and
/// <see cref="NotRemembered"/> is set (the login flow's notice). Never a plaintext file (the CoreSecrets rule).
/// </summary>
internal static class SecretStore
{
    // name -> value for this run (null = known absent). With no OS store this IS the store.
    private static readonly ConcurrentDictionary<string, string?> _cache = new();

    /// <summary>True once a value was kept in memory only ("sign-in not remembered").</summary>
    internal static volatile bool NotRemembered;

    /// <summary>True once a stored value could not be removed from the OS store: a logout did not
    /// stick. AccountSeed.Logout resets it; the shell tells the user.</summary>
    internal static volatile bool ClearFailed;

    /// <summary>Tests only: replaces the OS write.</summary>
    internal static Func<string, string?, bool>? OsWriteOverride;

    /// <summary>A CCP_USERDATA_DIR sandbox (tests, kc) does not isolate the keyring, so it never touches it: memory only.</summary>
    internal static bool Sandboxed = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR") != null;

    internal static void Seed()
    {
        CoreSecrets.RetrieveProvider = Retrieve;
        CoreSecrets.StoreProvider = Store;
    }

    internal static string? Retrieve(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;
        var (ok, value) = OsRead(name);
        if (ok) _cache[name] = value;   // a failed lookup (locked, prompt dismissed) retries next time
        return value;
    }

    internal static void Store(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) value = null;
        _cache[name] = value;
        if (OsWrite(name, value)) return;
        if (value is null)
        {
            // Memory-only runs (sandbox, no Secret Service) wrote nothing to the OS store: the memory clear is the clear.
            if (Sandboxed || NotRemembered) return;
            Log.Warning("Could not clear {Name} from the secret store", name);
            ClearFailed = true;
            return;
        }
        Log.Warning("No usable secret store: {Name} is kept in memory for this run only", name);
        NotRemembered = true;
    }

    private static (bool Ok, string? Value) OsRead(string name) =>
        Sandboxed ? (true, null) : OperatingSystem.IsWindows() ? (true, Dpapi.Read(CorePaths.UserData, name))
        : OperatingSystem.IsLinux() ? Libsecret.Read(name) : (true, null);

    private static bool OsWrite(string name, string? value) =>
        OsWriteOverride is { } fake ? fake(name, value) : !Sandboxed && (OperatingSystem.IsWindows() ? Dpapi.Write(CorePaths.UserData, name, value)
        : OperatingSystem.IsLinux() && Libsecret.Write(name, value));
}

/// <summary>
/// The Secret Service through libsecret's non-variadic *v_sync calls. No schema: items carry
/// {application, name} attributes. Every failure (no library, no bus, no service, locked,
/// dismissed prompt) reads as null / writes as false.
/// </summary>
internal static class Libsecret
{
    private const string Lib = "libsecret-1.so.0", GLib = "libglib-2.0.so.0";

    [DllImport(Lib)] private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password, IntPtr cancellable, out IntPtr error);
    [DllImport(Lib)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(Lib)] private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(Lib)] private static extern void secret_password_free(IntPtr password);
    [DllImport(GLib)] private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);
    [DllImport(GLib)] private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(GLib)] private static extern void g_hash_table_unref(IntPtr table);
    [DllImport(GLib)] private static extern void g_error_free(IntPtr error);

    internal static string? Lookup(string name) => Read(name).Value;

    /// <summary>Ok is false when the lookup failed (as opposed to finding nothing).</summary>
    internal static (bool Ok, string? Value) Read(string name) => Call(name, "lookup", attrs =>
    {
        var p = secret_password_lookupv_sync(IntPtr.Zero, attrs, IntPtr.Zero, out var err);
        if (!Check(err, "lookup", name)) return (false, null);
        if (p == IntPtr.Zero) return (true, (string?)null);
        try { return (true, Marshal.PtrToStringUTF8(p)); } finally { secret_password_free(p); }
    });

    internal static bool Write(string name, string? value) => Call(name, "store", attrs =>
    {
        IntPtr err;
        var ok = value is null
            ? secret_password_clearv_sync(IntPtr.Zero, attrs, IntPtr.Zero, out err)
            : secret_password_storev_sync(IntPtr.Zero, attrs, IntPtr.Zero, "Conditioning Control Panel: " + name, value, IntPtr.Zero, out err);
        // clear returns FALSE when there was nothing to remove; only an error is a failure.
        return Check(err, "store", name) && (ok != 0 || value is null);
    });

    private static T? Call<T>(string name, string op, Func<IntPtr, T?> body)
    {
        IntPtr table = IntPtr.Zero, k1 = IntPtr.Zero, v1 = IntPtr.Zero, k2 = IntPtr.Zero, v2 = IntPtr.Zero;
        try
        {
            var glib = NativeLibrary.Load(GLib);
            table = g_hash_table_new(NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
            g_hash_table_insert(table, k1 = Marshal.StringToCoTaskMemUTF8("application"), v1 = Marshal.StringToCoTaskMemUTF8("ConditioningControlPanel"));
            g_hash_table_insert(table, k2 = Marshal.StringToCoTaskMemUTF8("name"), v2 = Marshal.StringToCoTaskMemUTF8(name));
            return body(table);
        }
        catch (Exception ex)
        {
            Log.Warning("Secret {Op} for {Name} unavailable: {Error}", op, name, ex.Message);
            return default;
        }
        finally
        {
            if (table != IntPtr.Zero) g_hash_table_unref(table);
            foreach (var p in new[] { k1, v1, k2, v2 }) Marshal.FreeCoTaskMem(p);
        }
    }

    // GError { GQuark domain; gint code; gchar *message; }
    private static bool Check(IntPtr err, string op, string name)
    {
        if (err == IntPtr.Zero) return true;
        Log.Warning("Secret {Op} for {Name} failed: {Error}", op, name, Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(err, 8)));
        g_error_free(err);
        return false;
    }
}

/// <summary>
/// WPF's DPAPI files, byte-compatible: <c>auth_token.dat</c> / <c>api_key.dat</c>
/// (Services/Auth/SecureAuthTokenStore.cs:14, SecureApiKeyStore.cs:14) and
/// <c>{prefix}_auth.dat</c> / <c>{prefix}_cache.dat</c> with entropy
/// <c>ConditioningControlPanel_{Label}_v1</c> (SecureTokenStorage.cs:30, DiscordTokenStorage.cs:17).
/// The plaintext is the caller's string (WPF's Newtonsoft JSON for the provider items), UTF-8.
/// </summary>
internal static class Dpapi
{
    internal static (string Path, byte[] Entropy) Slot(string dir, string name)
    {
        var (file, label) = name switch
        {
            CoreSecrets.AuthToken => ("auth_token", "AuthToken"),
            CoreSecrets.ApiKey => ("api_key", "ApiKey"),
            _ => (name, char.ToUpperInvariant(name[0]) + name.Split('_')[0][1..]),
        };
        return (Path.Combine(dir, file + ".dat"), Encoding.UTF8.GetBytes($"ConditioningControlPanel_{label}_v1"));
    }

    [SupportedOSPlatform("windows")]
    internal static string? Read(string dir, string name)
    {
        var (path, entropy) = Slot(dir, name);
        try
        {
            return File.Exists(path)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser))
                : null;
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to read {Name} secret", name); return null; }
    }

    [SupportedOSPlatform("windows")]
    internal static bool Write(string dir, string name, string? value)
    {
        var (path, entropy) = Slot(dir, name);
        try
        {
            if (value is null) { File.Delete(path); return true; }
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to store {Name} secret", name); return false; }
    }
}
