using System.Collections.Generic;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 定義の中の enum 1つ。<see cref="ContainingTypes"/> は、型の中に入れ子にした enum の外側の型名(外側から順)。
/// <see cref="File"/> は定義ディレクトリからの相対パス。
/// </summary>
public sealed record EnumSchema(
    string Namespace,
    IReadOnlyList<string> ContainingTypes,
    string Name,
    IReadOnlyList<EnumMemberSchema> Members,
    string File)
{
    /// <summary>C# での書き方の完全名(入れ子の型も <c>.</c> でつなぐ。例: <c>N.Item.Kind</c>)。</summary>
    public string FullName => TypeNames.Join(Namespace, ContainingTypes.Append(Name), '.');

    /// <summary>CLR の完全名(<see cref="System.Type.FullName"/> と同じ。入れ子の型は <c>+</c> でつなぐ。例: <c>N.Item+Kind</c>)。</summary>
    public string ClrFullName => TypeNames.Join(Namespace, ContainingTypes.Append(Name), '+');
}
