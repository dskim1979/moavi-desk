/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Security.Cryptography;
using System.Text;
using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// The kept sign-in password (Settings › 일반 › 비밀번호 저장): DPAPI-protected for this Windows user, so
/// the config file is useless on another account or PC. A LAB PC or a kiosk never keeps one.
/// </summary>
internal static class PasswordVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MOAVI Desk sign-in");

    public static void Keep(AppConfig config, string password)
    {
        config.SavedPassword = config.RememberPassword && !config.IsSharedPc && OperatingSystem.IsWindows()
            ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser))
            : string.Empty;
    }

    public static string? Read(AppConfig config)
    {
        if (!config.RememberPassword || config.IsSharedPc || config.SavedPassword.Length == 0 || !OperatingSystem.IsWindows()) { return null; }
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(config.SavedPassword), Entropy,
                                                                   DataProtectionScope.CurrentUser));
        }
        catch { return null; }   // another Windows user's, or damaged
    }

    public static void Forget(AppConfig config) => config.SavedPassword = string.Empty;
}
