/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace MoaviDesk.Services;

/// <summary>
/// Android: the kept password is sealed with an AES-256-GCM key in the Android Keystore ("moavi-desk-sign-in"),
/// made on first use; the key never leaves the Keystore (hardware-backed where the phone has it), so the app's
/// files alone open nothing. Stored as IV (12 bytes) + ciphertext and tag.
/// </summary>
internal sealed class KeystoreVault : IVault
{
    private const string Alias = "moavi-desk-sign-in";
    private const string Store = "AndroidKeyStore";

    private static IKey Key()
    {
        var ks = KeyStore.GetInstance(Store)!;
        ks.Load(null);
        if (ks.GetKey(Alias, null) is IKey existing) { return existing; }
        var gen = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Store)!;
        gen.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                     .SetBlockModes(KeyProperties.BlockModeGcm)
                     .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                     .SetKeySize(256)
                     .Build());
        return gen.GenerateKey()!;
    }

    public byte[] Protect(byte[] clear)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, Key());
        var iv = cipher.GetIV()!;
        return [.. iv, .. cipher.DoFinal(clear)!];
    }

    public byte[] Unprotect(byte[] sealedData)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, sealedData[..12]));
        return cipher.DoFinal(sealedData[12..])!;
    }
}
