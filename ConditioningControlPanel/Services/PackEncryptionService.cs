using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Handles AES encryption/decryption for content packs.
/// Uses machine-bound key derivation to prevent simple file copying.
/// </summary>
public static class PackEncryptionService
{
    // Salt for key derivation (constant, adds complexity)
    private static readonly byte[] Salt = new byte[]
    {
        0x43, 0x6F, 0x6E, 0x64, 0x69, 0x74, 0x69, 0x6F,
        0x6E, 0x69, 0x6E, 0x67, 0x50, 0x61, 0x6E, 0x65
    };

    // App-specific secret (part of the key)
    private const string AppSecret = "CCP_Pack_v1_2024";

    /// <summary>
    /// Derives a machine-bound encryption key.
    /// This makes copied pack folders useless on other machines.
    /// </summary>
    private static byte[] DeriveKey()
    {
        // Combine app secret with machine-specific data
        var machineId = Environment.MachineName + Environment.UserName;
        var combined = AppSecret + machineId;

        using var deriveBytes = new Rfc2898DeriveBytes(
            combined,
            Salt,
            100000, // iterations
            HashAlgorithmName.SHA256);

        return deriveBytes.GetBytes(32); // 256-bit key
    }

    /// <summary>
    /// Encrypts data using AES-256-CBC
    /// </summary>
    public static byte[] Encrypt(byte[] plainData)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);

        // Prepend IV to encrypted data (IV is not secret)
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data using AES-256-CBC
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV from beginning of data
        var iv = new byte[16];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, 16);
        aes.IV = iv;

        // Decrypt the rest
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedData, 16, encryptedData.Length - 16);
    }

    /// <summary>
    /// Encrypts a file and saves with .enc extension
    /// </summary>
    public static void EncryptFile(string sourcePath, string destPath)
    {
        var plainData = File.ReadAllBytes(sourcePath);
        var encrypted = Encrypt(plainData);
        File.WriteAllBytes(destPath, encrypted);
    }

    /// <summary>
    /// Decrypts a file to memory (never writes plaintext to disk)
    /// </summary>
    public static byte[] DecryptFile(string encryptedPath)
    {
        var encrypted = File.ReadAllBytes(encryptedPath);
        return Decrypt(encrypted);
    }

    /// <summary>
    /// Decrypts a file and returns as a memory stream (for image loading)
    /// </summary>
    public static MemoryStream DecryptFileToStream(string encryptedPath)
    {
        var decrypted = DecryptFile(encryptedPath);
        return new MemoryStream(decrypted);
    }

    /// <summary>
    /// Generates a random filename hash for obfuscation
    /// </summary>
    public static string GenerateObfuscatedFilename()
    {
        var bytes = new byte[8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Encrypts and saves a manifest JSON
    /// </summary>
    public static void SaveEncryptedManifest(string json, string destPath)
    {
        var plainData = Encoding.UTF8.GetBytes(json);
        var encrypted = Encrypt(plainData);
        File.WriteAllBytes(destPath, encrypted);
    }

    /// <summary>
    /// Loads and decrypts a manifest JSON
    /// </summary>
    public static string LoadEncryptedManifest(string encryptedPath)
    {
        var encrypted = File.ReadAllBytes(encryptedPath);
        var decrypted = Decrypt(encrypted);
        return Encoding.UTF8.GetString(decrypted);
    }
}
