using System;
using System.Security.Cryptography;

namespace MasterMemory.Addons
{
    /// <summary>
    /// 既定の暗号化。旧 master-data-pipeline の AesCrypto と同じ形式で、互いに読み書きできる。
    /// AES-128-CBC / PKCS7、鍵は PBKDF2(SHA-256、1000 回)でパスワードとソルトから導出する。
    /// 形式は [ Salt(16) | IV(16) | 暗号文 ]。
    /// </summary>
    /// <remarks>
    /// AES-GCM は、IL2CPP の実機でネイティブの AEAD 実装がなく動かないことがあるため使わない。
    /// </remarks>
    public sealed class AesMasterDataCipher : IMasterDataCipher
    {
        private const int KeySizeInBytes = 16; // AES-128
        private const int SaltSizeInBytes = 16;
        private const int IvSizeInBytes = 16;
        private const int HeaderSizeInBytes = SaltSizeInBytes + IvSizeInBytes;
        private const int Pbkdf2IterationCount = 1000;

        public byte[] Encrypt(byte[] data, string key)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));

            var salt = new byte[SaltSizeInBytes];
            var iv = new byte[IvSizeInBytes];
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(iv);

            using (var aes = CreateAes())
            using (var encryptor = aes.CreateEncryptor(DeriveKey(key, salt), iv))
            {
                var cipherText = encryptor.TransformFinalBlock(data, 0, data.Length);

                var result = new byte[HeaderSizeInBytes + cipherText.Length];
                Buffer.BlockCopy(salt, 0, result, 0, SaltSizeInBytes);
                Buffer.BlockCopy(iv, 0, result, SaltSizeInBytes, IvSizeInBytes);
                Buffer.BlockCopy(cipherText, 0, result, HeaderSizeInBytes, cipherText.Length);
                return result;
            }
        }

        public byte[] Decrypt(byte[] data, string key)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (data.Length < HeaderSizeInBytes)
            {
                throw new CryptographicException("暗号化されたデータが短すぎます(ソルトと IV が含まれていません)。");
            }

            var salt = new byte[SaltSizeInBytes];
            var iv = new byte[IvSizeInBytes];
            Buffer.BlockCopy(data, 0, salt, 0, SaltSizeInBytes);
            Buffer.BlockCopy(data, SaltSizeInBytes, iv, 0, IvSizeInBytes);

            using (var aes = CreateAes())
            using (var decryptor = aes.CreateDecryptor(DeriveKey(key, salt), iv))
            {
                // 暗号文は別の配列にコピーせず、オフセットを指定して復号する
                return decryptor.TransformFinalBlock(data, HeaderSizeInBytes, data.Length - HeaderSizeInBytes);
            }
        }

        private static Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using (var deriveBytes = new Rfc2898DeriveBytes(password, salt, Pbkdf2IterationCount, HashAlgorithmName.SHA256))
            {
                return deriveBytes.GetBytes(KeySizeInBytes);
            }
        }
    }
}
