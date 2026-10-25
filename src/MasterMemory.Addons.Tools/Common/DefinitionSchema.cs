using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>定義ディレクトリから読んだテーブルと enum。<see cref="Errors"/> が空でなければ、定義として扱えない。</summary>
public sealed record DefinitionSchema(
    IReadOnlyList<TableSchema> Tables,
    IReadOnlyList<EnumSchema> Enums,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// <paramref name="table"/> の列の型名(構文のまま)から enum を探す。C# と同じく、テーブルの型の中 → 外側の型 → 名前空間の内側から外側の順に探す。
    /// 見つからなければ(using で別の名前空間を使っている場合)、末尾が一致する enum が1つだけのときにそれを使う。
    /// </summary>
    public EnumSchema? FindEnum(TableSchema table, string typeName)
    {
        return Find(Enums, e => e.FullName, table, typeName);
    }

    /// <summary><paramref name="table"/> から見た型名(<c>typeof</c> に書いた名前)のテーブルを、<see cref="FindEnum"/> と同じ順で探す。</summary>
    public TableSchema? FindTable(TableSchema table, string typeName)
    {
        return Find(Tables, t => t.FullTypeName, table, typeName);
    }

    private static T? Find<T>(IReadOnlyList<T> items, Func<T, string> fullNameOf, TableSchema table, string typeName)
        where T : class
    {
        var name = typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName["global::".Length..] : typeName;
        var scopes = table.FullTypeName.Split('.');
        for (var length = scopes.Length; length >= 0; length--)
        {
            var scope = string.Join(".", scopes.Take(length));
            var fullName = scope.Length == 0 ? name : $"{scope}.{name}";
            if (items.FirstOrDefault(i => fullNameOf(i) == fullName) is { } found)
            {
                return found;
            }
        }

        var candidates = items.Where(i => fullNameOf(i).EndsWith("." + name, StringComparison.Ordinal)).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
}
