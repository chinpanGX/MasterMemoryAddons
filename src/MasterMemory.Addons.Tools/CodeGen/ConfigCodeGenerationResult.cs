using System.Collections.Generic;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>
/// <see cref="ConfigCodeGenerator"/> の結果。<see cref="SyntaxErrors"/> があれば定義のエラー(構文エラー、テーブルの型が record など。終了コード 1)、
/// <see cref="Problem"/> があれば設定や定義の置き方の誤り(終了コード 2)で、どちらのときも <see cref="Outputs"/> は空。
/// <see cref="SourceCount"/> は読んだ定義のファイルの数。<see cref="Sources"/> はパースした定義(マスタ生成の定義の読み込みで使い回す)。
/// </summary>
public sealed record ConfigCodeGenerationResult(
    IReadOnlyList<OutputFiles> Outputs,
    IReadOnlyList<string> SyntaxErrors,
    string? Problem,
    int SourceCount,
    IReadOnlyList<DefinitionSource> Sources);
