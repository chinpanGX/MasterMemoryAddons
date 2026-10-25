using System;

namespace MasterMemory.Addons
{
    /// <summary>
    /// サーバー専用であることを示す。クライアント用の出力からは、この列・型が削除される。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property |
        AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false, Inherited = false)]
    public sealed class ServerOnlyAttribute : Attribute
    {
    }
}
