/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Security.Cryptography;
using System.Text;

namespace MoaviDesk.Services;

/// <summary>
/// macOS: the key that protects the kept password (KeyedVault) lives in the user's login keychain as a generic
/// password ("MOAVI Desk" / "sign-in key"), made on first use; the config file alone opens nothing.
/// </summary>
internal static class KeychainVault
{
    private const string Lib = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("MOAVI Desk");
    private static readonly byte[] Account = Encoding.UTF8.GetBytes("sign-in key");
    private static byte[]? _key;

    public static IVault Create() => new KeyedVault(Key);

    private static byte[] Key()
    {
        if (_key is not null) { return _key; }
        if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account,
                                           out var length, out var data, IntPtr.Zero) == 0)
        {
            try
            {
                var found = new byte[length];
                Marshal.Copy(data, found, 0, (int)length);
                if (found.Length == 64) { return _key = found; }
            }
            finally { SecKeychainItemFreeContent(IntPtr.Zero, data); }
        }
        var key = RandomNumberGenerator.GetBytes(64);
        var status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)Account.Length, Account,
                                                   (uint)key.Length, key, IntPtr.Zero);
        if (status != 0) { throw new CryptographicException($"keychain {status}"); }
        return _key = key;
    }

    [DllImport(Lib)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
                                                             byte[] account, out uint passwordLength, out IntPtr passwordData, IntPtr item);

    [DllImport(Lib)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength,
                                                            byte[] account, uint passwordLength, byte[] passwordData, IntPtr item);

    [DllImport(Lib)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
}
