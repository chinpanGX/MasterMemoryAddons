using System.Collections.Generic;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 1つのテーブルの CSV を、水平分割したファイルも含めて結合したもの。
/// <see cref="Files"/> は結合した順(ファイル名の昇順)の、CSV のディレクトリからの相対パス。
/// <see cref="HeaderLine"/> は、最初のファイル(<c>Files[0]</c>)でヘッダが始まる行の番号(1 から)。
/// コメントの行に引用符で囲んだ改行があると、4 とは限らない。
/// </summary>
public sealed record CsvTable(
    string TableName,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Header,
    int HeaderLine,
    IReadOnlyList<CsvRow> Rows);
