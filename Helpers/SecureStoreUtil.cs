using System;
using System.Security.Cryptography;
using System.Text;

namespace VenuePlus.Helpers;

public static class SecureStoreUtil
{
    public static string ProtectStringWithKey(string plain, string keyBase64)
    {
        var key = Convert.FromBase64String(keyBase64);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        using var pbkdf2 = new Rfc2898DeriveBytes(key, salt, 100_000, HashAlgorithmName.SHA256);
        var encKey = pbkdf2.GetBytes(32);
        var input = Encoding.UTF8.GetBytes(plain ?? string.Empty);
        var cipher = new byte[input.Length];
        var tag = new byte[16];
        using (var aesgcm = new AesGcm(encKey, 16))
        {
            aesgcm.Encrypt(nonce, input, cipher, tag);
        }
        var combined = new byte[16 + 12 + cipher.Length + 16];
        Buffer.BlockCopy(salt, 0, combined, 0, 16);
        Buffer.BlockCopy(nonce, 0, combined, 16, 12);
        Buffer.BlockCopy(cipher, 0, combined, 28, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, 28 + cipher.Length, 16);
        return Convert.ToBase64String(combined);
    }

    public static string UnprotectToStringWithKey(string base64, string keyBase64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        var data = Convert.FromBase64String(base64);
        var salt = new byte[16];
        var nonce = new byte[12];
        Buffer.BlockCopy(data, 0, salt, 0, 16);
        Buffer.BlockCopy(data, 16, nonce, 0, 12);
        var tag = new byte[16];
        Buffer.BlockCopy(data, data.Length - 16, tag, 0, 16);
        var cipherLen = data.Length - 28 - 16;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(data, 28, cipher, 0, cipherLen);
        var key = Convert.FromBase64String(keyBase64);
        using var pbkdf2 = new Rfc2898DeriveBytes(key, salt, 100_000, HashAlgorithmName.SHA256);
        var encKey = pbkdf2.GetBytes(32);
        var plain = new byte[cipherLen];
        using (var aesgcm = new AesGcm(encKey, 16))
        {
            aesgcm.Decrypt(nonce, cipher, tag, plain);
        }
        return Encoding.UTF8.GetString(plain);
    }
}
