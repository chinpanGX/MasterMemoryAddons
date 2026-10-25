using System;
using System.Linq;
using System.Text;
using Xunit;

namespace MasterMemory.Addons.Core.Tests;

public class MasterDataBinaryTests
{
    private const string Schema = "3f9a0c1e5b7d2468ace013579bdf2468ace013579bdf2468ace013579bdf2468";

    private const string OtherSchema = "0000000000000000000000000000000000000000000000000000000000000001";

    private static readonly byte[] Data = Encoding.UTF8.GetBytes("MemoryDatabase のバイト列の代わり");

    [Fact]
    public void WriteThenRead_ReturnsData_WithoutEncryption()
    {
        // 検証: 暗号化せずに書き、同じスキーマで読む
        // 期待: 元のデータに戻る。ヘッダは 72 バイトで、"MMAD" で始まり、暗号化のフラグは立たない
        var binary = MasterDataBinary.Write(Data, Schema, null);

        Assert.Equal(MasterDataBinary.HeaderSize + Data.Length, binary.Length);
        Assert.Equal("MMAD", Encoding.ASCII.GetString(binary, 0, 4));
        Assert.Equal(2, BitConverter.ToUInt16(binary, 4));
        Assert.Equal(0, BitConverter.ToUInt16(binary, 6));
        Assert.Equal(Data, MasterDataBinary.Read(binary, Schema));
    }

    [Fact]
    public void WriteThenRead_ReturnsData_WithEncryption()
    {
        // 検証: AesMasterDataCipher で暗号化して書き、cipher を省いて読む
        // 期待: 暗号化のフラグが立ち、中身は平文ではない。読むと元のデータに戻る(鍵はスキーマのハッシュ)
        var binary = MasterDataBinary.Write(Data, Schema, new AesMasterDataCipher());

        Assert.Equal(1, BitConverter.ToUInt16(binary, 6));
        Assert.False(binary.Skip(MasterDataBinary.HeaderSize).SequenceEqual(Data));
        Assert.Equal(Data, MasterDataBinary.Read(binary, Schema));
    }

    [Fact]
    public void Write_StoresSameDataHash_RegardlessOfEncryption()
    {
        // 検証: 同じデータを、暗号化なし・暗号化あり(2回)で書く
        // 期待: データのハッシュ(40〜71 バイト)は、暗号化の有無や乱数に関係なく同じ
        var plain = MasterDataBinary.Write(Data, Schema, null);
        var first = MasterDataBinary.Write(Data, Schema, new AesMasterDataCipher());
        var second = MasterDataBinary.Write(Data, Schema, new AesMasterDataCipher());

        Assert.Equal(plain.Skip(40).Take(32), first.Skip(40).Take(32));
        Assert.Equal(first.Skip(40).Take(32), second.Skip(40).Take(32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(71)]
    public void Read_ThrowsNotMasterData_WhenTooShort(int length)
    {
        // 検証: ヘッダより短いバイト列を読む
        // 期待: NotMasterData の例外
        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(new byte[length], Schema));
        Assert.Equal(MasterDataFormatErrorReason.NotMasterData, ex.Reason);
    }

    [Fact]
    public void Read_ThrowsNotMasterData_ForOldFormatWithoutHeader()
    {
        // 検証: ヘッダのない、旧 master-data-pipeline の形式(暗号文だけ)を読む
        // 期待: NotMasterData の例外
        var old = new AesMasterDataCipher().Encrypt(Data, Schema);

        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(old, Schema));
        Assert.Equal(MasterDataFormatErrorReason.NotMasterData, ex.Reason);
    }

