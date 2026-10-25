namespace MasterMemory.Addons
{
    /// <summary>
    /// バイナリの平文のヘッダ(<see cref="MasterDataBinary.ReadHeader"/> の結果)。ハッシュは 16 進(小文字)。
    /// 鍵(<c>MasterDataSchema.Hash</c>)なしで読めるので、マニフェストや配信の確認に使える。
    /// </summary>
    public sealed class MasterDataHeader
    {
        public MasterDataHeader(ushort formatVersion, bool encrypted, string schemaId, string dataHash)
        {
            FormatVersion = formatVersion;
            Encrypted = encrypted;
            SchemaId = schemaId;
            DataHash = dataHash;
        }

        /// <summary>形式のバージョン。</summary>
        public ushort FormatVersion { get; }

        /// <summary>中身が暗号化されているか。</summary>
        public bool Encrypted { get; }

        /// <summary>スキーマの ID(<see cref="MasterDataBinary.ComputeSchemaId"/>)。</summary>
        public string SchemaId { get; }

        /// <summary>データのハッシュ(暗号化する前の中身の SHA-256)。</summary>
        public string DataHash { get; }
    }
}
