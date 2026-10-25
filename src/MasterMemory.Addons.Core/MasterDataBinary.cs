using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MasterMemory.Addons
{
    /// <summary>
    /// マスタ生成が作るバイナリの形式。先頭に平文のヘッダ(72 バイト)を置き、その後ろに中身(暗号化ありなら暗号文)を置く。
    /// ヘッダはリトルエンディアンで、マジック "MMAD"(4)・形式のバージョン(2)・フラグ(2。bit0 = 暗号化あり)・
    /// スキーマの ID(32。<see cref="ComputeSchemaId"/>)・データのハッシュ(32。暗号化する前の中身の SHA-256)。
    /// 形式は docs/design/pipeline.md の「バイナリのヘッダ」。
    /// </summary>
    /// <remarks>
    /// スキーマのハッシュ(<c>MasterDataSchema.Hash</c>)は暗号化の鍵なので、ヘッダやエラーの表示には出さない。
    /// 外に出すのは、ハッシュから戻せないスキーマの ID だけ。
    /// </remarks>
    public static class MasterDataBinary
    {
        /// <summary>ヘッダの長さ(バイト)。</summary>
        public const int HeaderSize = 72;

        /// <summary>今の形式のバージョン。</summary>
        public const ushort FormatVersion = 2;

        private const ushort EncryptedFlag = 1;
        private const int HashSize = 32;
        private const int VersionOffset = 4;
        private const int FlagsOffset = 6;
        private const int SchemaIdOffset = 8;
        private const int DataHashOffset = SchemaIdOffset + HashSize;

        private static readonly byte[] Magic = { (byte)'M', (byte)'M', (byte)'A', (byte)'D' };

        private static readonly byte[] SchemaIdPrefix = { (byte)'s', (byte)'c', (byte)'h', (byte)'e', (byte)'m', (byte)'a', (byte)':' };

        /// <summary>
        /// スキーマのハッシュから、外に出してよいスキーマの ID(SHA-256 の 16 進。小文字)を計算する。
        /// <c>SHA-256("schema:" + ハッシュの 32 バイト)</c> で、ID からハッシュ(暗号化の鍵)には戻せない。
        /// ヘッダ・マニフェストの <c>schemaId</c>・CDN の置き場所に使う。
        /// </summary>
        public static string ComputeSchemaId(string schemaHash)
        {
            return ToHex(ComputeSchemaIdBytes(ParseHash(schemaHash, nameof(schemaHash))));
        }

        /// <summary>
        /// MasterMemory の <c>DatabaseBuilder.Build()</c> が返した <paramref name="data"/> にヘッダを付ける。
        /// <paramref name="cipher"/> を渡すと、中身を <paramref name="schemaHash"/> を鍵にして暗号化する。
        /// <paramref name="schemaHash"/> の英大文字・小文字は区別しない。
        /// </summary>
        public static byte[] Write(byte[] data, string schemaHash, IMasterDataCipher? cipher)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var schemaId = ComputeSchemaIdBytes(ParseHash(schemaHash, nameof(schemaHash)));

            var payload = cipher == null ? data : cipher.Encrypt(data, ToKey(schemaHash));
            var result = new byte[HeaderSize + payload.Length];
            Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(VersionOffset), FormatVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(FlagsOffset), cipher == null ? (ushort)0 : EncryptedFlag);
            Buffer.BlockCopy(schemaId, 0, result, SchemaIdOffset, HashSize);
            Buffer.BlockCopy(ComputeHash(data), 0, result, DataHashOffset, HashSize);
            Buffer.BlockCopy(payload, 0, result, HeaderSize, payload.Length);
            return result;
        }

        /// <summary>
        /// ヘッダだけを読む(鍵は要らない。復号もデータのハッシュの確認もしない)。マニフェストや配信の確認に使う。
        /// </summary>
        /// <exception cref="MasterDataFormatException">本アドオンのバイナリでない、形式が違う。</exception>
        public static MasterDataHeader ReadHeader(byte[] binary)
        {
            if (binary == null) throw new ArgumentNullException(nameof(binary));
            if (binary.Length < HeaderSize || !binary.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new MasterDataFormatException(MasterDataFormatErrorReason.NotMasterData,
                    "マスターデータのバイナリではありません(ヘッダがありません)。");
            }

            var version = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(VersionOffset));
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(binary.AsSpan(FlagsOffset));
            if (version != FormatVersion || (flags & ~EncryptedFlag) != 0)
            {
                throw new MasterDataFormatException(MasterDataFormatErrorReason.UnsupportedVersion,
                    $"対応していない形式のバイナリです(形式のバージョン {version}、フラグ {flags})。");
            }

            return new MasterDataHeader(
                version,
                (flags & EncryptedFlag) != 0,
                ToHex(binary.AsSpan(SchemaIdOffset, HashSize)),
                ToHex(binary.AsSpan(DataHashOffset, HashSize)));
        }

        /// <summary>
        /// ヘッダを確かめ、暗号化されていれば復号して、<c>new MemoryDatabase(bytes)</c> に渡すバイト列を返す。
        /// <paramref name="schemaHash"/> はアプリの <c>MasterDataSchema.Hash</c>(暗号化の鍵も兼ねる。英大文字・小文字は区別しない)。
        /// <paramref name="cipher"/> を省くと、暗号化されたバイナリは <see cref="AesMasterDataCipher"/> で復号する。
        /// </summary>
        /// <exception cref="MasterDataFormatException">本アドオンのバイナリでない、形式が違う、スキーマが違う、壊れている。</exception>
        public static byte[] Read(byte[] binary, string schemaHash, IMasterDataCipher? cipher = null)
        {
            if (binary == null) throw new ArgumentNullException(nameof(binary));
            var expectedSchemaId = ComputeSchemaId(schemaHash);
            var header = ReadHeader(binary);

            // 鍵が表示やログに出ないよう、メッセージには ID だけを書く
            if (header.SchemaId != expectedSchemaId)
            {
                throw new MasterDataFormatException(MasterDataFormatErrorReason.SchemaMismatch,
                    $"バイナリのスキーマ(ID {header.SchemaId})が、アプリのスキーマ(ID {expectedSchemaId})と違います。"
                    + "同じ定義から作ったバイナリを使ってください。");
            }

            var payload = new byte[binary.Length - HeaderSize];
            Buffer.BlockCopy(binary, HeaderSize, payload, 0, payload.Length);

            var data = payload;
            if (header.Encrypted)
            {
                try
                {
                    data = (cipher ?? new AesMasterDataCipher()).Decrypt(payload, ToKey(schemaHash));
                }
                catch (CryptographicException ex)
                {
                    throw new MasterDataFormatException(MasterDataFormatErrorReason.Corrupted, "バイナリを復号できません(壊れています)。", ex);
                }
            }

            if (ToHex(ComputeHash(data)) != header.DataHash)
            {
                throw new MasterDataFormatException(MasterDataFormatErrorReason.Corrupted,
                    "バイナリの中身がヘッダのデータのハッシュと合いません(壊れています)。");
            }

            return data;
        }

        // 鍵はハッシュの文字列。照合と同じく英大文字・小文字に関係なく決まるよう、小文字にそろえる
        private static string ToKey(string schemaHash)
        {
            return schemaHash.ToLowerInvariant();
        }

        private static byte[] ComputeSchemaIdBytes(byte[] schemaHash)
        {
            var input = new byte[SchemaIdPrefix.Length + schemaHash.Length];
            Buffer.BlockCopy(SchemaIdPrefix, 0, input, 0, SchemaIdPrefix.Length);
            Buffer.BlockCopy(schemaHash, 0, input, SchemaIdPrefix.Length, schemaHash.Length);
            return ComputeHash(input);
        }

        private static byte[] ComputeHash(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        // MasterDataSchema.Hash(SHA-256 の 16 進。英大文字・小文字は区別しない)をバイト列にする
        private static byte[] ParseHash(string hash, string parameterName)
        {
            if (hash == null) throw new ArgumentNullException(parameterName);
            if (hash.Length != HashSize * 2)
            {
                throw new ArgumentException("スキーマのハッシュは SHA-256 の 16 進(64 文字)で渡してください。", parameterName);
            }

            var bytes = new byte[HashSize];
            for (var i = 0; i < HashSize; i++)
            {
                var high = HexValue(hash[i * 2]);
                var low = HexValue(hash[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    throw new ArgumentException("スキーマのハッシュは SHA-256 の 16 進(64 文字)で渡してください。", parameterName);
                }

                bytes[i] = (byte)(high * 16 + low);
            }

            return bytes;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }
    }
}