    [Theory]
    [InlineData(4, 1)] // 形式のバージョン 1(スキーマのハッシュをヘッダに書いていた形式)
    [InlineData(4, 3)] // 形式のバージョン 3
    [InlineData(6, 2)] // 知らないフラグ
    public void Read_ThrowsUnsupportedVersion(int offset, byte value)
    {
        // 検証: 形式のバージョンやフラグを、知らない値に書き換えて読む
        // 期待: UnsupportedVersion の例外
        var binary = MasterDataBinary.Write(Data, Schema, null);
        binary[offset] = value;

        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(binary, Schema));
        Assert.Equal(MasterDataFormatErrorReason.UnsupportedVersion, ex.Reason);
    }

    [Fact]
    public void Read_ThrowsSchemaMismatch_WhenSchemaDiffers()
    {
        // 検証: 別のスキーマのハッシュで読む(アプリとデータの定義が食い違っている)
        // 期待: SchemaMismatch の例外。復号はしない
        var binary = MasterDataBinary.Write(Data, Schema, new AesMasterDataCipher());

        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(binary, OtherSchema));
        Assert.Equal(MasterDataFormatErrorReason.SchemaMismatch, ex.Reason);
        Assert.Contains(MasterDataBinary.ComputeSchemaId(Schema), ex.Message);
        Assert.Contains(MasterDataBinary.ComputeSchemaId(OtherSchema), ex.Message);
        Assert.DoesNotContain(Schema, ex.Message);
        Assert.DoesNotContain(OtherSchema, ex.Message);
    }

    [Fact]
    public void Write_StoresSchemaId_NotSchemaHash()
    {
        // 検証: 暗号化して書き、ヘッダの 8〜39 バイトを見る(review の R1 の再現。以前は鍵のハッシュがそのまま入っていた)
        // 期待: スキーマの ID が入り、鍵(スキーマのハッシュ)のバイト列はバイナリのどこにもない
        var binary = MasterDataBinary.Write(Data, Schema, new AesMasterDataCipher());

        Assert.Equal(MasterDataBinary.ComputeSchemaId(Schema), Convert.ToHexString(binary, 8, 32).ToLowerInvariant());
        var schemaBytes = Convert.FromHexString(Schema);
        Assert.Equal(-1, binary.AsSpan().IndexOf(schemaBytes));
    }

    [Fact]
    public void ComputeSchemaId_DiffersFromHash_AndIgnoresCase()
    {
        // 検証: スキーマの ID を、小文字と英大文字のハッシュから計算する
        // 期待: ID は小文字の 64 文字で、ハッシュとは違う。大文字・小文字で同じ ID になる
        var id = MasterDataBinary.ComputeSchemaId(Schema);

        Assert.Equal(64, id.Length);
        Assert.Equal(id.ToLowerInvariant(), id);
        Assert.NotEqual(Schema, id);
        Assert.Equal(id, MasterDataBinary.ComputeSchemaId(Schema.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WriteThenRead_IgnoresCaseOfSchemaHash(bool upperOnWrite, bool upperOnRead)
    {
        // 検証: 暗号化して、書くときと読むときで英大文字・小文字の違うハッシュを渡す(review の R5 の再現。以前は Corrupted)
        // 期待: 元のデータに戻る
        var binary = MasterDataBinary.Write(Data, upperOnWrite ? Schema.ToUpperInvariant() : Schema, new AesMasterDataCipher());

        Assert.Equal(Data, MasterDataBinary.Read(binary, upperOnRead ? Schema.ToUpperInvariant() : Schema));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_ThrowsCorrupted_WhenPayloadIsChanged(bool encrypt)
    {
        // 検証: 中身の最後の1バイトを書き換えて読む
        // 期待: Corrupted の例外(復号できない、またはデータのハッシュが合わない)
        var binary = MasterDataBinary.Write(Data, Schema, encrypt ? new AesMasterDataCipher() : null);
        binary[binary.Length - 1] ^= 0xFF;

        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.Read(binary, Schema));
        Assert.Equal(MasterDataFormatErrorReason.Corrupted, ex.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadHeader_ReturnsHeaderFields_WithoutKey(bool encrypt)
    {
        // 検証: 書いたバイナリのヘッダだけを、鍵を渡さずに読む(review の R8)
        // 期待: 形式のバージョン・暗号化の有無・スキーマの ID・データのハッシュ(暗号化する前の中身の SHA-256)が返る
        var binary = MasterDataBinary.Write(Data, Schema, encrypt ? new AesMasterDataCipher() : null);

        var header = MasterDataBinary.ReadHeader(binary);

        Assert.Equal(MasterDataBinary.FormatVersion, header.FormatVersion);
        Assert.Equal(encrypt, header.Encrypted);
        Assert.Equal(MasterDataBinary.ComputeSchemaId(Schema), header.SchemaId);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Data)).ToLowerInvariant(), header.DataHash);
    }

    [Fact]
    public void ReadHeader_ThrowsNotMasterData_WhenTooShort()
    {
        // 検証: ヘッダより短いバイト列のヘッダを読む
        // 期待: NotMasterData の例外
        var ex = Assert.Throws<MasterDataFormatException>(() => MasterDataBinary.ReadHeader(new byte[10]));
        Assert.Equal(MasterDataFormatErrorReason.NotMasterData, ex.Reason);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz9a0c1e5b7d2468ace013579bdf2468ace013579bdf2468ace013579bdf2468")]
    public void Write_Throws_WhenSchemaHashIsNotSha256Hex(string schemaHash)
    {
        // 検証: SHA-256 の 16 進(64 文字)でないスキーマのハッシュを渡す
        // 期待: ArgumentException
        Assert.Throws<ArgumentException>(() => MasterDataBinary.Write(Data, schemaHash, null));
    }
}
