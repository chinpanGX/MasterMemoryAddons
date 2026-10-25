namespace MasterMemory.Addons
{
    /// <summary>バイナリを読めない理由。</summary>
    public enum MasterDataFormatErrorReason
    {
        /// <summary>本アドオンのバイナリでない(ヘッダがない、マジックが違う)。</summary>
        NotMasterData,

        /// <summary>形式のバージョンやフラグに対応していない(新しい Tools で作ったバイナリなど)。</summary>
        UnsupportedVersion,

        /// <summary>バイナリのスキーマのハッシュが、アプリの <c>MasterDataSchema.Hash</c> と違う(定義が食い違っている)。</summary>
        SchemaMismatch,

        /// <summary>復号できない、またはデータのハッシュが合わない(壊れている)。</summary>
        Corrupted,
    }
}
