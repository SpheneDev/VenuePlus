using System;
using System.Security.Cryptography;

namespace VenuePlus.Security;

public sealed class SecurityKeysService
{
    public sealed class KeyBundle
    {
        public string PublicKey { get; init; } = string.Empty;
        public string EncryptedPrivateKey { get; init; } = string.Empty;
        public string KeyEncSalt { get; init; } = string.Empty;
        public string KeyEncNonce { get; init; } = string.Empty;
    }

    public KeyBundle GenerateNewKeys(string pin)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicDer = ecdsa.ExportSubjectPublicKeyInfo();
        var privateDer = ecdsa.ExportPkcs8PrivateKey();

        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(pin, salt);

        var ciphertext = new byte[privateDer.Length];
        var tag = new byte[16];
        using (var aesgcm = new AesGcm(key, 16))
        {
            aesgcm.Encrypt(nonce, privateDer, ciphertext, tag);
        }

        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        return new KeyBundle
        {
            PublicKey = Convert.ToBase64String(publicDer),
            EncryptedPrivateKey = Convert.ToBase64String(combined),
            KeyEncSalt = Convert.ToBase64String(salt),
            KeyEncNonce = Convert.ToBase64String(nonce)
        };
    }

    public static byte[] Sign(byte[] payload, string pin, string encryptedPrivateKey, string keyEncSalt, string keyEncNonce)
    {
        var salt = Convert.FromBase64String(keyEncSalt);
        var nonce = Convert.FromBase64String(keyEncNonce);
        var enc = Convert.FromBase64String(encryptedPrivateKey);
        var key = DeriveKey(pin, salt);
        var cipherLen = enc.Length - 16;
        var cipher = new byte[cipherLen];
        var tag = new byte[16];
        Buffer.BlockCopy(enc, 0, cipher, 0, cipherLen);
        Buffer.BlockCopy(enc, cipherLen, tag, 0, 16);
        var privateDer = new byte[cipherLen];
        using (var aesgcm = new AesGcm(key, 16))
        {
            aesgcm.Decrypt(nonce, cipher, tag, privateDer);
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateDer, out _);
        return ecdsa.SignData(payload, HashAlgorithmName.SHA256);
    }

    public static bool Verify(byte[] payload, byte[] signature, string publicKey)
    {
        using var ecdsa = ECDsa.Create();
        var publicDer = Convert.FromBase64String(publicKey);
        ecdsa.ImportSubjectPublicKeyInfo(publicDer, out _);
        return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
    }

    private static byte[] DeriveKey(string pin, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
    }
}
