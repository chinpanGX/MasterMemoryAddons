using System.Collections.Generic;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 定義の中の1テーブル(<c>[MemoryTable]</c> の型)。<see cref="Columns"/> は全カラム(ServerOnly / ClientOnly を含む)。
/// <see cref="ContainingTypes"/> は、型の中に入れ子にしたテーブルの外側の型名(外側から順)。
/// <see cref="File"/> は定義ディレクトリからの相対パス。
/// </summary>
public sealed record TableSchema(
    string TableName,
    string Namespace,
    IReadOnlyList<string> ContainingTypes,
    string TypeName,
    IReadOnlyList<ColumnSchema> Columns,
    string File)
{
    public string FullTypeName => TypeNames.Join(Namespace, ContainingTypes.Append(TypeName), '.');
}
