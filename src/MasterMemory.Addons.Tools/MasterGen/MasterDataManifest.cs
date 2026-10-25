using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>
/// バイナリごとに出すマニフェスト(docs/design/master-gen.md の「マニフェスト」)。
/// 配信で、どのアプリのビルドが読めるバイナリか、client と server が同じデータを使っているかを見分けるために使う。
/// </summary>
public sealed record MasterDataManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("schemaId")] string SchemaId,
    [property: JsonPropertyName("dataHash")] string DataHash,
    [property: JsonPropertyName("encrypted")] bool Encrypted,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("createdAt")] string CreatedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>バイナリの隣に置くマニフェストのパス。拡張子を .manifest.json に変える(masterdata.bytes → masterdata.manifest.json)。</summary>
    public static string PathFor(string binaryPath)
    {
        return Path.ChangeExtension(binaryPath, ".manifest.json");
    }

    /// <summary>
    /// ヘッダを付けたバイナリ(<see cref="MasterDataBinary.Write"/> の結果)から作る。形式のバージョン・スキーマの ID・
    /// データのハッシュ・暗号化の有無は、ヘッダから読む(マニフェストは CDN などに置くので、鍵のスキーマのハッシュは書かない)。
    /// </summary>
    public static MasterDataManifest Create(byte[] binary, DateTime createdAtUtc)
    {
        var header = MasterDataBinary.ReadHeader(binary);
        return new MasterDataManifest(
            header.FormatVersion,
            header.SchemaId,
            header.DataHash,
            header.Encrypted,
            binary.Length,
            createdAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions) + "\n";
    }
}
