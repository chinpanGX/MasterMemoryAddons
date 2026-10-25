using System;

namespace MasterMemory.Addons
{
    /// <summary>
    /// クライアント専用であることを示す。サーバー用の出力からは、この列・型が削除される。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property |
        AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false, Inherited = false)]
    public sealed class ClientOnlyAttribute : Attribute
    {
    }
}
