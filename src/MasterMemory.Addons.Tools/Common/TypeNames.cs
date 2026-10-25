using System.Collections.Generic;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>定義から読んだ型の名前を組み立てる。</summary>
internal static class TypeNames
{
    /// <summary>名前空間と型名(外側から順)をつなぐ。入れ子の型の区切りは <paramref name="nestedSeparator"/>。</summary>
    public static string Join(string @namespace, IEnumerable<string> types, char nestedSeparator)
    {
        var nested = string.Join(nestedSeparator, types);
        return @namespace.Length == 0 ? nested : $"{@namespace}.{nested}";
    }
}
