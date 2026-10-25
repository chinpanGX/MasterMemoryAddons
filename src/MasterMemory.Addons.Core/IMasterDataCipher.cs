namespace MasterMemory.Addons
{
    /// <summary>
    /// マスターデータのバイナリの暗号化と復号。独自の方式に差し替えるときは、これを実装する。
    /// 既定の実装は <see cref="AesMasterDataCipher"/>。
    /// </summary>
    public interface IMasterDataCipher
    {
        /// <param name="data">暗号化するデータ。</param>
        /// <param name="key">パスワード。ツールは生成した MasterDataKey.Value を渡す。実装は使っても無視してもよい。</param>
        byte[] Encrypt(byte[] data, string key);

        /// <param name="data">暗号化されたデータ。</param>
        /// <param name="key">暗号化したときと同じパスワード(通常は MasterDataKey.Value)。</param>
        byte[] Decrypt(byte[] data, string key);
    }
}
