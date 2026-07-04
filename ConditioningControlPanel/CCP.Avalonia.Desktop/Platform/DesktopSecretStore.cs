using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Platform;

/// <summary>
/// Desktop-specific secure credential storage. Uses DPAPI on Windows, the macOS
/// Keychain (via the <c>security</c> CLI) on macOS, and an AES-GCM encrypted
/// file fallback on Linux.
/// </summary>
public sealed class DesktopSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CCP_Desktop_SecretStore_v1");

    private static string StorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConditioningControlPanel",
        "secrets");

    public void Store(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (OperatingSystem.IsWindows())
        {
            StoreWindows(key, value);
        }
        else if (OperatingSystem.IsMacOS())
        {
            StoreMacOs(key, value);
        }
        else
        {
            StoreLinux(key, value);
        }
    }

    public byte[]? Retrieve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (OperatingSystem.IsWindows())
        {
            return RetrieveWindows(key);
        }

        if (OperatingSystem.IsMacOS())
        {
            return RetrieveMacOs(key);
        }

        return RetrieveLinux(key);
    }

    public void Delete(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (OperatingSystem.IsWindows())
        {
            DeleteWindows(key);
        }
        else if (OperatingSystem.IsMacOS())
        {
            DeleteMacOs(key);
        }
        else
        {
            DeleteLinux(key);
        }
    }

    private static string GetFilePath(string key)
    {
        var safeKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
        return Path.Combine(StorageDirectory, $"{safeKey}.bin");
    }

    private static string GetMacOsServiceName(string key)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(key));

    #region Windows (DPAPI)

    [SupportedOSPlatform("windows")]
    private static void StoreWindows(string key, byte[] value)
    {
        var path = GetFilePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? RetrieveWindows(string key)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            return ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteWindows(string key)
    {
        var path = GetFilePath(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    #endregion

    #region Linux (encrypted file fallback)

    private static void StoreLinux(string key, byte[] value)
    {
        var path = GetFilePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = EncryptWithUserKey(value);
        File.WriteAllBytes(path, encrypted);
    }

    private static byte[]? RetrieveLinux(string key)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            return DecryptWithUserKey(encrypted);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteLinux(string key)
    {
        var path = GetFilePath(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static byte[] EncryptWithUserKey(byte[] plaintext)
    {
        using var aes = new AesGcm(DeriveLinuxKey(), tagSizeInBytes: 16);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = GC.AllocateUninitializedArray<byte>(nonce.Length + tag.Length + ciphertext.Length);
        nonce.CopyTo(result.AsSpan(0, nonce.Length));
        tag.CopyTo(result.AsSpan(nonce.Length, tag.Length));
        ciphertext.CopyTo(result.AsSpan(nonce.Length + tag.Length, ciphertext.Length));
        return result;
    }

    private static byte[] DecryptWithUserKey(byte[] encrypted)
    {
        const int nonceLength = 12;
        const int tagLength = 16;

        if (encrypted.Length < nonceLength + tagLength)
            throw new CryptographicException("Invalid encrypted data.");

        using var aes = new AesGcm(DeriveLinuxKey(), tagSizeInBytes: tagLength);
        var nonce = encrypted.AsSpan(0, nonceLength);
        var tag = encrypted.AsSpan(nonceLength, tagLength);
        var ciphertext = encrypted.AsSpan(nonceLength + tagLength);
        var plaintext = GC.AllocateUninitializedArray<byte>(ciphertext.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveLinuxKey()
    {
        // Deterministic, per-user/per-machine fallback key. This is not as secure as
        // an OS keychain, but it avoids storing plaintext secrets on disk and does not
        // require interop with freedesktop.org Secret Service or systemd-creds.
        var user = Environment.UserName;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var machine = Environment.MachineName;
        var seed = $"{user}\0{home}\0{machine}\0{Convert.ToBase64String(Entropy)}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    #endregion

    #region macOS (Keychain via security CLI)

    private static void StoreMacOs(string key, byte[] value)
    {
        // Update semantics: remove any existing item first.
        DeleteMacOs(key);

        var service = GetMacOsServiceName(key);
        var base64Value = Convert.ToBase64String(value);

        // SECURITY: pass the password via stdin, never on the command line. A
        // "-w <value>" argv would expose the secret to any local user via `ps`.
        // Omitting the -w value makes `security` read the password from standard
        // input (readpassphrase falls back to stdin when the spawned process has
        // no controlling terminal). -U still updates an existing item in place.
        var arguments = $"add-generic-password -s \"{service}\" -a \"CCP\" -U -w";

        using var process = StartSecurityProcess(arguments, redirectStandardInput: true);
        process.StandardInput.Write(base64Value);
        process.StandardInput.Write('\n');
        process.StandardInput.Close();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException($"security add-generic-password failed: {error}");
        }
    }

    private static byte[]? RetrieveMacOs(string key)
    {
        var service = GetMacOsServiceName(key);
        var arguments = $"find-generic-password -s \"{service}\" -a \"CCP\" -w";

        using var process = StartSecurityProcess(arguments);
        process.WaitForExit();

        if (process.ExitCode != 0)
            return null;

        var base64Value = process.StandardOutput.ReadToEnd().Trim();
        if (string.IsNullOrEmpty(base64Value))
            return null;

        try
        {
            return Convert.FromBase64String(base64Value);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteMacOs(string key)
    {
        var service = GetMacOsServiceName(key);
        var arguments = $"delete-generic-password -s \"{service}\" -a \"CCP\"";

        using var process = StartSecurityProcess(arguments);
        process.WaitForExit();
        // Intentionally ignore errors: the item may not exist.
    }

    private static Process StartSecurityProcess(string arguments, bool redirectStandardInput = false)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "security",
                Arguments = arguments,
                RedirectStandardInput = redirectStandardInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        return process;
    }

    #endregion
}
