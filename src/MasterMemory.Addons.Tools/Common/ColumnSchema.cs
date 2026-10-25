namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// テーブルの列(プロパティ)1つ。<see cref="TypeName"/> は定義に書かれた型の名前(構文のまま)。
/// <see cref="IsUnique"/> は <c>[Unique]</c>、<see cref="Exists"/> は <c>[Exists]</c>(データ段の検証)。
/// </summary>
public sealed record ColumnSchema(string Name, string TypeName, bool IsPrimaryKey, bool IsUnique = false, ExistsRule? Exists = null);

/// <summary>
/// <c>[Exists]</c> の内容。<see cref="TableTypeName"/> は <c>typeof</c> に書いた型の名前(構文のまま)、<see cref="Column"/> は列の名前(省けば主キー)。
/// </summary>
public sealed record ExistsRule(string TableTypeName, string? Column, bool AllowEmpty);
