/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Security.Cryptography;
using System.Text;
using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>Protects a secret for this user on this device (Platform.Vault).</summary>
internal interface IVault
{
    byte[] Protect(byte[] clear);
    byte[] Unprotect(byte[] sealedData);
}

/// <summary>
/// The kept sign-in password (Settings › 일반 › 비밀번호 저장), protected for this user on this device, so the
/// config file is useless on another account or machine: Windows DPAPI, the macOS keychain, the Android Keystore
/// (Platform.Vault). A LAB PC or a kiosk never keeps one.
/// </summary>
internal static class PasswordVault
{
    public static void Keep(AppConfig config, string password)
    {
        config.SavedPassword = string.Empty;
        if (!config.RememberPassword || config.IsSharedPc || Platform.Vault is not { } vault) { return; }
        try { config.SavedPassword = Convert.ToBase64String(vault.Protect(Encoding.UTF8.GetBytes(password))); }
        catch { /* not kept */ }
    }

    public static string? Read(AppConfig config)
    {
        if (!config.RememberPassword || config.IsSharedPc || config.SavedPassword.Length == 0 || Platform.Vault is not { } vault) { return null; }
        try { return Encoding.UTF8.GetString(vault.Unprotect(Convert.FromBase64String(config.SavedPassword))); }
        catch { return null; }   // another user's, or damaged
    }

    public static void Forget(AppConfig config) => config.SavedPassword = string.Empty;
}

/// <summary>Windows: DPAPI for this Windows user.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class DpapiVault : IVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MOAVI Desk sign-in");
    public byte[] Protect(byte[] clear) => ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] sealedData) => ProtectedData.Unprotect(sealedData, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>AES-256-CBC + HMAC-SHA256 with a 64-byte key the platform keeps (macOS: in the keychain).</summary>
internal sealed class KeyedVault(Func<byte[]> key) : IVault
{
    public byte[] Protect(byte[] clear)
    {
        var k = key();
        using var aes = Aes.Create();
        aes.Key = k[..32];
        aes.GenerateIV();
        var body = aes.EncryptCbc(clear, aes.IV);
        var data = aes.IV.Concat(body).ToArray();
        return data.Concat(HMACSHA256.HashData(k[32..], data)).ToArray();
    }

    public byte[] Unprotect(byte[] sealedData)
    {
        var k = key();
        var data = sealedData[..^32];
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(k[32..], data), sealedData[^32..]))
        {
            throw new CryptographicException("tampered");
        }
        using var aes = Aes.Create();
        aes.Key = k[..32];
        return aes.DecryptCbc(data[16..], data[..16]);
    }
}
