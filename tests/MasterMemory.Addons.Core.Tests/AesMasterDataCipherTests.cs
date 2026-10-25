using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace MasterMemory.Addons.Core.Tests;

public class AesMasterDataCipherTests
{
    private const string Key = "3f9a0c1e5b7d2468ace013579bdf2468ace013579bdf2468ace013579bdf2468";

    private readonly AesMasterDataCipher cipher = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(1000)]
    public void EncryptThenDecrypt_ReturnsOriginal(int length)
    {
        // 検証: 長さの違うデータを暗号化してから復号する
        // 期待: 元のデータと一致する
        var data = CreateData(length);

        var decrypted = cipher.Decrypt(cipher.Encrypt(data, Key), Key);

        Assert.Equal(data, decrypted);
    }

    [Fact]
    public void Encrypt_UsesRandomSaltAndIv()
    {
        // 検証: 同じデータを2回暗号化する
        // 期待: ソルトと IV が毎回変わるので、暗号文が一致しない
        var data = CreateData(100);

        var first = cipher.Encrypt(data, Key);
        var second = cipher.Encrypt(data, Key);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Decrypt_ReadsLegacyFormat()
    {
        // 検証: 旧 master-data-pipeline の AesCrypto.Encrypt と同じ手順で暗号化したデータを復号する
        // 期待: 元のデータと一致する
        var data = Encoding.UTF8.GetBytes("legacy master data");
        var salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var iv = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();

        var encrypted = LegacyAesCrypto.Encrypt(data, Key, salt, iv);

        Assert.Equal(data, cipher.Decrypt(encrypted, Key));
    }

    [Fact]
    public void Encrypt_WritesLegacyFormat()
    {
        // 検証: 新しい実装で暗号化したデータを、旧の手順で復号する([Salt | IV | 暗号文] の形式)
        // 期待: 元のデータと一致する
        var data = Encoding.UTF8.GetBytes("new master data");

        var encrypted = cipher.Encrypt(data, Key);

        Assert.Equal(data, LegacyAesCrypto.Decrypt(encrypted, Key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_DoesNotReturnOriginal()
    {
        // 検証: 暗号化と違う鍵で復号する
        // 期待: CryptographicException になるか、元のデータとは違う結果になる
        var data = CreateData(100);
        var encrypted = cipher.Encrypt(data, Key);

        // 鍵が違うと、ほとんどの場合はパディングの検証で例外になる。
        // まれにパディングが偶然合っても、元のデータには戻らない
        try
        {
            var decrypted = cipher.Decrypt(encrypted, "wrong key");
            Assert.NotEqual(data, decrypted);
        }
        catch (CryptographicException)
        {
        }
    }

    [Fact]
    public void Decrypt_TooShortData_Throws()
    {
        // 検証: ソルトと IV の 32 バイトに満たないデータを復号する
        // 期待: CryptographicException になる
        Assert.Throws<CryptographicException>(() => cipher.Decrypt(new byte[31], Key));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        // 検証: Encrypt / Decrypt にデータや鍵として null を渡す
        // 期待: ArgumentNullException になる
        Assert.Throws<ArgumentNullException>(() => cipher.Encrypt(null!, Key));
        Assert.Throws<ArgumentNullException>(() => cipher.Encrypt(new byte[1], null!));
        Assert.Throws<ArgumentNullException>(() => cipher.Decrypt(null!, Key));
        Assert.Throws<ArgumentNullException>(() => cipher.Decrypt(new byte[32], null!));
    }

    private static byte[] CreateData(int length)
    {
        return Enumerable.Range(0, length).Select(i => (byte)(i * 31)).ToArray();
    }

    /// <summary>
    /// 旧 master-data-pipeline の AesCrypto(tools/dotnet/csharp-codegen/Generators/AesCryptoGenerator.cs)と同じ手順。
    /// テストで比べるため、ソルトと IV を外から渡せるようにしている。
    /// </summary>
    private static class LegacyAesCrypto
    {
        public static byte[] Encrypt(byte[] plainData, string password, byte[] salt, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = DeriveKey(password, salt);
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor();
            var cipherText = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);
            return salt.Concat(iv).Concat(cipherText).ToArray();
        }

        public static byte[] Decrypt(byte[] encryptedData, string password)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = encryptedData[16..32];
            aes.Key = DeriveKey(password, encryptedData[..16]);

            using var decryptor = aes.CreateDecryptor();
            var cipherText = encryptedData[32..];
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, 1000, HashAlgorithmName.SHA256, 16);
        }
    }
}
